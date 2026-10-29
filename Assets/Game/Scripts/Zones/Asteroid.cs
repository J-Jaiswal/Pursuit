using UnityEngine;

/// <summary>
/// Self-contained asteroid hazard for cave-tunnel zones.
///
/// WHAT IT DOES:
///   - Floats freely through space with a random initial velocity + spin — no scripted path.
///     Bounces off walls and other asteroids via real physics.
///   - Deals Sharp-style instant damage when the player collides with it.
///   - Applies a velocity impulse away from the player on hit, scaled by the
///     actual impact speed and using the real collision contact normal.
///   - Plays impact VFX and audio on hit.
///   - Has a per-hit cooldown to prevent repeated damage from a single grazing contact.
///   - Fully pauses (freezes physics) during GameState is not Playing (death, debrief, pause).
/// </summary>


[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Asteroid : MonoBehaviour
{
    // ?? Floating Movement ?????????????????????????????????????????????

    [Header("Floating Movement")]
    [Tooltip("Minimum initial drift speed (world units/sec) in the XY plane.")]
    [SerializeField] private float minSpeed = 0.5f;

    [Tooltip("Maximum initial drift speed (world units/sec) in the XY plane.")]
    [SerializeField] private float maxSpeed = 1.5f;

    [Tooltip("Min/max angular speed (deg/sec) for random tumble spin.")]
    [SerializeField] private float minSpin = 10f;
    [SerializeField] private float maxSpin = 60f;

    [Tooltip("Clamps speed back into [minSpeed, maxSpeed] each physics step so collisions " +
             "can change direction without the asteroid gradually speeding up or stalling out.")]
    [SerializeField] private bool clampSpeedAfterCollisions = true;

    // Damage 

    [Header("Damage")]
    [Tooltip("Flat damage dealt to the player on impact. Routed mostly to rocket HP (Sharp type).")]
    [SerializeField] private float impactDamage = 25f;

    [Tooltip("Engine HP share of the impact damage (0-1). Sharp hits carry very little engine stress.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float engineShare = 0.05f;

    [Tooltip("Seconds before this asteroid can damage the player again. Prevents rapid multi-hit.")]
    [SerializeField] private float damageCooldown = 0.8f;

    // Player Knockback

    [Header("Player Knockback")]
    [Tooltip("Base speed (world units/sec) applied to the asteroid, away from the player, on a typical hit.")]
    [SerializeField] private float knockbackSpeed = 4f;

    [Tooltip("How much the actual impact speed (collision.relativeVelocity) influences knockback. " +
             "0 = always use knockbackSpeed exactly (old fixed behavior). 1 = fully driven by impact speed.")]
    [Range(0f, 1f)]
    [SerializeField] private float impactSpeedInfluence = 0.6f;

    [Tooltip("Multiplier applied to relative impact speed before blending into knockback speed.")]
    [SerializeField] private float impactSpeedMultiplier = 0.5f;

    [Tooltip("Minimum and maximum knockback speed allowed, regardless of impact force.")]
    [SerializeField] private float minKnockbackSpeed = 2f;
    [SerializeField] private float maxKnockbackSpeed = 9f;

    // Feedback

    [Header("Feedback")]
    [Tooltip("Optional particle effect played on impact. Assign a child ParticleSystem here.")]
    [SerializeField] private ParticleSystem impactParticles;

    [Tooltip("Camera shake intensity when the player hits this asteroid.")]
    [Range(0f, 1f)]
    [SerializeField] private float shakeIntensity = 0.35f;

    [Tooltip("Camera shake duration (seconds).")]
    [SerializeField] private float shakeDuration = 0.15f;

    [Tooltip("Play a light bump SFX when this asteroid collides with a wall/other asteroid (non-player).")]
    [SerializeField] private bool playBumpSfxOnEnvironmentHit = false;

    // Private State 

    private Rigidbody rb;
    private float lockedZ;
    private float nextDamageTime;

    // Pause-state cache (for freezing physics when GameState != Playing)
    private bool wasFrozenForPause;
    private Vector3 cachedVelocity;
    private Vector3 cachedAngularVelocity;

    // Lifecycle ------------------------------>

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 0f;      // no drag — keeps drifting indefinitely between collisions
        rb.angularDamping = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Lock Z position only — free to translate/rotate in X/Y and rotate on any axis.
        rb.constraints = RigidbodyConstraints.FreezePositionZ;

        EnsureBouncyPhysicMaterial();
    }

    private void Start()
    {
        lockedZ = transform.position.z;

        // Random initial drift direction + speed, XY plane only.
        Vector2 dir = Random.insideUnitCircle.sqrMagnitude > 0.0001f
            ? Random.insideUnitCircle.normalized
            : Vector2.right;
        float speed = Random.Range(minSpeed, maxSpeed);
        rb.linearVelocity = new Vector3(dir.x * speed, dir.y * speed, 0f);

        // Random tumble spin.
        Vector3 spinAxis = Random.onUnitSphere;
        float spinMag = Random.Range(minSpin, maxSpin) * Mathf.Deg2Rad;
        rb.angularVelocity = spinAxis * spinMag;
    }

    private void FixedUpdate()
    {
        if (!IsPlaying())
        {
            FreezeForPause();
            return;
        }
        UnfreezeAfterPause();

        // Defensive Z lock — belt-and-suspenders alongside the Rigidbody constraint,
        // in case anything else nudges the transform directly.
        if (!Mathf.Approximately(rb.position.z, lockedZ))
            rb.position = new Vector3(rb.position.x, rb.position.y, lockedZ);

        if (clampSpeedAfterCollisions)
        {
            Vector3 vel = rb.linearVelocity;
            float speed = vel.magnitude;

            if (speed > 0.0001f)
            {
                float clamped = Mathf.Clamp(speed, minSpeed, maxSpeed);
                if (!Mathf.Approximately(clamped, speed))
                    rb.linearVelocity = vel.normalized * clamped;
            }
        }
    }

    // Pause Handling ------------------------------>

    private void FreezeForPause()
    {
        if (wasFrozenForPause) return;

        cachedVelocity = rb.linearVelocity;
        cachedAngularVelocity = rb.angularVelocity;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        wasFrozenForPause = true;
    }

    private void UnfreezeAfterPause()
    {
        if (!wasFrozenForPause) return;

        rb.isKinematic = false;
        rb.linearVelocity = cachedVelocity;
        rb.angularVelocity = cachedAngularVelocity;
        wasFrozenForPause = false;
    }

    // Collision  ------------------------------>

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsPlaying()) return;

        if (!collision.gameObject.CompareTag("Player"))
        {
            // Wall or another asteroid — physics already handled the bounce.
            if (playBumpSfxOnEnvironmentHit && SoundEffectsManager.Instance != null)
            {
                Vector3 pt = collision.contactCount > 0 ? collision.contacts[0].point : transform.position;
                SoundEffectsManager.Instance.PlaySFX(SFXType.PlayerDamage, pt); // swap for a dedicated bump SFX type if you have one
            }
            return;
        }

        if (Time.time < nextDamageTime) return;
        nextDamageTime = Time.time + damageCooldown;

        CollisionHandler handler = collision.gameObject.GetComponentInParent<CollisionHandler>();
        if (handler == null) return;

        Vector3 impactPoint = collision.contactCount > 0
            ? collision.contacts[0].point
            : transform.position;

        // Sharp-type damage: mostly rocket HP, tiny engine share
        handler.TakeDamage(
            impactDamage,
            impactPoint,
            playCrashEffect: false,    // we handle all feedback here
            engineShare: engineShare,
            deathCause: RaceContext.DeathCause.Hazard
        );

        ApplyPlayerKnockback(collision);
        PlayImpactFeedback(impactPoint);
    }

    // Knockback -------------------->

   
    private void ApplyPlayerKnockback(Collision collision)
    {
        // Prefer the true contact normal (points from the player surface toward
        // the asteroid). Fall back to center-to-center if no contact is reported.
        Vector2 dir;
        if (collision.contactCount > 0)
        {
            Vector3 n = collision.contacts[0].normal;
            dir = new Vector2(n.x, n.y);
        }
        else
        {
            Vector2 away = new Vector2(
                transform.position.x - collision.transform.position.x,
                transform.position.y - collision.transform.position.y
            );
            dir = away;
        }

        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Random.insideUnitCircle.normalized;

        // How fast the asteroid and player were actually closing at impact.
        float impactSpeed = collision.relativeVelocity.magnitude * impactSpeedMultiplier;

        // Blend between the fixed base speed and the impact-driven speed.
        float blendedSpeed = Mathf.Lerp(knockbackSpeed, impactSpeed, impactSpeedInfluence);
        float finalSpeed = Mathf.Clamp(blendedSpeed, minKnockbackSpeed, maxKnockbackSpeed);

        rb.linearVelocity = new Vector3(dir.x * finalSpeed, dir.y * finalSpeed, 0f);
    }

    // Feedback ------------------------------>

    private void PlayImpactFeedback(Vector3 impactPoint)
    {
        if (impactParticles != null)
        {
            impactParticles.transform.position = impactPoint;
            impactParticles.Play();
        }

        CameraShake.Shake(shakeIntensity, shakeDuration);

        if (SoundEffectsManager.Instance != null)
            SoundEffectsManager.Instance.PlaySFX(SFXType.PlayerDamage, impactPoint);
    }

    // Physics Material ( current having bugs ) ------------------->

    private void EnsureBouncyPhysicMaterial()
    {
        Collider col = GetComponent<Collider>();
        if (col.sharedMaterial != null) return;

        PhysicsMaterial mat = new PhysicsMaterial("AsteroidBouncy")
        {
            bounciness = 0.85f,
            dynamicFriction = 0.05f,
            staticFriction = 0.05f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };
        col.material = mat;
    }

    // Utility --------------------------->
    private bool IsPlaying() =>
        GameManager.Instance != null &&
        GameManager.Instance.CurrentState == GameState.Playing;
}