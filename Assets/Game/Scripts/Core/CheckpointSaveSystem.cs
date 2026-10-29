using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single source of truth for checkpoint persistence across scenes.
/// Survives scene loads via DontDestroyOnLoad so the menu scene can read
/// the same data that the level scene writes.
/// Supports querying and saving by scene build index OR scene name for
/// maximum resilience across build settings changes.
/// </summary>
public class CheckpointSaveSystem : MonoBehaviour
{
    public static CheckpointSaveSystem Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Key Helpers (Numeric Build Index) ─────────────────────────────────────

    private static string IdxKey(int sceneIndex)    => $"CP_Idx_{sceneIndex}";
    private static string PosXKey(int sceneIndex)   => $"CP_PosX_{sceneIndex}";
    private static string PosYKey(int sceneIndex)   => $"CP_PosY_{sceneIndex}";
    private static string PosZKey(int sceneIndex)   => $"CP_PosZ_{sceneIndex}";
    private static string MisXKey(int sceneIndex)   => $"CP_MisX_{sceneIndex}";
    private static string MisYKey(int sceneIndex)   => $"CP_MisY_{sceneIndex}";
    private static string MisZKey(int sceneIndex)   => $"CP_MisZ_{sceneIndex}";
    private static string MisDlyKey(int sceneIndex) => $"CP_MisDly_{sceneIndex}";

    // Key Helpers (Scene Name) ------------------->

    private static string IdxKey(string sceneName)    => $"CP_Idx_Name_{sceneName}";
    private static string PosXKey(string sceneName)   => $"CP_PosX_Name_{sceneName}";
    private static string PosYKey(string sceneName)   => $"CP_PosY_Name_{sceneName}";
    private static string PosZKey(string sceneName)   => $"CP_PosZ_Name_{sceneName}";
    private static string MisXKey(string sceneName)   => $"CP_MisX_Name_{sceneName}";
    private static string MisYKey(string sceneName)   => $"CP_MisY_Name_{sceneName}";
    private static string MisZKey(string sceneName)   => $"CP_MisZ_Name_{sceneName}";
    private static string MisDlyKey(string sceneName) => $"CP_MisDly_Name_{sceneName}";

    // Public API (Int sceneIndex) ------------------->

    
    /// Saves the checkpoint index and player spawn position for a scene by build index.
    /// Also mirrors save to the active scene name if available.
   
    public static void SaveCheckpoint(int sceneIndex, int checkpointIndex, Vector3 spawnPos)
    {
        int current = GetSavedCheckpointIndex(sceneIndex);
        if (checkpointIndex <= current) return;

        PlayerPrefs.SetInt(IdxKey(sceneIndex),   checkpointIndex);
        PlayerPrefs.SetFloat(PosXKey(sceneIndex), spawnPos.x);
        PlayerPrefs.SetFloat(PosYKey(sceneIndex), spawnPos.y);
        PlayerPrefs.SetFloat(PosZKey(sceneIndex), spawnPos.z);

        // Mirror save to active scene name
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(activeScene.name))
        {
            PlayerPrefs.SetInt(IdxKey(activeScene.name),   checkpointIndex);
            PlayerPrefs.SetFloat(PosXKey(activeScene.name), spawnPos.x);
            PlayerPrefs.SetFloat(PosYKey(activeScene.name), spawnPos.y);
            PlayerPrefs.SetFloat(PosZKey(activeScene.name), spawnPos.z);
        }

        PlayerPrefs.Save();

