using System;
using UnityEngine;

public class CollisionHandler : MonoBehaviour
{
    [SerializeField] private RocketConfigSO config;
    [SerializeField] private ParticleSystem crashEffect;
    [SerializeField] private ParticleSystem engineExplosionEffect; // NEW: plays when engine HP hits 0

    [Header("Visual Effects & Anchors")]
    [Tooltip("Optional empty GameObject on the player representing the VFX anchor point for localized damage/burn effects. Defaults to player transform if unassigned.")]
    [SerializeField] private Transform damageEffectPoint;

    [Tooltip("Continuous laser/burn spark particle system attached to the player.")]
    [SerializeField] private ParticleSystem laserBurnEffect;

    //  Events ----------------->
    /// Fires when Rocket (hull) HP changes. Normalized 0–1
    public event Action<float> OnHealthChanged;

    /// >Fires when Engine HP changes. Normalized 0–1
    public event Action<float> OnEngineHealthChanged;

    /// Fires once when rocket hull HP drops below CriticalHealthThreshold
    public event Action OnCriticalDamage;

    /// Fires once when engine HP drops to or below EngineCriticalThreshold (≤20%)
    public event Action OnEngineCritical;

    /// Fires when engine HP reaches zero — engine explosion death
    public event Action OnEngineDestroyed;

    //  State ---------------------->
    private Rigidbody rb;
    private float currentRocketHealth;
    private float currentEngineHealth;

    private bool isHullCritical;
    private bool isEngineCritical;
    private bool isDead;                    // prevents double-death from simultaneous pool hits

    private float nextCrushingDamageTime;

    //  Public read-only accessors --------------------->
    public float MaxRocketHealth => config != null ? config.MaxRocketHealth : 100f;
    public float MaxEngineHealth => config != null ? config.MaxEngineHealth : 100f;

    public float NormalizedRocketHealth =>
        MaxRocketHealth > 0f ? currentRocketHealth / MaxRocketHealth : 0f;
    public float NormalizedEngineHealth =>
        MaxEngineHealth > 0f ? currentEngineHealth / MaxEngineHealth : 0f;

    public Transform DamageEffectPoint => damageEffectPoint != null ? damageEffectPoint : transform;

   
    /// Activates or deactivates the continuous laser burn particle system on the player.
    /// Can position it at the contact point or player's damage effect anchor.

    public void SetLaserBurnVFX(bool active, Vector3? contactPoint = null)
    {
        if (laserBurnEffect == null) return;

        if (active && !isDead)
        {
            Vector3 pos = contactPoint ?? (damageEffectPoint != null ? damageEffectPoint.position : transform.position);
            laserBurnEffect.transform.position = pos;

            if (!laserBurnEffect.isPlaying)
                laserBurnEffect.Play();
        }
        else
        {
            if (laserBurnEffect.isPlaying)
                laserBurnEffect.Stop();
        }
    }

