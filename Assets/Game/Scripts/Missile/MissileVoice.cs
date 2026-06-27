using System.Collections;
using UnityEngine;

/// <summary>
/// NOVA — the missile's voice. Attached to the Missile GameObject alongside MissileAI.
///
/// Responsibilities:
///   • Subscribes to RaceContext.OnCommentTrigger for missile-relevant events
///   • Builds contextual LLM prompts using the live GameStateSnapshot
///   • Enqueues generated lines (or fallbacks) into CommentaryQueue
///   • Drives the missile's visual emission pulse while TTS is loading (felt before heard)
///
/// Audio: The AudioSource on this GameObject must be configured in the Inspector:
///   Spatial Blend = 1.0 (full 3D)  |  Max Distance = 60  |  Rolloff = Logarithmic
///   Doppler Level = 0.5 (slight pitch shift on approach)
///
/// SETUP:
///   1. Add AudioSource to Missile GameObject. Configure 3D settings above.
///   2. Add a Renderer (e.g., MeshRenderer) and assign to missileRenderer.
///   3. Enable "Emission" on the missile material.
///   4. Assign CommentaryQueue and LLMService references.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MissileVoice : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private CommentaryQueue commentaryQueue;
    [SerializeField] private LLMService      llmService;
    [SerializeField] private LLMSecretsSO    secrets;

    [Header("Visual Pulse")]
    [Tooltip("The renderer whose emission intensity pulses while TTS is loading.")]
    [SerializeField] private Renderer missileRenderer;
    [Tooltip("Emission colour at peak pulse intensity.")]
    [SerializeField] private Color    emissionPulseColor = new Color(1f, 0.3f, 0f); // orange glow

    [Header("Fallback Lines")]
    [TextArea(2, 4)]
    [SerializeField] private string[] fallbackLines =
    {
        "I'm still here.",
        "You can't hide in a cave.",
        "Every turn brings you closer to me.",
        "Slowing down. Interesting choice.",
        "I've seen better pilots.",
    };

    // ── State ─────────────────────────────────────────────────────────────────
    private AudioSource audioSource;
    private bool        isLLMCallPending;  // prevents piling up simultaneous LLM requests
    private bool        isEmissionPulsing;
    private bool        proximityLineTriggered; // once-per-approach proximity line

    // ── System Prompt ─────────────────────────────────────────────────────────
    private const string SystemPrompt =
        "You are NOVA — an intelligent hunting missile pursuing a rocket pilot through a cave. " +
        "You are cold, predatory, precise. Speak in sentences under 8 words. " +
        "No punctuation except apostrophes. No emojis. No asterisks. " +
        "Respond with plain text only — one line, nothing else.";

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        // Configure 3D audio programmatically as a safety default
        // (override these in the Inspector for per-level tuning)
        audioSource.spatialBlend  = 1.0f;
        audioSource.maxDistance   = 60f;
        audioSource.rolloffMode   = AudioRolloffMode.Logarithmic;
        audioSource.dopplerLevel  = 0.5f;
    }

    private void Start()
    {
        // Register AudioSource with the queue so it can play on this 3D source
        if (commentaryQueue != null)
            commentaryQueue.RegisterNovaSource(audioSource);

        // Subscribe to queue events for visual pulse
        CommentaryQueue.OnNovaTTSLoadingChanged += HandleNovaTTSLoadingChanged;

        // Subscribe to race events
        RaceContext.OnCommentTrigger += HandleCommentTrigger;

        // Track proximity once per close approach
        MissileAI.OnMissileReset += ResetProximityTrigger;
    }

    private void OnDestroy()
    {
        CommentaryQueue.OnNovaTTSLoadingChanged -= HandleNovaTTSLoadingChanged;
        RaceContext.OnCommentTrigger            -= HandleCommentTrigger;
        MissileAI.OnMissileReset               -= ResetProximityTrigger;
    }

    private void Update()
    {
        // Once-per-approach proximity line: within 5 units = CRITICAL (overrides cooldown)
        if (!proximityLineTriggered && MissileAI.Instance != null)
        {
            if (MissileAI.Instance.DistanceToPlayer < 5f)
            {
                proximityLineTriggered = true;
                RequestLine(CommentaryQueue.CommentPriority.Critical, "NOVA within 5 units of player");
            }
        }
        else if (MissileAI.Instance != null && MissileAI.Instance.DistanceToPlayer >= 10f)
        {
            // Reset once missile pulls back so the next approach fires again
            proximityLineTriggered = false;
        }
    }

    // ── Trigger Handling ──────────────────────────────────────────────────────

    private void HandleCommentTrigger(CommentTrigger trigger, RaceContext.GameStateSnapshot snapshot)
    {
        CommentaryQueue.CommentPriority priority;

        switch (trigger)
        {
            case CommentTrigger.MissileCaughtPlayer:
                priority = CommentaryQueue.CommentPriority.Critical;
                break;

            case CommentTrigger.MissileSurgeActivated:
            case CommentTrigger.PlayerNearFinish:
                priority = CommentaryQueue.CommentPriority.High;
                break;

            case CommentTrigger.MissileRetreatActivated:
            case CommentTrigger.PlayerCollectedFuel:
            case CommentTrigger.PlayerCollectedRepair:
            case CommentTrigger.PlayerOnThirdCrash:
            case CommentTrigger.PlayerRespawned:
            case CommentTrigger.PlayerEngineOverheated:
                priority = CommentaryQueue.CommentPriority.Normal;
                break;

            default:
                return; // NOVA doesn't react to fuel/health threshold triggers — that's MC's domain
        }

        RequestLine(priority, BuildContextDescription(trigger, snapshot));
    }

    // ── LLM Request ───────────────────────────────────────────────────────────

    private void RequestLine(CommentaryQueue.CommentPriority priority, string contextHint)
    {
        // NOVA is silent during RETREAT — missile is "stunned" per GDD
        if (MissileAI.Instance != null
            && MissileAI.Instance.CurrentBehavior == MissileBehavior.Retreat
            && priority != CommentaryQueue.CommentPriority.Critical)
            return;

        // Don't stack simultaneous LLM calls
        if (isLLMCallPending && priority != CommentaryQueue.CommentPriority.Critical) return;
        if (llmService == null || commentaryQueue == null) return;

        isLLMCallPending = true;

        RaceContext.GameStateSnapshot snap = RaceContext.Instance != null
            ? RaceContext.Instance.BuildSnapshot()
            : default;

        string userPrompt = BuildUserPrompt(snap);
        string voiceId    = secrets != null ? secrets.NovaVoiceId : "aura-orion-en";

        llmService.RequestLLMLine(SystemPrompt, userPrompt, fallbackLines, timeoutSeconds: 3f,
            onSuccess: text =>
            {
                isLLMCallPending = false;
                if (RaceContext.Instance != null) RaceContext.Instance.LastNovaLine = text;
                commentaryQueue.TryEnqueue(text, CommentaryQueue.CommentSpeaker.Nova, priority, voiceId, ttsTimeout: 2f);
            },
            onFallback: text =>
            {
                isLLMCallPending = false;
                commentaryQueue.TryEnqueue(text, CommentaryQueue.CommentSpeaker.Nova, priority, voiceId, ttsTimeout: 2f);
            });
    }

    // ── Prompt Builder ────────────────────────────────────────────────────────

    private string BuildUserPrompt(RaceContext.GameStateSnapshot s)
    {
        string lastLine = string.IsNullOrEmpty(s.LastNovaLine) ? "none" : s.LastNovaLine;

        return $"Player health: {s.PlayerHealthNormalized * 100f:F0}% | Fuel: {s.PlayerFuelNormalized * 100f:F0}% | Engine Heat: {s.PlayerEngineHeatNormalized * 100f:F0}%\n" +
               $"Missile distance: {s.MissileDistance:F1} units\n" +
               $"Level progress: {s.LevelProgress * 100f:F0}%\n" +
               $"Corridor ahead: {s.CorridorWidthAhead}\n" +
               $"Player speed: {s.PlayerSpeedTrend}\n" +
               $"Your current behavior: {s.MissileBehaviorName}\n" +
               $"Your last line was: \"{lastLine}\"\n" +
               "Say something in character.";
    }

    private string BuildContextDescription(CommentTrigger trigger, RaceContext.GameStateSnapshot s)
    {
        return trigger switch
        {
            CommentTrigger.MissileCaughtPlayer    => "missile just caught the player",
            CommentTrigger.MissileSurgeActivated  => "surge activated — player is weak",
            CommentTrigger.MissileRetreatActivated => "retreat — give false hope",
            CommentTrigger.PlayerCollectedFuel    => "player collected fuel pickup",
            CommentTrigger.PlayerCollectedRepair  => "player collected repair pickup",
            CommentTrigger.PlayerOnThirdCrash     => "player crashed for the third time",
            CommentTrigger.PlayerNearFinish       => "player is near the finish line",
            CommentTrigger.PlayerRespawned        => "player just respawned at checkpoint",
            _                                     => "general update",
        };
    }

    // ── Visual Pulse ──────────────────────────────────────────────────────────

    private void HandleNovaTTSLoadingChanged(bool isLoading)
    {
        if (isLoading)
        {
            if (!isEmissionPulsing)
                StartCoroutine(PulseEmissionCoroutine());
        }
        else
        {
            isEmissionPulsing = false;
            SetEmissionIntensity(0f);
        }
    }

    private IEnumerator PulseEmissionCoroutine()
    {
        isEmissionPulsing = true;
        while (isEmissionPulsing)
        {
            float intensity = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f; // 0–1 pulse
            SetEmissionIntensity(intensity);
            yield return null;
        }
    }

    private void SetEmissionIntensity(float t)
    {
        if (missileRenderer == null) return;
        if (!missileRenderer.material.HasProperty("_EmissionColor")) return;

        Color c = Color.Lerp(Color.black, emissionPulseColor, t);
        missileRenderer.material.SetColor("_EmissionColor", c);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ResetProximityTrigger() => proximityLineTriggered = false;
}
