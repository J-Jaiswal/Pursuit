using UnityEngine;

/// <summary>
/// Scene-scoped manager for Level UI panels. Enforces modal exclusivity, panel priorities,
/// Time.timeScale pausing/resuming. Does NOT persist across scenes.
/// </summary>
public class PanelManager : MonoBehaviour
{
    public static PanelManager Instance { get; private set; }

    [Header("Primary Level Panels")]
    [SerializeField] private UIPanel hudPanel;
    [SerializeField] private UIPanel pausePanel;
    [SerializeField] private UIPanel deathPanel;
    [SerializeField] private UIPanel levelCompletePanel;
    [SerializeField] private UIPanel commentaryPanel;

    [Header("Input Settings")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;

    private UIPanel activeModalPanel;

    public UIPanel HudPanel => hudPanel;
    public UIPanel PausePanel => pausePanel;
    public UIPanel DeathPanel => deathPanel;
    public UIPanel LevelCompletePanel => levelCompletePanel;
    public UIPanel CommentaryPanel => commentaryPanel;
    public UIPanel ActiveModalPanel => activeModalPanel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        AutoDiscoverPanels();
    }

    private void Start()
    {
        GameManager.OnStateChanged  += HandleGameStateChanged;
        GameManager.OnPlayerDied    += HandlePlayerDied;
        GameManager.OnLevelComplete += HandleLevelComplete;

        // Initial state: HUD visible, all modal and commentary panels hidden
        if (hudPanel != null) hudPanel.SetInstant(true);
        if (pausePanel != null) pausePanel.SetInstant(false);
        if (deathPanel != null) deathPanel.SetInstant(false);
        if (levelCompletePanel != null) levelCompletePanel.SetInstant(false);
        if (commentaryPanel != null) commentaryPanel.SetInstant(false);
    }

