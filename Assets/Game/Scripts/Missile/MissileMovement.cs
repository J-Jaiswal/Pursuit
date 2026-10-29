using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles "how the missile moves": wall-avoidance raycasts, choosing a steer target
/// (path lookahead vs. direct-to-player), speed/rubber-banding, and the stuck/loop-escape
///  MissileAI calls ComputeMotion() each FixedUpdate and applies the
/// returned velocity/rotation to its own Rigidbody.
/// </summary>
public class MissileMovement
{
    private readonly Transform self;
    private readonly MissileConfigSO config;
    private readonly LayerMask wallLayerMask;
    private readonly TrackPath trackPath;
    private readonly bool showDebugRays;
    [SerializeField] private bool verboseLogging = false;

    private readonly float lookaheadDistance;
    private readonly float directChaseThreshold;
    private readonly float stuckCheckWindow;
    private readonly float stuckDisplacementThreshold;
    private readonly float escapeDuration;

    private float missileProgress;
    private float playerProgress;

    private readonly List<(float time, Vector3 pos)> posHistory = new();
    private bool isEscaping;
    private float escapeTimer;
    private Vector3 escapeDir;
    private float lastLoggedTime;

    public float CurrentSpeed { get; private set; }

    public MissileMovement(Transform self, MissileConfigSO config, LayerMask wallLayerMask, TrackPath trackPath,
        bool showDebugRays, float lookaheadDistance, float directChaseThreshold,
        float stuckCheckWindow, float stuckDisplacementThreshold, float escapeDuration)
    {
        this.self = self;
        this.config = config;
        this.wallLayerMask = wallLayerMask;
        this.trackPath = trackPath;
        this.showDebugRays = showDebugRays;
        this.lookaheadDistance = lookaheadDistance;
        this.directChaseThreshold = directChaseThreshold;
        this.stuckCheckWindow = stuckCheckWindow;
        this.stuckDisplacementThreshold = stuckDisplacementThreshold;
        this.escapeDuration = escapeDuration;

        if (trackPath != null)
            missileProgress = trackPath.GetProgress(self.position, 0f);
    }

    /// <summary>Call once per FixedUpdate before ComputeMotion — updates path progress and stuck detection.</summary>
    public void UpdateTracking(Vector3 playerPos, float dt, float expectedSpeed)
    {
        if (trackPath != null)
        {
            missileProgress = trackPath.GetProgress(self.position, missileProgress);
            playerProgress = trackPath.GetProgress(playerPos, playerProgress);
        }
        UpdateStuckTracking(dt, expectedSpeed);
    }

