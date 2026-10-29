using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Base class for in-scene UI panels. Provides smooth, unscaled alpha and scale transitions
/// that animate reliably even when Time.timeScale = 0.
/// </summary>
public enum PanelPriority
{
    None = 0,
    HUD = 1,
    Commentary = 2,
    Pause = 3,
    Settings = 3,
    LevelComplete = 4,
    Death = 5
}

[RequireComponent(typeof(CanvasGroup))]
public class UIPanel : MonoBehaviour
{
    [Header("Panel Configuration")]
    [SerializeField] private PanelPriority priority = PanelPriority.Pause;
    [SerializeField] private float fadeDuration = 0.2f;
    [SerializeField] private bool useScalePunch = true;
    [SerializeField] private float punchScaleAmount = 0.95f;

    public PanelPriority Priority => priority;
    public bool IsVisible { get; private set; }

    protected CanvasGroup canvasGroup;
    private RectTransform rectTransform;
    private Coroutine transitionCoroutine;
    private Vector3 originalScale = Vector3.one;

    protected virtual void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            originalScale = rectTransform.localScale;
            if (originalScale == Vector3.zero)
                originalScale = Vector3.one;
        }

        IsVisible = gameObject.activeSelf && (canvasGroup != null && canvasGroup.alpha > 0.05f);
    }

  
    /// Shows the panel with an unscaled alpha fade and optional scale punch.
 
    public virtual void Show(Action onComplete = null)
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        gameObject.SetActive(true);
        IsVisible = true;

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        if (!gameObject.activeInHierarchy || fadeDuration <= 0.001f)
        {
            SetInstant(true);
            onComplete?.Invoke();
            return;
        }

        float fromAlpha = canvasGroup != null ? canvasGroup.alpha : 0f;
        transitionCoroutine = StartCoroutine(AnimatePanel(fromAlpha, 1f, true, onComplete));
    }


    /// Hides the panel with an unscaled alpha fade and deactivates the GameObject when complete.

    public virtual void Hide(Action onComplete = null)
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        IsVisible = false;

        if (!gameObject.activeInHierarchy || fadeDuration <= 0.001f)
        {
            SetInstant(false);
            onComplete?.Invoke();
            return;
        }

        float fromAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
        transitionCoroutine = StartCoroutine(AnimatePanel(fromAlpha, 0f, false, () =>
        {
            if (!IsVisible)
            {
                gameObject.SetActive(false);
            }
            onComplete?.Invoke();
        }));
    }


    /// Instantly forces panel visibility without transition animations.

    public virtual void SetInstant(bool visible)
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        IsVisible = visible;
        gameObject.SetActive(visible);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = visible;
            canvasGroup.interactable = visible;
        }

        if (rectTransform != null)
        {
            rectTransform.localScale = originalScale;
        }
    }

    private IEnumerator AnimatePanel(float fromAlpha, float toAlpha, bool isShowing, Action onComplete)
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.blocksRaycasts = isShowing;
        canvasGroup.interactable = isShowing;

        float elapsed = 0f;
        Vector3 startScale = (isShowing && useScalePunch) ? originalScale * punchScaleAmount : (rectTransform != null ? rectTransform.localScale : originalScale);
        Vector3 targetScale = originalScale;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, smoothT);
            }

            if (rectTransform != null && useScalePunch)
            {
                rectTransform.localScale = Vector3.Lerp(startScale, targetScale, smoothT);
            }

            yield return null;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = toAlpha;
            canvasGroup.blocksRaycasts = isShowing;
            canvasGroup.interactable = isShowing;
        }

        if (rectTransform != null)
        {
            rectTransform.localScale = originalScale;
        }

        transitionCoroutine = null;
        onComplete?.Invoke();
    }
}
