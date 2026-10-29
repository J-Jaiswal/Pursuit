using System;
using UnityEngine;

/// <summary>
/// Handles catch detection and the detonation sequence (VFX, SFX, damage, cleanup).
/// (Pure C#) MissileAI ticks this every FixedUpdate and checks HasDetonated to stop
/// moving/steering once the missile is gone.
/// </summary>
public class MissileCombat
{
    private readonly GameObject missileObject;
    private readonly MissileConfigSO config;
    private readonly Rigidbody rb;

    public event Action OnCaughtPlayer;
    public event Action OnDetonationStarted;

    private float catchCooldownRemaining;
    public bool HasDetonated { get; private set; }

    public MissileCombat(GameObject missileObject, MissileConfigSO config, Rigidbody rb)
    {
        this.missileObject = missileObject;
        this.config = config;
        this.rb = rb;
    }

    public void Tick(float dt)
    {
        if (catchCooldownRemaining > 0f) catchCooldownRemaining -= dt;
    }

    /// <summary>Cheap, layer-matrix-independent distance check , call every FixedUpdate.</summary>
    public void CheckProximityCatch(float distanceToPlayer, Vector3 playerPos,
        CollisionHandler playerCollision, float catchRadiusMultiplier)
    {
        if (HasDetonated || catchCooldownRemaining > 0f) return;

        float catchRadius = config != null ? config.CatchRadius * catchRadiusMultiplier : 1f;
        if (distanceToPlayer <= catchRadius)
        {
            catchCooldownRemaining = config != null ? config.CatchCooldown : 0.1f;
            Detonate(playerPos, playerCollision);
        }
    }

    public void Detonate(Vector3 impactPoint, CollisionHandler playerCollision)
    {
        if (HasDetonated) return;
        HasDetonated = true;
        OnDetonationStarted?.Invoke();

        if (config != null && config.ExplosionVFXPrefab != null)
            UnityEngine.Object.Instantiate(config.ExplosionVFXPrefab, impactPoint, Quaternion.identity);

        if (SoundEffectsManager.Instance != null)
            SoundEffectsManager.Instance.PlaySFX(SFXType.Explosion, impactPoint);
        else if (config != null && config.ExplosionSFX != null)
            AudioSource.PlayClipAtPoint(config.ExplosionSFX, impactPoint);

        CameraShake.Shake(1.0f, 0.4f);
        OnCaughtPlayer?.Invoke();

        if (playerCollision != null)
        {
            playerCollision.TakeDamage(99999f, impactPoint, playCrashEffect: false,
                                        deathCause: RaceContext.DeathCause.Missile);
        }
        else
        {
            Debug.LogError("[MissileCombat] Detonate � playerCollision is NULL, damage not applied.");
        }

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
        }

        foreach (var r in missileObject.GetComponentsInChildren<Renderer>())
        {
            if (r.GetComponent<ParticleSystem>() != null) continue; // let explosion particles finish
            r.enabled = false;
        }
        foreach (var c in missileObject.GetComponentsInChildren<Collider>())
            c.enabled = false;

        // NOTE: Do NOT Destroy(missileObject) here!
        // 1. Allows Nova's death/caught AudioSource to finish playing without being cut off.
        // 2. Allows PlayerRespawnHandler to reposition and reuse the missile on checkpoint respawn.
    }

    /// <summary>
    /// Resets the detonation state so the missile can be repositioned and reused
    /// after a checkpoint respawn. Re-enables all renderers and colliders.
    /// Called by MissileAI.ResetToPosition() — must be called BEFORE Destroy() fires.
    /// </summary>
    public void ResetDetonation()
    {
        HasDetonated           = false;
        catchCooldownRemaining = 0f;

        foreach (var r in missileObject.GetComponentsInChildren<Renderer>())
            r.enabled = true;
        foreach (var c in missileObject.GetComponentsInChildren<Collider>())
            c.enabled = true;
    }
}