    /// <summary>Computes the final world-space velocity + facing rotation for this step.</summary>
    public (Vector3 velocity, Quaternion rotation, bool hasRotation) ComputeMotion(
        Vector3 currentVelocity, Quaternion currentRotation, Vector3 playerPos,
        MissileBehavior behavior, float speedMult, float avoidWeightMult,
        float baseSpeed, float minSpeed, float maxSpeed, float farThreshold, float closeThreshold,
        float distanceToPlayer, Vector3 modelRotationOffsetEuler, float dt)
    {
        Vector3 wallAvoidance = ComputeWallAvoidance();

        // Escaping a loop trap overrides everything else — aim purely at open space.
        if (isEscaping)
        {
            Vector3 escMove = (escapeDir + wallAvoidance * 0.5f).normalized;
            escMove.z = 0f;
            float escSpeed = ComputeSpeed(behavior, speedMult, baseSpeed, minSpeed, maxSpeed,
                                           farThreshold, closeThreshold, distanceToPlayer);
            Vector3 escVel = Vector3.Lerp(currentVelocity, escMove * escSpeed, dt * 10f);

            bool escHasRot = escMove.sqrMagnitude > 0.01f;
            Quaternion escRot = escHasRot
                ? Quaternion.Slerp(currentRotation,
                    Quaternion.LookRotation(escMove, Vector3.up) * Quaternion.Euler(modelRotationOffsetEuler), dt * 8f)
                : currentRotation;

            return (escVel, escRot, escHasRot);
        }

        // Decide steer target: direct chase is only safe if the player is BOTH close
        // along the path AND actually visible in a straight line (no wall between us).
        // In spirals/curves, "close in waypoint index" does not mean "line of sight is clear",
        // so we must check visibility explicitly instead of assuming it.
        float progressDiff = playerProgress - missileProgress;
        float pathDirection = Mathf.Sign(progressDiff); // +1 if player is ahead in the list, -1 if behind

        bool closeAlongPath = trackPath == null || Mathf.Abs(progressDiff) <= directChaseThreshold;
        bool playerVisible = HasLineOfSight(playerPos);

        Vector3 steerTarget;
        if (closeAlongPath && playerVisible)
        {
            steerTarget = playerPos; // safe direct chase
        }
        else if (trackPath != null)
        {
            steerTarget = GetVisibleLookaheadPoint(pathDirection); // follow the path, visibility-clamped
        }
        else
        {
            steerTarget = playerPos; // no path asset at all — nothing else to steer at
        }

        // --- DEBUG LOGGING (throttled to ~4x/sec so it doesn't flood the console) ---
        if (verboseLogging && Time.time - lastLoggedTime > 0.25f)
        {
            lastLoggedTime = Time.time;
            Debug.Log($"[MissileMovement] missileProg={missileProgress:F2} playerProg={playerProgress:F2} " +
                      $"diff={progressDiff:F2} dir={pathDirection:F0} closeAlongPath={closeAlongPath} " +
                      $"playerVisible={playerVisible} steerTarget={steerTarget} missilePos={self.position} " +
                      $"playerPos={playerPos}");
        }
        // --- END DEBUG LOGGING ---

        Vector3 toTarget = (steerTarget - self.position).normalized;
        toTarget.z = 0f;
        toTarget.Normalize();

        Vector3 moveDir = behavior == MissileBehavior.Retreat
            ? (-toTarget + wallAvoidance * avoidWeightMult).normalized
            : (toTarget + wallAvoidance * avoidWeightMult).normalized;
        moveDir.z = 0f;
        moveDir.Normalize();

        float speed = ComputeSpeed(behavior, speedMult, baseSpeed, minSpeed, maxSpeed,
                                    farThreshold, closeThreshold, distanceToPlayer);
        CurrentSpeed = speed;

        Vector3 velocity = Vector3.Lerp(currentVelocity, moveDir * speed, dt * 10f);
        bool hasRotation = moveDir.sqrMagnitude > 0.01f;
        Quaternion rotation = hasRotation
            ? Quaternion.Slerp(currentRotation,
                Quaternion.LookRotation(moveDir, Vector3.up) * Quaternion.Euler(modelRotationOffsetEuler), dt * 8f)
            : currentRotation;

        return (velocity, rotation, hasRotation);
    }

    /// <summary>For gizmo drawing — where the missile is currently aiming along the path.</summary>
    public Vector3 GetLookaheadGizmoPoint() =>
        trackPath != null ? trackPath.GetLookaheadPoint(missileProgress, lookaheadDistance) : self.position;

    /// <summary>True if a straight line from the missile to `target` is unobstructed by a wall.</summary>
    private bool HasLineOfSight(Vector3 target)
    {
        Vector3 toTarget = target - self.position;
        float dist = toTarget.magnitude;
        if (dist < 0.01f) return true;
        return !Physics.Raycast(self.position, toTarget.normalized, dist, wallLayerMask);
    }

    /// <summary>
    /// Walks the lookahead distance back toward the missile's own path progress until it
    /// finds a point that's actually reachable in a straight line — prevents steering at
    /// a lookahead point that's on the other side of a wall (common in spirals/curves).
    /// </summary>
    private Vector3 GetVisibleLookaheadPoint(float pathDirection)
    {
        float step = lookaheadDistance / 4f;

        for (float dist = lookaheadDistance; dist > step * 0.5f; dist -= step)
        {
            Vector3 candidate = trackPath.GetLookaheadPoint(missileProgress, dist * pathDirection);
            if (HasLineOfSight(candidate)) return candidate;
        }

        // Even the shortest lookahead was blocked — fall back to a point right on top of
        // the missile's own progress, which should always be visible.
        return trackPath.GetLookaheadPoint(missileProgress, 0.5f * pathDirection);
    }

    private float ComputeSpeed(MissileBehavior behavior, float speedMult, float baseSpeed,
        float minSpeed, float maxSpeed, float farThreshold, float closeThreshold, float distanceToPlayer)
    {
        float speed;
        if (behavior == MissileBehavior.Stalk)
        {
            float playerSpeed = RaceContext.Instance != null ? RaceContext.Instance.PlayerSpeed : 5f;
            speed = Mathf.Max(playerSpeed * speedMult, minSpeed);
        }
        else
        {
            speed = baseSpeed * speedMult;
        }

        if (distanceToPlayer > farThreshold) speed *= 1.15f;
        else if (distanceToPlayer < closeThreshold) speed *= 0.85f;

        return Mathf.Clamp(speed, minSpeed, maxSpeed);
    }

