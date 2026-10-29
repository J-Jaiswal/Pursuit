using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mission Control — the 2D radio voice that guides the player.
/// Reacts to threshold events (engine heat, health, missile proximity).
///
/// Current Build: Tier 1 Pre-baked commentary only.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MissionControl : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private CommentaryQueue commentaryQueue;
    [SerializeField] private LLMService      llmService;
    [SerializeField] private LLMSecretsSO    secrets;

    [Header("Pre-baked Lines (Tier 1)")]
    [Tooltip("Assign the MCLineBankSO asset here after creating it via Assets > Create > Commentary > MC Line Bank.")]
    [SerializeField] private MCLineBankSO mcLineBank;

    [Header("Audio Tuning")]
    [Tooltip("Volume multiplier for Mission Control voice.")]
    [Range(0f, 1.5f)] [SerializeField] private float voiceVolume = 1.0f;

    //  State ------------------------>
    private AudioSource mcAudioSource;
    private bool        closeProximityLineTriggered;
    private float       lastMCSpokenTime = -100f;

    // Global clip history — prevents cross-category sonic repeats within last N plays
    private readonly Queue<string> globalClipHistory = new Queue<string>();
    private const int GlobalHistorySize = 6;

    //  Unity Lifecycle --------------------->

    private void Awake()
    {
        mcAudioSource = GetComponent<AudioSource>();
        if (mcAudioSource != null)
        {
            // 2D audio — no spatial blend
            mcAudioSource.spatialBlend = 0f;
            mcAudioSource.volume       = voiceVolume;
            mcAudioSource.ignoreListenerPause = true;
        }
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

    // Trigger Handling (Tier 1 Only) ----------------->

    private void HandleCommentTrigger(CommentTrigger trigger, RaceContext.GameStateSnapshot snapshot)
    {
        switch (trigger)
        {
            // ── Tier 1: Pre-baked, bypass cooldown, zero network call ─────────

            case CommentTrigger.PlayerFuelLow:
                if (Time.unscaledTime - lastMCSpokenTime < 2.0f) return;
                EnqueuePrebakedMC(mcLineBank?.fuelLow,
                                  CommentaryQueue.CommentPriority.High,
                                  bypassCooldown: true);
                return;

            case CommentTrigger.PlayerEngineOverheating:
            case CommentTrigger.PlayerEngineOverheated:
            case CommentTrigger.PlayerEngineHealthCritical:
                // Unified engine thermal / blowout alert
                if (Time.unscaledTime - lastMCSpokenTime < 2.0f) return;
                EnqueuePrebakedMC(mcLineBank?.engineOverheated,
                                  CommentaryQueue.CommentPriority.High,
                                  bypassCooldown: true);
                return;

            case CommentTrigger.PlayerHealthCritical:
                if (Time.unscaledTime - lastMCSpokenTime < 2.0f) return;
                EnqueuePrebakedMC(mcLineBank?.healthCritical,
                                  CommentaryQueue.CommentPriority.High,
                                  bypassCooldown: true);
                return;

            case CommentTrigger.LevelCompleted:
                EnqueuePrebakedMC(mcLineBank?.levelComplete,
                                  CommentaryQueue.CommentPriority.High,
                                  bypassCooldown: true);
                return;

            default:
                return;
        }
    }

    private void HandleMissileDistance(float distance)
    {
        // Only evaluate proximity if the missile is active in pursuit
        if (MissileAI.Instance == null || !MissileAI.Instance.IsActive) return;

        // Once per close approach: missile within 10 units — Tier 1 pre-baked, bypass cooldown
        if (!closeProximityLineTriggered && distance < 10f)
        {
            closeProximityLineTriggered = true;
            if (Time.unscaledTime - lastMCSpokenTime > 1.5f)
            {
                EnqueuePrebakedMC(mcLineBank?.missileProximityClose,
                                  CommentaryQueue.CommentPriority.High,
                                  bypassCooldown: true);
            }
        }
        else if (distance >= 15f)
        {
            closeProximityLineTriggered = false; // reset for next approach
        }
    }

    //  Tier 1: Pre-baked Dialogues Enqueue 

    

    private void EnqueuePrebakedMC(
        MCLineBankSO.LineCategory      category,
        CommentaryQueue.CommentPriority priority,
        bool                           bypassCooldown)
    {
        if (commentaryQueue == null || mcLineBank == null || category == null) return;

        var (clip, subtitle) = mcLineBank.Pick(category, globalClipHistory, GlobalHistorySize);
        if (clip == null)
        {
            Debug.LogWarning($"[MissionControl] [T1] No clip available in pool '{category.categoryName}'. Assign clips in MCLineBankSO.");
            return;
        }

        lastMCSpokenTime = Time.unscaledTime;
        commentaryQueue.TryEnqueuePrebaked(clip, subtitle,
                                            CommentaryQueue.CommentSpeaker.MissionControl,
                                            priority, bypassCooldown);
    }

    /* ═══════════════════════════════════════════════════════════════════════════
       TIER 2 CODE (COMMENTED OUT AS LATENCY IS KILLING THE WHOLE GAMEPLAY)
       ═══════════════════════════════════════════════════════════════════════════
    [Header("Fallback Lines")]
    [TextArea(2, 4)]
    [SerializeField] private string[] fallbackLines =
    {
        "Watch your systems.", "Stay on course.", "It's gaining on you.", "Keep moving. Don't stop.", "Fuel levels are a concern."
    };
    private bool isLLMCallPending;

    private const string SystemPrompt =
        "You are Mission Control — guiding a pilot through a dangerous cave. " +
        "You are calm, clipped, professional. Speak in urgent sentences under 8 words. " +
        "No humor. Pure tension. Respond with plain text only — one line.";

    private void RequestLine(CommentaryQueue.CommentPriority priority, string situationDesc, RaceContext.GameStateSnapshot snapshot)
    {
        if (isLLMCallPending && priority != CommentaryQueue.CommentPriority.High) return;
        if (llmService == null || commentaryQueue == null) return;

        isLLMCallPending = true;
        string userPrompt = BuildUserPrompt(situationDesc, snapshot);
        string voiceId    = secrets != null ? secrets.MissionControlVoiceId : "aura-asteria-en";

        llmService.RequestLLMLine(SystemPrompt, userPrompt, fallbackLines, timeoutSeconds: 3f,
             onSuccess: text =>
             {
                 isLLMCallPending = false;
                 if (RaceContext.Instance != null) RaceContext.Instance.LastMCLine = text;
                 commentaryQueue.TryEnqueue(text, CommentaryQueue.CommentSpeaker.MissionControl, priority, voiceId, ttsTimeout: 3f);
             },
             onFallback: text =>
             {
                 isLLMCallPending = false;
                 commentaryQueue.TryEnqueue(text, CommentaryQueue.CommentSpeaker.MissionControl, priority, voiceId, ttsTimeout: 3f);
             });
    }

    private string BuildUserPrompt(string situationDesc, RaceContext.GameStateSnapshot s)
    {
        string zoneInfo = !string.IsNullOrEmpty(s.CurrentZoneName) ? $"Current Sector: {s.CurrentZoneName}\n" : "";
        return $"Situation: {situationDesc}\n" + zoneInfo +
               $"Current phase: {s.currentPhaseName} (phase {s.phaseNumber} of {s.totalPhases}, attempt {s.attemptCount})\n" +
               $"Spatial progress: {s.SpatialProgressPercent * 100f:F0}%\n" +
               $"Player health: {s.PlayerHealthNormalized * 100f:F0}% | Fuel: {s.PlayerFuelNormalized * 100f:F0}%\n" +
               $"Missile distance: {s.MissileDistance:F1} units\n" +
               "Say one line in character.";
    }
    ═══════════════════════════════════════════════════════════════════════════ */
}
