using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


/// Drives the Pause Panel UI button bindings (Resume, Restart, Quit).
/// Pause toggling and modal visibility are owned exclusively by PanelManager.

public class PausePanel : MonoBehaviour
{
    public static PausePanel Instance { get; private set; }

    [Header("Pause UI")]
    [Tooltip("Root GameObject of the pause overlay panel.")]
    [SerializeField] private GameObject pausePanel;

    [Header("Pause Buttons (optional — auto-discovered if not wired via Inspector)")]
    [SerializeField] private GameObject resumeButton;
    [SerializeField] private GameObject restartButton;
    [SerializeField] private GameObject quitButton;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        AutoDiscoverPauseUI();
    }

    private void Start()
    {
        AutoDiscoverPauseUI();

        BindButton(resumeButton,  OnResumeClicked,  "PauseMenu_ResumeButton");
        BindButton(restartButton, OnRestartClicked, "PauseMenu_RestartButton");
        BindButton(quitButton,    OnQuitClicked,    "PauseMenu_QuitButton");
    }

    private void AutoDiscoverPauseUI()
    {
        if (pausePanel == null)
        {
            GameObject[] allGOs = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var go in allGOs)
            {
                string n = go.name.ToLower();
                if (n.Contains("pausepanel") || n.Contains("pause_panel") || n.Contains("pausemenu") || n == "pause")
                {
                    pausePanel = go;
                    break;
                }
            }
        }

        if (pausePanel != null)
        {
            Button[] buttons = pausePanel.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                string n = btn.gameObject.name.ToLower();
                if (resumeButton == null && (n.Contains("resume") || n.Contains("continue") || n.Contains("unpause")))
                    resumeButton = btn.gameObject;
                else if (restartButton == null && (n.Contains("restart") || n.Contains("retry") || n.Contains("reload")))
                    restartButton = btn.gameObject;
                else if (quitButton == null && (n.Contains("quit") || n.Contains("exit") || n.Contains("menu")))
                    quitButton = btn.gameObject;
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    //  Button Callbacks ------------------->

    /// Resume button callback
    public void OnResumeClicked()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.Resume();
        else
            PanelManager.Instance?.ResumeGame();
    }

    /// Restart button callback in pause menu.
    public void OnRestartClicked()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        int sceneIdx     = SceneManager.GetActiveScene().buildIndex;

        // Close the pause modal first — this also restores timeScale via PanelManager
        PanelManager.Instance?.CloseActiveModal(restoreHud: false);

        // Safety-net: guarantee timeScale is 1 before the scene loads
        Time.timeScale = 1f;

        // Clear checkpoint so the restart begins from the level start
        CheckpointSaveSystem.ResetCheckpoint(sceneIdx);
        CheckpointSaveSystem.ResetCheckpoint(sceneName);

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene(sceneName, "Restarting Sector...");
        else if (LevelManager.Instance != null)
            LevelManager.Instance.ReloadScene();
        else
            SceneManager.LoadScene(sceneIdx);
    }

 
    public void OnQuitClicked()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void BindButton(GameObject buttonGO, UnityEngine.Events.UnityAction callback, string debugName)
    {
        if (buttonGO == null) return;

        Button btn = buttonGO.GetComponentInChildren<Button>(includeInactive: true);
        if (btn == null)
        {
            Debug.LogWarning($"[PausePanel] {debugName}: No Button component on '{buttonGO.name}' or children.");
            return;
        }

        btn.onClick.RemoveListener(callback);
        btn.onClick.AddListener(callback);
    }
}
