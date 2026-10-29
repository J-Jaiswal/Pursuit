using UnityEngine;

/// <summary>
/// Classic crusher hazard: two platforms (walls/blocks) move toward each other
/// on a looping state machine, dealing continuous damage to any player caught between them.

/// </summary>
public class CrushingHazard : MonoBehaviour
{
    // Crush Axis

    public enum CrushAxis { X, Y, Z }

    [Header("Crusher Setup")]
    [Tooltip("Local axis along which PlatformA and PlatformB slide toward each other.\n" +
             "Y = top/bottom crusher, X = left/right, Z = front/back (rare in 2.5D).")]
    [SerializeField] private CrushAxis crushAxis = CrushAxis.Y;

    [Tooltip("Total distance closed: each platform travels (crushDistance / 2) inward from its open position.")]
    [Range(0.5f, 20f)]
    [SerializeField] private float crushDistance = 4f;

    // Platform References

    [Header("Platforms")]
    [Tooltip("First crusher platform (e.g. top wall). Must be a child of this GameObject. " +
             "Position it at its OPEN (resting) location in the Editor.")]
    [SerializeField] private Transform platformA;

    [Tooltip("Second crusher platform (e.g. bottom wall). Must be a child of this GameObject. " +
             "Position it at its OPEN (resting) location in the Editor.")]
    [SerializeField] private Transform platformB;

    // Timing  

    [Header("Timing")]
    [Tooltip("Seconds the crusher stays fully open before starting to close.")]
    [SerializeField] private float openHoldTime = 1.5f;

    [Tooltip("Seconds the crusher stays fully closed before opening again.")]
    [SerializeField] private float closedHoldTime = 0.8f;

    [Tooltip("Speed (units per second) at which each platform slides.")]
    [Range(0.5f, 30f)]
    [SerializeField] private float crushSpeed = 5f;

    [Tooltip("Ease the closing and opening motion for a more dramatic slam feel.")]
    [SerializeField] private bool easedMotion = true;

    [Tooltip("(0–1) Fraction through the closing travel at which the motion is eased. " +
             "Only relevant when Eased Motion is on.")]
    [Range(0f, 1f)]
    [SerializeField] private float easeInFraction = 0.3f;

    // Damage 

    [Header("Damage")]
    [Tooltip("Damage dealt per tick while the player is caught in the crusher.")]
    [SerializeField] private float damagePerTick = 25f;

    [Tooltip("Seconds between damage ticks while the player is inside.")]
    [SerializeField] private float tickInterval = 0.35f;

