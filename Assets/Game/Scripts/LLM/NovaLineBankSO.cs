using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject holding all pre-baked AudioClip pools for NOVA (the missile).
/// Each LineCategory contains a parallel arrays of AudioClips and subtitle strings.
/// Call Pick() at runtime to get a non-repeating clip from any category.
/// </summary>
/// 
[CreateAssetMenu(fileName = "NovaLineBank", menuName = "Commentary/Nova Line Bank")]
public class NovaLineBankSO : ScriptableObject
{
    // Category Type --------------------------->

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

    // Trigger Pools ---------------------->

    [Header("Spawn")]
    [Tooltip("MissileSpawnActivated — missile enters active pursuit.")]
    public LineCategory spawn = new() { categoryName = "spawn" };

    [Header("Proximity (< 5 units, Critical)")]
    [Tooltip("Nova within 5 units of the player — fires as Critical.")]
    public LineCategory proximityClose = new() { categoryName = "proximityClose" };

    [Header("Caught Player (Critical)")]
    [Tooltip("MissileCaughtPlayer — direct contact detonation.")]
    public LineCategory caught = new() { categoryName = "caught" };

    [Header("Player Death — by Cause")]
    [Tooltip("PlayerDied + DeathCause.Missile")]
    public LineCategory died_missile  = new() { categoryName = "died_missile" };
    [Tooltip("PlayerDied + DeathCause.LaserGate")]
    public LineCategory died_laser    = new() { categoryName = "died_laser" };
    [Tooltip("PlayerDied + DeathCause.HydraulicCrusher")]
    public LineCategory died_crusher  = new() { categoryName = "died_crusher" };
    [Tooltip("PlayerDied + DeathCause.WallCrash")]
    public LineCategory died_wall     = new() { categoryName = "died_wall" };
    [Tooltip("PlayerDied + DeathCause.Overheat")]
    public LineCategory died_overheat = new() { categoryName = "died_overheat" };
    [Tooltip("PlayerDied + DeathCause.Fuel")]
    public LineCategory died_fuel     = new() { categoryName = "died_fuel" };
    [Tooltip("PlayerDied + any other/unspecified cause")]
    public LineCategory died_generic  = new() { categoryName = "died_generic" };

    [Header("Surge")]
    [Tooltip("MissileSurgeActivated — missile accelerating to close gap.")]
    public LineCategory surge = new() { categoryName = "surge" };

    [Header("Near Finish")]
    [Tooltip("PlayerNearFinish — player approaching the exit.")]
    public LineCategory nearFinish = new() { categoryName = "nearFinish" };

    [Header("Third Crash")]
    [Tooltip("PlayerOnThirdCrash — player has crashed three times.")]
    public LineCategory thirdCrash = new() { categoryName = "thirdCrash" };

    // Public API ----------------------------->

    /// Pick a non-repeating clip from the given category, updating global history.
    /// Returns (null, "") if the category has no assigned clips.

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

    /// <summary>
    /// Returns the death-cause-specific LineCategory for a PlayerDied event.
    /// Falls back to died_generic if the cause has no dedicated pool or the pool is empty.
    /// </summary>
    public LineCategory GetDeathPool(RaceContext.DeathCause cause)
    {
        LineCategory pool = cause switch
        {
            RaceContext.DeathCause.Missile            => died_missile,
            RaceContext.DeathCause.LaserGate          => died_laser,
            RaceContext.DeathCause.HydraulicCrusher   => died_crusher,
            RaceContext.DeathCause.WallCrash          => died_wall,
            RaceContext.DeathCause.Overheat           => died_overheat,
            RaceContext.DeathCause.Fuel               => died_fuel,
            _                                         => died_generic,
        };

        // If the specific pool is empty, fall back to generic
        bool hasClips = pool?.clips != null && pool.clips.Length > 0;
        return hasClips ? pool : died_generic;
    }
}