    private void OnDestroy()
    {
        GameManager.OnStateChanged  -= HandleGameStateChanged;
        GameManager.OnPlayerDied    -= HandlePlayerDied;
        GameManager.OnLevelComplete -= HandleLevelComplete;

        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        HandleKeyboardInput();
    }

    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(pauseKey))
        {
            TogglePause();
        }
    }

    // ── Auto-Discovery ────────────────────────────────────────────────────────

    private void AutoDiscoverPanels()
    {
        UIPanel[] allPanels = FindObjectsByType<UIPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        FindPanelByKeywords(ref hudPanel, allPanels, new[] { "hudpanel", "gamepanel", "hud", "game" }, PanelPriority.HUD);
        FindPanelByKeywords(ref pausePanel, allPanels, new[] { "pausepanel", "pausemenu", "pause" }, PanelPriority.Pause);
        FindPanelByKeywords(ref deathPanel, allPanels, new[] { "deathpanel", "gameoverpanel", "death", "gameover" }, PanelPriority.Death);
        FindPanelByKeywords(ref levelCompletePanel, allPanels, new[] { "levelcompletepanel", "victorypanel", "winpanel", "completedpanel", "levelcomplete", "victory", "win" }, PanelPriority.LevelComplete);
        FindPanelByKeywords(ref commentaryPanel, allPanels, new[] { "commentarypanel", "dialoguepanel", "subtitlepanel", "commentary", "dialogue" }, PanelPriority.Commentary);
    }

    private void FindPanelByKeywords(ref UIPanel target, UIPanel[] existingPanels, string[] keywords, PanelPriority priority)
    {
        if (target != null) return;

        // 1. Search existing UIPanel components
        foreach (var p in existingPanels)
        {
            if (p == null) continue;
            string n = p.gameObject.name.ToLower().Replace(" ", "").Replace("_", "").Replace("-", "");
            foreach (var kw in keywords)
            {
                if (n == kw || n.Contains(kw))
                {
                    target = p;
                    return;
                }
            }
        }

        // 2. Search all scene GameObjects with a RectTransform (under a Canvas)
        GameObject[] allGOs = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var go in allGOs)
        {
            if (go == null) continue;
            if (go.GetComponent<RectTransform>() == null) continue;

            string rawName = go.name.ToLower();
            if (rawName.Contains("button") || rawName.Contains("text") || rawName.Contains("icon") || rawName.Contains("queue") || rawName.Contains("audio"))
                continue;

            string n = rawName.Replace(" ", "").Replace("_", "").Replace("-", "");
            foreach (var kw in keywords)
            {
                if (n == kw || n.Contains(kw))
                {
                    target = go.GetComponent<UIPanel>() ?? go.AddComponent<UIPanel>();
                    return;
                }
            }
        }
    }

    // ── Game State Subscriptions ──────────────────────────────────────────────

    private void HandleGameStateChanged(GameState state)
    {
        switch (state)
        {
            case GameState.Playing:
                CloseActiveModal();
                if (hudPanel != null && !hudPanel.IsVisible) hudPanel.Show();
                break;

            case GameState.Paused:
                if (pausePanel != null && activeModalPanel != pausePanel)
                {
                    OpenPanel(pausePanel, freezeTime: true);
                }
                break;

            case GameState.Dead:
                if (deathPanel != null && activeModalPanel != deathPanel)
                {
                    OpenPanel(deathPanel, freezeTime: false);
                }
                break;

            case GameState.LevelComplete:
                if (levelCompletePanel != null && activeModalPanel != levelCompletePanel)
                {
                    OpenPanel(levelCompletePanel, freezeTime: true);
                }
                break;
        }
    }

    private void HandlePlayerDied()
    {
        ShowDeathPanel();
    }

    private void HandleLevelComplete()
    {
        ShowLevelCompletePanel();
    }

    // Modal & Panel Controls ---------------------->


    /// Toggles the Pause menu. Rejects toggle if a higher-priority modal (Death / LevelComplete) is active.

    public void TogglePause()
    {
        if (activeModalPanel != null && activeModalPanel.Priority > PanelPriority.Pause)
        {
            Debug.Log($"[PanelManager] Pause ignored: Active modal '{activeModalPanel.name}' has higher priority ({activeModalPanel.Priority}).");
            return;
        }

        if (GameManager.Instance != null)
        {
            if (GameManager.Instance.CurrentState == GameState.Playing)
                GameManager.Instance.Pause();
            else if (GameManager.Instance.CurrentState == GameState.Paused)
                GameManager.Instance.Resume();
        }
        else
        {
            if (activeModalPanel == pausePanel)
                ResumeGame();
            else if (activeModalPanel == null)
                PauseGame();
        }
    }

    public void PauseGame()
    {
        if (pausePanel == null) return;
        OpenPanel(pausePanel, freezeTime: true);
        GameManager.Instance?.Pause();
    }

    public void ResumeGame()
    {
        CloseActiveModal();
        GameManager.Instance?.Resume();
    }

    public void ShowDeathPanel()
    {
        if (deathPanel == null) return;
        OpenPanel(deathPanel, freezeTime: false);
    }

    public void ShowLevelCompletePanel()
    {
        if (levelCompletePanel == null) return;
        OpenPanel(levelCompletePanel, freezeTime: true);
    }


    /// Opens a UI panel with priority validation and optional timeScale freeze.

    public bool OpenPanel(UIPanel panel, bool freezeTime = false)
    {
        if (panel == null) return false;

        // Check if an existing modal has higher priority
        if (activeModalPanel != null && activeModalPanel.Priority > panel.Priority)
        {
            Debug.LogWarning($"[PanelManager] Cannot open '{panel.name}' (Priority {panel.Priority}) because '{activeModalPanel.name}' (Priority {activeModalPanel.Priority}) is active.");
            return false;
        }

        // Hide current active modal if opening a higher or equal priority panel
        if (activeModalPanel != null && activeModalPanel != panel)
        {
            activeModalPanel.Hide();
        }

        activeModalPanel = panel;
        activeModalPanel.Show();

        // Conceal in-game HUD while modal is active
        if (hudPanel != null && panel != hudPanel)
        {
            if (hudPanel.gameObject.activeInHierarchy && hudPanel.IsVisible)
                hudPanel.Hide();
            else
                hudPanel.SetInstant(false);
        }

        // Conceal commentary while modal is active
        if (commentaryPanel != null && panel != commentaryPanel)
        {
            if (commentaryPanel.gameObject.activeInHierarchy && commentaryPanel.IsVisible)
                commentaryPanel.Hide();
            else
                commentaryPanel.SetInstant(false);
        }

        if (freezeTime)
        {
            Time.timeScale = 0f;
        }

        return true;
    }

 
    /// Closes the currently active modal panel, optionally restores the HUD, and unfreezes timeScale.
  
    public void CloseActiveModal(bool restoreHud = true)
    {
        if (activeModalPanel != null)
        {
            activeModalPanel.Hide();
            activeModalPanel = null;
        }

        // Restore in-game HUD only if requested (e.g. unpausing or respawning within same scene)
        if (restoreHud && hudPanel != null && !hudPanel.IsVisible)
        {
            hudPanel.Show();
        }

        Time.timeScale = 1f;
    }
}
