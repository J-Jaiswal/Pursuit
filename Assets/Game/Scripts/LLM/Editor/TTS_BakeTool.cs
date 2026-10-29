#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Editor tool to batch-bake text lines into .wav AudioClip assets via Deepgram TTS.
/// Menu: Tools > Space GO > Bake TTS Lines
///
/// SETUP:
///   1. Requires the 'com.unity.editorcoroutines' package (Package Manager > Unity Registry).
///   2. Create a plain text file where each line has the format:
///        [categoryName] "Line text here."
///      Lines without a valid [category] tag are skipped.
///   3. Open this window, fill in the fields, and click Bake.
///   4. Baked .wav files are saved to the Output Folder and imported as AudioClips.
///      File naming: <category>_<index>.wav
///
/// RATE LIMITING: A 300ms delay is inserted between each Deepgram request.
///
/// WAV NOTE: Deepgram returns raw 16-bit signed PCM (little-endian, container=none).
/// This tool prepends a standard 44-byte RIFF/WAV header before writing to disk.
/// </summary>
public class TTS_BakeTool : EditorWindow
{
    // ── Window State ──────────────────────────────────────────────────────────
    private string deepgramApiKey  = "";
    private string voiceModelId    = "aura-2-hermes-en";
    private int    sampleRate      = 24000;
    private string inputFilePath   = "";
    private string outputFolderPath = "Assets/Audio/Prebaked";

    [Tooltip("Peak normalizes audio and applies extra gain (1.0 = peak 95%, 1.2 = +20% gain with soft limiter).")]
    private float  volumeGainMultiplier = 1.25f;
    private bool   enablePeakNormalization = true;

    private bool   isBaking;
    private string statusMessage   = "";
    private int    totalLines;
    private int    processedLines;

    private Vector2 scrollPos;

    // ── Menu Item ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/Space GO/Bake TTS Lines")]
    public static void ShowWindow()
    {
        var window = GetWindow<TTS_BakeTool>("TTS Bake Tool");
        window.minSize = new Vector2(480, 340);
        window.Show();
    }

