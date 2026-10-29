using UnityEngine;

[CreateAssetMenu(fileName = "LLMSecrets", menuName = "Config/LLMSecrets")]
public class LLMSecretsSO : ScriptableObject
{
    [Header("Groq (LLM)")]
    [Tooltip("Your Groq API key. Get one at console.groq.com")]
    [SerializeField] private string groqApiKey = "";

    [Tooltip("Groq model to use. openai/gpt-oss-20b is fast and reliable.")]
    [SerializeField] private string groqModel = "openai/gpt-oss-20b";

    [Tooltip("Max tokens for NOVA / MC one-liners (keep this low for speed)")]
    [SerializeField] private int maxTokensOneLiner = 60;

    [Tooltip("Max tokens for the post-run debrief (2-3 sentences)")]
    [SerializeField] private int maxTokensDebrief = 120;

    [Header("Deepgram TTS")]
    [Tooltip("Your Deepgram API key. Get one at console.deepgram.com")]
    [SerializeField] private string deepgramApiKey = "";

    [Tooltip("Deepgram voice model for NOVA. Recommend: aura-2-hermes-en (cold/deep)")]
    [SerializeField] private string novaVoiceId = "aura-2-hermes-en";

    [Tooltip("Deepgram voice model for Mission Control. Recommend: aura-2-zeus-en (professional)")]
    [SerializeField] private string missionControlVoiceId = "aura-2-zeus-en";

    [Tooltip("PCM sample rate for TTS audio. Deepgram supports 8000–48000 Hz.")]
    [SerializeField] private int ttsSampleRate = 24000;

    // Public Accessors ------------->

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
