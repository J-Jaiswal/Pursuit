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
    public static event Action OnMissileCaughtPlayer; // forwarded from MissileAI for clean external subscription
    public bool MissileDidCatchOnFinalHit { get; set; } // set by RaceContext; read by PostRaceDebrief for debrief cause

    [Tooltip("When true, LevelComplete does NOT auto-load the next scene. PostRaceDebrief.OnContinueClicked() calls LoadNextScene() instead.")]
    [SerializeField] private bool waitForDebrief = true;

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
        DontDestroyOnLoad(gameObject);
    }

    public void UpdateState(GameState newState)
    {
        currentState = newState;
        OnStateChanged?.Invoke(currentState);
    }

    public void PlayerDied()
    {
        if (currentState != GameState.Playing) return;
        StartCoroutine(PlayerDiedCoroutine());
    }

    private IEnumerator PlayerDiedCoroutine()
    {
        UpdateState(GameState.Dead);
        OnPlayerDied?.Invoke();

        float duration = config != null ? config.DeathSequenceDuration : 1.5f;
        yield return new WaitForSeconds(duration);

        // Check if level manager checkpoint exists
        if (LevelManager.Instance != null && LevelManager.Instance.HasCheckpoint)
        {
            LevelManager.Instance.RespawnPlayer();
        }
        else
        {
            ReloadScene();
        }
    }

    public void LevelComplete()
    {
        if (currentState != GameState.Playing) return;
        StartCoroutine(LevelCompleteCoroutine());
    }

    private IEnumerator LevelCompleteCoroutine()
    {
        UpdateState(GameState.LevelComplete);
        OnLevelComplete?.Invoke();

        if (!waitForDebrief)
        {
            float duration = config != null ? config.LandingSequenceDuration : 1.0f;
            yield return new WaitForSeconds(duration);
            LoadNextScene();
        }
        // else: PostRaceDebrief.OnContinueClicked() calls LoadNextScene()
        yield break;
    }

    private void ReloadScene()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void LoadNextScene()
    {
        int next = SceneManager.GetActiveScene().buildIndex + 1;
        if (next > SceneManager.sceneCountInBuildSettings - 1) next = 0;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(next);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UpdateState(GameState.Playing);
    }
}
