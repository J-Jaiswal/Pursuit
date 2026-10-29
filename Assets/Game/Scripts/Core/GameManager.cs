using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState { Playing, Dead, LevelComplete, Paused }

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private RocketConfigSO config;

    public static event Action<GameState> OnStateChanged;
    public static event Action OnPlayerDied;
    public static event Action OnLevelComplete;
    public static event Action OnMissileCaughtPlayer;
    // NOTE: MissileDidCatchOnFinalHit was removed — it was only used by PostRaceDebrief (deprecated).
    public int MaxPhaseReached { get; set; } = 0;

    private GameState currentState = GameState.Playing;
    public GameState CurrentState => currentState;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void UpdateState(GameState newState)
    {
        currentState = newState;
        OnStateChanged?.Invoke(currentState);
    }

    // ── Pause / Resume ────────────────────────────────────────────────────────

    /// <summary>
    /// Pauses the game: freezes physics/time and broadcasts Paused state.
    /// All Update loops that guard on GameState != Playing stop automatically.
    /// </summary>
    public void Pause()
    {
        if (currentState != GameState.Playing) return;
        Time.timeScale = 0f;
        UpdateState(GameState.Paused);
    }

    /// <summary>Resumes from pause.</summary>
    public void Resume()
    {
        if (currentState != GameState.Paused) return;
        Time.timeScale = 1f;
        UpdateState(GameState.Playing);
    }

    // ── Player Death ──────────────────────────────────────────────────────────

    public void PlayerDied(RaceContext.DeathCause cause)
    {
        if (currentState != GameState.Playing) return;

        if (RaceContext.Instance != null)
        {
            RaceContext.Instance.SetDeathCause(cause);
        }

        StartCoroutine(PlayerDiedCoroutine());
    }

    private IEnumerator PlayerDiedCoroutine()
    {
        UpdateState(GameState.Dead);
        OnPlayerDied?.Invoke();

        // Small fixed beat so VFX / audio can play before the snapshot is taken.
        // Note: the fuel-tumble animation waits BEFORE calling PlayerDied, so by
        // the time we arrive here the tumble is already done. Engine and rocket
        // deaths are instant — the 0.5s gives explosion particles time to show.
        yield return new WaitForSeconds(0.5f);

        if (RaceContext.Instance != null)
        {
            RaceContext.Instance.CaptureFinalSnapshot();
        }
    }

    // ── Level Complete ────────────────────────────────────────────────────────

    public void LevelComplete()
    {
        if (currentState != GameState.Playing) return;
        StartCoroutine(LevelCompleteCoroutine());
    }

    private IEnumerator LevelCompleteCoroutine()
    {
        UpdateState(GameState.LevelComplete);
        OnLevelComplete?.Invoke();

        // Clear this level's checkpoint save so next session starts fresh
        CheckpointSaveSystem.ResetCheckpoint(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);

        yield break;
    }

    // ── Scene Loading ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns to the main menu scene (build index 0).
    /// Use this instead of the old LoadNextScene() for all level-complete and
    /// post-race navigation in v1 (single-level build).
    /// </summary>
    public void ReturnToMenu()
    {
        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene("Menu", "Returning to Base...");
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("Menu");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Always restore timeScale — catches carry-over from a paused or slowed state.
        Time.timeScale = 1f;
        UpdateState(GameState.Playing);
    }
}
