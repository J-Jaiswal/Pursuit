using UnityEngine;

/// <summary>
/// Per-level missile tuning parameters. Create one asset per level via
/// Assets > Create > Config > MissileConfig and assign to the MissileAI component.
/// </summary>
[CreateAssetMenu(fileName = "MissileConfig", menuName = "Config/MissileConfig")]
public class MissileConfigSO : ScriptableObject
{
    [Header("Speed")]
    [SerializeField] private float baseSpeed = 8f;
    [SerializeField] private float minSpeed = 4f;
    [SerializeField] private float maxSpeed = 18f;

    [Header("Spawn")]
    [SerializeField] private float spawnDelay = 10f;

    [Header("Catch")]
    [SerializeField] private float catchRadius = 3f;
    [SerializeField] private float catchDamage = 40f;
    [SerializeField] private float catchCooldown = 1.5f;

    [Header("Wall Hit")]
    [SerializeField] private float crashRecoveryTime = 2.5f;
    [SerializeField] private float blastRadius = 5f;
    [SerializeField] private float blastDamage = 20f;

    [Header("Rubber-Band Thresholds")]
    [SerializeField] private float farThreshold = 40f;
    [SerializeField] private float closeThreshold = 12f;

    [Header("Respawn")]
    [SerializeField] private float gracePeriodAfterRespawn = 5f;
    [SerializeField] private float respawnBehindDistance = 40f;

    [Header("Corridor Detection")]
    [SerializeField] private float tightCorridorThreshold = 8f;

    [Header("Corner Trap")]
    [SerializeField] private float cornerTrapDwellTime = 3f;
    [SerializeField] private float cornerTrapMovementThreshold = 5f;

    [Header("Wall Avoidance")]
    [SerializeField] private float wallAvoidanceRayLength = 5f;

    [Header("Behavior — Speed Multipliers")]
    [Tooltip("HOLD: 1.0 = base speed")]
    [SerializeField] private float holdSpeedMultiplier = 1.0f;
    [Tooltip("SURGE: 1.8 = fast and aggressive")]
    [SerializeField] private float surgeSpeedMultiplier = 1.8f;
    [Tooltip("RETREAT: pulls back at 1.2x")]
    [SerializeField] private float retreatSpeedMultiplier = 1.2f;
    [Tooltip("CORNER_TRAP: 1.4x predictive approach")]
    [SerializeField] private float cornerTrapSpeedMultiplier = 1.4f;

    [Header("Behavior — Wall Avoidance Weights")]
    [Tooltip("HOLD: full avoidance")]
    [SerializeField] private float holdWallAvoidance = 1.0f;
    [Tooltip("SURGE: reduced — reckless approach")]
    [SerializeField] private float surgeWallAvoidance = 0.6f;
    [Tooltip("STALK: careful — enhanced avoidance")]
    [SerializeField] private float stalkWallAvoidance = 1.2f;
    [Tooltip("RETREAT: full avoidance while pulling back")]
    [SerializeField] private float retreatWallAvoidance = 1.0f;
    [Tooltip("CORNER_TRAP: slightly reduced for cutting corners")]
    [SerializeField] private float cornerTrapWallAvoidance = 0.8f;

    // ── Public Accessors ──────────────────────────────────────────────────────

    public float BaseSpeed => baseSpeed;
    public float MinSpeed => minSpeed;
    public float MaxSpeed => maxSpeed;
    public float SpawnDelay => spawnDelay;
    public float CatchRadius => catchRadius;
    public float CatchDamage => catchDamage;
    public float CatchCooldown => catchCooldown;
    public float CrashRecoveryTime => crashRecoveryTime;
    public float BlastRadius => blastRadius;
    public float BlastDamage => blastDamage;
    public float FarThreshold => farThreshold;
    public float CloseThreshold => closeThreshold;
    public float GracePeriodAfterRespawn => gracePeriodAfterRespawn;
    public float RespawnBehindDistance => respawnBehindDistance;
    public float TightCorridorThreshold => tightCorridorThreshold;
    public float CornerTrapDwellTime => cornerTrapDwellTime;
    public float CornerTrapMovementThreshold => cornerTrapMovementThreshold;
    public float WallAvoidanceRayLength => wallAvoidanceRayLength;

    public float HoldSpeedMultiplier => holdSpeedMultiplier;
    public float SurgeSpeedMultiplier => surgeSpeedMultiplier;
    public float RetreatSpeedMultiplier => retreatSpeedMultiplier;
    public float CornerTrapSpeedMultiplier => cornerTrapSpeedMultiplier;

    public float HoldWallAvoidance => holdWallAvoidance;
    public float SurgeWallAvoidance => surgeWallAvoidance;
    public float StalkWallAvoidance => stalkWallAvoidance;
    public float RetreatWallAvoidance => retreatWallAvoidance;
    public float CornerTrapWallAvoidance => cornerTrapWallAvoidance;
}
