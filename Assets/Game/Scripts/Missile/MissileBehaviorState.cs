using System;
using UnityEngine;

/// <summary>
/// Owns the missile's behavior state machine (Hold/Surge/Stalk/Retreat/CornerTrap/Attack)
/// and the per-state speed/avoidance tuning lookups. Pure C# � no MonoBehaviour � so
/// MissileAI updates it explicitly each FixedUpdate rather than relying on Unity's
/// script execution order between components.
/// </summary>
public class MissileBehaviorState
{
    private readonly MissileConfigSO config;
    public event Action<MissileBehavior> OnBehaviorChanged;

    public MissileBehavior Current { get; private set; } = MissileBehavior.Hold;

    private float behaviorTimer;
    private float cornerTrapTimer;
    private float positionSampleTimer;
    private Vector3 lastPlayerPositionSample;

    public MissileBehaviorState(MissileConfigSO config, Vector3 initialPlayerPos)
    {
        this.config = config;
        lastPlayerPositionSample = initialPlayerPos;
    }

    /// <summary>Runs one FSM step. Call every FixedUpdate while the missile is active.</summary>
    public void Evaluate(float distanceToPlayer, Vector3 playerPos, float attackRange, float dt)
    {
        if (distanceToPlayer <= attackRange)
        {
            SetBehavior(MissileBehavior.Attack);
            return;
        }

        if (Current == MissileBehavior.Retreat)
        {
            behaviorTimer -= dt;
            if (behaviorTimer <= 0f) SetBehavior(MissileBehavior.Hold);
            return;
        }

        float playerFuel = RaceContext.Instance != null ? RaceContext.Instance.PlayerFuelNormalized : 1f;
        float playerHealth = RaceContext.Instance != null ? RaceContext.Instance.PlayerHealthNormalized : 1f;
        bool isTight = RaceContext.Instance != null && RaceContext.Instance.CorridorWidthAhead == "tight";

        UpdateCornerTrapTracking(playerPos, dt);

        MissileBehavior target;
        if (playerFuel < 0.25f || playerHealth < 0.30f || isTight)
            target = MissileBehavior.Surge;
        else if (distanceToPlayer < 15f)
            target = MissileBehavior.Stalk;
        else if (cornerTrapTimer >= (config != null ? config.CornerTrapDwellTime : 3f))
            target = MissileBehavior.CornerTrap;
        else
            target = MissileBehavior.Hold;

        if (target != Current) SetBehavior(target);
    }

    /// <summary>Called by MissileAI when a wall collision is reported.</summary>
    public void TriggerWallHit(float crashRecoveryTime)
    {
        if (Current == MissileBehavior.Retreat) return;
        SetBehavior(MissileBehavior.Retreat);
        behaviorTimer = crashRecoveryTime;
    }

    /// <summary>Called when the player picks up fuel/repair � briefly backs off.</summary>
    public void TriggerPickupRetreat(float halfRecoveryTime)
    {
        if (Current == MissileBehavior.Retreat) return;
        SetBehavior(MissileBehavior.Retreat);
        behaviorTimer = halfRecoveryTime;
    }

    private void SetBehavior(MissileBehavior newBehavior)
    {
        Current = newBehavior;
        OnBehaviorChanged?.Invoke(newBehavior);
    }

    private void UpdateCornerTrapTracking(Vector3 playerPos, float dt)
    {
        positionSampleTimer += dt;
        if (positionSampleTimer < 1f) return;

        positionSampleTimer = 0f;
        float threshold = config != null ? config.CornerTrapMovementThreshold : 5f;
        float moved = Vector3.Distance(playerPos, lastPlayerPositionSample);

        cornerTrapTimer = moved < threshold ? cornerTrapTimer + 1f : 0f;
        lastPlayerPositionSample = playerPos;
    }

    public float GetSpeedMultiplier()
    {
        if (config == null) return 1f;
        return Current switch
        {
            MissileBehavior.Hold => config.HoldSpeedMultiplier,
            MissileBehavior.Surge => config.SurgeSpeedMultiplier,
            MissileBehavior.Retreat => config.RetreatSpeedMultiplier,
            MissileBehavior.CornerTrap => config.CornerTrapSpeedMultiplier,
            MissileBehavior.Stalk => 1f,
            MissileBehavior.Attack => config.SurgeSpeedMultiplier * 1.2f,
            _ => 1f,
        };
    }

    public float GetWallAvoidanceWeight()
    {
        if (config == null) return 1f;
        return Current switch
        {
            MissileBehavior.Hold => config.HoldWallAvoidance,
            MissileBehavior.Surge => config.SurgeWallAvoidance,
            MissileBehavior.Stalk => config.StalkWallAvoidance,
            MissileBehavior.Retreat => config.RetreatWallAvoidance,
            MissileBehavior.CornerTrap => config.CornerTrapWallAvoidance,
            MissileBehavior.Attack => 0.4f,
            _ => 1f,
        };
    }

    /// <summary>
    /// Resets the behavior state machine to its default Hold state and clears
    /// all internal timers. Called by MissileAI when the missile is repositioned
    /// at a checkpoint respawn so no stale surge/corner-trap state carries over.
    /// </summary>
    public void Reset()
    {
        Current             = MissileBehavior.Hold;
        behaviorTimer       = 0f;
        cornerTrapTimer     = 0f;
        positionSampleTimer = 0f;
        // OnBehaviorChanged is NOT fired — missile is not yet active at this point.
    }
}