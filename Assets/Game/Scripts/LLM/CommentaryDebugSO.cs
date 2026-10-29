using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Live Runtime ScriptableObject for visually monitoring Commentary Mechanics,
/// RaceContext telemetry, Queue cooldowns, and Trigger history in the Unity Inspector.
/// Create asset via: Assets > Create > Config > CommentaryDebug
/// Assign it to RaceContext or CommentaryQueue in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "CommentaryDebug", menuName = "Config/CommentaryDebug")]
public class CommentaryDebugSO : ScriptableObject
{
    [System.Serializable]
    public struct TriggerLogEntry
    {
        public string Time;
        public string Trigger;
        public string Priority;
        public string Details;
    }

    [Header("─── Live Player Telemetry ───")]
    [Tooltip("Current rocket fuel percentage.")]
    public string PlayerFuel = "100%";
    [Tooltip("Current hull integrity percentage.")]
    public string PlayerHull = "100%";
    [Tooltip("Current engine health percentage.")]
    public string PlayerEngineHealth = "100%";
    [Tooltip("Current engine heat temperature percentage.")]
    public string EngineHeat = "0%";
    [Tooltip("Speed trend sampled over time.")]
    public string SpeedTrend = "Steady";
    [Tooltip("Corridor width detection ahead.")]
    public string CorridorAhead = "Open";
    [Tooltip("Level and phase spatial completion.")]
    public string TrackProgress = "Phase 1 (0%)";
    [Tooltip("Number of attempts/deaths in the current phase.")]
    public int PhaseAttempts = 1;

    [Header("─── Missile & Tactical Telemetry ───")]
    [Tooltip("Live 3D distance between player and missile.")]
    public string MissileDistance = "999 units";
    [Tooltip("Closest distance recorded during this run.")]
    public string ClosestDistance = "999 units";
    [Tooltip("Active behavior state of the missile.")]
    public string MissileBehavior = "Idle";
    [Tooltip("Whether the missile has unobstructed line of sight to player.")]
    public bool HasLineOfSight = false;
    [Tooltip("Name of the zone/sector the player is currently inside.")]
    public string PlayerZone = "Start";
    [Tooltip("Name of the zone/sector the missile is currently inside.")]
    public string MissileZone = "Start";

    [Header("─── Queue & Cooldown State ───")]
    [Tooltip("Shared cooldown timer between spoken lines (20s limit).")]
    public string CooldownStatus = "Ready";
    [Tooltip("True while TTS audio is loading or playing.")]
    public bool IsQueueProcessing = false;
    [Tooltip("Number of commentary lines waiting in queue (max depth 2).")]
    public int PendingQueueCount = 0;
    [Tooltip("Whether the last commentary line was Tier 1 (pre-baked) or Tier 2 (live LLM).")]
    public string LastLineTier = "—";
    [Tooltip("Distance to the nearest uncollected fuel pickup.")]
    public string NearestFuelPickup = "999 units";
    [Tooltip("Seconds since the player last collected a fuel pickup.")]
    public string FuelDroughtTimer = "0s";

    [Header("─── Dialogue Cache ───")]
    [TextArea(2, 3)]
    public string LastNovaLine = "";
    [TextArea(2, 3)]
    public string LastMissionControlLine = "";

    [Header("─── Recent Trigger Event Log (Last 10) ───")]
    [SerializeField] private List<TriggerLogEntry> recentTriggers = new List<TriggerLogEntry>();

    // ── Live Update Methods (called from RaceContext & CommentaryQueue) ───────

