using UnityEngine;

/// <summary>
/// Stores API keys and voice configuration for the LLM and TTS systems.
/// Create via Assets > Create > Config > LLMSecrets.
///
/// IMPORTANT: Do not commit this asset to source control if it contains real keys.
/// Add the asset file to .gitignore, or use a key rotation strategy before shipping.
///
/// To find Deepgram voice model IDs, visit: https://developers.deepgram.com/docs/tts-models
/// Example voice IDs: "aura-orion-en" (deep/cold), "aura-asteria-en" (clear/professional)
/// </summary>
[CreateAssetMenu(fileName = "LLMSecrets", menuName = "Config/LLMSecrets")]
public class LLMSecretsSO : ScriptableObject
{
    [Header("Groq (LLM)")]
    [Tooltip("Your Groq API key. Get one at console.groq.com")]
    [SerializeField] private string groqApiKey = "";

    [Tooltip("Groq model to use. llama3-8b-8192 is fast and cheap.")]
    [SerializeField] private string groqModel = "llama3-8b-8192";

    [Tooltip("Max tokens for NOVA / MC one-liners (keep this low for speed)")]
    [SerializeField] private int maxTokensOneLiner = 60;

    [Tooltip("Max tokens for the post-run debrief (2-3 sentences)")]
    [SerializeField] private int maxTokensDebrief = 120;

    [Header("Deepgram TTS")]
    [Tooltip("Your Deepgram API key. Get one at console.deepgram.com")]
    [SerializeField] private string deepgramApiKey = "";

    [Tooltip("Deepgram voice model for NOVA. Recommend: aura-orion-en (cold/deep)")]
    [SerializeField] private string novaVoiceId = "aura-orion-en";

    [Tooltip("Deepgram voice model for Mission Control. Recommend: aura-asteria-en (professional)")]
    [SerializeField] private string missionControlVoiceId = "aura-asteria-en";

    [Tooltip("PCM sample rate for TTS audio. Deepgram supports 8000–48000 Hz.")]
    [SerializeField] private int ttsSampleRate = 24000;

    // ── Public Accessors ──────────────────────────────────────────────────────

    public string GroqApiKey => groqApiKey;
    public string GroqModel => groqModel;
    public int MaxTokensOneLiner => maxTokensOneLiner;
    public int MaxTokensDebrief => maxTokensDebrief;

    public string DeepgramApiKey => deepgramApiKey;
    public string NovaVoiceId => novaVoiceId;
    public string MissionControlVoiceId => missionControlVoiceId;
    public int TTSSampleRate => ttsSampleRate;

    public bool HasGroqKey => !string.IsNullOrEmpty(groqApiKey);
    public bool HasDeepgramKey => !string.IsNullOrEmpty(deepgramApiKey);
}
