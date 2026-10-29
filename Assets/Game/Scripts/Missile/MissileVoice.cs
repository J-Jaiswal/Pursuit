using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MissileVoice : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private CommentaryQueue commentaryQueue;
    [SerializeField] private LLMService llmService;
    [SerializeField] private LLMSecretsSO secrets;

    [Header("Pre-baked Lines (Tier 1)")]
    [Tooltip("Assign the NovaLineBankSO asset here after creating it via Assets > Create > Commentary > Nova Line Bank.")]
    [SerializeField] private NovaLineBankSO novaLineBank;

    [Header("Audio Tuning")]
    [Tooltip("3D Spatial Blend: 0.0 = pure 2D radio, 1.0 = pure 3D world position. 0.35 gives clear cockpit radio volume with directional cues.")]
    [Range(0f, 1f)] [SerializeField] private float spatialBlend = 0.35f;
    [Tooltip("Minimum distance at which audio plays at 100% volume.")]
    [SerializeField] private float minAudibleDistance = 15f;
    [Tooltip("Maximum distance at which audio can be heard.")]
    [SerializeField] private float maxAudibleDistance = 100f;
    [Tooltip("Volume multiplier for Nova's voice.")]
    [Range(0f, 1.5f)] [SerializeField] private float voiceVolume = 1.0f;

    [Header("Visual Pulse")]
    [Tooltip("The renderer whose emission intensity pulses while TTS is loading.")]
    [SerializeField] private Renderer missileRenderer;
    [Tooltip("Emission colour at peak pulse intensity.")]
    [SerializeField] private Color emissionPulseColor = new Color(1f, 0.3f, 0f); // orange glow

    // ── State ─────────────────────────────────────────────────────────────────
    private AudioSource audioSource;
    private bool isEmissionPulsing;
    private bool proximityLineTriggered; // once-per-approach proximity line
    private float lastNovaSpokenTime = -100f;
    private float lastMissileCatchOrDeathTime = -100f;

    // Global clip history — prevents cross-category sonic repeats within last N plays
    private readonly Queue<string> globalClipHistory = new Queue<string>();
    private const int GlobalHistorySize = 6;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.spatialBlend = spatialBlend;
            audioSource.minDistance  = minAudibleDistance;
            audioSource.maxDistance  = maxAudibleDistance;
            audioSource.rolloffMode  = AudioRolloffMode.Linear;
            audioSource.dopplerLevel = 0.2f;
            audioSource.volume       = voiceVolume;
            // Ignore listener pause so Nova's death taunt finishes cleanly even on death UI
            audioSource.ignoreListenerPause = true;
        }
    }

    private void Start()
    {
        // Register AudioSource with the queue so it can play on this source
        if (commentaryQueue != null)
            commentaryQueue.RegisterNovaSource(audioSource);

        // Subscribe to queue events for visual pulse
        CommentaryQueue.OnNovaTTSLoadingChanged += HandleNovaTTSLoadingChanged;

        // Subscribe to race events
        RaceContext.OnCommentTrigger += HandleCommentTrigger;

        // ── TIER 2: COMMENTED OUT FOR CURRENT BUILD ──────────────────────────
        // ambientTauntCoroutine = StartCoroutine(AmbientTauntLoop());
    }

    private void OnDestroy()
    {
        CommentaryQueue.OnNovaTTSLoadingChanged -= HandleNovaTTSLoadingChanged;
        RaceContext.OnCommentTrigger -= HandleCommentTrigger;

        /* TIER 2
        if (ambientTauntCoroutine != null)
        {
            StopCoroutine(ambientTauntCoroutine);
            ambientTauntCoroutine = null;
        }
        */
    }

    private void Update()
    {
        // Once-per-approach proximity line (< 5 units = CRITICAL, pre-baked, bypasses cooldown)
        if (!proximityLineTriggered && MissileAI.Instance != null && MissileAI.Instance.IsActive)
        {
            if (MissileAI.Instance.DistanceToPlayer < 5f)
            {
                proximityLineTriggered = true;
                // Don't fire proximity if surge was spoken less than 1.5s ago
                if (Time.unscaledTime - lastNovaSpokenTime > 1.5f)
                {
                    EnqueuePrebakedNova(novaLineBank?.proximityClose, CommentaryQueue.CommentPriority.Critical,
                                        bypassCooldown: true);
                }
            }
        }
        else if (MissileAI.Instance != null && MissileAI.Instance.DistanceToPlayer >= 10f)
        {
            // Reset once missile pulls back so the next approach fires again
            proximityLineTriggered = false;
        }
    }

    // ── Trigger Handling (Tier 1 Only) ────────────────────────────────────────

    private void HandleCommentTrigger(CommentTrigger trigger, RaceContext.GameStateSnapshot snapshot)
    {
        switch (trigger)
        {
            // ── Tier 1: Critical Catch / Death ─────────────────────────────────
            case CommentTrigger.MissileCaughtPlayer:
                // Direct missile contact detonation
                lastMissileCatchOrDeathTime = Time.unscaledTime;
                EnqueuePrebakedNova(novaLineBank?.caught, CommentaryQueue.CommentPriority.Critical,
                                    bypassCooldown: true);
                return;

            case CommentTrigger.PlayerDied:
                // If player died from missile detonation and we just fired Caught (< 2.0s ago),
                // do NOT play a second death line that clobbers the caught taunt.
                if (snapshot.DeathCause == RaceContext.DeathCause.Missile &&
                    (Time.unscaledTime - lastMissileCatchOrDeathTime) < 2.0f)
                {
                    return;
                }

                lastMissileCatchOrDeathTime = Time.unscaledTime;
                var deathPool = novaLineBank != null ? novaLineBank.GetDeathPool(snapshot.DeathCause) : null;
                EnqueuePrebakedNova(deathPool, CommentaryQueue.CommentPriority.Critical,
                                    bypassCooldown: true);
                return;

            // ── Tier 1: High Priority (Pre-baked) ──────────────────────────────
            case CommentTrigger.MissileSpawnActivated:
                EnqueuePrebakedNova(novaLineBank?.spawn, CommentaryQueue.CommentPriority.High,
                                    bypassCooldown: true);
                return;

            case CommentTrigger.MissileSurgeActivated:
                // Don't surge-taunt if missile is already closer than 7 units (caught/proximity is about to fire)
                // or if another Nova line fired in the last 2.0s
                if (MissileAI.Instance != null && MissileAI.Instance.DistanceToPlayer < 7f) return;
                if (Time.unscaledTime - lastNovaSpokenTime < 2.0f) return;

                EnqueuePrebakedNova(novaLineBank?.surge, CommentaryQueue.CommentPriority.High,
                                    bypassCooldown: true);
                return;

            case CommentTrigger.PlayerNearFinish:
                EnqueuePrebakedNova(novaLineBank?.nearFinish, CommentaryQueue.CommentPriority.High,
                                    bypassCooldown: false);
                return;

            // ── Tier 1: Normal Priority (Pre-baked) ────────────────────────────
            case CommentTrigger.PlayerOnThirdCrash:
                EnqueuePrebakedNova(novaLineBank?.thirdCrash, CommentaryQueue.CommentPriority.Normal,
                                    bypassCooldown: false);
                return;

            /* ── TIER 2 (COMMENTED OUT FOR FUTURE BUILD) ────────────────────────
            case CommentTrigger.PlayerAdvancedPhase:
                RequestLine(CommentaryQueue.CommentPriority.Normal,
                            BuildContextDescription(trigger, snapshot));
                return;

            case CommentTrigger.PlayerEnteredZone:
                if (snapshot.ZoneSpeakerTarget == CommentaryZoneTrigger.ZoneSpeakerTarget.MissionControlOnly)
                    return;
                RequestLine(snapshot.ZoneCommentPriority,
                            BuildContextDescription(trigger, snapshot));
                return;
            ─────────────────────────────────────────────────────────────────── */

            default:
                return;
        }
    }

    // ── Pre-baked Enqueue ─────────────────────────────────────────────────────

    /// <summary>
    /// Enqueue a Tier 1 pre-baked Nova line.
    /// Applies the retreat-silence rule for non-Critical lines.
    /// No-ops if novaLineBank is null or the category has no assigned clips.
    /// </summary>
    private void EnqueuePrebakedNova(
        NovaLineBankSO.LineCategory category,
        CommentaryQueue.CommentPriority priority,
        bool bypassCooldown)
    {
        if (commentaryQueue == null || novaLineBank == null || category == null) return;

        // Activation guard: Nova stays completely silent until the missile is active.
        // Critical lines (MissileCaughtPlayer, PlayerDied) are exempt — they must always play.
        if (priority != CommentaryQueue.CommentPriority.Critical &&
            (MissileAI.Instance == null || !MissileAI.Instance.IsActive))
            return;

        // Retreat-silence guard: missile says nothing non-Critical while retreating
        if (priority != CommentaryQueue.CommentPriority.Critical &&
            MissileAI.Instance != null &&
            MissileAI.Instance.CurrentBehavior == MissileBehavior.Retreat)
            return;

        var (clip, subtitle) = novaLineBank.Pick(category, globalClipHistory, GlobalHistorySize);
        if (clip == null)
        {
            Debug.LogWarning($"[MissileVoice] [T1] No clip available in pool '{category.categoryName}'. Assign clips in NovaLineBankSO.");
            return;
        }

        lastNovaSpokenTime = Time.unscaledTime;
        commentaryQueue.TryEnqueuePrebaked(clip, subtitle,
                                            CommentaryQueue.CommentSpeaker.Nova,
                                            priority, bypassCooldown);
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

    /* ═══════════════════════════════════════════════════════════════════════════
       TIER 2 CODE (COMMENTED OUT AS LATENCY IS KILLING THE GAMEPLAY)
       ═══════════════════════════════════════════════════════════════════════════
    [Header("Ambient Taunt Timer (Tier 2 — Live LLM)")]
    [SerializeField] private float ambientTauntIntervalMin = 15f;
    [SerializeField] private float ambientTauntIntervalMax = 20f;
    private bool isLLMCallPending;
    private Coroutine ambientTauntCoroutine;

    private const string SystemPrompt =
        "You are NOVA — an intelligent hunting missile pursuing a rocket pilot through a cave. " +
        "You are cold, predatory, precise. Speak in sentences under 8 words. " +
        "No punctuation except apostrophes. No emojis. No asterisks. " +
        "Respond with plain text only — one line, nothing else.";

    private IEnumerator AmbientTauntLoop()
    {
        yield return new WaitForSeconds(5f);
        while (true)
        {
            float interval = Random.Range(ambientTauntIntervalMin, ambientTauntIntervalMax);
            yield return new WaitForSeconds(interval);

            if (MissileAI.Instance == null || !MissileAI.Instance.IsActivePursuing) continue;
            if (MissileAI.Instance.DistanceToPlayer < 8f) continue;
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) continue;

            RequestLine(CommentaryQueue.CommentPriority.Normal, BuildAmbientTauntContext());
        }
    }

    private string BuildAmbientTauntContext()
    {
        var s = RaceContext.Instance != null ? RaceContext.Instance.BuildSnapshot() : default;
        return $"ambient taunt — attempt {s.attemptCount}, " +
               $"closest approach this run: {s.ClosestMissileDistance:F1} units, " +
               $"current behavior: {s.MissileBehaviorName}, " +
               $"spatial progress: {s.SpatialProgressPercent * 100f:F0}%";
    }

    private void RequestLine(CommentaryQueue.CommentPriority priority, string contextHint)
    {
        if (MissileAI.Instance != null && MissileAI.Instance.CurrentBehavior == MissileBehavior.Retreat
            && priority != CommentaryQueue.CommentPriority.Critical) return;

        if (isLLMCallPending && priority != CommentaryQueue.CommentPriority.Critical) return;
        if (llmService == null || commentaryQueue == null) return;

        isLLMCallPending = true;
        RaceContext.GameStateSnapshot snap = RaceContext.Instance != null ? RaceContext.Instance.BuildSnapshot() : default;
        string userPrompt = BuildUserPrompt(snap, contextHint);
        string voiceId = secrets != null ? secrets.NovaVoiceId : "aura-orion-en";

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

    private string BuildUserPrompt(RaceContext.GameStateSnapshot s, string contextHint)
    {
        string lastLine = string.IsNullOrEmpty(s.LastNovaLine) ? "none" : s.LastNovaLine;
        string losText = s.MissileHasLineOfSight ? "direct visual line-of-sight" : "hidden behind cave corner";
        string zoneInfo = !string.IsNullOrEmpty(s.CurrentZoneName) ? $"Player zone: {s.CurrentZoneName} | Your zone: {s.MissileCurrentZoneName}\n" : "";

        return $"Context: {contextHint}\n" + zoneInfo + $"Line of sight: {losText}\n" +
               $"Player health: {s.PlayerHealthNormalized * 100f:F0}% | Fuel: {s.PlayerFuelNormalized * 100f:F0}% | Engine Heat: {s.PlayerEngineHeatNormalized * 100f:F0}%\n" +
               $"Current phase: {s.currentPhaseName} (phase {s.phaseNumber} of {s.totalPhases}, attempt {s.attemptCount})\n" +
               $"Missile distance: {s.MissileDistance:F1} units\n" + $"Spatial progress: {s.SpatialProgressPercent * 100f:F0}%\n" +
               $"Corridor ahead: {s.CorridorWidthAhead}\n" + $"Player speed: {s.PlayerSpeedTrend}\n" +
               $"Your current behavior: {s.MissileBehaviorName}\n" + $"Your last line was: \"{lastLine}\"\n" +
               "Say something in character.";
    }

    private string BuildContextDescription(CommentTrigger trigger, RaceContext.GameStateSnapshot s)
    {
        return trigger switch
        {
            CommentTrigger.PlayerAdvancedPhase => $"player advanced to phase '{s.currentPhaseName}' ({s.phaseNumber} of {s.totalPhases})",
            CommentTrigger.PlayerEnteredZone => $"player entered zone '{s.CurrentZoneName}': {s.ZoneSituationContext}",
            _ => "general update",
        };
    }
    ═══════════════════════════════════════════════════════════════════════════ */
}
