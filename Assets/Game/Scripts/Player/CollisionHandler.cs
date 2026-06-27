using System;
using UnityEngine;

public class CollisionHandler : MonoBehaviour
{
    [SerializeField] private RocketConfigSO config;
    [SerializeField] private ParticleSystem crashEffect;
    [SerializeField] private AudioSource damageAudio;

    public event Action<float> OnHealthChanged;
    public event Action OnCriticalDamage;

    private float currentHealth;
    private bool isCritical;
    private float nextCrushingDamageTime;

    private void Start()
    {
        currentHealth = config != null ? config.MaxHealth : 100f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        if (collision.gameObject.CompareTag("Finish"))
        {
            GameManager.Instance.LevelComplete();
            return;
        }

        if (collision.gameObject.CompareTag("Start")) return;

        Vector3 impactPoint = collision.contactCount > 0 ? collision.contacts[0].point : transform.position;
        Hazard hazard = collision.gameObject.GetComponent<Hazard>();

        if (hazard != null)
        {
            if (hazard.Type == HazardType.Sharp)
            {
                TakeDamage(hazard.Damage, impactPoint);
            }
            else if (hazard.Type == HazardType.Explosive)
            {
                Explode(hazard);
            }
        }
        else
        {
            if (config != null)
            {
                TakeDamage(config.DamageRate, impactPoint);
            }
        }
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
                TakeDamage(hazard.Damage, impactPoint);
                nextCrushingDamageTime = Time.time + hazard.DamageInterval;
            }
        }
    }

    private void Explode(Hazard hazard)
    {
        Vector3 explosionPos = hazard.transform.position;
        Collider[] colliders = Physics.OverlapSphere(explosionPos, hazard.ExplosionRadius);
        foreach (Collider hit in colliders)
        {
            CollisionHandler handler = hit.GetComponent<CollisionHandler>();
            if (handler != null)
            {
                handler.TakeDamage(hazard.Damage, explosionPos);
            }
        }
    }

    public void TakeDamage(float damage, Vector3 impactPoint, bool playCrashEffect = true)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, config != null ? config.MaxHealth : 100f);

        float normalizedHealth = (config != null && config.MaxHealth > 0f) ? currentHealth / config.MaxHealth : 0f;
        OnHealthChanged?.Invoke(normalizedHealth);

        if (config != null && normalizedHealth < config.CriticalHealthThreshold)
        {
            if (!isCritical)
            {
                isCritical = true;
                OnCriticalDamage?.Invoke();
            }
        }

        if (playCrashEffect)
        {
            if (crashEffect != null)
            {
                crashEffect.transform.position = impactPoint;
                crashEffect.Play();
            }

            if (damageAudio != null)
            {
                damageAudio.Play();
            }

            CameraShake.Shake(0.5f, 0.2f);
        }

        if (currentHealth <= 0f)
        {
            GameManager.Instance.PlayerDied();
        }
    }

    public void Repair(float amount)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        currentHealth += amount;
        float maxH = config != null ? config.MaxHealth : 100f;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxH);

        float normalizedHealth = currentHealth / maxH;
        OnHealthChanged?.Invoke(normalizedHealth);

        if (config != null && normalizedHealth >= config.CriticalHealthThreshold)
        {
            isCritical = false;
        }
    }

    public void ResetHealth()
    {
        currentHealth = config != null ? config.MaxHealth : 100f;
        isCritical = false;
        OnHealthChanged?.Invoke(1f);
    }
}