    //  Lifecycle --------------------> 

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        currentRocketHealth = MaxRocketHealth;
        currentEngineHealth = MaxEngineHealth;
    }

    private void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (currentRocketHealth <= 0f) currentRocketHealth = MaxRocketHealth;
        if (currentEngineHealth <= 0f) currentEngineHealth = MaxEngineHealth;
    }

    //  Collision Events --------------------->

    private void OnCollisionEnter(Collision collision)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        int finishLayer = LayerMask.NameToLayer("Finish");
        if (collision.gameObject.CompareTag("Finish") || (finishLayer != -1 && collision.gameObject.layer == finishLayer))
        {
            // Deactivate the finish object and the player before triggering level complete
            collision.gameObject.SetActive(false);
            gameObject.SetActive(false);
            GameManager.Instance.LevelComplete();
            return;
        }

        if (collision.gameObject.CompareTag("Start")) return;

        // Safe landing surfaces & Checkpoint layer objects do not deal collision damage
        int checkpointLayer = LayerMask.NameToLayer("Checkpoint");
        if (checkpointLayer != -1 && collision.gameObject.layer == checkpointLayer)
        {
            return;
        }

        if (collision.gameObject.GetComponentInParent<CheckpointZone>() != null ||
            collision.gameObject.name.IndexOf("Checkpoint", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            collision.gameObject.name.IndexOf("LandingPad", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
        }

        Vector3 impactPoint = collision.contactCount > 0 ? collision.contacts[0].point : transform.position;
        Hazard hazard = collision.gameObject.GetComponent<Hazard>();

        if (hazard != null)
        {
            float obstacleRecoil = config != null ? config.ObstacleRecoilForce : 15f;
            ApplyRecoil(collision, obstacleRecoil);

            switch (hazard.Type)
            {
                case HazardType.Sharp:
                    // Sharp = puncture: almost entirely structural (rocket HP)
                    TakeDamage(hazard.Damage, impactPoint, engineShare: 0.05f);
                    break;
                case HazardType.Explosive:
                    Explode(hazard);
                    break;
                // Crushing handled in OnCollisionStay
            }
        }
        else
        {
            // Generic wall / obstacle: mostly structural, minor engine vibration
            float wallRecoil = config != null ? config.WallRecoilForce : 8f;
            ApplyRecoil(collision, wallRecoil);

            float share = config != null ? config.CollisionEngineShare : 0.1f;
            TakeDamage(config != null ? config.DamageRate : 20f, impactPoint, engineShare: share);
        }
    }
    /// Applies an instant bounce/recoil impulse away from the collision surface.
    /// Eliminates sticking by nullifying incoming velocity and kicking the rocket back.
 
    private void ApplyRecoil(Collision collision, float recoilForce)
    {
        if (rb == null || recoilForce <= 0f || isDead) return;

        Vector3 normal = Vector3.zero;
        if (collision.contactCount > 0)
        {
            normal = collision.contacts[0].normal;
        }
        else
        {
            normal = (transform.position - collision.transform.position).normalized;
        }

        // Lock recoil to 2.5D XY plane
        normal.z = 0f;
        if (normal.sqrMagnitude > 0.001f)
        {
            normal.Normalize();
        }
        else
        {
            normal = -transform.up;
        }

        // Cancel velocity pushing into the wall so the bounce is snappy and doesn't get suppressed
        Vector3 currentVel = rb.linearVelocity;
        float dot = Vector3.Dot(currentVel, normal);
        if (dot < 0f)
        {
            rb.linearVelocity = currentVel - normal * dot;
        }

        rb.AddForce(normal * recoilForce, ForceMode.Impulse);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        Hazard hazard = collision.gameObject.GetComponent<Hazard>();
        if (hazard != null && hazard.Type == HazardType.Crushing)
        {
            if (Time.time >= nextCrushingDamageTime)
            {
                Vector3 impactPoint = collision.contactCount > 0 ? collision.contacts[0].point : transform.position;
                // Crushing: sustained pressure — slightly more engine stress than a plain wall hit
                TakeDamage(hazard.Damage, impactPoint, engineShare: 0.15f);
                nextCrushingDamageTime = Time.time + hazard.DamageInterval;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        int finishLayer = LayerMask.NameToLayer("Finish");
        if (other.CompareTag("Finish") || (finishLayer != -1 && other.gameObject.layer == finishLayer))
        {
            // Deactivate the finish trigger and the player before triggering level complete
            other.gameObject.SetActive(false);
            gameObject.SetActive(false);
            GameManager.Instance.LevelComplete();
        }
    }

    private void Explode(Hazard hazard)
    {
        Vector3 explosionPos = hazard.transform.position;

        if (SoundEffectsManager.Instance != null)
            SoundEffectsManager.Instance.PlaySFX(SFXType.Explosion);

        Collider[] colliders = Physics.OverlapSphere(explosionPos, hazard.ExplosionRadius);
        foreach (Collider hit in colliders)
        {
            CollisionHandler handler = hit.GetComponent<CollisionHandler>();
            if (handler != null)
            {
                // Explosive: shockwave rattles engine internals more than a plain collision
                handler.TakeDamage(hazard.Damage, explosionPos, engineShare: 0.25f);
            }
        }
    }

    //  Core Damage API ------------------->

    /// Applies damage split between engine and rocket HP pools.
      
    public void TakeDamage(float totalDamage, Vector3 impactPoint,
                           bool  playCrashEffect = true,
                           float engineShare     = 0.1f,
                           RaceContext.DeathCause deathCause = RaceContext.DeathCause.Hazard)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;
        if (isDead) return;

        float engineDamage = totalDamage * engineShare;
        float rocketDamage = totalDamage * (1f - engineShare);

        ApplyEngineDamage(engineDamage, impactPoint, deathCause);
        ApplyRocketDamage(rocketDamage, impactPoint, playCrashEffect, deathCause);
    }

    private void ApplyEngineDamage(float damage, Vector3 impactPoint,
                                   RaceContext.DeathCause deathCause)
    {
        if (isDead || damage <= 0f) return;

        currentEngineHealth = Mathf.Clamp(currentEngineHealth - damage, 0f, MaxEngineHealth);
        float normalized = NormalizedEngineHealth;
        OnEngineHealthChanged?.Invoke(normalized);

        // Engine critical warning (fires once per drop below threshold)
        float critThresh = config != null ? config.EngineCriticalThreshold : 0.2f;
        if (!isEngineCritical && normalized <= critThresh)
        {
            isEngineCritical = true;
            OnEngineCritical?.Invoke();
        }
        else if (isEngineCritical && normalized > critThresh + 0.05f)
        {
            isEngineCritical = false; // hysteresis reset
        }

        // Engine destroyed → explosion death
        if (currentEngineHealth <= 0f)
        {
            isDead = true;
            SetLaserBurnVFX(false);

            if (engineExplosionEffect != null)
            {
                engineExplosionEffect.transform.position = impactPoint;
                engineExplosionEffect.Play();
            }

            CameraShake.Shake(1.0f, 0.4f);

            if (SoundEffectsManager.Instance != null)
                SoundEffectsManager.Instance.PlaySFX(SFXType.Explosion);

            OnEngineDestroyed?.Invoke();

            if (GameManager.Instance != null)
                GameManager.Instance.PlayerDied(deathCause);
        }
    }

    private void ApplyRocketDamage(float damage, Vector3 impactPoint,
                                   bool playCrashEffect,
                                   RaceContext.DeathCause deathCause)
    {
        if (isDead || damage <= 0f) return;

        currentRocketHealth = Mathf.Clamp(currentRocketHealth - damage, 0f, MaxRocketHealth);
        float normalized = NormalizedRocketHealth;
        OnHealthChanged?.Invoke(normalized);

        // Hull critical warning (fires once per drop below threshold)
        float critThresh = config != null ? config.CriticalHealthThreshold : 0.3f;
        if (!isHullCritical && normalized < critThresh)
        {
            isHullCritical = true;
            OnCriticalDamage?.Invoke();
        }
        else if (isHullCritical && normalized >= critThresh + 0.05f)
        {
            isHullCritical = false; // hysteresis reset
        }

        if (playCrashEffect)
        {
            if (crashEffect != null)
            {
                crashEffect.transform.position = impactPoint;
                crashEffect.Play();
            }

            if (SoundEffectsManager.Instance != null)
                SoundEffectsManager.Instance.PlaySFX(SFXType.PlayerDamage);

            CameraShake.Shake(0.5f, 0.2f);
        }

        // Rocket hull destroyed → structural death
        if (currentRocketHealth <= 0f)
        {
            isDead = true;
            SetLaserBurnVFX(false);

            if (crashEffect != null)
            {
                crashEffect.transform.position = impactPoint;
                crashEffect.Play();
            }

            CameraShake.Shake(1.0f, 0.4f);

            if (SoundEffectsManager.Instance != null)
                SoundEffectsManager.Instance.PlaySFX(SFXType.Explosion);

            if (GameManager.Instance != null)
                GameManager.Instance.PlayerDied(deathCause);
        }
    }

    //  Repair API ------------------------->

    /// <summary>
    /// Fully restores both engine HP and rocket HP to their maximums.
    /// Called by RepairPickup — one pickup heals everything.
    /// </summary>
    public void FullRepair()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        currentRocketHealth = MaxRocketHealth;
        currentEngineHealth = MaxEngineHealth;

        isDead         = false;
        isHullCritical = false;
        isEngineCritical = false;
        SetLaserBurnVFX(false);

        OnHealthChanged?.Invoke(1f);
        OnEngineHealthChanged?.Invoke(1f);
    }


    /// Legacy partial-repair path (kept for backward compatibility).
    /// Repairs rocket HP only by the given amount.
    /// For a full restore of both pools, use FullRepair() instead.
 
    public void Repair(float amount)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        currentRocketHealth = Mathf.Clamp(currentRocketHealth + amount, 0f, MaxRocketHealth);
        float normalized = NormalizedRocketHealth;
        OnHealthChanged?.Invoke(normalized);

        float critThresh = config != null ? config.CriticalHealthThreshold : 0.3f;
        if (isHullCritical && normalized >= critThresh + 0.05f)
        {
            isHullCritical = false;
        }
    }

 
    /// Resets both health pools to full. Called by GameManager on scene reload / new run.

    public void ResetHealth()
    {
        currentRocketHealth = MaxRocketHealth;
        currentEngineHealth = MaxEngineHealth;
        isDead           = false;
        isHullCritical   = false;
        isEngineCritical = false;
        SetLaserBurnVFX(false);
        OnHealthChanged?.Invoke(1f);
        OnEngineHealthChanged?.Invoke(1f);
    }

    private void OnDisable()
    {
        SetLaserBurnVFX(false);
    }
}
