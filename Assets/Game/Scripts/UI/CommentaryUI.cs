using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Subtitle display system that listens to CommentaryQueue.
/// Shows/hides visual panels for NOVA and Mission Control.
/// Only displays while the game is in Playing state.
/// </summary>
public class CommentaryUI : MonoBehaviour
{
    public static CommentaryUI Instance { get; private set; }

    [Header("Root Commentary Panel")]
    [Tooltip("The root CommentaryPanel GameObject containing both Nova and MC objects.")]
    [SerializeField] private GameObject commentaryPanel;

    [Header("NOVA UI Elements")]
    [Tooltip("The panel containing the NOVA subtitle elements (e.g. NovaObject).")]
    [SerializeField] private GameObject novaPanel;
    [Tooltip("The TextMeshPro text component for NOVA commentary.")]
    [SerializeField] private TMP_Text novaText;

    [Header("Mission Control UI Elements")]
    [Tooltip("The panel containing the Mission Control subtitle elements (e.g. MCObject).")]
    [SerializeField] private GameObject mcPanel;
    [Tooltip("The TextMeshPro text component for Mission Control commentary.")]
    [SerializeField] private TMP_Text mcText;

    [Header("Animation Settings")]
    [Tooltip("Duration in seconds for the panel to slide into view.")]
    [SerializeField] private float slideInDuration = 0.35f;
    [Tooltip("Duration in seconds for the panel to slide out of view.")]
    [SerializeField] private float slideOutDuration = 0.30f;
    [Tooltip("Additional offscreen pixel offset for sliding.")]
    [SerializeField] private float slideOffset = 800f;

    private RectTransform novaRect;
    private RectTransform mcRect;
    private CanvasGroup novaCanvasGroup;
    private CanvasGroup mcCanvasGroup;

    private Vector2 novaRestingPos;
    private Vector2 mcRestingPos;
    private bool restingPositionsCached;

