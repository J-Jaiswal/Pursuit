using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Persistent global manager  that handles asynchronous scene transitions,
/// progress bar interpolation, percentage readout, audio click buffers, and screen fades.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    [Header("UI Overlay References")]
    [SerializeField] private CanvasGroup loadingCanvasGroup;
    [SerializeField] private Slider      progressBar;
    [SerializeField] private TMP_Text    percentageText;
    [Tooltip("Header text indicating current status (e.g. 'ENTERING SECTOR...', 'RETURNING TO BASE...').")]
    [SerializeField] private TMP_Text    statusText;

    [Header("Transition Settings")]
    [Tooltip("Time (seconds) for the loading canvas to fade in or fade out.")]
    [SerializeField] private float fadeDuration = 0.25f;

    [Tooltip("Brief audio buffer allowing button click SFX to finish before visual transition starts.")]
    [SerializeField] private float audioClickBuffer = 0.12f;

    [Tooltip("Minimum time (seconds) the loading screen remains visible to prevent 1-frame screen flashes.")]
    [SerializeField] private float minimumLoadingTime = 0.5f;

    [Tooltip("Speed multiplier for the visual progress bar interpolation.")]
    [SerializeField] private float progressLerpSpeed = 3f;

    private bool isLoading;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Immediately disable duplicate instance and any attached canvases before destroying
            if (loadingCanvasGroup != null)
            {
                loadingCanvasGroup.alpha = 0f;
                loadingCanvasGroup.blocksRaycasts = false;
                loadingCanvasGroup.interactable = false;
                loadingCanvasGroup.gameObject.SetActive(false);
            }
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        HideInstant();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // When scene finishes loading, if no active loading coroutine is running, guarantee hidden state
        if (!isLoading)
        {
            HideInstant();
        }
    }


    /// Instantly hides the loading canvas and disables duplicate loading objects in the scene.

    public void HideInstant()
    {
        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.gameObject.SetActive(false);

            Canvas c = loadingCanvasGroup.GetComponent<Canvas>() ?? loadingCanvasGroup.GetComponentInParent<Canvas>();
            if (c != null && (c.gameObject == loadingCanvasGroup.gameObject || c.transform.IsChildOf(transform)))
            {
                c.enabled = false;
            }
        }

        // Deactivate any orphaned/duplicate scene-placed LoadingCanvas GameObjects
        GameObject[] allGOs = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var go in allGOs)
        {
            if (go == null) continue;
            if (loadingCanvasGroup != null && (go == loadingCanvasGroup.gameObject || go == gameObject)) continue;

            string n = go.name.ToLower();
            if (n.Contains("loadingcanvas") || n.Contains("loadingscreen") || (n == "loading" && go.GetComponent<Canvas>() != null))
            {
                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 0f;
                    cg.blocksRaycasts = false;
                    cg.interactable = false;
                }
                Canvas canvas = go.GetComponent<Canvas>();
                if (canvas != null) canvas.enabled = false;
                go.SetActive(false);
            }
        }
    }

    /// Loads a scene asynchronously by scene name with custom status text.

    public void LoadScene(string sceneName, string statusMessage = "ENTERING SECTOR...")
    {
        if (isLoading) return;
        StartCoroutine(LoadSceneCoroutine(sceneName, statusMessage));
    }

    /// <summary>
    /// Loads a scene asynchronously by scene build index with custom status text.
    /// </summary>
    public void LoadScene(int sceneBuildIndex, string statusMessage = "ENTERING SECTOR...")
    {
        if (isLoading) return;
        string scenePath = SceneUtility.GetScenePathByBuildIndex(sceneBuildIndex);
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError($"[SceneLoader] Invalid build index: {sceneBuildIndex}. Scene not found in Build Settings.");
            return;
        }

        StartCoroutine(LoadSceneCoroutine(sceneName, statusMessage));
    }

    private IEnumerator LoadSceneCoroutine(string sceneName, string statusMessage)
    {
        isLoading = true;

        // 1. Audio Click Grace Period (uses unscaled time because timeScale may be 0 from Pause/Death)
        yield return new WaitForSecondsRealtime(audioClickBuffer);

        // 2. Setup Loading UI elements
        if (progressBar != null) progressBar.value = 0f;
        if (percentageText != null) percentageText.text = "0%";
        if (statusText != null) statusText.text = statusMessage;

        // 3. Activate and Fade In Loading Screen (Alpha 0 -> 1)
        if (loadingCanvasGroup != null)
        {
            loadingCanvasGroup.gameObject.SetActive(true);
            Canvas c = loadingCanvasGroup.GetComponent<Canvas>() ?? loadingCanvasGroup.GetComponentInParent<Canvas>();
            if (c != null) c.enabled = true;

            loadingCanvasGroup.blocksRaycasts = true;
            loadingCanvasGroup.interactable = true;
            yield return StartCoroutine(FadeCanvasGroup(loadingCanvasGroup, 0f, 1f, fadeDuration));
        }

        // 4. Begin Asynchronous Loading
        AsyncOperation asyncOp = SceneManager.LoadSceneAsync(sceneName);
        if (asyncOp == null)
        {
            Debug.LogError($"[SceneLoader] Failed to load scene '{sceneName}'. Verify it is added to Build Settings.");
            HideInstant();
            isLoading = false;
            yield break;
        }

        asyncOp.allowSceneActivation = false;

        float timer = 0f;
        bool activationTriggered = false;

        while (!asyncOp.isDone)
        {
            timer += Time.unscaledDeltaTime;

            // Unity async progress runs 0.0 -> 0.9 while loading assets into memory
            float rawProgress = Mathf.Clamp01(asyncOp.progress / 0.9f);

            // Smoothly animate visual progress bar
            if (progressBar != null)
            {
                progressBar.value = Mathf.MoveTowards(progressBar.value, rawProgress, Time.unscaledDeltaTime * progressLerpSpeed);
                if (percentageText != null)
                {
                    int displayPct = Mathf.RoundToInt(progressBar.value * 100f);
                    percentageText.text = $"{displayPct}%";
                }
            }

            // Once assets are loaded (0.9) and minimum display time elapsed, trigger activation once
            if (!activationTriggered && asyncOp.progress >= 0.9f && timer >= minimumLoadingTime)
            {
                activationTriggered = true;

                if (progressBar != null) progressBar.value = 1f;
                if (percentageText != null) percentageText.text = "100%";
                if (statusText != null) statusText.text = "READY";

                // Brief visual hold at 100%
                yield return new WaitForSecondsRealtime(0.1f);
                asyncOp.allowSceneActivation = true;
            }

            yield return null;
        }

        // 5. Restore timeScale AFTER the new scene has activated (prevents visible unpause glitches)
        Time.timeScale = 1f;

        // Let the new scene hierarchy, cameras, and lighting settle for 1 frame
        yield return new WaitForSecondsRealtime(0.05f);

        // 6. Fade Out Loading Screen (Alpha 1 -> 0) and Deactivate GameObject
        if (loadingCanvasGroup != null)
        {
            yield return StartCoroutine(FadeCanvasGroup(loadingCanvasGroup, 1f, 0f, fadeDuration));
            loadingCanvasGroup.alpha = 0f;
            loadingCanvasGroup.blocksRaycasts = false;
            loadingCanvasGroup.interactable = false;
            loadingCanvasGroup.gameObject.SetActive(false);

            Canvas c = loadingCanvasGroup.GetComponent<Canvas>() ?? loadingCanvasGroup.GetComponentInParent<Canvas>();
            if (c != null && (c.gameObject == loadingCanvasGroup.gameObject || c.transform.IsChildOf(transform)))
            {
                c.enabled = false;
            }
        }

        isLoading = false;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;

        float elapsed = 0f;
        cg.alpha = from;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }

        cg.alpha = to;
    }
}
