using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Owns the Death Panel text population (cause text & pulsing animation)
/// and button click handlers for Death and Level Complete panels.
///
/// Panel visibility, transitions, and modal exclusivity are owned strictly by PanelManager.
/// </summary>
public class UIPanels : MonoBehaviour
{
    public static UIPanels Instance { get; private set; }

    //  Death Panel UI Elements
    [Header("Death Panel")]
    [Tooltip("Text showing the cause of death (e.g. 'CAUGHT BY MISSILE').")]
    [SerializeField] private TMP_Text   deathCauseText;
    [Tooltip("Restarts from level beginning and clears the checkpoint save.")]
    [SerializeField] private Button     deathRestartButton;
    [Tooltip("Continues from the last saved checkpoint (or from start if none).")]
    [SerializeField] private Button     deathContinueButton;
    [Tooltip("Returns to the main menu (scene 0).")]
    [SerializeField] private Button     deathMenuButton;

    // Level Complete Panel UI Elements
    [Header("Level Complete Panel")]
    [Tooltip("Restarts the level from the beginning.")]
    [SerializeField] private Button     levelCompleteRestartButton;
    [Tooltip("Returns to the main menu.")]
    [SerializeField] private Button     levelCompleteMenuButton;
    [Tooltip("Quits the game.")]
    [SerializeField] private Button     levelCompleteQuitButton;

    // Death Cause Animation Settings 
    [Header("Death Cause Animation Settings")]
    [Tooltip("Minimum scale multiplier for death cause text pulse.")]
    [SerializeField] private float deathCausePulseMinScale = 0.92f;
    [Tooltip("Maximum scale multiplier for death cause text pulse.")]
    [SerializeField] private float deathCausePulseMaxScale = 1.15f;
    [Tooltip("Speed/frequency of death cause text pulse.")]
    [SerializeField] private float deathCausePulseSpeed    = 3f;

   
    private Coroutine deathCausePulseCoroutine;
    private Vector3   originalDeathCauseTextScale = Vector3.one;

