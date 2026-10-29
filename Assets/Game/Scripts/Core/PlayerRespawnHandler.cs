using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the full respawn pipeline. Called explicitly by UIManager's death panel buttons.
/// On death (triggered by Continue button):
///  - If a checkpoint has been saved for this scene → teleport player, fully restore
///     health/fuel, reposition missile with per-checkpoint delay, then resume playing.
///  - If no checkpoint saved → reload the scene from the start.
/// </summary>
public class PlayerRespawnHandler : MonoBehaviour
{
    public static PlayerRespawnHandler Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    [Header("Timing")]
    [Tooltip("Seconds to wait after death before teleporting. " +
             "Should cover explosion VFX and any death UI display.")]
    [SerializeField] private float respawnDelay = 2.0f;

    [Header("Player References")]
    [SerializeField] private Rigidbody       playerRigidbody;
    [SerializeField] private CollisionHandler playerCollision;
    [SerializeField] private Fuel            playerFuel;

    private void Start()
    {
        // Auto-find player references if not assigned in Inspector.
        if (playerRigidbody == null || playerCollision == null || playerFuel == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                if (playerRigidbody == null) playerRigidbody = player.GetComponent<Rigidbody>();
                if (playerCollision == null) playerCollision = player.GetComponent<CollisionHandler>()
                    ?? player.GetComponentInChildren<CollisionHandler>();
                if (playerFuel     == null) playerFuel      = player.GetComponent<Fuel>()
                    ?? player.GetComponentInChildren<Fuel>();
            }
        }

        // NOTE: We do NOT subscribe to GameManager.OnPlayerDied.
        // Respawn is now driven explicitly by the death panel buttons in UIManager.
        // If loading a scene with an active saved checkpoint.
        // position the player and missile directly at the saved checkpoint.
        int sceneIdx    = SceneManager.GetActiveScene().buildIndex;
        string sceneName = SceneManager.GetActiveScene().name;

        bool hasSave = CheckpointSaveSystem.HasSavedCheckpoint(sceneName) ||
                       CheckpointSaveSystem.HasSavedCheckpoint(sceneIdx);

        if (hasSave)
        {
            int savedCheckpointIdx = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneName);
            Vector3 spawnPos       = CheckpointSaveSystem.GetSavedSpawnPosition(sceneName);

