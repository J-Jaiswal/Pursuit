using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Core missile AI. Handles physics movement (player homing + wall avoidance),
/// the five-state behavior machine, rubber-banding, catch detection, and
/// respawn reset. All behavior decisions are deterministic — no LLM involvement.
///
/// SETUP:
///   1. Add this component and a Rigidbody to the Missile GameObject.
///   2. Put Missile on a "Missile" Physics Layer.
///   3. In Physics Settings > Layer Collision Matrix, uncheck Missile × Player
///      (so the missile doesn't physically push the player).
///   4. Set wallLayerMask to include only your wall/terrain layers.
///   5. Assign a MissileConfigSO asset.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MissileAI : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static MissileAI Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [SerializeField] private MissileConfigSO config;

    [Tooltip("Layer mask for wall raycast detection. Should include walls/terrain but NOT Player or Missile layers.")]
    [SerializeField] private LayerMask wallLayerMask;

    [Tooltip("Correction offset in degrees for the 3D model orientation (e.g. if model faces sideways/backwards).")]
    [SerializeField] private Vector3 modelRotationOffset = Vector3.zero;

    // ── Static Events (subscribed by RaceContext, MissileVoice, etc.) ─────────
    public static event Action                  OnMissileActivated;
    public static event Action<MissileBehavior> OnBehaviorChanged;
    public static event Action                  OnMissileCaughtPlayer;
    public static event Action                  OnMissileHitWall;
    public static event Action                  OnMissileReset;

    // ── State ─────────────────────────────────────────────────────────────────
    private Rigidbody   rb;
    private Transform   playerTransform;
    private GameState   previousGameState = GameState.Playing;

    private MissileBehavior currentBehavior = MissileBehavior.Hold;
    private float           behaviorTimer;          // countdown for RETREAT state
    private bool            isActive;               // false until after spawn delay
    private bool            isGrace;                // grace period after respawn

    private Collider[]  colliders;
    private Renderer[]  renderers;

    // Corner-trap tracking
    private float   cornerTrapTimer;
    private Vector3 lastPlayerPositionSample;
    private float   positionSampleTimer;

    // Catch cooldown (prevents rapid successive catches)
    private float catchCooldownRemaining;

    // ── Properties ────────────────────────────────────────────────────────────
    public MissileBehavior CurrentBehavior   => currentBehavior;
    public float           DistanceToPlayer  { get; private set; }
    public float           CurrentSpeed      { get; private set; }

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        rb = GetComponent<Rigidbody>();
        rb.useGravity      = false;
        rb.freezeRotation  = false; // missile rotates to face movement direction
        rb.interpolation   = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints     = RigidbodyConstraints.FreezePositionZ;

        colliders = GetComponentsInChildren<Collider>();
        renderers = GetComponentsInChildren<Renderer>();

        // Default wall layer mask to "Default" if not set
        if (wallLayerMask == 0)
            wallLayerMask = LayerMask.GetMask("Default");
    }

    private void SetVisualsAndCollisionActive(bool active)
    {
        if (colliders != null)
        {
            foreach (var col in colliders)
            {
                if (col != null) col.enabled = active;
            }
        }
        if (renderers != null)
        {
            foreach (var r in renderers)
            {
                if (r != null) r.enabled = active;
            }
        }
    }

    private void Start()
    {
        // Find player by tag — missile is a prefab so we resolve at runtime
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;

        GameManager.OnStateChanged    += HandleStateChanged;
        RaceContext.OnCommentTrigger  += HandleCommentTrigger;

        StartCoroutine(SpawnDelayCoroutine());
    }

    private void OnDestroy()
    {
        GameManager.OnStateChanged    -= HandleStateChanged;
        RaceContext.OnCommentTrigger  -= HandleCommentTrigger;

        if (Instance == this) Instance = null;
    }

    private void FixedUpdate()
    {
        if (!isActive || playerTransform == null) return;
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;
        if (isGrace) return;

        DistanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // Decrement catch cooldown
        if (catchCooldownRemaining > 0f)
            catchCooldownRemaining -= Time.fixedDeltaTime;

        EvaluateState();
        ApplyMovement();

        if (catchCooldownRemaining <= 0f)
            CheckCatch();
    }

    // ── Spawn & Respawn ───────────────────────────────────────────────────────

    private IEnumerator SpawnDelayCoroutine()
    {
        rb.isKinematic = true;
        yield return new WaitForSeconds(config != null ? config.SpawnDelay : 10f);
        rb.isKinematic = false;
        isActive       = true;
        OnMissileActivated?.Invoke();
    }

    /// <summary>
    /// Called when player respawns at checkpoint.
    /// Teleports missile 40 units behind checkpoint, resets all state, starts grace period.
    /// </summary>
    public void ResetMissile(Vector3 checkpointPosition, float gracePeriod)
    {
        StopAllCoroutines();

        float behind = config != null ? config.RespawnBehindDistance : 40f;
        // Keep in the same Z plane as checkpoint, offset in X-Y plane (going opposite to default heading of travel +Y)
        Vector3 spawnPos = checkpointPosition - Vector3.up * behind;
        spawnPos.z = checkpointPosition.z;
        transform.position = spawnPos;
        transform.rotation = Quaternion.identity;

        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic     = true;

        SetBehavior(MissileBehavior.Hold);
        cornerTrapTimer          = 0f;
        catchCooldownRemaining   = 0f;
        behaviorTimer            = 0f;

        isActive = false;
        isGrace  = true;

        SetVisualsAndCollisionActive(true); // Re-enable visuals and collision

        StartCoroutine(GracePeriodCoroutine(gracePeriod));
        OnMissileReset?.Invoke();
    }

    private IEnumerator GracePeriodCoroutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        rb.isKinematic = false;
        isGrace        = false;
        isActive       = true;
    }

    // ── State Machine ─────────────────────────────────────────────────────────

    private void EvaluateState()
    {
        // RETREAT is timer-driven — don't override it while active
        if (currentBehavior == MissileBehavior.Retreat)
        {
            behaviorTimer -= Time.fixedDeltaTime;
            if (behaviorTimer <= 0f)
                SetBehavior(MissileBehavior.Hold);
            return;
        }

        // Read live player stats from RaceContext (safe null-checks for compile)
        float  playerFuel   = RaceContext.Instance != null ? RaceContext.Instance.PlayerFuelNormalized   : 1f;
        float  playerHealth = RaceContext.Instance != null ? RaceContext.Instance.PlayerHealthNormalized : 1f;
        bool   isTight      = RaceContext.Instance != null && RaceContext.Instance.CorridorWidthAhead == "tight";

        UpdateCornerTrapTracking();

        MissileBehavior target;

        if (playerFuel < 0.25f || playerHealth < 0.30f || isTight)
            target = MissileBehavior.Surge;
        else if (DistanceToPlayer < 15f)
            target = MissileBehavior.Stalk;
        else if (cornerTrapTimer >= (config != null ? config.CornerTrapDwellTime : 3f))
            target = MissileBehavior.CornerTrap;
        else
            target = MissileBehavior.Hold;

        if (target != currentBehavior)
            SetBehavior(target);
    }

    private void SetBehavior(MissileBehavior newBehavior)
    {
        currentBehavior = newBehavior;
        OnBehaviorChanged?.Invoke(newBehavior);
    }

    private void UpdateCornerTrapTracking()
    {
        positionSampleTimer += Time.fixedDeltaTime;
        if (positionSampleTimer < 1f) return;

        positionSampleTimer = 0f;
        float threshold     = config != null ? config.CornerTrapMovementThreshold : 5f;
        float moved         = Vector3.Distance(playerTransform.position, lastPlayerPositionSample);

        if (moved < threshold)
            cornerTrapTimer += 1f;
        else
            cornerTrapTimer = 0f;

        lastPlayerPositionSample = playerTransform.position;
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    private void ApplyMovement()
    {
        Vector3 wallAvoidance = ComputeWallAvoidance();
        float   avoidWeight   = GetWallAvoidanceWeight();

        Vector3 toPlayer = (playerTransform.position - transform.position).normalized;
        toPlayer.z = 0f;
        toPlayer.Normalize();

        Vector3 moveDir;

        if (currentBehavior == MissileBehavior.Retreat)
        {
            // Move away from player — still use wall avoidance to not get stuck
            Vector3 awayFromPlayer = -toPlayer;
            moveDir = (awayFromPlayer + wallAvoidance * avoidWeight).normalized;
        }
        else
        {
            moveDir = (toPlayer + wallAvoidance * avoidWeight).normalized;
        }

        moveDir.z = 0f;
        moveDir.Normalize();

        float speed = ComputeSpeed();
        CurrentSpeed    = speed;
        Vector3 targetVelocity = new Vector3(moveDir.x * speed, moveDir.y * speed, 0f);
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, targetVelocity, Time.fixedDeltaTime * 10f);

        // Smoothly rotate missile to face movement direction in 2D (LookRotation on X-Y plane)
        if (moveDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up) * Quaternion.Euler(modelRotationOffset);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, Time.fixedDeltaTime * 8f));
        }
    }

    private Vector3 ComputeWallAvoidance()
    {
        Vector3 avoidance = Vector3.zero;
        float   rayLen    = config != null ? config.WallAvoidanceRayLength : 5f;
        float   fwdLen    = rayLen * 1.5f; // longer forward ray for anticipation

        (Vector3 dir, float len)[] rays =
        {
            ( transform.right,    rayLen),
            (-transform.right,    rayLen),
            ( transform.up,       rayLen),
            (-transform.up,       rayLen),
            ( transform.forward,  fwdLen),
        };

        foreach (var (dir, len) in rays)
        {
            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, len, wallLayerMask))
            {
                float proximity = 1f - (hit.distance / len); // 0 = far, 1 = wall touching
                avoidance += -dir * proximity;
            }
        }

        return avoidance;
    }

    private float ComputeSpeed()
    {
        float baseMult = GetSpeedMultiplier();

        float speed;
        if (currentBehavior == MissileBehavior.Stalk)
        {
            // Match player speed for menacing stalking feel
            float playerSpeed = RaceContext.Instance != null ? RaceContext.Instance.PlayerSpeed : 5f;
            speed = Mathf.Max(playerSpeed, config != null ? config.MinSpeed : 4f);
        }
        else
        {
            speed = (config != null ? config.BaseSpeed : 8f) * baseMult;
        }

        return ApplyRubberBand(speed);
    }

    private float ApplyRubberBand(float speed)
    {
        float far   = config != null ? config.FarThreshold   : 40f;
        float close = config != null ? config.CloseThreshold : 12f;

        if (DistanceToPlayer > far)
            speed *= 1.15f;
        else if (DistanceToPlayer < close)
            speed *= 0.85f;

        float min = config != null ? config.MinSpeed : 4f;
        float max = config != null ? config.MaxSpeed : 18f;
        return Mathf.Clamp(speed, min, max);
    }

    private float GetSpeedMultiplier()
    {
        if (config == null) return 1f;
        return currentBehavior switch
        {
            MissileBehavior.Hold       => config.HoldSpeedMultiplier,
            MissileBehavior.Surge      => config.SurgeSpeedMultiplier,
            MissileBehavior.Retreat    => config.RetreatSpeedMultiplier,
            MissileBehavior.CornerTrap => config.CornerTrapSpeedMultiplier,
            MissileBehavior.Stalk      => 1f, // speed handled separately in ComputeSpeed
            _                          => 1f,
        };
    }

    private float GetWallAvoidanceWeight()
    {
        if (config == null) return 1f;
        return currentBehavior switch
        {
            MissileBehavior.Hold       => config.HoldWallAvoidance,
            MissileBehavior.Surge      => config.SurgeWallAvoidance,
            MissileBehavior.Stalk      => config.StalkWallAvoidance,
            MissileBehavior.Retreat    => config.RetreatWallAvoidance,
            MissileBehavior.CornerTrap => config.CornerTrapWallAvoidance,
            _                          => 1f,
        };
    }

    // ── Catch Detection ───────────────────────────────────────────────────────

    private void CheckCatch()
    {
        float catchR = config != null ? config.CatchRadius : 3f;
        if (DistanceToPlayer > catchR) return;

        // Deal damage via CollisionHandler
        CollisionHandler ch = playerTransform.GetComponent<CollisionHandler>();
        if (ch != null)
        {
            // Deal lethal damage to kill player instantly
            ch.TakeDamage(99999f, transform.position);
        }

        CameraShake.Shake(0.8f, 0.35f);
        OnMissileCaughtPlayer?.Invoke();

        // Deactivate visuals/collision, disable tracking, and stop physics movement
        rb.linearVelocity = Vector3.zero;
        isActive = false;
        SetVisualsAndCollisionActive(false);
    }

    // ── External Triggers (called by MissileCollisionHandler) ─────────────────

    /// <summary>Triggered by MissileCollisionHandler when missile hits a wall.</summary>
    public void TriggerWallHit()
    {
        if (currentBehavior == MissileBehavior.Retreat) return; // already recovering

        SetBehavior(MissileBehavior.Retreat);
        behaviorTimer = config != null ? config.CrashRecoveryTime : 2.5f;
        OnMissileHitWall?.Invoke();
    }

    // ── Event Handlers ────────────────────────────────────────────────────────

    private void HandleStateChanged(GameState newState)
    {
        bool wasDeadWithCheckpoint = previousGameState == GameState.Dead
                                     && LevelManager.Instance != null
                                     && LevelManager.Instance.HasCheckpoint;

        if (newState == GameState.Playing && wasDeadWithCheckpoint)
        {
            // Player has already been teleported to checkpoint — use their current position
            Vector3 checkpointPos = playerTransform != null
                ? playerTransform.position
                : transform.position;

            float grace = config != null ? config.GracePeriodAfterRespawn : 5f;
            ResetMissile(checkpointPos, grace);
        }
        else if (newState == GameState.Dead || newState == GameState.LevelComplete)
        {
            isActive          = false;
            isGrace           = false;
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic    = true;
        }

        previousGameState = newState;
    }

    private void HandleCommentTrigger(CommentTrigger trigger, RaceContext.GameStateSnapshot _)
    {
        // Pickup collection → brief retreat (false security per GDD)
        if (trigger == CommentTrigger.PlayerCollectedFuel || trigger == CommentTrigger.PlayerCollectedRepair)
        {
            if (currentBehavior != MissileBehavior.Retreat && isActive)
            {
                SetBehavior(MissileBehavior.Retreat);
                float fullRecovery = config != null ? config.CrashRecoveryTime : 2.5f;
                behaviorTimer = fullRecovery * 0.5f; // half-length retreat for pickup
            }
        }
    }
}