    // Unity Lifecycle ---------------->

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        AutoDiscoverUIElements();
    }

    private void Start()
    {
        AutoDiscoverUIElements();

        if (deathCauseText != null)
        {
            originalDeathCauseTextScale = deathCauseText.transform.localScale;
            if (originalDeathCauseTextScale == Vector3.zero)
                originalDeathCauseTextScale = Vector3.one;
        }

        if (deathRestartButton  != null) deathRestartButton.onClick.AddListener(OnDeathRestartClicked);
        if (deathContinueButton != null) deathContinueButton.onClick.AddListener(OnDeathContinueClicked);
        if (deathMenuButton     != null) deathMenuButton.onClick.AddListener(OnDeathMenuClicked);

        if (levelCompleteRestartButton != null) levelCompleteRestartButton.onClick.AddListener(OnLevelCompleteRestartClicked);
        if (levelCompleteMenuButton    != null) levelCompleteMenuButton.onClick.AddListener(OnLevelCompleteMenuClicked);
        if (levelCompleteQuitButton    != null) levelCompleteQuitButton.onClick.AddListener(OnLevelCompleteQuitClicked);

        GameManager.OnStateChanged += HandleGameStateChanged;
        GameManager.OnPlayerDied   += HandlePlayerDied;
    }

    private void OnDestroy()
    {
        StopDeathCausePulse();

        if (deathRestartButton  != null) deathRestartButton.onClick.RemoveListener(OnDeathRestartClicked);
        if (deathContinueButton != null) deathContinueButton.onClick.RemoveListener(OnDeathContinueClicked);
        if (deathMenuButton     != null) deathMenuButton.onClick.RemoveListener(OnDeathMenuClicked);

        if (levelCompleteRestartButton != null) levelCompleteRestartButton.onClick.RemoveListener(OnLevelCompleteRestartClicked);
        if (levelCompleteMenuButton    != null) levelCompleteMenuButton.onClick.RemoveListener(OnLevelCompleteMenuClicked);
        if (levelCompleteQuitButton    != null) levelCompleteQuitButton.onClick.RemoveListener(OnLevelCompleteQuitClicked);

        GameManager.OnStateChanged -= HandleGameStateChanged;
        GameManager.OnPlayerDied   -= HandlePlayerDied;

        if (Instance == this) Instance = null;
    }

    private void AutoDiscoverUIElements()
    {
        // 1. Auto-discover deathCauseText & death buttons
        GameObject deathPanelGO = PanelManager.Instance != null && PanelManager.Instance.DeathPanel != null
            ? PanelManager.Instance.DeathPanel.gameObject
            : GameObject.Find("DeathPanel") ?? GameObject.Find("Death");

        if (deathPanelGO != null)
        {
            if (deathCauseText == null)
            {
                TMP_Text[] texts = deathPanelGO.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in texts)
                {
                    string n = t.gameObject.name.ToLower();
                    if (n.Contains("cause") || n.Contains("reason") || n.Contains("death"))
                    {
                        deathCauseText = t;
                        break;
                    }
                }
            }

            Button[] dButtons = deathPanelGO.GetComponentsInChildren<Button>(true);
            foreach (var btn in dButtons)
            {
                string n = btn.gameObject.name.ToLower();
                if (deathRestartButton == null && (n.Contains("restart") || n.Contains("retry") || n.Contains("replay")))
                    deathRestartButton = btn;
                else if (deathContinueButton == null && (n.Contains("continue") || n.Contains("respawn")))
                    deathContinueButton = btn;
                else if (deathMenuButton == null && (n.Contains("menu") || n.Contains("home") || n.Contains("main")))
                    deathMenuButton = btn;
            }
        }

        // 2. Auto-discover level complete buttons
        GameObject winPanelGO = PanelManager.Instance != null && PanelManager.Instance.LevelCompletePanel != null
            ? PanelManager.Instance.LevelCompletePanel.gameObject
            : GameObject.Find("LevelCompletePanel") ?? GameObject.Find("VictoryPanel");

        if (winPanelGO != null)
        {
            Button[] buttons = winPanelGO.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                string n = btn.gameObject.name.ToLower();
                if (levelCompleteRestartButton == null && (n.Contains("restart") || n.Contains("retry") || n.Contains("replay")))
                    levelCompleteRestartButton = btn;
                else if (levelCompleteMenuButton == null && (n.Contains("menu") || n.Contains("home") || n.Contains("main")))
                    levelCompleteMenuButton = btn;
                else if (levelCompleteQuitButton == null && (n.Contains("quit") || n.Contains("exit")))
                    levelCompleteQuitButton = btn;
            }
        }
    }

    // Death Cause Logic ------------------>    

    private void HandlePlayerDied()
    {
        PopulateDeathCause();
    }

    private void HandleGameStateChanged(GameState state)
    {
        if (state == GameState.Dead)
        {
            PopulateDeathCause();
        }
        else
        {
            StopDeathCausePulse();
        }
    }

    public void PopulateDeathCause()
    {
        RaceContext.DeathCause cause = RaceContext.Instance != null
            ? RaceContext.Instance.PendingDeathCause
            : RaceContext.DeathCause.Hazard;

        if (deathCauseText != null)
        {
            deathCauseText.text = cause switch
            {
                RaceContext.DeathCause.Missile       => "CAUGHT BY MISSILE",
                RaceContext.DeathCause.Fuel          => "OUT OF FUEL",
                RaceContext.DeathCause.EngineFailure => "ENGINE DESTROYED",
                RaceContext.DeathCause.Overheat      => "ENGINE DESTROYED",
                RaceContext.DeathCause.Hazard        => "CRASHED",
                _                                    => "MISSION FAILED",
            };
            StartDeathCausePulse();
        }
    }

    private void StartDeathCausePulse()
    {
        if (deathCauseText == null) return;
        if (deathCausePulseCoroutine != null) StopCoroutine(deathCausePulseCoroutine);
        deathCausePulseCoroutine = StartCoroutine(UIAnimUtils.PulseTextScale(
            deathCauseText, originalDeathCauseTextScale, deathCausePulseMinScale, deathCausePulseMaxScale, deathCausePulseSpeed));
    }

    private void StopDeathCausePulse()
    {
        if (deathCausePulseCoroutine != null)
        {
            StopCoroutine(deathCausePulseCoroutine);
            deathCausePulseCoroutine = null;
        }

        if (deathCauseText != null)
            deathCauseText.transform.localScale = originalDeathCauseTextScale;
    }

    // Button Click Handlers ------------------>

   
    /// Continue: respawns at last checkpoint, or reloads scene from start if none saved.

    public void OnDeathContinueClicked()
    {
        StopDeathCausePulse();
        PanelManager.Instance?.CloseActiveModal(restoreHud: true);
        PlayerRespawnHandler.Instance?.TriggerRespawn();
    }

  
    /// Restart: clears the checkpoint save and reloads from the very beginning.
   
    public void OnDeathRestartClicked()
    {
        StopDeathCausePulse();
        PanelManager.Instance?.CloseActiveModal(restoreHud: false);

        int sceneIdx = SceneManager.GetActiveScene().buildIndex;
        string sceneName = SceneManager.GetActiveScene().name;
        CheckpointSaveSystem.ResetCheckpoint(sceneIdx);
        CheckpointSaveSystem.ResetCheckpoint(sceneName);

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene(sceneName, "Restarting Sector...");
        else if (LevelManager.Instance != null)
            LevelManager.Instance.ReloadScene();
        else
            SceneManager.LoadScene(sceneIdx);
    }

    /// Menu: returns to the main menu scene (build index 0).
    public void OnDeathMenuClicked()
    {
        StopDeathCausePulse();
        PanelManager.Instance?.CloseActiveModal(restoreHud: false);

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene("Menu", "Returning to Base...");
        else if (GameManager.Instance != null)
            GameManager.Instance.ReturnToMenu();
        else
            SceneManager.LoadScene("Menu");
    }

    public void OnLevelCompleteRestartClicked()
    {
        PanelManager.Instance?.CloseActiveModal(restoreHud: false);

        int sceneIdx = SceneManager.GetActiveScene().buildIndex;
        string sceneName = SceneManager.GetActiveScene().name;
        CheckpointSaveSystem.ResetCheckpoint(sceneIdx);
        CheckpointSaveSystem.ResetCheckpoint(sceneName);

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene(sceneName, "Restarting Sector...");
        else if (LevelManager.Instance != null)
            LevelManager.Instance.ReloadScene();
        else
            SceneManager.LoadScene(sceneIdx);
    }

    public void OnLevelCompleteMenuClicked()
    {
        PanelManager.Instance?.CloseActiveModal(restoreHud: false);

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene("Menu", "Returning to Base...");
        else if (GameManager.Instance != null)
            GameManager.Instance.ReturnToMenu();
        else
            SceneManager.LoadScene("Menu");
    }

    public void OnLevelCompleteQuitClicked()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}