    private Vector3 ComputeWallAvoidance()
    {
        Vector3 avoidance = Vector3.zero;
        float rayLen = config != null ? config.WallAvoidanceRayLength : 5f;
        float fwdLen = rayLen * 1.5f;

        Vector3 right = new Vector3(self.right.x, self.right.y, 0f).normalized;
        Vector3 up = new Vector3(self.up.x, self.up.y, 0f).normalized;
        Vector3 fwd = new Vector3(self.forward.x, self.forward.y, 0f).normalized;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        if (up.sqrMagnitude < 0.001f) up = Vector3.up;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.up;

        Vector3 upRight = (right + up).normalized;
        Vector3 upLeft = (-right + up).normalized;
        Vector3 downRight = (right - up).normalized;
        Vector3 downLeft = (-right - up).normalized;

        (Vector3 dir, float len)[] rays =
        {
            (right, rayLen), (-right, rayLen), (up, rayLen), (-up, rayLen),
            (upRight, rayLen), (upLeft, rayLen), (downRight, rayLen), (downLeft, rayLen),
            (fwd, fwdLen),
        };

        foreach (var (dir, len) in rays)
        {
            if (Physics.Raycast(self.position, dir, out RaycastHit hit, len, wallLayerMask))
            {
                float proximity = 1f - (hit.distance / len);
                avoidance += -dir * proximity;
                if (showDebugRays)
                {
                    Debug.DrawRay(self.position, dir * hit.distance, Color.red);
                    Debug.DrawRay(hit.point, hit.normal * 0.5f, Color.yellow);
                }
            }
            else if (showDebugRays)
            {
                Debug.DrawRay(self.position, dir * len, Color.green);
            }
        }

        if (showDebugRays && avoidance.sqrMagnitude > 0.001f)
            Debug.DrawRay(self.position, avoidance * 2f, Color.cyan);

        return avoidance;
    }

    private void UpdateStuckTracking(float dt, float expectedSpeed)
    {
        posHistory.Add((Time.time, self.position));
        while (posHistory.Count > 0 && Time.time - posHistory[0].time > stuckCheckWindow)
            posHistory.RemoveAt(0);

        if (isEscaping)
        {
            escapeTimer -= dt;
            if (escapeTimer <= 0f) isEscaping = false;
            return;
        }

        if (posHistory.Count < 2) return;

        float netDisplacement = Vector3.Distance(posHistory[0].pos, self.position);
        float expectedTravel = expectedSpeed * stuckCheckWindow;

        if (expectedTravel > 1f && netDisplacement < stuckDisplacementThreshold)
            BeginEscape();
    }

    private void BeginEscape()
    {
        isEscaping = true;
        escapeTimer = escapeDuration;
        escapeDir = FindClearestDirection();
        posHistory.Clear();
        if (verboseLogging)
            Debug.Log($"[MissileMovement] Loop trap detected — forcing escape along {escapeDir}");
    }

    private Vector3 FindClearestDirection()
    {
        float rayLen = config != null ? config.WallAvoidanceRayLength * 2f : 10f;
        Vector3 right = new Vector3(self.right.x, self.right.y, 0f).normalized;
        Vector3 up = new Vector3(self.up.x, self.up.y, 0f).normalized;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        if (up.sqrMagnitude < 0.001f) up = Vector3.up;

        Vector3[] dirs =
        {
            right, -right, up, -up,
            (right + up).normalized, (-right + up).normalized,
            (right - up).normalized, (-right - up).normalized
        };

        Vector3 best = Vector3.right;
        float bestDist = -1f;
        foreach (var dir in dirs)
        {
            float dist = Physics.Raycast(self.position, dir, out RaycastHit hit, rayLen, wallLayerMask)
                ? hit.distance : rayLen;
            if (dist > bestDist) { bestDist = dist; best = dir; }
        }
        return best;
    }

    /// <summary>
    /// Clears stuck-detection history and any active escape manoeuvre.
    /// Call this when the missile is repositioned at a checkpoint respawn
    /// so stale position samples don't trigger an immediate loop-escape.
    /// </summary>
    public void Reset()
    {
        posHistory.Clear();
        isEscaping  = false;
        escapeTimer = 0f;
        escapeDir   = Vector3.zero;
        missileProgress = trackPath != null
            ? trackPath.GetProgress(self.position, 0f) : 0f;
        playerProgress  = 0f;
        CurrentSpeed    = 0f;
    }
}