using UnityEngine;

[RequireComponent(typeof(MissileAI))]
public class MissileCollisionHandler : MonoBehaviour
{
    private MissileConfigSO config;

    private MissileAI missileAI;
    private Transform playerTransform;

    private void Awake()
    {
        missileAI = GetComponent<MissileAI>();
        if (missileAI != null)
            config = missileAI.Config;
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Ignore the player — catch is handled by MissileAI proximity check
        if (collision.gameObject.CompareTag("Player")) return;

        // Ignore the finish line
        if (collision.gameObject.CompareTag("Finish")) return;

        // Anything else is a wall/hazard hit
        HandleWallHit(collision);
    }

    private void HandleWallHit(Collision collision)
    {
        // Tell the state machine
        missileAI.TriggerWallHit();

        // Determine impact point
        Vector3 impactPoint = collision.contactCount > 0
            ? collision.contacts[0].point
            : transform.position;

        PlayExplosionEffect(impactPoint);

        // Blast radius damage to nearby player
        if (playerTransform == null) return;
        float blastR = config != null ? config.BlastRadius : 5f;
        float blastD = config != null ? config.BlastDamage : 20f;

        if (blastR <= 0f) return; // avoid divide-by-zero in falloff calc below

        float distToPlayer = Vector3.Distance(impactPoint, playerTransform.position);
        if (distToPlayer <= blastR)
        {
            CollisionHandler playerCH = playerTransform.GetComponent<CollisionHandler>();
            if (playerCH != null)
            {
                // Scale damage by proximity (full damage at zero distance, falloff to edge).
                // Missile blast: 20% engine stress (shockwave), 80% hull structural damage.
                float falloff = 1f - (distToPlayer / blastR);
                playerCH.TakeDamage(blastD * falloff, impactPoint, engineShare: 0.2f);
            }
        }
    }

    public void PlayExplosionEffect(Vector3 position)
    {
        if (config == null) return;

        if (config.ExplosionVFXPrefab != null)
            Instantiate(config.ExplosionVFXPrefab, position, Quaternion.identity);

        if (SoundEffectsManager.Instance != null)
        {
            SoundEffectsManager.Instance.PlaySFX(SFXType.Explosion, position);
        }
        else if (config.ExplosionSFX != null)
        {
            AudioSource.PlayClipAtPoint(config.ExplosionSFX, position);
        }
    }
}