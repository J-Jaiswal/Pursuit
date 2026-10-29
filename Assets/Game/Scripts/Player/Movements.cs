using System.Collections;
using UnityEngine;

public class Movements : MonoBehaviour
{
    [SerializeField] private RocketConfigSO config;
    [SerializeField] private ParticleSystem leftThrust;
    [SerializeField] private ParticleSystem rightThrust;
    //[SerializeField] private ParticleSystem mainThrust;
    //[SerializeField] private AudioSource mainEngine;

    private Rigidbody rb;
    private CollisionHandler collision;
    private Fuel fuel;

    private float currentThrust;

    // healthEfficiency now reads ENGINE HP (not rocket HP).
    // At 50% engine health the rocket outputs 50% thrust — this is the core feel of engine degradation.
    private float healthEfficiency = 1f;

    // isCriticalTumbling is only ever set true by the FUEL EMPTY path.
    // Engine and rocket health deaths are clean instant deaths with no tumble.
    private bool isCriticalTumbling;

    [SerializeField] RocketEngineFX engineFX;

    public float NormalizedThrust
    {
        get
        {
            if (!enabled || isCriticalTumbling) return 0f;
            float maxT = config != null ? config.MaxThrust : 1500f;
            return maxT > 0f ? currentThrust / maxT : 0f;
        }
    }

    public bool IsCriticalTumbling => isCriticalTumbling;

    private void Start()
    {
        rb        = GetComponent<Rigidbody>();
        collision = GetComponent<CollisionHandler>();
        fuel      = GetComponent<Fuel>();

        if (rb != null)
        {
            rb.maxAngularVelocity = config != null ? config.MaxAngularVelocity : 1f;
            rb.angularDamping = 2f;
            rb.constraints = RigidbodyConstraints.FreezePositionZ
                            | RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationY;
        }

        GameManager.OnStateChanged += HandleStateChanged;

        if (collision != null)
        {
            // ENGINE health drives thrust efficiency.
            // Rocket (hull) health no longer affects movement — it is purely a death condition.
            collision.OnEngineHealthChanged += HandleEngineHealthChanged;
        }

        if (fuel != null)
        {
            // Tumble is ONLY triggered by fuel empty — all other deaths are instant.
            fuel.OnFuelEmptyTumble += HandleFuelEmptyTumble;
        }
    }

    private void OnDestroy()
    {
        GameManager.OnStateChanged -= HandleStateChanged;

        if (collision != null)
        {
            collision.OnEngineHealthChanged -= HandleEngineHealthChanged;
        }

        if (fuel != null)
        {
            fuel.OnFuelEmptyTumble -= HandleFuelEmptyTumble;
        }
    }

    private void FixedUpdate()
    {
        if (isCriticalTumbling) return;

        handleThrust();
        handleRotation();
    }

    private void handleThrust()
    {
        bool hasFuel    = fuel == null || fuel.HasFuel;
        bool isThrusting = Input.GetKey(KeyCode.Space) && hasFuel;

        float targetThrust = isThrusting ? (config != null ? config.MaxThrust : 1500f) : 0f;
        float smoothing    = config != null ? config.ThrustSmoothing : 3f;
        currentThrust = Mathf.Lerp(currentThrust, targetThrust, Time.fixedDeltaTime * smoothing);

        // healthEfficiency scales with ENGINE HP — degraded engine = less thrust
        float force = currentThrust * healthEfficiency;
        if (rb != null)
        {
            rb.AddRelativeForce(Vector3.up * force);
        }

        if (isThrusting)
        {
            engineFX.OnThrustHeld(Time.deltaTime, NormalizedThrust);
        }
        else
        {
            engineFX.OnThrustReleased();
        }
    }

    private void handleRotation()
    {
        bool hasFuel = fuel == null || fuel.HasFuel;
        if (!hasFuel)
        {
            if (leftThrust  != null && leftThrust.isPlaying)  leftThrust.Stop();
            if (rightThrust != null && rightThrust.isPlaying) rightThrust.Stop();
            engineFX?.OnSideThrusterReleased();
            return;
        }

        float torque = config != null ? config.RotationTorque : 100f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)  )
        {
            if (rb != null)
            {
                rb.AddTorque(Vector3.forward * torque, ForceMode.Force);
                engineFX?.OnSideThrusterActive(1f);
            }
            if (rightThrust != null && !rightThrust.isPlaying) rightThrust.Play();
        }
        else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            if (rb != null)
            {
                rb.AddTorque(Vector3.forward * -torque, ForceMode.Force);
                engineFX?.OnSideThrusterActive(1f);
            }
            if (leftThrust != null && !leftThrust.isPlaying) leftThrust.Play();
        }
        else
        {
            if (leftThrust  != null && leftThrust.isPlaying)  leftThrust.Stop();
            if (rightThrust != null && rightThrust.isPlaying) rightThrust.Stop();
            engineFX?.OnSideThrusterReleased();
        }
    }

    // Health / Tumble Handlers ------------------->

    /// Engine HP changed — scale thrust output to match degradation.
    /// Also drives the engine FX visuals (flame colour / intensity).
 
    private void HandleEngineHealthChanged(float normalizedEngineHealth)
    {
        healthEfficiency = normalizedEngineHealth;
        engineFX.SetHealth(normalizedEngineHealth);
    }

    /// Fuel ran out — start the tumble death sequence (the ONLY tumble death).
    /// Engine and rocket health deaths skip the tumble and die instantly.
    private void HandleFuelEmptyTumble()
    {
        if (isCriticalTumbling) return;
        isCriticalTumbling = true;

        StopMovementAndEffects();
        StartCoroutine(FuelTumbleCoroutine());
    }

    private IEnumerator FuelTumbleCoroutine()
    {
        float duration = config != null ? config.DeathSequenceDuration : 1.5f;
        yield return new WaitForSeconds(duration);

        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
        {
            GameManager.Instance.PlayerDied(RaceContext.DeathCause.Fuel);
        }
    }

    //  Game State Handler --------------->

    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Dead || state == GameState.LevelComplete)
        {
            StopMovementAndEffects();
            enabled = false;
        }
        else if (state == GameState.Playing)
        {
            enabled            = true;
            isCriticalTumbling = false;

            if (rb != null)
            {
                rb.linearVelocity  = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            healthEfficiency = 1f;
            engineFX.SetHealth(1f);
        }
    }

    private void StopMovementAndEffects()
    {
        if (leftThrust  != null && leftThrust.isPlaying)  leftThrust.Stop();
        if (rightThrust != null && rightThrust.isPlaying) rightThrust.Stop();
        engineFX.OnThrustReleased();
        engineFX?.OnSideThrusterReleased();
    }
}