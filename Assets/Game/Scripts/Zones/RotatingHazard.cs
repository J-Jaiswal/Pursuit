using UnityEngine;

/// <summary>
/// Dual-axis rotating hazard .
/// Rotates a GameObject on two independent axes simultaneously,
/// creating a complex, visually interesting obstacle that is harder to read at a glance
/// than a single-axis spinner — ideal for 2.5D where depth perception is limited.
///
/// WHAT IT DOES:
///   Each axis (Primary and Secondary) can be independently configured with:
///     • Which local axis to rotate on (X, Y, or Z)
///     • Rotation Mode:
///         Continuous — spins forever at a constant speed (classic fan/blade hazard)
///         Oscillating — swings back and forth between ±amplitude degrees, like a
///                        pendulum or trap arm (more predictable cadence, good for
///                        teaching moments early in a level)
///     • Speed — degrees per second (Continuous) or oscillation frequency (Oscillating)
///     • Amplitude — only used in Oscillating mode: max swing angle in degrees
///     • PhaseOffset — offsets the start of the oscillation (0–1) so two RotatingHazards
///                      in the same area don't sync up visually
///
///   Primary axis runs the main visible spin (usually Z in 2.5D).
///   Secondary axis adds a perpendicular wobble or tilt (usually X or Y).
///   Together they create the impression of a 3D tumbling object in a 2.5D game.
///
/// DAMAGE:
///   Self-contained — uses OnTriggerStay to deal continuous damage ticks.
///   No dependency on Hazard.cs; all damage values are serialized inline.
///   Requires the hazard's trigger collider to be on this GameObject (or a child)
///   and the player's collider to have tag "Player".
/// </summary>

public class RotatingHazard : MonoBehaviour
{
    // Shared Enums

    public enum RotationMode { Continuous, Oscillating }
    public enum LocalAxis { X, Y, Z }

    // Primary Axis

    [Header("Primary Rotation")]
    [Tooltip("Local axis for the main rotation (Z is forward — the classic 2.5D spinner axis).")]
    [SerializeField] private LocalAxis primaryAxis = LocalAxis.Z;

    [Tooltip("Continuous: spins at constant speed.\nOscillating: swings back and forth like a pendulum.")]
    [SerializeField] private RotationMode primaryMode = RotationMode.Continuous;

    [Tooltip("Continuous: degrees per second.\nOscillating: oscillation frequency in cycles per second (Hz).")]
    [Range(10f, 720f)]
    [SerializeField] private float primarySpeed = 180f;

    [Tooltip("Only used in Oscillating mode. Max swing angle in degrees (swings ±amplitude).")]
    [Range(5f, 180f)]
    [SerializeField] private float primaryAmplitude = 90f;

    [Tooltip("Offset the oscillation start phase (0–1). Useful to desync two nearby hazards.")]
    [Range(0f, 1f)]
    [SerializeField] private float primaryPhaseOffset = 0f;

    // Secondary Axis

    [Header("Secondary Rotation")]
    [Tooltip("Local axis for the secondary wobble or tilt. Should differ from Primary Axis.")]
    [SerializeField] private LocalAxis secondaryAxis = LocalAxis.X;

    [Tooltip("Continuous: spins at constant speed.\nOscillating: swings back and forth.")]
    [SerializeField] private RotationMode secondaryMode = RotationMode.Oscillating;

    [Tooltip("Continuous: degrees per second.\nOscillating: oscillation frequency in Hz.")]
    [Range(0f, 360f)]
    [SerializeField] private float secondarySpeed = 60f;

    [Tooltip("Only used in Oscillating mode. Max tilt angle in degrees (tilts ±amplitude).")]
    [Range(5f, 180f)]
    [SerializeField] private float secondaryAmplitude = 45f;

    [Tooltip("Offset the secondary oscillation phase (0–1). Set differently from primaryPhaseOffset " +
             "to make the two axes feel independent and less predictable.")]
    [Range(0f, 1f)]
    [SerializeField] private float secondaryPhaseOffset = 0.25f;

    // Optional Visual Target 

    [Header("Optional")]
    [Tooltip("If set, rotation is applied to this Transform instead of this GameObject's Transform. " +
             "Useful when the collider must stay fixed but only the visual mesh should spin.")]
    [SerializeField] private Transform visualTarget;

    // Damage

    [Header("Damage")]
    [Tooltip("Damage per tick while player overlaps the trigger collider. Set to 0 for visual-only.")]
    [SerializeField] private float damagePerTick = 20f;

    [Tooltip("Seconds between damage ticks.")]
    [SerializeField] private float tickInterval = 0.3f;

