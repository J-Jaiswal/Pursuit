  using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Trigger zone placed in the level. When the player enters it:
///   1. Player and missile spawn positions are saved to CheckpointSaveSystem (persistent).
///   2. LevelManager is notified to apply the phase's missile difficulty settings.
///   3. The zone deactivates itself (one-shot trigger).
/// </summary>
public class CheckpointZone : MonoBehaviour
{
    [Tooltip("Unique ascending index for this checkpoint (0 = first, 1 = second, etc.). " +
             "Must match the index used in LevelManager.phases[].")]
    [SerializeField] private int checkpointIndex;

    [Header("Spawn Points")]
    [Tooltip("Where the player teleports to when dying after reaching this checkpoint.")]
    [SerializeField] private Transform playerSpawnPoint;

    [Tooltip("Where the missile repositions after the player respawns at this checkpoint. " +
             "Leave null to fall back to 40 units below the player spawn point.")]
    [SerializeField] private Transform missileSpawnPoint;

    [Tooltip("Seconds before the missile activates after a checkpoint respawn.")]
    [SerializeField] [Min(0f)] private float missileSpawnDelay = 10f;

    
    private Collider zoneCollider;

    private void Awake()
    {
        zoneCollider = GetComponent<Collider>();
    }

    private void Start()
    {
        int sceneIdx    = SceneManager.GetActiveScene().buildIndex;
        string sceneName = SceneManager.GetActiveScene().name;
        int saved       = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneName);
        if (saved < 0)
            saved = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneIdx);

        if (saved >= checkpointIndex)
        {
            if (zoneCollider != null)
                zoneCollider.enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        int sceneIdx     = SceneManager.GetActiveScene().buildIndex;
        string sceneName = SceneManager.GetActiveScene().name;

        // Determine player spawn position — fallback to this zone's own position.
        Vector3 playerPos = playerSpawnPoint != null
            ? playerSpawnPoint.position
            : transform.position;

        // 1. Save player spawn (and checkpoint index) persistently.
        CheckpointSaveSystem.SaveCheckpoint(sceneIdx, sceneName, checkpointIndex, playerPos);

        // 2. Save missile spawn override if one is assigned.
        if (missileSpawnPoint != null)
        {
            CheckpointSaveSystem.SaveMissileOverride(sceneIdx, missileSpawnPoint.position, missileSpawnDelay);
        }
        else
        {
            // No explicit missile spawn — save only the delay, clear any old position override.
            CheckpointSaveSystem.SaveMissileDelay(sceneIdx, missileSpawnDelay);
            CheckpointSaveSystem.ClearMissilePositionOverride(sceneIdx);
        }

        // 3. Apply phase difficulty and update in-memory checkpoint index.
        LevelManager.Instance?.RegisterCheckpoint(checkpointIndex);

        // 4. Notify UI to display Checkpoint Reached pulsing banner and play chime audio
        UIManager.Instance?.ShowCheckpointReached();
        SoundEffectsManager.Instance?.PlaySFX(SFXType.LevelStart);

        Debug.Log($"[CheckpointZone] Checkpoint {checkpointIndex} reached. " +
                  $"PlayerSpawn: {playerPos}, " +
                  $"MissileSpawn: {(missileSpawnPoint != null ? missileSpawnPoint.position.ToString() : "default")}, " +
                  $"MissileDelay: {missileSpawnDelay}s");

        // One-shot — disable this zone's collider so it can't be triggered again, keeping visuals active.
        if (zoneCollider != null)
            zoneCollider.enabled = false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw player spawn point (green sphere)
        if (playerSpawnPoint != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.6f);
            Gizmos.DrawSphere(playerSpawnPoint.position, 0.5f);
            Gizmos.DrawLine(transform.position, playerSpawnPoint.position);
        }

        // Draw missile spawn point (red sphere)
        if (missileSpawnPoint != null)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
            Gizmos.DrawSphere(missileSpawnPoint.position, 0.5f);
            Gizmos.DrawLine(transform.position, missileSpawnPoint.position);
        }
    }
#endif
}
