using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Reads CheckpointSaveSystem on menu load, shows the correct button state,
/// manages Menu BGM playback, and handles all Menu button click & hover sound effects centrally.
/// </summary>
public class Menu: MonoBehaviour
{
    [Header("Level Target")]
    [Tooltip("Exact name of the level scene to load (must be in Build Settings).")]
    [SerializeField] private string levelSceneName = "Level";
    [Tooltip("Build index of the same level scene — used as fallback for checkpoint lookups.")]
    [SerializeField] private int levelSceneBuildIndex = 1;

    [Header("UI References")]
    [SerializeField] private Button continueButton;
    [SerializeField] private Button playButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button guideButton;
    [SerializeField] private TMP_Text continueLabel;
    [SerializeField] private TMP_Text playLabel;
    [Tooltip("Shows the saved checkpoint details, or a 'not saved' message if none exists.")]
    [SerializeField] private TMP_Text checkpointInfoText;

    [Header("Guide Panel")]
    [Tooltip("Root GameObject of the main menu panel (shown on menu, hidden when guide opens).")]
    [SerializeField] private GameObject menuPanel;
    [Tooltip("Root GameObject of the guide panel (hidden on menu, shown when guide opens).")]
    [SerializeField] private GameObject guidePanel;
    [Tooltip("GuidePanelController on the guide panel — reset to page 0 each time guide opens.")]
    [SerializeField] private GuidePanelController guidePanelController;

    [Header("Menu Background Music (BGM)")]
    [Tooltip("Background music track played in loop while on the main menu scene.")]
    [SerializeField] private AudioClip menuBgmClip;
    [Range(0f, 1f)] [SerializeField] private float menuBgmVolume = 0.5f;
    [Tooltip("Automatically begin BGM playback when the menu starts.")]
    [SerializeField] private bool playMenuBgmOnStart = true;

    [Header("Menu Button Audio")]
    [Tooltip("Audio clip played once when hovering over buttons in the menu.")]
    [SerializeField] private AudioClip buttonHoverClip;
    [Tooltip("Audio clip played once when clicking buttons in the menu.")]
    [SerializeField] private AudioClip buttonClickClip;
    [Range(0f, 1f)] [SerializeField] private float buttonAudioVolume = 1f;

    private AudioSource menuBgmSource;

    private void Awake()
    {
        InitializeMenuAudio();
        BindButtons();
    }

    private void Start()
    {
        BindButtons();
        RefreshUI();

        if (playMenuBgmOnStart)
        {
            PlayMenuBGM(menuBgmClip);
        }
    }

    private void OnEnable()
    {
        BindButtons();
        RefreshUI();

        if (playMenuBgmOnStart && menuBgmSource != null && !menuBgmSource.isPlaying)
        {
            menuBgmSource.UnPause();
            if (!menuBgmSource.isPlaying && menuBgmClip != null)
                menuBgmSource.Play();
        }
    }

    private void InitializeMenuAudio()
    {
        if (menuBgmSource == null)
        {
            menuBgmSource = GetComponent<AudioSource>();
            if (menuBgmSource == null)
            {
                GameObject bgmGO = new GameObject("Menu_BGM_Source");
                bgmGO.transform.SetParent(transform, false);
                menuBgmSource = bgmGO.AddComponent<AudioSource>();
            }

            menuBgmSource.playOnAwake = false;
            menuBgmSource.loop = true;
            menuBgmSource.spatialBlend = 0f; // 2D Stereo
            menuBgmSource.volume = menuBgmVolume;
        }
    }

    public void PlayMenuBGM(AudioClip clip = null)
    {
        if (menuBgmSource == null) InitializeMenuAudio();

        AudioClip target = clip != null ? clip : menuBgmClip;
        if (target == null) return;

        menuBgmSource.clip = target;
        menuBgmSource.volume = menuBgmVolume;
        menuBgmSource.loop = true;
        menuBgmSource.Play();
    }

    public void StopMenuBGM()
    {
        if (menuBgmSource != null && menuBgmSource.isPlaying)
            menuBgmSource.Stop();
    }

    private void BindButtons()
    {
        // Auto-find references if not assigned in Inspector
        if (continueButton == null)
        {
            GameObject go = GameObject.Find("ContinueButton") ?? GameObject.Find("Continue");
            if (go != null) continueButton = go.GetComponent<Button>();
        }

        if (playButton == null)
        {
            GameObject go = GameObject.Find("PlayButton") ?? GameObject.Find("Play");
            if (go != null) playButton = go.GetComponent<Button>();
        }

        if (quitButton == null)
        {
            GameObject go = GameObject.Find("QuitButton") ?? GameObject.Find("Quit");
            if (go != null) quitButton = go.GetComponent<Button>();
        }

        // Attach primary button listeners
        SetupPrimaryButton(continueButton, OnContinueClicked);
        SetupPrimaryButton(playButton, OnPlayClicked);
        SetupPrimaryButton(quitButton, QuitGame);
        SetupPrimaryButton(guideButton, OpenGuide);

        // Also add hover sound & visual scaling to all other buttons on the canvas
        Button[] allButtons = FindObjectsByType<Button>(FindObjectsSortMode.None);
        foreach (Button btn in allButtons)
        {
            if (btn == null) continue;

            // Ensure visual hover effect exists
            if (btn.GetComponent<ButtonHoverEffect>() == null)
            {
                btn.gameObject.AddComponent<ButtonHoverEffect>();
            }

            // Hook hover audio once
            AttachHoverSound(btn);
        }
    }

