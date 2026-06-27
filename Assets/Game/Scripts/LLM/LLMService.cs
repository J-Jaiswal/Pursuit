using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Singleton service for all external API calls:
///   • Groq LLM   — returns a text line via callback
///   • Deepgram TTS — returns an AudioClip via callback (linear16 PCM → AudioClip)
///
/// Architecture: All calls use coroutines + callbacks (no async/await) for full Unity compatibility.
/// If a call times out or fails, the onFallback callback is invoked with a random fallback line.
///
/// SETUP: Place on a dedicated "LLMService" GameObject that persists across scenes (DontDestroyOnLoad).
/// Assign an LLMSecretsSO asset to the secrets field.
/// </summary>
public class LLMService : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static LLMService Instance { get; private set; }

    [SerializeField] private LLMSecretsSO secrets;

    // ── Groq API Constants ────────────────────────────────────────────────────
    private const string GroqEndpoint = "https://api.groq.com/openai/v1/chat/completions";

    // ── Deepgram TTS Constants ────────────────────────────────────────────────
    // We request linear16 (raw PCM) so we can build an AudioClip manually — reliable across all platforms.
    private const string DeepgramEndpoint = "https://api.deepgram.com/v1/speak";

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Requests a single text line from Groq.
    /// On success: onSuccess(text). On failure/timeout: onFallback(randomFallback).
    /// Pass maxTokensOverride > 0 to override the default from LLMSecretsSO (e.g. debrief needs more tokens).
    /// </summary>
    public void RequestLLMLine(string systemPrompt, string userPrompt, string[] fallbackLines,
                               float timeoutSeconds, Action<string> onSuccess, Action<string> onFallback,
                               int maxTokensOverride = -1)
    {
        if (secrets == null || !secrets.HasGroqKey)
        {
            UseFallback(fallbackLines, onFallback);
            return;
        }
        StartCoroutine(GroqCoroutine(systemPrompt, userPrompt, fallbackLines, timeoutSeconds, onSuccess, onFallback, maxTokensOverride));
    }

    /// <summary>
    /// Requests TTS audio from Deepgram for the given text and voice model.
    /// On success: onClip(AudioClip). On failure/timeout: onClip(null).
    /// </summary>
    public void RequestTTS(string text, string voiceModelId, float timeoutSeconds, Action<AudioClip> onClip)
    {
        if (secrets == null || !secrets.HasDeepgramKey || string.IsNullOrEmpty(text))
        {
            onClip?.Invoke(null);
            return;
        }
        StartCoroutine(DeepgramCoroutine(text, voiceModelId, timeoutSeconds, onClip));
    }

    // ── Groq Coroutine ────────────────────────────────────────────────────────

    private IEnumerator GroqCoroutine(string systemPrompt, string userPrompt, string[] fallbackLines,
                                       float timeoutSeconds, Action<string> onSuccess, Action<string> onFallback,
                                       int maxTokensOverride = -1)
    {
        int maxTok = maxTokensOverride > 0 ? maxTokensOverride
                   : (secrets != null ? secrets.MaxTokensOneLiner : 60);

        string requestBody = BuildGroqRequestJson(systemPrompt, userPrompt, maxTok, secrets.GroqModel);
        byte[] bodyBytes   = Encoding.UTF8.GetBytes(requestBody);

        using UnityWebRequest request = new UnityWebRequest(GroqEndpoint, "POST");
        request.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + secrets.GroqApiKey);
        request.timeout = Mathf.CeilToInt(timeoutSeconds);

        float startTime = Time.time;
        var operation   = request.SendWebRequest();

        while (!operation.isDone)
        {
            if (Time.time - startTime > timeoutSeconds)
            {
                request.Abort();
                UseFallback(fallbackLines, onFallback);
                yield break;
            }
            yield return null;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[LLMService] Groq error: {request.error}");
            UseFallback(fallbackLines, onFallback);
            yield break;
        }

        string responseText = ParseGroqResponse(request.downloadHandler.text);
        if (string.IsNullOrEmpty(responseText))
        {
            UseFallback(fallbackLines, onFallback);
        }
        else
        {
            onSuccess?.Invoke(responseText.Trim());
        }
    }

    // ── Deepgram TTS Coroutine ────────────────────────────────────────────────

    private IEnumerator DeepgramCoroutine(string text, string voiceModelId, float timeoutSeconds, Action<AudioClip> onClip)
    {
        int sampleRate    = secrets != null ? secrets.TTSSampleRate : 24000;
        string url        = $"{DeepgramEndpoint}?model={voiceModelId}&encoding=linear16&sample_rate={sampleRate}&container=none";
        string jsonBody   = $"{{\"text\":\"{EscapeJsonString(text)}\"}}";
        byte[] bodyBytes  = Encoding.UTF8.GetBytes(jsonBody);

        using UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Token " + secrets.DeepgramApiKey);
        request.timeout = Mathf.CeilToInt(timeoutSeconds);

        float startTime = Time.time;
        var   operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            if (Time.time - startTime > timeoutSeconds)
            {
                request.Abort();
                onClip?.Invoke(null);
                yield break;
            }
            yield return null;
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[LLMService] Deepgram TTS error: {request.error}");
            onClip?.Invoke(null);
            yield break;
        }

        byte[]    pcmBytes = request.downloadHandler.data;
        AudioClip clip     = PCMBytesToAudioClip(pcmBytes, sampleRate);
        onClip?.Invoke(clip);
    }

    // ── JSON Helpers ──────────────────────────────────────────────────────────

    private string BuildGroqRequestJson(string systemPrompt, string userPrompt, int maxTokens, string model)
    {
        string sys  = EscapeJsonString(systemPrompt);
        string usr  = EscapeJsonString(userPrompt);
        return $"{{\"model\":\"{model}\",\"messages\":[{{\"role\":\"system\",\"content\":\"{sys}\"}},{{\"role\":\"user\",\"content\":\"{usr}\"}}],\"max_tokens\":{maxTokens},\"temperature\":0.85}}";
    }

    private string ParseGroqResponse(string json)
    {
        // Manual parse: find "content":"<value>" inside the choices array.
        // Avoids Newtonsoft dependency — JsonUtility can't handle mixed-type nested arrays reliably.
        const string marker = "\"content\":\"";
        int idx = json.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;

        int start = idx + marker.Length;
        int end   = json.IndexOf("\"", start, StringComparison.Ordinal);
        if (end < 0) return null;

        // Unescape basic JSON escape sequences
        return json.Substring(start, end - start)
                   .Replace("\\n", "\n")
                   .Replace("\\\"", "\"")
                   .Replace("\\\\", "\\");
    }

    private string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    // ── Audio Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Converts raw 16-bit signed PCM bytes (little-endian) from Deepgram into a Unity AudioClip.
    /// This approach works reliably on all platforms without requiring a third-party MP3 decoder.
    /// </summary>
    private AudioClip PCMBytesToAudioClip(byte[] pcmBytes, int sampleRate)
    {
        if (pcmBytes == null || pcmBytes.Length < 2) return null;

        int sampleCount = pcmBytes.Length / 2; // 16-bit = 2 bytes per sample
        float[] floatSamples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            // Little-endian 16-bit signed int → normalised float
            short sample = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
            floatSamples[i] = sample / 32768f;
        }

        AudioClip clip = AudioClip.Create("TTS_Clip", sampleCount, 1, sampleRate, false);
        clip.SetData(floatSamples, 0);
        return clip;
    }

    // ── Fallback Helper ───────────────────────────────────────────────────────

    private void UseFallback(string[] fallbackLines, Action<string> onFallback)
    {
        if (fallbackLines == null || fallbackLines.Length == 0)
        {
            onFallback?.Invoke("...");
            return;
        }
        string line = fallbackLines[UnityEngine.Random.Range(0, fallbackLines.Length)];
        onFallback?.Invoke(line);
    }
}
