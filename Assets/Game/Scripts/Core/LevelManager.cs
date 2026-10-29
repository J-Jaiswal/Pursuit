using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages per-level state: which checkpoint the player is currently at,
/// and the spawn configuration for each checkpoint (via PhaseConfigSO).
/// </summary>
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Tooltip("One PhaseConfigSO per checkpoint, ordered by index (0 = first checkpoint reached).")]
    [SerializeField] private PhaseConfigSO[] phases;

    [SerializeField] private MissileAI missile;
    [SerializeField] private Rigidbody playerRigidbody;

    private int attemptCount = 1;

    public static event System.Action<int, string> OnPhaseChanged;

    public int AttemptCount        => attemptCount;
    public int CurrentPhaseIndex   { get; private set; } = 0;
    public int TotalPhases         => phases != null ? phases.Length : 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        GameManager.OnPlayerDied += HandlePlayerDied;

        // Restore the highest checkpoint reached for this scene from persistent save.
        int sceneIdx    = SceneManager.GetActiveScene().buildIndex;
        string sceneName = SceneManager.GetActiveScene().name;
        int saved       = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneName);
        if (saved < 0)
            saved = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneIdx);

        if (saved >= 0 && phases != null && saved < phases.Length)
        {
            CurrentPhaseIndex = saved;
            // Re-apply the missile difficulty for the restored phase.
            if (phases[saved] != null)
                ApplyPhaseDifficulty(phases[saved]);
            Debug.Log($"[LevelManager] Restored to checkpoint {saved} from save.");
        }

        OnPhaseChanged?.Invoke(CurrentPhaseIndex, GetCurrentPhaseName());
    }

    private void OnDestroy()
    {
        GameManager.OnPlayerDied -= HandlePlayerDied;
    }

    private void HandlePlayerDied()
    {
        attemptCount++;
    }

    // Checkpoint Registration ------------->

    /// Called by CheckpointZone when the player enters a checkpoint trigger.
    /// Advances the phase index and applies the new missile difficulty settings.
    public void RegisterCheckpoint(int index)
    {
        if (index <= CurrentPhaseIndex) return; // only advance forward
        CurrentPhaseIndex = index;

        // Apply missile difficulty for this phase.
        if (phases != null && index < phases.Length && phases[index] != null)
            ApplyPhaseDifficulty(phases[index]);

        OnPhaseChanged?.Invoke(CurrentPhaseIndex, GetCurrentPhaseName());
    }

    // Difficulty Application ------------->
    private void ApplyPhaseDifficulty(PhaseConfigSO phase)
    {
        if (missile == null || phase == null) return;
        missile.ApplyDifficultyOverride(
            phase.MissileSpeedMultiplier,
            phase.MissileAvoidanceWeightMultiplier,  // derived from aggression
            phase.MissileCatchRadiusMultiplier);
        Debug.Log($"[LevelManager] Applied difficulty for phase '{phase.PhaseName}': " +
                  $"Speed×{phase.MissileSpeedMultiplier}, " +
                  $"Avoidance×{phase.MissileAvoidanceWeightMultiplier:F2}, " +
                  $"CatchRadius×{phase.MissileCatchRadiusMultiplier}");
    }

    // Phase Data Access --------------->
    /// Returns the PhaseConfigSO for a given checkpoint index.
    /// Used by PlayerRespawnHandler to read spawn point and missile delay overrides.
    
    public PhaseConfigSO GetPhaseConfig(int index)
    {
        if (phases == null || index < 0 || index >= phases.Length) return null;
        return phases[index];
    }

    /// Returns the phase name for the current checkpoint (used by LLM snapshot).
    public string GetCurrentPhaseName()
    {
        if (phases == null || phases.Length == 0) return string.Empty;
        int clampedIndex = Mathf.Clamp(CurrentPhaseIndex, 0, phases.Length - 1);
        PhaseConfigSO phase = phases[clampedIndex];
        return phase != null ? phase.PhaseName : string.Empty;
    }

    // Scene Reload -------------->
    public void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