    public void UpdateSnapshot(RaceContext.GameStateSnapshot s, bool los, string pZone, string mZone, float cooldownRemaining, bool processing, int queueCount)
    {
        PlayerFuel         = $"{s.PlayerFuelNormalized * 100f:F0}%";
        PlayerHull         = $"{s.PlayerHealthNormalized * 100f:F0}%";
        PlayerEngineHealth = $"{s.PlayerEngineHealthNormalized * 100f:F0}%";
        EngineHeat         = $"{s.PlayerEngineHeatNormalized * 100f:F0}%";
        SpeedTrend         = s.PlayerSpeedTrend;
        CorridorAhead      = s.CorridorWidthAhead;
        TrackProgress      = $"{s.currentPhaseName} ({s.phaseNumber}/{s.totalPhases}) - {s.SpatialProgressPercent * 100f:F0}%";
        PhaseAttempts      = s.attemptCount;

        MissileDistance    = $"{s.MissileDistance:F1} units";
        ClosestDistance    = $"{s.ClosestMissileDistance:F1} units";
        MissileBehavior    = s.MissileBehaviorName;
        HasLineOfSight     = los;
        PlayerZone         = pZone;
        MissileZone        = mZone;

        CooldownStatus     = cooldownRemaining <= 0f ? "Ready (0.0s)" : $"Cooling Down ({cooldownRemaining:F1}s)";
        IsQueueProcessing  = processing;
        PendingQueueCount  = queueCount;

        NearestFuelPickup  = s.NearestFuelPickupDistance < 999f ? $"{s.NearestFuelPickupDistance:F0} units" : "none";
        FuelDroughtTimer   = $"{s.TimeSinceLastFuelPickup:F0}s since pickup";

        LastNovaLine           = s.LastNovaLine;
        LastMissionControlLine = s.LastMCLine;
    }

    public void LogTriggerEvent(string triggerName, string priority, string details)
    {
        var entry = new TriggerLogEntry
        {
            Time = $"{Time.time:F1}s",
            Trigger = triggerName,
            Priority = priority,
            Details = details
        };

        recentTriggers.Insert(0, entry);
        if (recentTriggers.Count > 10)
        {
            recentTriggers.RemoveAt(recentTriggers.Count - 1);
        }
    }

    public void ClearLog()
    {
        recentTriggers.Clear();
    }

    // ── Editor Test Buttons (Right-click asset or component in Inspector) ─────

    [ContextMenu("Test Trigger: Low Fuel")]
    public void TestTriggerLowFuel()
    {
        if (RaceContext.Instance != null)
            RaceContext.Instance.SendMessage("FireTrigger", CommentTrigger.PlayerFuelLow, SendMessageOptions.DontRequireReceiver);
    }

    [ContextMenu("Test Trigger: Critical Health")]
    public void TestTriggerCriticalHealth()
    {
        if (RaceContext.Instance != null)
            RaceContext.Instance.SendMessage("FireTrigger", CommentTrigger.PlayerHealthCritical, SendMessageOptions.DontRequireReceiver);
    }

    [ContextMenu("Test Trigger: Missile Surge")]
    public void TestTriggerSurge()
    {
        if (RaceContext.Instance != null)
            RaceContext.Instance.SendMessage("FireTrigger", CommentTrigger.MissileSurgeActivated, SendMessageOptions.DontRequireReceiver);
    }

    [ContextMenu("Test Trigger: Proximity Alert (< 5 units)")]
    public void TestTriggerProximity()
    {
        if (RaceContext.Instance != null)
            RaceContext.Instance.SendMessage("FireTrigger", CommentTrigger.MissileCaughtPlayer, SendMessageOptions.DontRequireReceiver);
    }

    [ContextMenu("Test Trigger: Engine Overheated")]
    public void TestTriggerOverheated()
    {
        if (RaceContext.Instance != null)
            RaceContext.Instance.SendMessage("FireTrigger", CommentTrigger.PlayerEngineOverheated, SendMessageOptions.DontRequireReceiver);
    }

    [ContextMenu("Test Trigger: Missile Spawned")]
    public void TestTriggerMissileSpawned()
    {
        if (RaceContext.Instance != null)
            RaceContext.Instance.SendMessage("FireTrigger", CommentTrigger.MissileSpawnActivated, SendMessageOptions.DontRequireReceiver);
    }

    [ContextMenu("Test Trigger: Player Death Taunt")]
    public void TestTriggerPlayerDeath()
    {
        if (RaceContext.Instance != null)
            RaceContext.Instance.SendMessage("FireTrigger", CommentTrigger.PlayerDied, SendMessageOptions.DontRequireReceiver);
    }

    [ContextMenu("Test Trigger: Level Complete Appraisal")]
    public void TestTriggerLevelComplete()
    {
        if (RaceContext.Instance != null)
            RaceContext.Instance.SendMessage("FireTrigger", CommentTrigger.LevelCompleted, SendMessageOptions.DontRequireReceiver);
    }
}
