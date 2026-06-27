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
///
/// SETUP: Place on a dedicated "CommentaryQueue" GameObject.
/// </summary>
public class CommentaryQueue : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static CommentaryQueue Instance { get; private set; }

    [Header("Cooldown")]
    [Tooltip("Minimum seconds between any two spoken lines (shared between NOVA and MC).")]
    [SerializeField] private float cooldownSeconds = 20f;

    // ── Events ─────────────────────────────────────────────────────────────── 
    /// <summary>Fires when NOVA's TTS is being loaded. Bool = true while loading, false when done.</summary>
    public static event Action<bool> OnNovaTTSLoadingChanged;

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
    }

    // ── State ─────────────────────────────────────────────────────────────────
    private AudioSource novaSource;
    private AudioSource mcSource;

    private readonly List<QueueItem> queue = new List<QueueItem>();
    private bool  isProcessing;
    private float lastSpokenTime = -100f; // allow immediate play at game start

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

    // ── Registration (called by voice scripts in their Awake/Start) ───────────

    public void RegisterNovaSource(AudioSource source)         => novaSource = source;
    public void RegisterMissionControlSource(AudioSource source) => mcSource   = source;

    // ── Public Enqueue ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempt to enqueue a line. The queue handles cooldown, priority, and TTS fetching.
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

        if (priority == CommentPriority.Critical)
        {
            // Interrupt everything — clear queue, stop current playback, play immediately
            StopAllCoroutines();
            StopCurrentAudio();
            queue.Clear();
            isProcessing = false;
            queue.Insert(0, item);
            StartCoroutine(DrainQueue());
            return;
        }

        // Discard if queue already full (max depth 2)
        if (queue.Count >= 2) return;

        queue.Add(item);

        if (!isProcessing)
            StartCoroutine(DrainQueue());
    }

    // ── Queue Processing ──────────────────────────────────────────────────────

    private IEnumerator DrainQueue()
    {
        isProcessing = true;

        while (queue.Count > 0)
        {
            // Enforce cooldown (CRITICAL items were inserted at index 0 — they skip this wait
            // because we reset lastSpokenTime only after speaking, and CRITICAL interrupts immediately)
            float timeSinceLast = Time.time - lastSpokenTime;
            if (timeSinceLast < cooldownSeconds)
            {
                yield return new WaitForSeconds(cooldownSeconds - timeSinceLast);
            }

            if (queue.Count == 0) break;

            QueueItem item = queue[0];
            queue.RemoveAt(0);

            yield return ProcessItem(item);
        }

        isProcessing = false;
    }

    private IEnumerator ProcessItem(QueueItem item)
    {
        bool isMissile = item.Speaker == CommentSpeaker.Nova;

        // Notify visual system that NOVA TTS is loading (triggers emission pulse on missile)
        if (isMissile) OnNovaTTSLoadingChanged?.Invoke(true);

        // Request TTS
        AudioClip clip     = null;
        bool      ttsDone  = false;

        LLMService.Instance.RequestTTS(item.Text, item.VoiceId, item.TTSTimeoutSeconds,
            c => { clip = c; ttsDone = true; });

        float elapsed = 0f;
        while (!ttsDone && elapsed < item.TTSTimeoutSeconds + 0.2f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (isMissile) OnNovaTTSLoadingChanged?.Invoke(false);

        // Select correct AudioSource
        AudioSource source = isMissile ? novaSource : mcSource;

        if (clip != null && source != null)
        {
            source.clip = clip;
            source.Play();
            lastSpokenTime = Time.time;

            // Wait for clip to finish before processing next item
            yield return new WaitForSeconds(clip.length);
        }
        else
        {
            // TTS failed or timed out — still update cooldown so we don't spam
            lastSpokenTime = Time.time;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void StopCurrentAudio()
    {
        if (novaSource != null && novaSource.isPlaying) novaSource.Stop();
        if (mcSource   != null && mcSource.isPlaying)   mcSource.Stop();
    }

    /// <summary>Returns true if the cooldown has elapsed (useful for gate checks in voice scripts).</summary>
    public bool IsCooldownReady => (Time.time - lastSpokenTime) >= cooldownSeconds;

    public bool IsProcessing => isProcessing;
}