        Debug.Log($"[CheckpointSaveSystem] Saved checkpoint {checkpointIndex} " +
                  $"at {spawnPos} for scene index {sceneIndex} (scene: '{activeScene.name}').");
    }

   
    /// Saves the checkpoint index and player spawn position explicitly for both scene index and name.
    
    public static void SaveCheckpoint(int sceneIndex, string sceneName, int checkpointIndex, Vector3 spawnPos)
    {
        int current = GetSavedCheckpointIndex(sceneIndex);
        if (checkpointIndex <= current && (!string.IsNullOrEmpty(sceneName) && checkpointIndex <= GetSavedCheckpointIndex(sceneName)))
            return;

        PlayerPrefs.SetInt(IdxKey(sceneIndex),   checkpointIndex);
        PlayerPrefs.SetFloat(PosXKey(sceneIndex), spawnPos.x);
        PlayerPrefs.SetFloat(PosYKey(sceneIndex), spawnPos.y);
        PlayerPrefs.SetFloat(PosZKey(sceneIndex), spawnPos.z);

        if (!string.IsNullOrEmpty(sceneName))
        {
            PlayerPrefs.SetInt(IdxKey(sceneName),   checkpointIndex);
            PlayerPrefs.SetFloat(PosXKey(sceneName), spawnPos.x);
            PlayerPrefs.SetFloat(PosYKey(sceneName), spawnPos.y);
            PlayerPrefs.SetFloat(PosZKey(sceneName), spawnPos.z);
        }

        PlayerPrefs.Save();

        Debug.Log($"[CheckpointSaveSystem] Saved checkpoint {checkpointIndex} " +
                  $"at {spawnPos} for scene '{sceneName}' (index {sceneIndex}).");
    }

    /// True if a checkpoint has been saved for the given scene build index.
    public static bool HasSavedCheckpoint(int sceneIndex) =>
        PlayerPrefs.HasKey(IdxKey(sceneIndex)) && PlayerPrefs.GetInt(IdxKey(sceneIndex)) >= 0;

    /// True if a checkpoint has been saved for the given scene name.
    public static bool HasSavedCheckpoint(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        if (PlayerPrefs.HasKey(IdxKey(sceneName)) && PlayerPrefs.GetInt(IdxKey(sceneName)) >= 0)
            return true;

        // Fallback - search build settings for matching scene name
        int sceneCount = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < sceneCount; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.Contains(sceneName) && HasSavedCheckpoint(i))
                return true;
        }
        return false;
    }

    /// Returns the saved checkpoint index for a scene build index, or -1 if none saved.
    public static int GetSavedCheckpointIndex(int sceneIndex) =>
        PlayerPrefs.HasKey(IdxKey(sceneIndex)) ? PlayerPrefs.GetInt(IdxKey(sceneIndex)) : -1;

    /// Returns the saved checkpoint index for a scene name, or -1 if none saved.
    public static int GetSavedCheckpointIndex(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return -1;
        if (PlayerPrefs.HasKey(IdxKey(sceneName)))
            return PlayerPrefs.GetInt(IdxKey(sceneName));

        int sceneCount = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < sceneCount; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.Contains(sceneName) && HasSavedCheckpoint(i))
                return GetSavedCheckpointIndex(i);
        }
        return -1;
    }

 
    /// Returns the saved player spawn position for a scene build index.
    /// Returns Vector3.zero if no checkpoint has been saved.
    
    public static Vector3 GetSavedSpawnPosition(int sceneIndex)
    {
        if (!HasSavedCheckpoint(sceneIndex)) return Vector3.zero;
        return new Vector3(
            PlayerPrefs.GetFloat(PosXKey(sceneIndex)),
            PlayerPrefs.GetFloat(PosYKey(sceneIndex)),
            PlayerPrefs.GetFloat(PosZKey(sceneIndex)));
    }

   
    /// Returns the saved player spawn position for a scene name.
   
    public static Vector3 GetSavedSpawnPosition(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return Vector3.zero;
        if (PlayerPrefs.HasKey(PosXKey(sceneName)))
        {
            return new Vector3(
                PlayerPrefs.GetFloat(PosXKey(sceneName)),
                PlayerPrefs.GetFloat(PosYKey(sceneName)),
                PlayerPrefs.GetFloat(PosZKey(sceneName)));
        }

        int sceneCount = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < sceneCount; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.Contains(sceneName) && HasSavedCheckpoint(i))
                return GetSavedSpawnPosition(i);
        }
        return Vector3.zero;
    }

    // Missile Override API -------------------->

    /// <summary>
    /// Saves the missile spawn position AND delay for a scene.
    /// </summary>
    public static void SaveMissileOverride(int sceneIndex, Vector3 missilePos, float missileDelay)
    {
        PlayerPrefs.SetFloat(MisXKey(sceneIndex),   missilePos.x);
        PlayerPrefs.SetFloat(MisYKey(sceneIndex),   missilePos.y);
        PlayerPrefs.SetFloat(MisZKey(sceneIndex),   missilePos.z);
        PlayerPrefs.SetFloat(MisDlyKey(sceneIndex), Mathf.Max(0f, missileDelay));

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(activeScene.name))
        {
            PlayerPrefs.SetFloat(MisXKey(activeScene.name),   missilePos.x);
            PlayerPrefs.SetFloat(MisYKey(activeScene.name),   missilePos.y);
            PlayerPrefs.SetFloat(MisZKey(activeScene.name),   missilePos.z);
            PlayerPrefs.SetFloat(MisDlyKey(activeScene.name), Mathf.Max(0f, missileDelay));
        }

        PlayerPrefs.Save();
    }

    
    /// Saves only the missile spawn delay (no position override).
    
    public static void SaveMissileDelay(int sceneIndex, float missileDelay)
    {
        PlayerPrefs.SetFloat(MisDlyKey(sceneIndex), Mathf.Max(0f, missileDelay));

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(activeScene.name))
        {
            PlayerPrefs.SetFloat(MisDlyKey(activeScene.name), Mathf.Max(0f, missileDelay));
        }

        PlayerPrefs.Save();
    }

    ///True if a missile spawn POSITION override has been saved for this scene index.
    public static bool HasMissileOverride(int sceneIndex) =>
        PlayerPrefs.HasKey(MisXKey(sceneIndex));

    /// True if a missile spawn POSITION override has been saved for this scene name.
    public static bool HasMissileOverride(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        if (PlayerPrefs.HasKey(MisXKey(sceneName))) return true;

        int sceneCount = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < sceneCount; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.Contains(sceneName) && HasMissileOverride(i))
                return true;
        }
        return false;
    }

  
    /// Returns the saved missile spawn position override for a scene index.
   
    public static Vector3 GetMissileSpawnPosition(int sceneIndex) =>
        new Vector3(
            PlayerPrefs.GetFloat(MisXKey(sceneIndex)),
            PlayerPrefs.GetFloat(MisYKey(sceneIndex)),
            PlayerPrefs.GetFloat(MisZKey(sceneIndex)));

 
    /// Returns the saved missile spawn position override for a scene name.
    
    public static Vector3 GetMissileSpawnPosition(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return Vector3.zero;
        if (PlayerPrefs.HasKey(MisXKey(sceneName)))
        {
            return new Vector3(
                PlayerPrefs.GetFloat(MisXKey(sceneName)),
                PlayerPrefs.GetFloat(MisYKey(sceneName)),
                PlayerPrefs.GetFloat(MisZKey(sceneName)));
        }

        int sceneCount = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < sceneCount; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.Contains(sceneName) && HasMissileOverride(i))
                return GetMissileSpawnPosition(i);
        }
        return Vector3.zero;
    }

  
    /// Returns the saved missile spawn delay for a scene index.

    public static float GetMissileSpawnDelay(int sceneIndex) =>
        PlayerPrefs.HasKey(MisDlyKey(sceneIndex))
            ? PlayerPrefs.GetFloat(MisDlyKey(sceneIndex))
            : 10f;

   
    /// Returns the saved missile spawn delay for a scene name.
   
    public static float GetMissileSpawnDelay(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return 10f;
        if (PlayerPrefs.HasKey(MisDlyKey(sceneName)))
            return PlayerPrefs.GetFloat(MisDlyKey(sceneName));

        int sceneCount = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < sceneCount; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.Contains(sceneName) && PlayerPrefs.HasKey(MisDlyKey(i)))
                return GetMissileSpawnDelay(i);
        }
        return 10f;
    }

   
    /// Clears the missile POSITION override only (keeps the delay).
  
    public static void ClearMissilePositionOverride(int sceneIndex)
    {
        PlayerPrefs.DeleteKey(MisXKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisYKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisZKey(sceneIndex));

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(activeScene.name))
        {
            PlayerPrefs.DeleteKey(MisXKey(activeScene.name));
            PlayerPrefs.DeleteKey(MisYKey(activeScene.name));
            PlayerPrefs.DeleteKey(MisZKey(activeScene.name));
        }

        PlayerPrefs.Save();
    }

  
    /// Clears ALL missile override data (position + delay) for a scene index.
    
    public static void ClearMissileOverride(int sceneIndex)
    {
        PlayerPrefs.DeleteKey(MisXKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisYKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisZKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisDlyKey(sceneIndex));

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(activeScene.name))
        {
            PlayerPrefs.DeleteKey(MisXKey(activeScene.name));
            PlayerPrefs.DeleteKey(MisYKey(activeScene.name));
            PlayerPrefs.DeleteKey(MisZKey(activeScene.name));
            PlayerPrefs.DeleteKey(MisDlyKey(activeScene.name));
        }

        PlayerPrefs.Save();
    }

    // Reset API ------------------>

   
    /// Clears the saved checkpoint for a specific scene build index.
    /// Also clears any matching scene name keys if the scene is active.
  
    public static void ResetCheckpoint(int sceneIndex)
    {
        PlayerPrefs.DeleteKey(IdxKey(sceneIndex));
        PlayerPrefs.DeleteKey(PosXKey(sceneIndex));
        PlayerPrefs.DeleteKey(PosYKey(sceneIndex));
        PlayerPrefs.DeleteKey(PosZKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisXKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisYKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisZKey(sceneIndex));
        PlayerPrefs.DeleteKey(MisDlyKey(sceneIndex));

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex == sceneIndex && !string.IsNullOrEmpty(activeScene.name))
        {
            ResetCheckpointKeysForName(activeScene.name);
        }

        PlayerPrefs.Save();
        Debug.Log($"[CheckpointSaveSystem] Cleared checkpoint for scene index {sceneIndex}.");
    }

   
    /// Clears the saved checkpoint for a specific scene name.
   
    public static void ResetCheckpoint(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        ResetCheckpointKeysForName(sceneName);

        // Also find build index for this scene name and clear numeric keys
        int sceneCount = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < sceneCount; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (path.Contains(sceneName))
            {
                PlayerPrefs.DeleteKey(IdxKey(i));
                PlayerPrefs.DeleteKey(PosXKey(i));
                PlayerPrefs.DeleteKey(PosYKey(i));
                PlayerPrefs.DeleteKey(PosZKey(i));
                PlayerPrefs.DeleteKey(MisXKey(i));
                PlayerPrefs.DeleteKey(MisYKey(i));
                PlayerPrefs.DeleteKey(MisZKey(i));
                PlayerPrefs.DeleteKey(MisDlyKey(i));
            }
        }

        PlayerPrefs.Save();
        Debug.Log($"[CheckpointSaveSystem] Cleared checkpoint for scene '{sceneName}'.");
    }

    private static void ResetCheckpointKeysForName(string sceneName)
    {
        PlayerPrefs.DeleteKey(IdxKey(sceneName));
        PlayerPrefs.DeleteKey(PosXKey(sceneName));
        PlayerPrefs.DeleteKey(PosYKey(sceneName));
        PlayerPrefs.DeleteKey(PosZKey(sceneName));
        PlayerPrefs.DeleteKey(MisXKey(sceneName));
        PlayerPrefs.DeleteKey(MisYKey(sceneName));
        PlayerPrefs.DeleteKey(MisZKey(sceneName));
        PlayerPrefs.DeleteKey(MisDlyKey(sceneName));
    }

    /// Clears ALL saved checkpoints across every scene.
    public static void ResetAll()
    {
        int sceneCount = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < sceneCount; i++)
            ResetCheckpoint(i);
        PlayerPrefs.Save();
        Debug.Log("[CheckpointSaveSystem] All checkpoints cleared.");
    }
}
