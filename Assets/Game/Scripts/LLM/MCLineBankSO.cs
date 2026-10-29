using System.Collections.Generic;
using UnityEngine;


// ScriptableObject holding all pre-baked AudioClip pools for Mission Control .


[CreateAssetMenu(fileName = "MCLineBank", menuName = "Commentary/MC Line Bank")]
public class MCLineBankSO : ScriptableObject
{
    // ── Category Type ─────────────────────────────────────────────────────────

    [System.Serializable]
    public class LineCategory
    {
        [HideInInspector] public string categoryName;

        [Tooltip("Pre-baked AudioClips for this trigger category.")]
        public AudioClip[] clips;

        [Tooltip("Subtitle text parallel to each clip (same array index).")]
        [TextArea(1, 2)]
        public string[] subtitles;

        /// <summary>Index of the last-picked clip. Managed by LinePicker — do not edit manually.</summary>
        [HideInInspector] public int lastPickedIndex = -1;
    }

    // ── Trigger Pools (5 Focused Survival Categories) ─────────────────────────

    [Header("1. Fuel Low (< 20%)")]
    [Tooltip("PlayerFuelLow — rocket fuel below 20%.")]
    public LineCategory fuelLow = new() { categoryName = "fuelLow" };

    [Header("2. Engine Overheated / Thermal Alert")]
    [Tooltip("PlayerEngineOverheating / PlayerEngineOverheated / PlayerEngineHealthCritical.")]
    public LineCategory engineOverheated = new() { categoryName = "engineOverheated" };

    [Header("3. Hull / Health Critical (< 30%)")]
    [Tooltip("PlayerHealthCritical — rocket hull below 30%.")]
    public LineCategory healthCritical = new() { categoryName = "healthCritical" };

    [Header("4. Missile Radar Proximity Alert (10 units)")]
    [Tooltip("Missile within 10 units of the player — early tactical radar warning.")]
    public LineCategory missileProximityClose = new() { categoryName = "missileProximityClose" };

    [Header("5. Level Complete (Victory Debrief)")]
    [Tooltip("LevelCompleted — player crossed finish line.")]
    public LineCategory levelComplete = new() { categoryName = "levelComplete" };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pick a non-repeating clip from the given category, updating global history.
    /// Returns (null, "") if the category has no assigned clips.
    /// </summary>
    public (AudioClip clip, string subtitle) Pick(
        LineCategory  cat,
        Queue<string> globalHistory,
        int           globalHistorySize)
    {
        if (cat == null) return (null, "");

        LinePicker.Pick(
            cat.clips, cat.subtitles,
            ref cat.lastPickedIndex,
            globalHistory, globalHistorySize,
            out AudioClip clip, out string subtitle);

        return (clip, subtitle);
    }
}
