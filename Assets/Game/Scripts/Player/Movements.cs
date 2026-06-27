using System.Collections;
using UnityEngine;

public class Movements : MonoBehaviour
{
    [SerializeField] private RocketConfigSO config;
    [SerializeField] private ParticleSystem leftThrust;
    [SerializeField] private ParticleSystem rightThrust;
    [SerializeField] private ParticleSystem mainThrust;
    [SerializeField] private AudioSource mainEngine;

    private Rigidbody rb;
    private CollisionHandler collision;
    private Fuel fuel;

    private float currentThrust;
    private float healthEfficiency = 1f;
    private bool isCriticalTumbling;

    public float NormalizedThrust
    {
        get
        {
            if (!enabled || isCriticalTumbling) return 0f;
            float maxT = config != null ? config.MaxThrust : 1500f;
            return maxT > 0f ? currentThrust / maxT : 0f;
        }
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        collision = GetComponent<CollisionHandler>();
        fuel = GetComponent<Fuel>();

        if (rb != null)
        {
            rb.freezeRotation = true;
        }

        GameManager.OnStateChanged += HandleStateChanged;

        if (collision != null)
        {
            collision.OnHealthChanged += HandleHealthChanged;
            collision.OnCriticalDamage += HandleCriticalDamage;
        }
    }

    private void OnDestroy()
    {
        GameManager.OnStateChanged -= HandleStateChanged;

        if (collision != null)
        {
            collision.OnHealthChanged -= HandleHealthChanged;
            collision.OnCriticalDamage -= HandleCriticalDamage;
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
        bool hasFuel = fuel == null || fuel.HasFuel;
        bool isThrusting = Input.GetKey(KeyCode.Space) && hasFuel;

        float targetThrust = isThrusting ? (config != null ? config.MaxThrust : 1500f) : 0f;
        float smoothing = config != null ? config.ThrustSmoothing : 3f;
        currentThrust = Mathf.Lerp(currentThrust, targetThrust, Time.fixedDeltaTime * smoothing);

        float force = currentThrust * healthEfficiency;
        if (rb != null)
        {
            rb.AddRelativeForce(Vector3.up * force * Time.fixedDeltaTime);
        }

        if (isThrusting)
        {
            if (mainThrust != null && !mainThrust.isPlaying) mainThrust.Play();
            if (mainEngine != null && !mainEngine.isPlaying) mainEngine.Play();
        }
        else
        {
            if (mainThrust != null && mainThrust.isPlaying) mainThrust.Stop();
            if (mainEngine != null && mainEngine.isPlaying) mainEngine.Stop();
        }
    }

    private void handleRotation()
    {
        bool hasFuel = fuel == null || fuel.HasFuel;
        if (!hasFuel)
        {
            if (leftThrust != null && leftThrust.isPlaying) leftThrust.Stop();
            if (rightThrust != null && rightThrust.isPlaying) rightThrust.Stop();
            return;
        }

        float rotateSpeed = config != null ? config.RotateSpeed : 100f;

        if (Input.GetKey(KeyCode.A))
        {
            if (rb != null)
            {
                transform.Rotate(Vector3.forward * rotateSpeed * Time.fixedDeltaTime);
            }
            if (mainEngine != null && !mainEngine.isPlaying) mainEngine.Play();
            if (rightThrust != null && !rightThrust.isPlaying) rightThrust.Play();
        }
        else if (Input.GetKey(KeyCode.D))
        {
            if (rb != null)
            {
                transform.Rotate(Vector3.forward * -rotateSpeed * Time.fixedDeltaTime);
            }
            if (mainEngine != null && !mainEngine.isPlaying) mainEngine.Play();
            if (leftThrust != null && !leftThrust.isPlaying) leftThrust.Play();
        }
        else
        {
            if (leftThrust != null && leftThrust.isPlaying) leftThrust.Stop();
            if (rightThrust != null && rightThrust.isPlaying) rightThrust.Stop();
        }
    }

    private void HandleHealthChanged(float normalizedHealth)
    {
        healthEfficiency = normalizedHealth;
    }

    private void HandleCriticalDamage()
    {
        if (isCriticalTumbling) return;
        isCriticalTumbling = true;

        if (rb != null)
        {
            rb.freezeRotation = false;
        }

        StopMovementAndEffects();
        StartCoroutine(CriticalTumbleCoroutine());
    }

    private IEnumerator CriticalTumbleCoroutine()
    {
        float duration = config != null ? config.DeathSequenceDuration : 1.5f;
        yield return new WaitForSeconds(duration);

        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
        {
            GameManager.Instance.PlayerDied();
        }
    }

    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Dead || state == GameState.LevelComplete)
        {
            StopMovementAndEffects();
            enabled = false;
        }
        else if (state == GameState.Playing)
        {
            enabled = true;
            isCriticalTumbling = false;
            if (rb != null)
            {
                rb.freezeRotation = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            healthEfficiency = 1f;
        }
    }

    private void StopMovementAndEffects()
    {
        if (mainThrust != null && mainThrust.isPlaying) mainThrust.Stop();
        if (mainEngine != null && mainEngine.isPlaying) mainEngine.Stop();
        if (leftThrust != null && leftThrust.isPlaying) leftThrust.Stop();
        if (rightThrust != null && rightThrust.isPlaying) rightThrust.Stop();
    }
}
