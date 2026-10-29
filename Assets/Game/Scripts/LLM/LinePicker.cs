using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static utility for picking a random AudioClip from a pool while avoiding:
///   1. Immediate same-clip repeat within a category (lastIndex guard).
///   2. Cross-category repeats within a short global history window (globalHistory guard).
///
/// Used by NovaLineBankSO and MCLineBankSO. Neither guard fires when the pool
/// is too small to satisfy it — the algorithm falls back gracefully.
/// </summary>
public static class LinePicker
{
    /// <summary>
    /// Pick a random clip from <paramref name="clips"/>, updating repeat-avoidance state.
    /// </summary>
    /// <param name="clips">Pool of AudioClips (may contain nulls — they are skipped).</param>
    /// <param name="subtitles">Parallel subtitle strings. May be shorter than clips array.</param>
    /// <param name="lastIndex">Per-category last-picked index. Pass -1 on first call. Updated in-place.</param>
    /// <param name="globalHistory">Shared queue of recently played clip names across all categories. Updated in-place.</param>
    /// <param name="globalHistorySize">Maximum number of entries kept in globalHistory.</param>
    /// <param name="clip">The chosen AudioClip, or null if the pool is empty/all null.</param>
    /// <param name="subtitle">The matching subtitle string, or empty string if unavailable.</param>
    public static void Pick(
        AudioClip[]   clips,
        string[]      subtitles,
        ref int       lastIndex,
        Queue<string> globalHistory,
        int           globalHistorySize,
        out AudioClip clip,
        out string    subtitle)
    {
        clip     = null;
        subtitle = "";

        if (clips == null || clips.Length == 0) return;

        // ── Build candidate list ──────────────────────────────────────────────
        // Count valid (non-null) clips so we know when guards can safely apply.
        int validCount = 0;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null) validCount++;

        if (validCount == 0) return;

        var candidates = new List<int>(validCount);

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null) continue;

            // Guard 1: skip the immediately-previous pick (only if pool > 1 valid clip)
            if (validCount > 1 && i == lastIndex) continue;

            // Guard 2: skip clips in global history (only if it wouldn't eliminate all candidates)
            if (globalHistory != null &&
                globalHistory.Contains(clips[i].name) &&
                validCount > globalHistory.Count)
                continue;

            candidates.Add(i);
        }

        // Fallback: all candidates were filtered — relax both guards and pick anything valid
        if (candidates.Count == 0)
        {
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null) { candidates.Add(i); break; }
        }

        if (candidates.Count == 0) return;

        // ── Select ───────────────────────────────────────────────────────────
        int chosen = candidates[Random.Range(0, candidates.Count)];
        clip       = clips[chosen];
        subtitle   = (subtitles != null && chosen < subtitles.Length) ? subtitles[chosen] : "";
        lastIndex  = chosen;

        // ── Update global history ─────────────────────────────────────────────
        if (globalHistory != null)
        {
            globalHistory.Enqueue(clip.name);
            while (globalHistory.Count > globalHistorySize)
                globalHistory.Dequeue();
        }
    }
}