    private Coroutine activeDisplayCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        AutoDiscoverElements();
        CacheComponentsAndPositions();
    }

    private void Start()
    {
        // Deactivate all commentary panels at start
        ResetPanelsToHidden();

        // Subscribe to CommentaryQueue & GameState events
        CommentaryQueue.OnCommentarySpoken += HandleCommentarySpoken;
        CommentaryQueue.OnCommentaryCleared += HandleCommentaryCleared;
        GameManager.OnStateChanged += HandleGameStateChanged;
    }

    private void OnDestroy()
    {
        CommentaryQueue.OnCommentarySpoken -= HandleCommentarySpoken;
        CommentaryQueue.OnCommentaryCleared -= HandleCommentaryCleared;
        GameManager.OnStateChanged -= HandleGameStateChanged;

        if (Instance == this) Instance = null;
    }

    private void CacheComponentsAndPositions()
    {
        if (novaPanel != null)
        {
            novaRect = novaPanel.GetComponent<RectTransform>();
            novaCanvasGroup = novaPanel.GetComponent<CanvasGroup>();
            if (novaCanvasGroup == null) novaCanvasGroup = novaPanel.AddComponent<CanvasGroup>();
            if (novaRect != null && !restingPositionsCached) novaRestingPos = novaRect.anchoredPosition;
        }

        if (mcPanel != null)
        {
            mcRect = mcPanel.GetComponent<RectTransform>();
            mcCanvasGroup = mcPanel.GetComponent<CanvasGroup>();
            if (mcCanvasGroup == null) mcCanvasGroup = mcPanel.AddComponent<CanvasGroup>();
            if (mcRect != null && !restingPositionsCached) mcRestingPos = mcRect.anchoredPosition;
        }

        restingPositionsCached = true;
    }

    private void AutoDiscoverElements()
    {
        // 1. Auto-discover commentaryPanel
        if (commentaryPanel == null)
        {
            if (gameObject.name.ToLower().Contains("commentary") || gameObject.name.ToLower().Contains("subtitle"))
            {
                commentaryPanel = gameObject;
            }
            else
            {
                GameObject[] allGOs = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var go in allGOs)
                {
                    string n = go.name.ToLower();
                    if (n.Contains("commentary") || n.Contains("subtitle") || n.Contains("dialoguepanel"))
                    {
                        commentaryPanel = go;
                        break;
                    }
                }
            }
        }

        // 2. Auto-discover child panels from commentaryPanel or scene
        Transform root = commentaryPanel != null ? commentaryPanel.transform : transform;

        if (novaPanel == null)
        {
            Transform found = FindChildByKeyword(root, "nova");
            if (found != null) novaPanel = found.gameObject;
        }

        if (novaText == null && novaPanel != null)
        {
            novaText = novaPanel.GetComponentInChildren<TMP_Text>(true);
        }
        EnforceSingleLine(novaText);

        if (mcPanel == null)
        {
            Transform found = FindChildByKeyword(root, "mc") ?? FindChildByKeyword(root, "missioncontrol") ?? FindChildByKeyword(root, "control");
            if (found != null) mcPanel = found.gameObject;
        }

        if (mcText == null && mcPanel != null)
        {
            mcText = mcPanel.GetComponentInChildren<TMP_Text>(true);
        }
        EnforceSingleLine(mcText);
    }

    private Transform FindChildByKeyword(Transform parent, string keyword)
    {
        Transform[] children = parent.GetComponentsInChildren<Transform>(true);
        foreach (var c in children)
        {
            if (c == parent) continue;
            if (c.name.ToLower().Contains(keyword.ToLower()))
            {
                return c;
            }
        }
        return null;
    }

    private void HandleGameStateChanged(GameState state)
    {
        // Only hide commentary when the game is paused
        if (state == GameState.Paused)
        {
            HandleCommentaryCleared();
        }
    }

    private void HandleCommentarySpoken(string text, CommentaryQueue.CommentSpeaker speaker, float duration)
    {
        // Allow commentary display during Playing, Dead, and LevelComplete (only suppress if paused)
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Paused)
            return;

        if (!gameObject.activeInHierarchy)
            return;

        if (activeDisplayCoroutine != null)
        {
            StopCoroutine(activeDisplayCoroutine);
            activeDisplayCoroutine = null;
            // Always reset panels to a clean hidden state before the new speaker
            // slides in — prevents the old panel from being left visible mid-animation.
            ResetPanelsToHidden();
        }

        activeDisplayCoroutine = StartCoroutine(DisplaySubtitleCoroutine(text, speaker, duration));
    }

    private void HandleCommentaryCleared()
    {
        if (activeDisplayCoroutine != null)
        {
            StopCoroutine(activeDisplayCoroutine);
            activeDisplayCoroutine = null;
        }

        ResetPanelsToHidden();
    }

    private void ResetPanelsToHidden()
    {
        if (novaRect != null && restingPositionsCached)
        {
            novaRect.anchoredPosition = GetNovaOffscreenPos();
            if (novaCanvasGroup != null) novaCanvasGroup.alpha = 0f;
        }
        if (mcRect != null && restingPositionsCached)
        {
            mcRect.anchoredPosition = GetMCOffscreenPos();
            if (mcCanvasGroup != null) mcCanvasGroup.alpha = 0f;
        }

        if (novaPanel != null) novaPanel.SetActive(false);
        if (mcPanel != null) mcPanel.SetActive(false);
        if (commentaryPanel != null) commentaryPanel.SetActive(false);
    }

    /// <summary>Force a TMP_Text component to display on a single line with ellipsis overflow.</summary>
    private static void EnforceSingleLine(TMP_Text label)
    {
        if (label == null) return;
        label.enableWordWrapping  = false;
        label.overflowMode        = TextOverflowModes.Ellipsis;
        label.maxVisibleLines     = 1;
    }

    private Vector2 GetNovaOffscreenPos()
    {
        // Use a fixed slideOffset so the result is independent of the panel's rect width
        return new Vector2(novaRestingPos.x - slideOffset, novaRestingPos.y);
    }

    private Vector2 GetMCOffscreenPos()
    {
        // Use a fixed slideOffset so the result is independent of the panel's rect width
        return new Vector2(mcRestingPos.x + slideOffset, mcRestingPos.y);
    }

    private IEnumerator DisplaySubtitleCoroutine(string text, CommentaryQueue.CommentSpeaker speaker, float duration)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Paused)
        {
            HandleCommentaryCleared();
            yield break;
        }

        if (!restingPositionsCached) CacheComponentsAndPositions();

        if (commentaryPanel != null)
        {
            commentaryPanel.SetActive(true);
            CanvasGroup cg = commentaryPanel.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
        }

        bool isNova = (speaker == CommentaryQueue.CommentSpeaker.Nova);
        RectTransform targetRect = isNova ? novaRect : mcRect;
        CanvasGroup targetCG = isNova ? novaCanvasGroup : mcCanvasGroup;
        GameObject targetPanel = isNova ? novaPanel : mcPanel;
        GameObject otherPanel = isNova ? mcPanel : novaPanel;
        TMP_Text targetText = isNova ? novaText : mcText;

        Vector2 restingPos = isNova ? novaRestingPos : mcRestingPos;
        Vector2 offscreenPos = isNova ? GetNovaOffscreenPos() : GetMCOffscreenPos();

        // Hide other panel
        if (otherPanel != null) otherPanel.SetActive(false);

        // Setup active panel
        if (targetPanel != null)
        {
            targetPanel.SetActive(true);
            if (targetText != null)
            {
                EnforceSingleLine(targetText); // re-enforce in case Inspector overrode it
                targetText.text = text;
            }
        }

        // ── Phase 1: Slide In ─────────────────────────────────────────────────
        float inTime = Mathf.Max(0.01f, slideInDuration);
        float elapsed = 0f;

        if (targetRect != null) targetRect.anchoredPosition = offscreenPos;
        if (targetCG != null) targetCG.alpha = 0f;

        while (elapsed < inTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / inTime);
            float ease = 1f - Mathf.Pow(1f - t, 3f); // EaseOutCubic

            if (targetRect != null)
                targetRect.anchoredPosition = Vector2.LerpUnclamped(offscreenPos, restingPos, ease);
            if (targetCG != null)
                targetCG.alpha = t;

            yield return null;
        }

        if (targetRect != null) targetRect.anchoredPosition = restingPos;
        if (targetCG != null) targetCG.alpha = 1f;

        // ── Phase 2: Display for audio/text duration ─────────────────────────
        float holdDuration = Mathf.Max(0.1f, duration - slideInDuration - slideOutDuration);
        float holdElapsed = 0f;
        while (holdElapsed < holdDuration)
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Paused)
            {
                HandleCommentaryCleared();
                yield break;
            }
            holdElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // ── Phase 3: Slide Out ────────────────────────────────────────────────
        float outTime = Mathf.Max(0.01f, slideOutDuration);
        elapsed = 0f;

        while (elapsed < outTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / outTime);
            float ease = t * t; // EaseInQuad

            if (targetRect != null)
                targetRect.anchoredPosition = Vector2.LerpUnclamped(restingPos, offscreenPos, ease);
            if (targetCG != null)
                targetCG.alpha = 1f - t;

            yield return null;
        }

        // ── Cleanup ───────────────────────────────────────────────────────────
        ResetPanelsToHidden();
        activeDisplayCoroutine = null;
    }
}
