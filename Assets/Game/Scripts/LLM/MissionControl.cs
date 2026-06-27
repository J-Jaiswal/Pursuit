using System.Collections;
using UnityEngine;

/// <summary>
/// Mission Control — the 2D radio voice that guides the player.
/// Reacts to threshold events (fuel, health, missile proximity, pickups, finish).
///
/// Key behaviour:
///   • Plays a brief radio static sound IMMEDIATELY when triggered — audio cue that comms are incoming
///   • The actual TTS line follows 0.5–1.5 seconds later (feels intentional, not like latency)
///   • Uses the shared CommentaryQueue cooldown (never talks over NOVA)
///
/// SETUP:
///   1. Add an AudioSource to this GameObject. Set Spatial Blend = 0 (2D).
///   2. Assign a short radio-static AudioClip to radioStaticClip.
///   3. Assign CommentaryQueue, LLMService, and LLMSecretsSO references.
///   4. Place on a dedicated "AudioManager" or "MissionControl" GameObject.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MissionControl : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private CommentaryQueue commentaryQueue;
    [SerializeField] private LLMService      llmService;
    [SerializeField] private LLMSecretsSO    secrets;

    [Header("Audio")]
    [Tooltip("Short radio static / beep played immediately when MC is triggered (before TTS loads).")]
    [SerializeField] private AudioClip radioStaticClip;
    [Tooltip("A second AudioSource for the radio static, separate from the MC voice source.")]
    [SerializeField] private AudioSource staticAudioSource;

    [Header("Fallback Lines")]
    [TextArea(2, 4)]
    [SerializeField] private string[] fallbackLines =
    {
        "Watch your systems.",
        "Stay on course.",
        "It's gaining on you.",
        "Keep moving. Don't stop.",
        "Fuel levels are a concern.",
    };

    // ── State ─────────────────────────────────────────────────────────────────
    private AudioSource mcAudioSource;
    private bool        isLLMCallPending;

    // Proximity threshold line (once per approach)
    private bool        closeProximityLineTriggered;

    // ── System Prompt ─────────────────────────────────────────────────────────
    private const string SystemPrompt =
        "You are Mission Control — guiding a pilot through a dangerous cave. " +
        "You are calm, clipped, professional. Speak in urgent sentences under 8 words. " +
        "No humor. Pure tension. Respond with plain text only — one line.";

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        mcAudioSource = GetComponent<AudioSource>();
        // 2D audio — no spatial blend
        mcAudioSource.spatialBlend = 0f;
    }

    private void Start()
    {
        if (commentaryQueue != null)
            commentaryQueue.RegisterMissionControlSource(mcAudioSource);

        RaceContext.OnCommentTrigger      += HandleCommentTrigger;
        RaceContext.OnMissileDistanceUpdated += HandleMissileDistance;
    }

    private void OnDestroy()
    {
        RaceContext.OnCommentTrigger         -= HandleCommentTrigger;
        RaceContext.OnMissileDistanceUpdated -= HandleMissileDistance;
    }

    // ── Trigger Handling ──────────────────────────────────────────────────────

    private void HandleCommentTrigger(CommentTrigger trigger, RaceContext.GameStateSnapshot snapshot)
    {
        CommentaryQueue.CommentPriority priority;
        string situationDesc;

        switch (trigger)
        {
            case CommentTrigger.PlayerFuelLow:
                priority    = CommentaryQueue.CommentPriority.High;
                situationDesc = "Fuel below 20%. Player needs a pickup urgently.";
                break;

            case CommentTrigger.PlayerHealthCritical:
                priority    = CommentaryQueue.CommentPriority.High;
                situationDesc = "Hull integrity below 30%. Player is in danger.";
                break;

            case CommentTrigger.MissileSurgeActivated:
                priority    = CommentaryQueue.CommentPriority.Normal;
                situationDesc = "The missile has accelerated into a surge — it's gaining fast.";
                break;

            case CommentTrigger.PlayerNearFinish:
                priority    = CommentaryQueue.CommentPriority.High;
                situationDesc = "Player is very close to the finish line.";
                break;

            case CommentTrigger.PlayerCollectedFuel:
                priority    = CommentaryQueue.CommentPriority.Low;
                situationDesc = "Player just collected a fuel pickup.";
                break;

            case CommentTrigger.PlayerCollectedRepair:
                priority    = CommentaryQueue.CommentPriority.Low;
                situationDesc = "Player just collected a repair pickup.";
                break;

            case CommentTrigger.PlayerRespawned:
                priority    = CommentaryQueue.CommentPriority.High;
                situationDesc = "Player just respawned at checkpoint. Missile is close behind.";
                break;

            case CommentTrigger.PlayerEngineOverheating:
                priority    = CommentaryQueue.CommentPriority.High;
                situationDesc = "Engine temperature is critical (above 80%). Urge the player to release throttle.";
                break;

            case CommentTrigger.PlayerEngineOverheated:
                priority    = CommentaryQueue.CommentPriority.High;
                situationDesc = "Engine has overheated! Core is taking damage continuously. Urge player to cool down.";
                break;

            default:
                return; // MC doesn't react to catch events or third crash (NOVA owns those)
        }

        RequestLine(priority, situationDesc, snapshot);
    }

    private void HandleMissileDistance(float distance)
    {
        // Once per close approach: missile within 10 units
        if (!closeProximityLineTriggered && distance < 10f)
        {
            closeProximityLineTriggered = true;
            RequestLine(CommentaryQueue.CommentPriority.High,
                        $"Missile is {distance:F0} units behind. Extremely close.",
                        RaceContext.Instance != null ? RaceContext.Instance.BuildSnapshot() : default);
        }
        else if (distance >= 15f)
        {
            closeProximityLineTriggered = false; // reset for next approach
        }
    }

    // ── LLM Request ───────────────────────────────────────────────────────────

    private void RequestLine(CommentaryQueue.CommentPriority priority, string situationDesc,
                             RaceContext.GameStateSnapshot snapshot)
    {
        if (isLLMCallPending && priority != CommentaryQueue.CommentPriority.High) return;
        if (llmService == null || commentaryQueue == null) return;

        // Play radio static IMMEDIATELY — MC "opening comms" cue
        PlayRadioStatic();

        isLLMCallPending = true;

        string userPrompt = BuildUserPrompt(situationDesc, snapshot);
        string voiceId    = secrets != null ? secrets.MissionControlVoiceId : "aura-asteria-en";

        llmService.RequestLLMLine(SystemPrompt, userPrompt, fallbackLines, timeoutSeconds: 3f,
            onSuccess: text =>
            {
                isLLMCallPending = false;
                if (RaceContext.Instance != null) RaceContext.Instance.LastMCLine = text;
                commentaryQueue.TryEnqueue(text, CommentaryQueue.CommentSpeaker.MissionControl,
                                           priority, voiceId, ttsTimeout: 3f);
            },
            onFallback: text =>
            {
                isLLMCallPending = false;
                commentaryQueue.TryEnqueue(text, CommentaryQueue.CommentSpeaker.MissionControl,
                                           priority, voiceId, ttsTimeout: 3f);
            });
    }

    // ── Prompt Builder ────────────────────────────────────────────────────────

    private string BuildUserPrompt(string situationDesc, RaceContext.GameStateSnapshot s)
    {
        return $"Situation: {situationDesc}\n" +
               $"Player health: {s.PlayerHealthNormalized * 100f:F0}% | Fuel: {s.PlayerFuelNormalized * 100f:F0}%\n" +
               $"Missile distance: {s.MissileDistance:F1} units\n" +
               "Say one line in character.";
    }

    // ── Audio Helpers ─────────────────────────────────────────────────────────

    private void PlayRadioStatic()
    {
        if (staticAudioSource != null && radioStaticClip != null)
        {
            staticAudioSource.PlayOneShot(radioStaticClip);
        }
    }
}
