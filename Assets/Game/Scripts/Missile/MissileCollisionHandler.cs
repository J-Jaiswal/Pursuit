using UnityEngine;

/// <summary>
/// Handles the missile's physical collision events.
/// On wall hit: notifies MissileAI, spawns explosion particles, deals blast damage to nearby player.
/// Kept separate from MissileAI to maintain a clean separation of concerns.
///
/// SETUP: This component sits on the same Missile GameObject as MissileAI.
/// </summary>
[RequireComponent(typeof(MissileAI))]
public class MissileCollisionHandler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Particle effect spawned at the wall-hit point. Assign an explosion prefab/particle.")]
    [SerializeField] private ParticleSystem explosionParticle;

    [Header("Config")]
    [SerializeField] private MissileConfigSO config;

    private MissileAI missileAI;
    private Transform playerTransform;

    private void Awake()
    {
        missileAI = GetComponent<MissileAI>();
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

        // Spawn explosion effect at impact
        if (explosionParticle != null)
        {
            explosionParticle.transform.position = impactPoint;
            if (!explosionParticle.isPlaying)
                explosionParticle.Play();
        }

        // Blast radius damage to nearby player
        if (playerTransform == null) return;
        float blastR = config != null ? config.BlastRadius   : 5f;
        float blastD = config != null ? config.BlastDamage   : 20f;

        float distToPlayer = Vector3.Distance(impactPoint, playerTransform.position);
        if (distToPlayer <= blastR)
        {
            CollisionHandler playerCH = playerTransform.GetComponent<CollisionHandler>();
            if (playerCH != null)
            {
                // Scale damage by proximity (full damage at zero distance, falloff to edge)
                float falloff = 1f - (distToPlayer / blastR);
                playerCH.TakeDamage(blastD * falloff, impactPoint);
            }
        }
    }
}
