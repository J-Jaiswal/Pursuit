using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared audio queue enforcing the 20-second cooldown between ANY two commentary lines,
/// the max-depth-2 queue rule, and CRITICAL priority override.
///
/// Voice scripts (MissileVoice, MissionControl) register their AudioSources with this queue
/// at startup. The queue handles TTS fetching and playback sequencing.
///
/// Architecture:
///   Voice script receives trigger → requests LLM text → calls TryEnqueue(text, speaker, ...)
///   Queue checks cooldown → fetches TTS via LLMService → plays on the correct AudioSource
/// </summary>
public class CommentaryQueue : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static CommentaryQueue Instance { get; private set; }

    [Header("Cooldown")]
    [Tooltip("Minimum seconds between any two spoken lines (shared between NOVA and MC).")]
    [SerializeField] private float cooldownSeconds = 20f;

    [Header("Mission Control Radio Static")]
    [Tooltip("Short walkie-talkie crackle played before every Mission Control voice line.")]
    [SerializeField] private AudioClip radioStaticClip;
    [Range(0f, 1f)]  [SerializeField] private float radioStaticVolume  = 1f;
    [Tooltip("How long (seconds) the radio static plays before Mission Control starts speaking.")]
    [Range(0f, 2f)]  [SerializeField] private float radioStaticDuration = 0.5f;

    // ── Events ─────────────────────────────────────────────────────────────── 
    /// <summary>Fires when NOVA's TTS is being loaded. Bool = true while loading, false when done.</summary>
    public static event Action<bool> OnNovaTTSLoadingChanged;

    /// <summary>Fires when a commentary line starts playing/showing.</summary>
    public static event Action<string, CommentSpeaker, float> OnCommentarySpoken;

    /// <summary>Fires when the active commentary is cleared or interrupted.</summary>
    public static event Action OnCommentaryCleared;

    // ── Internal Types ────────────────────────────────────────────────────────

    public enum CommentSpeaker   { Nova, MissionControl }
    public enum CommentPriority  { Low = 0, Normal = 1, High = 2, Critical = 3 }

    private class QueueItem
    {
        public string          Text;
        public CommentSpeaker  Speaker;
        public CommentPriority Priority;
        public string          VoiceId;
        public float           TTSTimeoutSeconds;
        /// <summary>Non-null → Tier 1 pre-baked path: skip RequestTTS entirely.</summary>
        public AudioClip       PrebakedClip;
        /// <summary>When true, this item skips the 20-second shared cooldown wait (does not interrupt).</summary>
        public bool            BypassCooldown;
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private AudioSource novaSource;
    private AudioSource mcSource;
    private AudioSource radioStaticSource;

    private readonly List<QueueItem> queue = new List<QueueItem>();
    private bool      isProcessing;
    private float     lastSpokenTime = -100f; // unscaled time of last finished speech
    private QueueItem currentActiveItem;
    private float     currentActiveItemStartTime = -100f;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Dedicated 2D source for radio static — separate from the MC voice source
        GameObject staticGO = new GameObject("RadioStatic_Source");
        staticGO.transform.SetParent(transform);
        radioStaticSource = staticGO.AddComponent<AudioSource>();
        radioStaticSource.playOnAwake  = false;
        radioStaticSource.spatialBlend = 0f; // 2D stereo
        radioStaticSource.volume       = radioStaticVolume;
    }

    // ── Registration (called by voice scripts in their Awake/Start) ───────────

    public void RegisterNovaSource(AudioSource source)         => novaSource = source;
    public void RegisterMissionControlSource(AudioSource source) => mcSource   = source;

    // ── Public Enqueue ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to enqueue a live-TTS line. The queue handles cooldown, priority, and TTS fetching.
    /// </summary>
    public void TryEnqueue(string text, CommentSpeaker speaker, CommentPriority priority, string voiceId,
                           float ttsTimeout = 2f)
    {
        if (string.IsNullOrEmpty(text)) return;

        var item = new QueueItem
        {
            Text              = text,
            Speaker           = speaker,
            Priority          = priority,
            VoiceId           = voiceId,
            TTSTimeoutSeconds = ttsTimeout,
        };

        EnqueueItem(item);
    }

    /// <summary>
    /// Enqueue a Tier 1 pre-baked AudioClip — no TTS network call at runtime.
    /// <para><paramref name="bypassCooldown"/> = true: item skips the 20-second shared cooldown wait
    /// but does NOT interrupt currently-playing audio (use Critical priority for that).</para>
    /// <para>If priority is High and the queue is already at depth 2, the lowest-priority
    /// non-Critical item is evicted to make room. If both queued items are Critical, the
    /// new item is discarded.</para>
    /// </summary>
    public void TryEnqueuePrebaked(
        AudioClip      clip,
        string         subtitleText,
        CommentSpeaker speaker,
        CommentPriority priority,
        bool           bypassCooldown = false)
    {
        if (clip == null) return;

        var item = new QueueItem
        {
            Text           = subtitleText ?? "",
            Speaker        = speaker,
            Priority       = priority,
            PrebakedClip   = clip,
            BypassCooldown = bypassCooldown,
        };

        EnqueueItem(item);
    }

    // ── Shared Enqueue Logic ──────────────────────────────────────────────────

    private void EnqueueItem(QueueItem item)
    {
        if (item.Priority == CommentPriority.Critical)
        {
            string label = item.PrebakedClip != null ? $"[T1] {item.PrebakedClip.name}" : $"\"{item.Text}\"";

            // If an active Critical line just started speaking (< 1.5s ago), don't cut it off mid-syllable.
            // Clear any lower priority pending items and queue this critical line to play right after.
            if (isProcessing && currentActiveItem != null && currentActiveItem.Priority == CommentPriority.Critical
                && (Time.unscaledTime - currentActiveItemStartTime) < 1.5f)
            {
                Debug.Log($"[CommentaryQueue] CRITICAL line received while another Critical line is speaking: {label}. Queuing next.");
                queue.Clear();
                queue.Insert(0, item);
                return;
            }

            Debug.Log($"[CommentaryQueue] CRITICAL line for {item.Speaker}: {label}. Interrupting and clearing queue.");
            StopAllCoroutines();
            StopCurrentAudio();
            queue.Clear();
            isProcessing = false;
            currentActiveItem = null;
            queue.Insert(0, item);
            StartCoroutine(DrainQueue());
            return;
        }

        // ── Non-Critical: discard if anything is already playing ──────────────
        // Commentary lines are time-sensitive — a stale alert played after the
        // moment has passed is worse than silence. Let the current line finish,
        // then re-trigger organically if the condition still applies.
        if (isProcessing)
        {
            string label = item.PrebakedClip != null ? item.PrebakedClip.name : $"\"{item.Text}\"";
            Debug.Log($"[CommentaryQueue] Bypassing {item.Priority} line for {item.Speaker} — audio already playing: {label}");
            return;
        }

        // Nothing is playing — enqueue and start drain
        if (queue.Count >= 2)
        {
            Debug.LogWarning($"[CommentaryQueue] Queue full (depth {queue.Count}). Discarding {item.Priority} line.");
            return;
        }

        queue.Add(item);
        string queueLabel = item.PrebakedClip != null ? $"[T1-Prebaked] {item.PrebakedClip.name}" : $"[T2-LiveLLM] \"{item.Text}\"";
        Debug.Log($"[CommentaryQueue] Enqueued for {item.Speaker} ({item.Priority}): {queueLabel} (depth: {queue.Count})");

        if (!isProcessing)
            StartCoroutine(DrainQueue());
    }

    // ── Queue Processing ──────────────────────────────────────────────────────

    private IEnumerator DrainQueue()
    {
        isProcessing = true;

        while (queue.Count > 0)
        {
            QueueItem item = queue[0];

            // Critical and BypassCooldown items skip the shared cooldown wait.
            // All other Non-Critical items wait out any remaining cooldown before speaking.
            bool skipCooldown = item.Priority == CommentPriority.Critical || item.BypassCooldown;
            if (!skipCooldown)
            {
                float timeSinceLast = Time.unscaledTime - lastSpokenTime;
                if (timeSinceLast < cooldownSeconds)
                {
                    float waitTime = cooldownSeconds - timeSinceLast;
                    Debug.Log($"[CommentaryQueue] Cooldown active ({timeSinceLast:F1}s / {cooldownSeconds:F1}s). Waiting {waitTime:F1}s before speaking...");
                    yield return new WaitForSecondsRealtime(waitTime);
                }
            }

            if (queue.Count == 0) break;

            // Re-fetch head after potential wait (a Critical item may have interrupted)
            item = queue[0];
            queue.RemoveAt(0);

            yield return ProcessItem(item);
        }

        isProcessing = false;
        currentActiveItem = null;
    }


    private IEnumerator ProcessItem(QueueItem item)
    {
        bool isMissile  = item.Speaker == CommentSpeaker.Nova;
        bool isPrebaked = item.PrebakedClip != null;

        // Emission pulse on missile — only for live TTS (pre-baked has no loading phase).
        if (isMissile && !isPrebaked) OnNovaTTSLoadingChanged?.Invoke(true);

        // ── Resolve AudioClip ─────────────────────────────────────────────────
        AudioClip clip;

        if (isPrebaked)
        {
            // Tier 1: clip already attached — zero network wait
            clip = item.PrebakedClip;
            Debug.Log($"[CommentaryQueue] [T1-Prebaked] Playing {item.Speaker} clip '{clip.name}' ({clip.length:F2}s).");
        }
        else
        {
            // Tier 2: request TTS from Deepgram
            AudioClip liveclip = null;
            bool      ttsDone  = false;

            LLMService.Instance.RequestTTS(item.Text, item.VoiceId, item.TTSTimeoutSeconds,
                c => { liveclip = c; ttsDone = true; });

            float elapsed = 0f;
            while (!ttsDone && elapsed < item.TTSTimeoutSeconds + 0.2f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            clip = liveclip;
        }

        if (isMissile && !isPrebaked) OnNovaTTSLoadingChanged?.Invoke(false);

        // ── Calculate playback duration ───────────────────────────────────────
        float duration;
        if (clip != null)
        {
            duration = clip.length;
            if (!isPrebaked)
                Debug.Log($"[CommentaryQueue] [T2-LiveLLM] Playing {item.Speaker} audio clip ({duration:F2}s).");
        }
        else
        {
            // TTS failed — fallback reading duration (approx 14 chars/sec, min 2.5s)
            duration = Mathf.Max(2.5f, item.Text.Length * 0.07f);
            Debug.LogWarning($"[CommentaryQueue] TTS unavailable for \"{item.Text}\". Subtitle-only for {duration:F2}s.");
        }

        // Notify UI to display subtitles
        OnCommentarySpoken?.Invoke(item.Text, item.Speaker, duration);

        // Select correct AudioSource and play
        AudioSource source = isMissile ? novaSource : mcSource;

        currentActiveItem = item;
        currentActiveItemStartTime = Time.unscaledTime;

        // ── Radio static pre-roll for Mission Control ──────────────────────────
        // Plays a short walkie-talkie crackle before the MC voice clip so it
        // feels like MC is keying up a radio channel. Nova lines are unaffected.
        if (!isMissile && radioStaticClip != null && radioStaticSource != null)
        {
            radioStaticSource.volume = radioStaticVolume;
            radioStaticSource.clip   = radioStaticClip;
            radioStaticSource.loop   = false;
            radioStaticSource.Play();

            // Wait exactly the configured duration, then stop the static cleanly.
            // MC voice begins immediately after.
            yield return new WaitForSecondsRealtime(radioStaticDuration);
            radioStaticSource.Stop();
        }
        // ──────────────────────────────────────────────────────────────────────

        if (clip != null && source != null)
        {
            source.clip = clip;
            source.Play();
        }

        // Always update cooldown and wait full duration in unscaled time
        lastSpokenTime = Time.unscaledTime;
        yield return new WaitForSecondsRealtime(duration);

        currentActiveItem = null;
        OnCommentaryCleared?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void StopCurrentAudio()
    {
        if (novaSource != null && novaSource.isPlaying) novaSource.Stop();
        if (mcSource   != null && mcSource.isPlaying)   mcSource.Stop();
        currentActiveItem = null;
        OnCommentaryCleared?.Invoke();
    }

    /// <summary>Returns true if the cooldown has elapsed (useful for gate checks in voice scripts).</summary>
    public bool IsCooldownReady => (Time.unscaledTime - lastSpokenTime) >= cooldownSeconds;

    public float CooldownRemaining => Mathf.Max(0f, cooldownSeconds - (Time.unscaledTime - lastSpokenTime));
    public int PendingCount => queue.Count;

    public bool IsProcessing => isProcessing;
}