    // ── GUI ───────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        GUILayout.Label("Space GO — TTS Bake Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUILayout.HelpBox(
            "Reads a tagged text file, calls Deepgram TTS for each line, and saves .wav files to the output folder.\n" +
            "Line format: [categoryName] \"Line text here.\"",
            MessageType.Info);

        EditorGUILayout.Space(6);

        // ── API Settings ──────────────────────────────────────────────────────
        GUILayout.Label("API Settings", EditorStyles.boldLabel);
        deepgramApiKey  = EditorGUILayout.TextField("Deepgram API Key", deepgramApiKey);
        voiceModelId    = EditorGUILayout.TextField("Voice Model ID", voiceModelId);
        sampleRate      = EditorGUILayout.IntField("Sample Rate (Hz)", sampleRate);

        EditorGUILayout.Space(4);
        GUILayout.Label("Audio Mastering", EditorStyles.boldLabel);
        enablePeakNormalization = EditorGUILayout.Toggle("Peak Normalization", enablePeakNormalization);
        volumeGainMultiplier    = EditorGUILayout.Slider("Volume Gain Boost", volumeGainMultiplier, 0.8f, 2.5f);

        EditorGUILayout.Space(6);

        // ── File Paths ────────────────────────────────────────────────────────
        GUILayout.Label("File Paths", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        inputFilePath = EditorGUILayout.TextField("Input Text File", inputFilePath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFilePanel("Select Tagged Text File", "", "txt");
            if (!string.IsNullOrEmpty(path)) inputFilePath = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        outputFolderPath = EditorGUILayout.TextField("Output Folder (Assets/...)", outputFolderPath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
            if (!string.IsNullOrEmpty(path))
            {
                // Convert absolute path to Assets-relative
                if (path.StartsWith(Application.dataPath))
                    outputFolderPath = "Assets" + path.Substring(Application.dataPath.Length);
                else
                    outputFolderPath = path;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // ── Progress ──────────────────────────────────────────────────────────
        if (isBaking)
        {
            float progress = totalLines > 0 ? (float)processedLines / totalLines : 0f;
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18), progress,
                                  $"{processedLines} / {totalLines} lines");
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.HelpBox(statusMessage, isBaking ? MessageType.None : MessageType.Info);
        }

        EditorGUILayout.Space(4);

        // ── Bake Button ───────────────────────────────────────────────────────
        EditorGUI.BeginDisabledGroup(isBaking);
        if (GUILayout.Button("Bake Lines", GUILayout.Height(32)))
        {
            StartBake();
        }
        EditorGUI.EndDisabledGroup();

        if (isBaking && GUILayout.Button("Cancel"))
        {
            isBaking = false;
            statusMessage = "Cancelled.";
        }
    }

    // ── Bake Entry Point ──────────────────────────────────────────────────────

    private void StartBake()
    {
        if (string.IsNullOrEmpty(deepgramApiKey))
        {
            statusMessage = "Error: Deepgram API key is required.";
            return;
        }
        if (string.IsNullOrEmpty(inputFilePath) || !File.Exists(inputFilePath))
        {
            statusMessage = "Error: Input file not found.";
            return;
        }

        // Ensure output folder exists
        string fullOutput = Path.Combine(Application.dataPath, outputFolderPath.Replace("Assets/", "").Replace("Assets\\", ""));
        if (!Directory.Exists(fullOutput))
            Directory.CreateDirectory(fullOutput);

        string[] rawLines = File.ReadAllLines(inputFilePath);
        var parsedLines = ParseLines(rawLines);

        if (parsedLines.Count == 0)
        {
            statusMessage = "No valid lines found. Check your file format: [categoryName] \"Line text\"";
            return;
        }

        totalLines    = parsedLines.Count;
        processedLines = 0;
        isBaking      = true;
        statusMessage = $"Starting bake of {totalLines} lines...";

        EditorCoroutineUtility.StartCoroutine(BakeCoroutine(parsedLines, fullOutput), this);
    }

    // ── Bake Coroutine ────────────────────────────────────────────────────────

    private IEnumerator BakeCoroutine(System.Collections.Generic.List<(string category, int idx, string text)> lines, string outputDir)
    {
        foreach (var (category, idx, text) in lines)
        {
            if (!isBaking) yield break;

            statusMessage = $"[{processedLines + 1}/{totalLines}] Requesting: [{category}] \"{text}\"";
            Repaint();

            bool done  = false;
            bool error = false;
            byte[] pcm = null;

            yield return EditorCoroutineUtility.StartCoroutine(
                DeepgramRequest(text, c => { pcm = c; done = true; }, () => { error = true; done = true; }),
                this);

            // Wait for the coroutine callback to fire (EditorCoroutines are synchronous per frame)
            float waited = 0f;
            while (!done && waited < 12f)
            {
                waited += 0.1f;
                yield return new EditorWaitForSeconds(0.1f);
            }

            if (!error && pcm != null && pcm.Length > 0)
            {
                // Normalize and boost volume before writing WAV header
                byte[] masteredPcm = MasterPCMBytes(pcm, enablePeakNormalization, volumeGainMultiplier);

                string filename  = $"{category}_{idx:D2}.wav";
                string filePath  = Path.Combine(outputDir, filename);
                byte[] wavBytes  = AddWavHeader(masteredPcm, sampleRate);
                File.WriteAllBytes(filePath, wavBytes);

                // Convert to Assets-relative and import
                string assetPath = "Assets/" + outputFolderPath.Replace("Assets/", "").Replace("Assets\\", "").TrimEnd('/', '\\') + "/" + filename;
                AssetDatabase.ImportAsset(assetPath.Replace("\\", "/"), ImportAssetOptions.ForceUpdate);

                statusMessage = $"✓ Saved: {filename}";
                Debug.Log($"[TTS_BakeTool] Saved: {filePath}");
            }
            else
            {
                statusMessage = $"✗ Failed: [{category}] \"{text}\"";
                Debug.LogWarning($"[TTS_BakeTool] Failed for line: \"{text}\"");
            }

            processedLines++;
            Repaint();

            // Rate limiting — 300ms between requests
            yield return new EditorWaitForSeconds(0.3f);
        }

        isBaking = false;
        statusMessage = $"Done! {processedLines} / {totalLines} lines processed. Check {outputFolderPath} in the Project window.";
        AssetDatabase.Refresh();
        Repaint();
    }

    // ── Deepgram Request ──────────────────────────────────────────────────────

    private IEnumerator DeepgramRequest(string text, Action<byte[]> onSuccess, Action onFail)
    {
        string url      = $"https://api.deepgram.com/v1/speak?model={voiceModelId}&encoding=linear16&sample_rate={sampleRate}&container=none";
        string jsonBody = $"{{\"text\":\"{EscapeJson(text)}\"}}";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Token " + deepgramApiKey);
        request.timeout = 12;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[TTS_BakeTool] Deepgram error: {request.error} | {request.downloadHandler?.text}");
            onFail?.Invoke();
            yield break;
        }

        byte[] data = request.downloadHandler.data;
        if (data == null || data.Length < 2)
        {
            Debug.LogError("[TTS_BakeTool] Deepgram returned empty or invalid PCM data.");
            onFail?.Invoke();
            yield break;
        }

        onSuccess?.Invoke(data);
    }

    // ── Audio Mastering Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Peak normalizes raw 16-bit linear PCM and applies volume gain with soft clipping prevention.
    /// </summary>
    private static byte[] MasterPCMBytes(byte[] rawPcm, bool normalize, float gain)
    {
        if (rawPcm == null || rawPcm.Length < 2) return rawPcm;

        int sampleCount = rawPcm.Length / 2;
        short[] samples = new short[sampleCount];

        // 1. Convert to 16-bit shorts and find peak
        short maxPeak = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(rawPcm[i * 2] | (rawPcm[i * 2 + 1] << 8));
            samples[i] = s;
            short abs = (short)Math.Abs((int)s);
            if (abs > maxPeak) maxPeak = abs;
        }

        if (maxPeak == 0) return rawPcm;

        // 2. Calculate scaling factor (target peak = 95% of 32767 ≈ 31128)
        float targetPeak = 31128f;
        float normScale = normalize ? (targetPeak / maxPeak) : 1.0f;
        float finalMultiplier = normScale * gain;

        // 3. Apply gain with soft tanh/clamp limiter to prevent hard clipping
        byte[] output = new byte[rawPcm.Length];
        for (int i = 0; i < sampleCount; i++)
        {
            float val = samples[i] * finalMultiplier;
            // Clamp within 16-bit signed range
            if (val > 32767f) val = 32767f;
            else if (val < -32768f) val = -32768f;

            short finalSample = (short)Math.Round(val);
            output[i * 2]     = (byte)(finalSample & 0xFF);
            output[i * 2 + 1] = (byte)((finalSample >> 8) & 0xFF);
        }

        return output;
    }

    // ── WAV Header ────────────────────────────────────────────────────────────

    /// <summary>
    /// Prepends a standard 44-byte RIFF/WAV header to raw 16-bit signed mono PCM data.
    /// Deepgram returns headerless PCM (container=none); Unity needs a proper .wav file.
    /// </summary>
    private static byte[] AddWavHeader(byte[] pcmData, int sampleRate)
    {
        const int channels    = 1;    // Deepgram mono
        const int bitDepth    = 16;
        int byteRate          = sampleRate * channels * bitDepth / 8;
        int blockAlign        = channels * bitDepth / 8;
        int dataSize          = pcmData.Length;
        int fileSize          = 36 + dataSize; // RIFF chunk size

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(fileSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);               // PCM sub-chunk size
        bw.Write((short)1);         // AudioFormat = PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitDepth);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(pcmData);

        return ms.ToArray();
    }

    // ── Line Parser ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parses lines of the format: [category] "Text here."
    /// Returns (category, per-category-index, text).
    /// </summary>
    private static System.Collections.Generic.List<(string, int, string)> ParseLines(string[] rawLines)
    {
        var result      = new System.Collections.Generic.List<(string, int, string)>();
        var catCounters = new System.Collections.Generic.Dictionary<string, int>();

        foreach (string raw in rawLines)
        {
            string line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("#"))
                continue;

            // Expect: [category] "text" OR [category] text (quotes optional)
            if (!line.StartsWith("[")) continue;

            int closeBracket = line.IndexOf(']');
            if (closeBracket < 0) continue;

            string category = line.Substring(1, closeBracket - 1).Trim();
            string text     = line.Substring(closeBracket + 1).Trim().Trim('"');

            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(text)) continue;

            if (!catCounters.ContainsKey(category)) catCounters[category] = 0;
            int idx = catCounters[category]++;

            result.Add((category, idx, text));
        }

        return result;
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n",  "\\n")
                .Replace("\r",  "\\r")
                .Replace("\t",  "\\t");
    }
}
#endif
