using UnityEngine;

/// <summary>
/// Top-level UI Orchestrator for ship statistics, warning banners, and telemetry.
/// Panel transitions and modal exclusivity are owned strictly by PanelManager.
/// </summary>


public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // Sub-Component References ------------------>
    [Header("Sub-Components")]
    [Tooltip("Telemetry bars, sliders, and critical vignette.")]
    [SerializeField] private UIStats stats;

    [Tooltip("Warning priority brain, pulse FX, and checkpoint banners.")]
    [SerializeField] private UIWarnings warnings;

    [Tooltip("Death cause text and panel button listeners.")]
    [SerializeField] private UIPanels panels;

    // Unity Lifecycle -------------------->

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // Auto-discover sibling components on the same GameObject if unassigned
        if (stats == null)    stats    = GetComponent<UIStats>();
        if (warnings == null) warnings = GetComponent<UIWarnings>();
        if (panels == null)   panels   = GetComponent<UIPanels>();
    }

    private void Start()
    {
        GameManager.OnStateChanged  += HandleGameStateChanged;
        GameManager.OnPlayerDied    += HandlePlayerDied;
        GameManager.OnLevelComplete += HandleLevelComplete;

        warnings?.RefreshWarningDisplay();
    }

    private void OnDestroy()
    {
        GameManager.OnStateChanged  -= HandleGameStateChanged;
        GameManager.OnPlayerDied    -= HandlePlayerDied;
        GameManager.OnLevelComplete -= HandleLevelComplete;

        if (Instance == this) Instance = null;
    }

    //  Game State Machine (Stats & Warnings Only) ------------------->

    private void HandleGameStateChanged(GameState state)
    {
        switch (state)
        {
            case GameState.Playing:
                stats?.StopVignette();
                warnings?.RefreshWarningDisplay();
                break;

            case GameState.Paused:
                warnings?.ClearWarning();
                break;

            case GameState.Dead:
                stats?.StopVignette();
                warnings?.ClearAllWarnings();
                break;

            case GameState.LevelComplete:
                stats?.StopVignette();
                warnings?.ClearAllWarnings();
                warnings?.ShowWarning("LEVEL COMPLETE!", Color.green);
                break;
        }
    }

    private void HandlePlayerDied()
    {
        warnings?.ClearAllWarnings();
    }

    private void HandleLevelComplete()
    {
        warnings?.ClearAllWarnings();
        warnings?.ShowWarning("LEVEL COMPLETE!", Color.green);
    }

    // Public Forwarding API (External Callers) --------------------->

    /// <summary>Activates or deactivates the HUD GamePanel via PanelManager.</summary>
    public void SetGamePanelActive(bool active)
    {
        if (PanelManager.Instance != null && PanelManager.Instance.HudPanel != null)
        {
            if (active) PanelManager.Instance.HudPanel.Show();
            else PanelManager.Instance.HudPanel.Hide();
        }
    }

    /// Register an exclusive panel with PanelManager.
    public void RegisterExclusivePanel(GameObject panel)
    {
        // Handled automatically by PanelManager priority system
    }

    /// Remove a panel from exclusive registry.
    public void UnregisterExclusivePanel(GameObject panel)
    {
        // Handled automatically by PanelManager priority system
    }

   
    /// Opens a panel exclusively through PanelManager.
   
    public bool ShowExclusivePanel(GameObject panel)
    {
        if (panel == null || PanelManager.Instance == null) return false;
        UIPanel uip = panel.GetComponent<UIPanel>();
        if (uip != null)
        {
            return PanelManager.Instance.OpenPanel(uip);
        }
        return false;
    }

    /// Hides a specific panel.
    public void HideExclusivePanel(GameObject panel)
    {
        if (panel == null) return;
        UIPanel uip = panel.GetComponent<UIPanel>();
        if (uip != null)
        {
            uip.Hide();
        }
    }

    /// Displays a pulsing "Checkpoint Reached" banner PNG for the specified duration
    public void ShowCheckpointReached(float duration = 2.5f)
    {
        warnings?.ShowCheckpointReached(duration);
    }

    /// Displays a pulsing "Missed Checkpoint" warning banner PNG for the specified duration.
    public void ShowMissedCheckpoint(float duration = 3.0f)
    {
        warnings?.ShowMissedCheckpoint(duration);
    }

    /// Updates custom status text. If message is empty, restores active game warnings.
    public void SetStatusText(string message, bool pulse = true)
    {
        warnings?.SetStatusText(message, pulse);
    }
}