    [Tooltip("Engine HP share of the damage. Spinning blades shred surfaces — primarily hull damage.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float engineShare = 0.1f;

    [Tooltip("Camera shake intensity on each damage tick.")]
    [Range(0f, 1f)]
    [SerializeField] private float shakeIntensity = 0.25f;

    // Private State 

    private Transform rotationTarget;    // either visualTarget or this.transform

    // Accumulated angles for continuous mode (avoid Rotate() precision drift)
    private float primaryAngle;
    private float secondaryAngle;

    // Oscillation base angles captured at Start()
    private Quaternion startRotation;

    // Damage
    private CollisionHandler playerInside;
    private float nextDamageTick;

    // Lifecycle ---------------------->

    private void Start()
    {
        rotationTarget = (visualTarget != null) ? visualTarget : transform;
        startRotation  = rotationTarget.localRotation;
    }

    private void Update()
    {
        if (!IsPlaying()) return;

        UpdateRotation();
        UpdateDamageTick();
    }

    // Rotation Update ----------------------->

    private void UpdateRotation()
    {
        float primaryDelta   = ComputeDelta(primaryMode, primarySpeed, primaryAmplitude,
                                            primaryPhaseOffset, ref primaryAngle, Time.time);
        float secondaryDelta = ComputeDelta(secondaryMode, secondarySpeed, secondaryAmplitude,
                                            secondaryPhaseOffset, ref secondaryAngle, Time.time);

        // Build two independent incremental rotations around their respective local axes
        Quaternion primaryRot   = Quaternion.AngleAxis(primaryAngle,   AxisVector(primaryAxis));
        Quaternion secondaryRot = Quaternion.AngleAxis(secondaryAngle, AxisVector(secondaryAxis));

        // Apply both as an offset from the start rotation (maintains precision)
        rotationTarget.localRotation = startRotation * primaryRot * secondaryRot;
    }


    /// Returns the current absolute angle for an axis given its mode.
    /// For Continuous, accumulates deltaTime * speed each frame.
    /// For Oscillating, computes Mathf.Sin-based angle from Time.time.

    private float ComputeDelta(
        RotationMode mode, float speed, float amplitude, float phaseOffset,
        ref float accumulatedAngle, float time)
    {
        if (mode == RotationMode.Continuous)
        {
            accumulatedAngle += speed * Time.deltaTime;
            // Keep in [0, 360) to avoid float precision loss over time
            if (accumulatedAngle >= 360f) accumulatedAngle -= 360f;
            return accumulatedAngle;
        }
        else // Oscillating
        {
            // sin-based oscillation: angle = amplitude * sin(2π * frequency * t + phase)
            float phase = phaseOffset * Mathf.PI * 2f;
            return amplitude * Mathf.Sin(time * speed * Mathf.PI * 2f + phase);
        }
    }

    // ── Damage Tick ───────────────────────────────────────────────────────────

    private void UpdateDamageTick()
    {
        if (playerInside == null) return;
        if (damagePerTick <= 0f) return;
        if (Time.time < nextDamageTick) return;

        nextDamageTick = Time.time + tickInterval;

        playerInside.TakeDamage(
            damagePerTick,
            transform.position,
            playCrashEffect: false,
            engineShare: engineShare,
            deathCause: RaceContext.DeathCause.Hazard
        );

        CameraShake.Shake(shakeIntensity, 0.12f);

        if (SoundEffectsManager.Instance != null)
            SoundEffectsManager.Instance.PlaySFX(SFXType.PlayerDamage, transform.position);
    }

    // Trigger Events ------------------------------->

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlaying()) return;
        if (!other.CompareTag("Player")) return;

        CollisionHandler handler = other.GetComponentInParent<CollisionHandler>();
        if (handler == null) return;

        playerInside = handler;
        nextDamageTick = 0f;   // first tick fires immediately on entry
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInside = null;
    }

    // Utility ------------------------------->

    private static Vector3 AxisVector(LocalAxis axis) => axis switch
    {
        LocalAxis.X => Vector3.right,
        LocalAxis.Y => Vector3.up,
        LocalAxis.Z => Vector3.forward,
        _           => Vector3.forward,
    };

    private bool IsPlaying() =>
        GameManager.Instance != null &&
        GameManager.Instance.CurrentState == GameState.Playing;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform t = (visualTarget != null) ? visualTarget : transform;

        // Draw primary axis in cyan
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(t.position, t.TransformDirection(AxisVector(primaryAxis)) * 0.6f);

        // Draw secondary axis in yellow
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(t.position, t.TransformDirection(AxisVector(secondaryAxis)) * 0.4f);

        // Draw the oscillation arcs as a label indicator (small sphere)
        if (primaryMode == RotationMode.Oscillating)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
            Gizmos.DrawWireSphere(t.position, 0.08f);
        }
        if (secondaryMode == RotationMode.Oscillating)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
            Gizmos.DrawWireSphere(t.position, 0.06f);
        }
    }
#endif
}