    private void SetupPrimaryButton(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn == null) return;

        btn.onClick.RemoveListener(action);
        btn.onClick.AddListener(action);

        if (btn.GetComponent<ButtonHoverEffect>() == null)
        {
            btn.gameObject.AddComponent<ButtonHoverEffect>();
        }

        AttachHoverSound(btn);
    }

    private void AttachHoverSound(Button btn)
    {
        if (btn == null) return;

        EventTrigger trigger = btn.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = btn.gameObject.AddComponent<EventTrigger>();
        }

        if (trigger.triggers == null)
        {
            trigger.triggers = new List<EventTrigger.Entry>();
        }

        // Remove any prior PointerEnter entries to guarantee no double-firing
        trigger.triggers.RemoveAll(entry => entry.eventID == EventTriggerType.PointerEnter);

        EventTrigger.Entry pointerEnterEntry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerEnter
        };
        pointerEnterEntry.callback.AddListener((data) =>
        {
            if (btn.interactable)
            {
                PlayHoverSound();
            }
        });
        trigger.triggers.Add(pointerEnterEntry);
    }

    // UI Refresh --------------------->

    public void RefreshUI()
    {
        bool hasSave = CheckpointSaveSystem.HasSavedCheckpoint(levelSceneName) ||
                       CheckpointSaveSystem.HasSavedCheckpoint(levelSceneBuildIndex);

        int savedIndex = -1;
        Vector3 spawnPos = Vector3.zero;

        if (hasSave)
        {
            savedIndex = CheckpointSaveSystem.GetSavedCheckpointIndex(levelSceneName);
            spawnPos = CheckpointSaveSystem.GetSavedSpawnPosition(levelSceneName);

            if (savedIndex < 0)
            {
                savedIndex = CheckpointSaveSystem.GetSavedCheckpointIndex(levelSceneBuildIndex);
                spawnPos = CheckpointSaveSystem.GetSavedSpawnPosition(levelSceneBuildIndex);
            }
        }

        Debug.Log($"[MenuCheckpointDisplay] RefreshUI: hasSave={hasSave}, savedCheckpoint={savedIndex} for '{levelSceneName}' (index {levelSceneBuildIndex})");

        if (continueButton != null)
            continueButton.gameObject.SetActive(hasSave);

        if (hasSave && continueLabel != null)
        {
            continueLabel.text = "CONTINUE";
        }

        if (playLabel != null)
            playLabel.text = hasSave ? "NEW GAME" : "PLAY";

        RefreshCheckpointInfo(hasSave, savedIndex, spawnPos);
    }

    private void RefreshCheckpointInfo(bool hasSave, int savedIndex, Vector3 spawnPos)
    {
        if (checkpointInfoText == null) return;

        if (!hasSave || savedIndex < 0)
        {
            checkpointInfoText.text = "Not saved any checkpoint";
            return;
        }

        checkpointInfoText.text =
            $"Checkpoint {savedIndex + 1} saved\n" +
            $"Position: ({spawnPos.x:F1}, {spawnPos.y:F1}, {spawnPos.z:F1})";
    }

    //  Button Callbacks --------------->

    public void OpenGuide()
    {
        PlayClickSound();
        if (menuPanel  != null) menuPanel.SetActive(false);
        if (guidePanel != null) guidePanel.SetActive(true);
        if (guidePanelController != null) guidePanelController.ResetToFirstPage();
        StopMenuBGM();
    }

    /// <summary>
    /// Closes the Guide panel: hides the guide, restores the menu, resumes BGM.
    /// Called by GuidePanelController when the Home button is pressed.
    /// </summary>
    public void CloseGuide()
    {
        PlayClickSound();
        if (guidePanel != null) guidePanel.SetActive(false);
        if (menuPanel  != null) menuPanel.SetActive(true);
        PlayMenuBGM();
    }

    public void OnPlayClicked()
    {
        PlayClickSound();
        Debug.Log($"[MenuCheckpointDisplay] OnPlayClicked: Resetting checkpoint for '{levelSceneName}' and loading scene.");
        CheckpointSaveSystem.ResetCheckpoint(levelSceneName);
        CheckpointSaveSystem.ResetCheckpoint(levelSceneBuildIndex);

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene(levelSceneName, "Initializing Sector Assets...");
        else
            SceneManager.LoadScene(levelSceneName);
    }

    public void QuitGame()
    {
        PlayClickSound();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OnContinueClicked()
    {
        PlayClickSound();
        Debug.Log($"[MenuCheckpointDisplay] OnContinueClicked: Loading scene '{levelSceneName}' from saved checkpoint.");

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.LoadScene(levelSceneName, "Resuming from Checkpoint...");
        else
            SceneManager.LoadScene(levelSceneName);
    }

    public void PlayHoverSound()
    {
        if (buttonHoverClip != null)
            AudioSource.PlayClipAtPoint(buttonHoverClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, buttonAudioVolume);
    }

    public void PlayClickSound()
    {
        if (buttonClickClip != null)
            AudioSource.PlayClipAtPoint(buttonClickClip, Camera.main != null ? Camera.main.transform.position : Vector3.zero, buttonAudioVolume);
    }
}