            if (savedCheckpointIdx < 0)
            {
                savedCheckpointIdx = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneIdx);
                spawnPos           = CheckpointSaveSystem.GetSavedSpawnPosition(sceneIdx);
            }

            if (savedCheckpointIdx >= 0)
            {
                if (playerRigidbody != null)
                {
                    playerRigidbody.isKinematic     = true;
                    playerRigidbody.position        = spawnPos;
                    playerRigidbody.rotation        = Quaternion.identity;
                    playerRigidbody.transform.rotation = Quaternion.identity;
                    playerRigidbody.linearVelocity  = Vector3.zero;
                    playerRigidbody.angularVelocity = Vector3.zero;
                    playerRigidbody.isKinematic     = false;
                }
                ResetMissile(spawnPos, sceneIdx, sceneName);
                Debug.Log($"[PlayerRespawnHandler] Scene started at saved checkpoint {savedCheckpointIdx} → {spawnPos}");
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Respawn Trigger --------->

   
    /// Called by UIManager.OnDeathContinueClicked().
    /// Respawns at the last checkpoint, or reloads the scene if none is saved.
  
    public void TriggerRespawn()
    {
        int sceneIdx    = SceneManager.GetActiveScene().buildIndex;
        string sceneName = SceneManager.GetActiveScene().name;

        bool hasSave = CheckpointSaveSystem.HasSavedCheckpoint(sceneName) ||
                       CheckpointSaveSystem.HasSavedCheckpoint(sceneIdx);

        if (hasSave)
        {
            StartCoroutine(RespawnCoroutine(sceneIdx, sceneName));
        }
        else
        {
            // No checkpoint reached yet — reload the full scene.
            StartCoroutine(ReloadAfterDelay());
        }
    }

    // ── Respawn Coroutine ─────────────────────────────────────────────────────

    private IEnumerator RespawnCoroutine(int sceneIdx, string sceneName)
    {
        // Wait for death VFX / UI.
        yield return new WaitForSecondsRealtime(respawnDelay);

        int checkpointIdx = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneName);
        Vector3 spawnPos  = CheckpointSaveSystem.GetSavedSpawnPosition(sceneName);

        if (checkpointIdx < 0)
        {
            checkpointIdx = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneIdx);
            spawnPos      = CheckpointSaveSystem.GetSavedSpawnPosition(sceneIdx);
        }

        // Teleport player, reset rotation to 0, and zero out velocity.
        if (playerRigidbody != null)
        {
            playerRigidbody.isKinematic     = true;
            playerRigidbody.position        = spawnPos;
            playerRigidbody.rotation        = Quaternion.identity;
            playerRigidbody.transform.rotation = Quaternion.identity;
            playerRigidbody.linearVelocity  = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
            playerRigidbody.isKinematic     = false;
        }

        // Fully restore player health (both rocket and engine HP).
        playerCollision?.ResetHealth();

        //  Fully restore fuel.
        playerFuel?.ResetFuel();

        // Reposition missile with per-checkpoint spawn overrides.
        ResetMissile(spawnPos, sceneIdx, sceneName);

        // Reset local hazards near the spawn zone (stop drifting asteroids & reset laser timings)
        ResetNearbyHazards(spawnPos, 35f);

        // Reset all consumable pickups in the level so the player has resources on retries
        FuelPickup.ResetAll();
        RepairPickup.ResetAll();

        // Close Death Panel and restore Time.timeScale
        PanelManager.Instance?.CloseActiveModal();
        UIManager.Instance?.SetGamePanelActive(true);

        // Resume game — also auto-resets engine heat via EngineHeat.HandleStateChanged.
        GameManager.Instance?.UpdateState(GameState.Playing);

        Debug.Log($"[PlayerRespawnHandler] Respawned at checkpoint {checkpointIdx} → {spawnPos}");
    }

    private void ResetNearbyHazards(Vector3 spawnPos, float radius)
    {
        Collider[] hits = Physics.OverlapSphere(spawnPos, radius);
        foreach (var hit in hits)
        {
            if (hit.attachedRigidbody != null && hit.attachedRigidbody != playerRigidbody)
            {
                hit.attachedRigidbody.linearVelocity  = Vector3.zero;
                hit.attachedRigidbody.angularVelocity = Vector3.zero;
            }
        }
    }

    private IEnumerator ReloadAfterDelay()
    {
        yield return new WaitForSecondsRealtime(respawnDelay);
        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene(SceneManager.GetActiveScene().name);
        else if (LevelManager.Instance != null)
            LevelManager.Instance.ReloadScene();
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Missile Reset -------------->

    private void ResetMissile(Vector3 playerSpawnPos, int sceneIdx, string sceneName)
    {
        if (MissileAI.Instance == null) return;

        // Read missile spawn overrides saved by CheckpointZone.
        Vector3 missilePos;
        if (CheckpointSaveSystem.HasMissileOverride(sceneName))
            missilePos = CheckpointSaveSystem.GetMissileSpawnPosition(sceneName);
        else if (CheckpointSaveSystem.HasMissileOverride(sceneIdx))
            missilePos = CheckpointSaveSystem.GetMissileSpawnPosition(sceneIdx);
        else
            missilePos = playerSpawnPos + Vector3.down * 40f;  // fallback: 40 units below player

        float missileDelay = CheckpointSaveSystem.HasSavedCheckpoint(sceneName)
            ? CheckpointSaveSystem.GetMissileSpawnDelay(sceneName)
            : CheckpointSaveSystem.GetMissileSpawnDelay(sceneIdx);

        MissileAI.Instance.ResetToPosition(missilePos, missileDelay);
    }
}
