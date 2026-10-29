using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Orchestrates the missile: owns the Rigidbody + Unity lifecycle (Awake/Start/FixedUpdate,
/// coroutines, scene/component lookups), and delegates actual logic to three focused helpers:
///   MissileBehaviorState → what state it's in
///   MissileMovement      → how it steers and moves
///   MissileCombat        → catch detection and detonation
/// Keep this file thin — new logic almost always belongs in one of the three helpers.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MissileAI : MonoBehaviour
{
    public static MissileAI Instance { get; private set; }

    [SerializeField] private MissileConfigSO config;
    [SerializeField] private LayerMask wallLayerMask;
    [SerializeField] private Vector3 modelRotationOffset = Vector3.zero;

    [Header("Path Following")]
    [SerializeField] private TrackPath trackPath;
    [SerializeField] private float lookaheadDistance = 3f;
    [SerializeField] private float directChaseThreshold = 4f;

    [Header("Stuck / Loop Escape")]
    [SerializeField] private float stuckCheckWindow = 1.5f;
    [SerializeField] private float stuckDisplacementThreshold = 3f;
    [SerializeField] private float escapeDuration = 1.2f;

    [Header("Debug Visualization")]
    [SerializeField] private bool showDebugRays = true;

    public static event Action OnMissileActivated;
    public static event Action<MissileBehavior> OnBehaviorChanged;
    public static event Action OnMissileCaughtPlayer;
    public static event Action OnMissileHitWall;

    private Rigidbody rb;
    private Transform playerTransform;
    private CollisionHandler playerCollision;
    private SphereCollider catchTrigger;

    private MissileBehaviorState behaviorState;
    private MissileMovement movement;
    private MissileCombat combat;

    private bool isActive;
    private float catchRadiusMultiplierOverride = 1f;
    private float speedMultiplierOverride = 1f;
    private float avoidanceWeightMultiplierOverride = 1f;

    public MissileBehavior CurrentBehavior => behaviorState.Current;
    public float DistanceToPlayer { get; private set; }
    public float CurrentSpeed => movement.CurrentSpeed;
    public MissileConfigSO Config => config;

    /// True while the missile is actively in pursuit (spawn delay has passed).
    public bool IsActive => isActive;

    /// True while the missile is active, has not detonated, and is not in the Retreat state.
    /// Used by voice scripts to gate pursuit commentary.

    public bool IsActivePursuing =>
        isActive &&
        combat    != null && !combat.HasDetonated &&
        behaviorState != null && behaviorState.Current != MissileBehavior.Retreat;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); enabled = false; return; }
        Instance = this;

        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.freezeRotation = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezePositionZ;

        foreach (var col in GetComponents<SphereCollider>())
            if (col.isTrigger) { catchTrigger = col; break; }
        if (catchTrigger == null)
        {
            catchTrigger = gameObject.AddComponent<SphereCollider>();
            catchTrigger.isTrigger = true;
        }

        if (wallLayerMask == 0) wallLayerMask = LayerMask.GetMask("Default");
    }

    private void Start()
    {
        FindPlayer();

        GameManager.OnStateChanged += HandleStateChanged;
        RaceContext.OnCommentTrigger += HandleCommentTrigger;

        UpdateCatchTriggerRadius();

        behaviorState = new MissileBehaviorState(config, playerTransform != null ? playerTransform.position : Vector3.zero);
        behaviorState.OnBehaviorChanged += b => OnBehaviorChanged?.Invoke(b);

        movement = new MissileMovement(transform, config, wallLayerMask, trackPath, showDebugRays,
            lookaheadDistance, directChaseThreshold, stuckCheckWindow, stuckDisplacementThreshold, escapeDuration);

        combat = new MissileCombat(gameObject, config, rb);
        combat.OnCaughtPlayer += () => OnMissileCaughtPlayer?.Invoke();
        combat.OnDetonationStarted += () => { if (Instance == this) Instance = null; };

        StartCoroutine(SpawnDelayCoroutine(config != null ? config.SpawnDelay : 10f));
    }

    private void FindPlayer()
    {
        playerCollision = FindAnyObjectByType<CollisionHandler>();
        if (playerCollision != null)
        {
            playerTransform = playerCollision.transform;
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null) { Debug.LogError("[MissileAI] Player NOT found in scene!"); return; }

        playerTransform = playerObj.transform;
        playerCollision = playerTransform.GetComponent<CollisionHandler>()
            ?? playerTransform.GetComponentInChildren<CollisionHandler>()
            ?? playerTransform.GetComponentInParent<CollisionHandler>();
    }

    private void OnDestroy()
    {
        GameManager.OnStateChanged -= HandleStateChanged;
        RaceContext.OnCommentTrigger -= HandleCommentTrigger;
        if (Instance == this) Instance = null;
    }

    private void FixedUpdate()
    {
        if (!isActive || combat.HasDetonated || playerTransform == null) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        float dt = Time.fixedDeltaTime;
        DistanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        combat.Tick(dt);
        combat.CheckProximityCatch(DistanceToPlayer, playerTransform.position, playerCollision, catchRadiusMultiplierOverride);
        if (combat.HasDetonated) return;

        movement.UpdateTracking(playerTransform.position, dt, movement.CurrentSpeed);
        behaviorState.Evaluate(DistanceToPlayer, playerTransform.position,
            config != null ? config.AttackRange : 10f, dt);

        ApplyMovement(dt);
    }

    private void ApplyMovement(float dt)
    {
        float speedMult = behaviorState.GetSpeedMultiplier() * speedMultiplierOverride;
        float avoidMult = behaviorState.GetWallAvoidanceWeight() * avoidanceWeightMultiplierOverride;

        var (velocity, rotation, hasRotation) = movement.ComputeMotion(
            rb.linearVelocity, rb.rotation, playerTransform.position, behaviorState.Current,
            speedMult, avoidMult,
            config != null ? config.BaseSpeed : 8f,
            config != null ? config.MinSpeed : 4f,
            config != null ? config.MaxSpeed : 18f,
            config != null ? config.FarThreshold : 40f,
            config != null ? config.CloseThreshold : 12f,
            DistanceToPlayer, modelRotationOffset, dt);

        rb.linearVelocity = velocity;
        if (hasRotation) rb.MoveRotation(rotation);
    }

    // SpawnDelayCoroutine is called both from Start (initial spawn) and from
    // ResetToPosition (checkpoint respawn). Delay is always a clean positive float.
    private IEnumerator SpawnDelayCoroutine(float delay)
    {
        isActive = false;
        rb.isKinematic = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Keep missile hidden and disabled during spawn delay
        SetVisualsAndCollidersActive(false);

        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        // Delay elapsed: reveal missile and activate hunt
        SetVisualsAndCollidersActive(true);
        rb.isKinematic = false;
        isActive = true;
        OnMissileActivated?.Invoke();
    }

  
    /// Enables or disables all renderers, particle systems, trails, and colliders on the missile.

    public void SetVisualsAndCollidersActive(bool active)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = active;

        foreach (var c in GetComponentsInChildren<Collider>(true))
            c.enabled = active;

        foreach (var p in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (active) p.Play();
            else { p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        }

        foreach (var t in GetComponentsInChildren<TrailRenderer>(true))
        {
            t.enabled = active;
            if (!active) t.Clear();
        }
    }

    public void ApplyDifficultyOverride(float speedMult, float avoidanceMult, float catchMult)
    {
        speedMultiplierOverride = speedMult;
        avoidanceWeightMultiplierOverride = avoidanceMult;
        catchRadiusMultiplierOverride = catchMult;
        UpdateCatchTriggerRadius();
    }

   
    /// Repositions the missile at a new world position and restarts its spawn
    /// delay countdown. Called by PlayerRespawnHandler on checkpoint respawn.
    /// Reuses the same GameObject so all event subscribers stay intact.

    /// <param name="position">World position to teleport the missile to.</param>
    /// <param name="spawnDelay">Seconds before missile activates (must be >= 0).</param>
    public void ResetToPosition(Vector3 position, float spawnDelay)
    {
        StopAllCoroutines();

        isActive = false;

        // Cancel any pending detonation — re-enables renderers and colliders.
        combat.ResetDetonation();

        // Clear FSM and movement history so no stale state carries over.
        behaviorState.Reset();
        movement.Reset();

        // Teleport.
        rb.isKinematic     = true;
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        transform.position = position;

        // Restart spawn countdown.
        StartCoroutine(SpawnDelayCoroutine(Mathf.Max(0f, spawnDelay)));

        Debug.Log($"[MissileAI] Reset to {position} with delay {spawnDelay}s.");
    }

    public void UpdateCatchTriggerRadius()
    {
        if (catchTrigger != null && config != null)
            catchTrigger.radius = config.CatchRadius * catchRadiusMultiplierOverride;
    }

    /// Called by MissileCollisionHandler when the missile hits a wall.
    public void TriggerWallHit()
    {
        behaviorState.TriggerWallHit(config != null ? config.CrashRecoveryTime : 2.5f);
        OnMissileHitWall?.Invoke();
    }

    private void HandleStateChanged(GameState newState)
    {
        if (newState == GameState.Dead || newState == GameState.LevelComplete)
        {
            isActive = false;
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    private void HandleCommentTrigger(CommentTrigger trigger, RaceContext.GameStateSnapshot _)
    {
        if ((trigger == CommentTrigger.PlayerCollectedFuel || trigger == CommentTrigger.PlayerCollectedRepair) && isActive)
            behaviorState.TriggerPickupRetreat((config != null ? config.CrashRecoveryTime : 2.5f) * 0.5f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showDebugRays || config == null) return;

        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, config.CatchRadius * catchRadiusMultiplierOverride);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, config.AttackRange);

        if (movement != null && Application.isPlaying)
        {
            Vector3 look = movement.GetLookaheadGizmoPoint();
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(look, 0.4f);
            Gizmos.DrawLine(transform.position, look);
        }
    }
#endif
}