    [Tooltip("Engine HP share of crushing damage. Crushing stresses engine internals.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float engineShare = 0.15f;

    [Tooltip("Camera shake intensity on each damage tick.")]
    [Range(0f, 1f)]
    [SerializeField] private float shakeIntensity = 0.35f;

    // State Machine 

    private enum CrushState { OpenHold, Closing, ClosedHold, Opening }

    private CrushState state = CrushState.OpenHold;
    private float stateTimer;
    private float crushProgress;   // 0 = fully open, 1 = fully closed

    // Open positions captured in Start() — the Editor-placed positions of the platforms.
    private Vector3 openLocalA;
    private Vector3 openLocalB;

    // Closed positions: each platform slides half of crushDistance inward.
    private Vector3 closedLocalA;
    private Vector3 closedLocalB;

    // Damage State

    private CollisionHandler playerInside;
    private float nextDamageTick;

    // Lifecycle ----------------------->

    private void Start()
    {
        if (platformA == null || platformB == null)
        {
            Debug.LogError($"[CrushingHazard] '{name}': PlatformA and/or PlatformB are not assigned!", this);
            enabled = false;
            return;
        }

        // Capture open positions (Editor-placed local positions)
        openLocalA = platformA.localPosition;
        openLocalB = platformB.localPosition;

        // Calculate closed positions: each platform moves inward by crushDistance/2
        Vector3 inwardDir = CrushAxisVector();
        closedLocalA = openLocalA - inwardDir * (crushDistance * 0.5f);
        closedLocalB = openLocalB + inwardDir * (crushDistance * 0.5f);

        stateTimer = openHoldTime;
        crushProgress = 0f;
    }

    private void Update()
    {
        if (!IsPlaying()) return;

        UpdateStateMachine();
        ApplyPlatformPositions();
        UpdateDamageTick();
    }

    // State Machine ----------------------------->

    private void UpdateStateMachine()
    {
        switch (state)
        {
            // Waiting open 
            case CrushState.OpenHold:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                    TransitionTo(CrushState.Closing);
                break;

            // Moving together
            case CrushState.Closing:
                crushProgress += Time.deltaTime * crushSpeed / crushDistance;
                crushProgress = Mathf.Clamp01(crushProgress);
                if (crushProgress >= 1f)
                    TransitionTo(CrushState.ClosedHold);
                break;

            // Waiting closed
            case CrushState.ClosedHold:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                    TransitionTo(CrushState.Opening);
                break;

            // Moving apart
            case CrushState.Opening:
                crushProgress -= Time.deltaTime * crushSpeed / crushDistance;
                crushProgress = Mathf.Clamp01(crushProgress);
                if (crushProgress <= 0f)
                    TransitionTo(CrushState.OpenHold);
                break;
        }
    }

    private void TransitionTo(CrushState next)
    {
        state = next;
        switch (next)
        {
            case CrushState.OpenHold:
                stateTimer = openHoldTime;
                crushProgress = 0f;
                // Player is no longer in danger once fully open
                playerInside = null;
                break;
            case CrushState.Closing:
                // crushProgress already where it needs to be
                break;
            case CrushState.ClosedHold:
                stateTimer = closedHoldTime;
                crushProgress = 1f;
                break;
            case CrushState.Opening:
                // crushProgress already at 1
                break;
        }
    }

    //  Platform Movement --------------------->

    private void ApplyPlatformPositions()
    {
        float t = easedMotion ? EasedProgress(crushProgress) : crushProgress;

        platformA.localPosition = Vector3.Lerp(openLocalA, closedLocalA, t);
        platformB.localPosition = Vector3.Lerp(openLocalB, closedLocalB, t);
    }

    private float EasedProgress(float raw)
    {
        // Fast close: slow ramp-in, then accelerates — use smoothstep clamped with bias
        return Mathf.SmoothStep(0f, 1f, raw);
    }

    // Damage ------------------------------>

    private void UpdateDamageTick()
    {
        if (playerInside == null) return;
        if (state == CrushState.OpenHold) return;   // safe while fully open
        if (Time.time < nextDamageTick) return;

        nextDamageTick = Time.time + tickInterval;

        playerInside.TakeDamage(
            damagePerTick,
            transform.position,
            playCrashEffect: false,
            engineShare: engineShare,
            deathCause: RaceContext.DeathCause.Hazard
        );

        CameraShake.Shake(shakeIntensity, 0.15f);

        if (SoundEffectsManager.Instance != null)
            SoundEffectsManager.Instance.PlaySFX(SFXType.PlayerDamage, transform.position);
    }

    // Trigger Events ------------------------------>

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlaying()) return;
        if (!other.CompareTag("Player")) return;
        if (state == CrushState.OpenHold) return;

        CollisionHandler handler = other.GetComponentInParent<CollisionHandler>();
        if (handler == null) return;

        playerInside = handler;
        nextDamageTick = 0f;   // first tick fires immediately
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInside = null;
    }

    // Utility ------------------------------>

    private Vector3 CrushAxisVector()
    {
        return crushAxis switch
        {
            CrushAxis.X => Vector3.right,
            CrushAxis.Y => Vector3.up,
            CrushAxis.Z => Vector3.forward,
            _           => Vector3.up,
        };
    }

    private bool IsPlaying() =>
        GameManager.Instance != null &&
        GameManager.Instance.CurrentState == GameState.Playing;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw the crush travel zone from the current platform positions
        Vector3 inwardDir = CrushAxisVector();
        float half = crushDistance * 0.5f;

        if (platformA != null)
        {
            Vector3 openWorld  = transform.TransformPoint(platformA.localPosition);
            Vector3 closeWorld = transform.TransformPoint(platformA.localPosition - inwardDir * half);
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.8f);
            Gizmos.DrawLine(openWorld, closeWorld);
            Gizmos.DrawSphere(closeWorld, 0.1f);
        }

        if (platformB != null)
        {
            Vector3 openWorld  = transform.TransformPoint(platformB.localPosition);
            Vector3 closeWorld = transform.TransformPoint(platformB.localPosition + inwardDir * half);
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.8f);
            Gizmos.DrawLine(openWorld, closeWorld);
            Gizmos.DrawSphere(closeWorld, 0.1f);
        }

        // Draw the crush zone center
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.3f);
        if (platformA != null && platformB != null)
        {
            Vector3 midA = transform.TransformPoint(platformA.localPosition - inwardDir * half);
            Vector3 midB = transform.TransformPoint(platformB.localPosition + inwardDir * half);
            Gizmos.DrawLine(midA, midB);
        }
    }
#endif
}
