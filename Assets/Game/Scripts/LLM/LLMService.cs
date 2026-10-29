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
        if (secrets == null)
        {
            Debug.LogError("[LLMService] [Groq] LLMSecretsSO is NOT assigned to LLMService! Using fallback line.");
            UseFallback(fallbackLines, onFallback);
            return;
        }

        if (!secrets.HasGroqKey)
        {
            Debug.LogWarning("[LLMService] [Groq] Groq API key is MISSING in LLMSecretsSO asset! Using fallback line.");
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
        if (secrets == null)
        {
            Debug.LogError("[LLMService] [Deepgram] LLMSecretsSO is NOT assigned! Audio synthesis skipped.");
            onClip?.Invoke(null);
            return;
        }

        if (!secrets.HasDeepgramKey)
        {
            Debug.LogWarning("[LLMService] [Deepgram] Deepgram API key is MISSING in LLMSecretsSO asset! Audio synthesis skipped.");
            onClip?.Invoke(null);
            return;
        }

        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("[LLMService] [Deepgram] RequestTTS called with empty or null text.");
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

        string model = secrets != null ? secrets.GroqModel : "openai/gpt-oss-20b";
        string requestBody = BuildGroqRequestJson(systemPrompt, userPrompt, maxTok, model);
        byte[] bodyBytes   = Encoding.UTF8.GetBytes(requestBody);

        Debug.Log($"[LLMService] [Groq] Sending POST request (Model: {model}, MaxTokens: {maxTok}, Timeout: {timeoutSeconds}s)...");

        using UnityWebRequest request = new UnityWebRequest(GroqEndpoint, "POST");
        request.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + secrets.GroqApiKey);
        request.timeout = Mathf.CeilToInt(timeoutSeconds);

        float startTime = Time.time;
        var operation   = request.SendWebRequest();

        yield return operation;

        float totalDuration = Time.time - startTime;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorBody = request.downloadHandler != null ? request.downloadHandler.text : "No response body";
            Debug.LogError($"[LLMService] [Groq] Request FAILED in {totalDuration:F2}s — Result: {request.result}, HTTP {request.responseCode} ({request.error}). Details: {errorBody}. Using fallback line.");
            UseFallback(fallbackLines, onFallback);
            yield break;
        }

        string rawResponse = request.downloadHandler.text;
        string responseText = ParseGroqResponse(rawResponse);

        if (string.IsNullOrEmpty(responseText))
        {
            Debug.LogWarning($"[LLMService] [Groq] HTTP 200 received in {totalDuration:F2}s but failed to parse content from response: {rawResponse}. Using fallback line.");
            UseFallback(fallbackLines, onFallback);
        }
        else
        {
            string cleanLine = responseText.Trim();
            Debug.Log($"[LLMService] [Groq] SUCCESS in {totalDuration:F2}s! Generated line: \"{cleanLine}\"");
            onSuccess?.Invoke(cleanLine);
        }
    }

    // ── Deepgram TTS Coroutine ────────────────────────────────────────────────

    private IEnumerator DeepgramCoroutine(string text, string voiceModelId, float timeoutSeconds, Action<AudioClip> onClip)
    {
        int sampleRate    = secrets != null ? secrets.TTSSampleRate : 24000;
        string url        = $"{DeepgramEndpoint}?model={voiceModelId}&encoding=linear16&sample_rate={sampleRate}&container=none";
        string jsonBody   = $"{{\"text\":\"{EscapeJsonString(text)}\"}}";
        byte[] bodyBytes  = Encoding.UTF8.GetBytes(jsonBody);

        Debug.Log($"[LLMService] [Deepgram] Requesting TTS audio (Voice: {voiceModelId}, SampleRate: {sampleRate}Hz, Timeout: {timeoutSeconds}s) for text: \"{text}\"");

        using UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Token " + secrets.DeepgramApiKey);
        request.timeout = Mathf.CeilToInt(timeoutSeconds);

        float startTime = Time.time;
        var   operation = request.SendWebRequest();

        yield return operation;

        float totalDuration = Time.time - startTime;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorBody = request.downloadHandler != null ? request.downloadHandler.text : "No response body";
            Debug.LogError($"[LLMService] [Deepgram] TTS request FAILED in {totalDuration:F2}s — Result: {request.result}, HTTP {request.responseCode} ({request.error}). Details: {errorBody}.");
            onClip?.Invoke(null);
            yield break;
        }

        byte[] pcmBytes = request.downloadHandler.data;
        if (pcmBytes == null || pcmBytes.Length < 2)
        {
            Debug.LogError($"[LLMService] [Deepgram] Received invalid or empty PCM audio payload ({pcmBytes?.Length ?? 0} bytes).");
            onClip?.Invoke(null);
            yield break;
        }

        AudioClip clip = PCMBytesToAudioClip(pcmBytes, sampleRate);
        if (clip != null)
        {
            Debug.Log($"[LLMService] [Deepgram] SUCCESS in {totalDuration:F2}s! Created AudioClip (Duration: {clip.length:F2}s, Samples: {clip.samples}, SampleRate: {clip.frequency}Hz).");
            onClip?.Invoke(clip);
        }
        else
        {
            Debug.LogError("[LLMService] [Deepgram] Failed to convert PCM bytes into AudioClip.");
            onClip?.Invoke(null);
        }
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
        float maxPeak = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            // Little-endian 16-bit signed int → normalised float
            short sample = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
            float f = sample / 32768f;
            floatSamples[i] = f;
            float abs = Mathf.Abs(f);
            if (abs > maxPeak) maxPeak = abs;
        }

        // Peak normalize to 0.95 peak so live lines match pre-baked loudness
        if (maxPeak > 0.01f)
        {
            float normMultiplier = Mathf.Min(0.95f / maxPeak, 3.0f); // cap boost at 3x to avoid amplifying pure noise
            for (int i = 0; i < sampleCount; i++)
            {
                floatSamples[i] = Mathf.Clamp(floatSamples[i] * normMultiplier, -1f, 1f);
            }
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
