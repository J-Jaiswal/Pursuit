using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the warning priority system, warning pulse animations (PNG indicators & text),
/// and checkpoint notification banners.
/// Priority: Hull Critical > Engine Critical > Overheating > Low Fuel > Custom Status.
/// State flags are read from UIStats.Instance.
/// </summary>
public class UIWarnings : MonoBehaviour
{
    public static UIWarnings Instance { get; private set; }

    // Text & Fallback UI 
    [Header("Warning & Status Text")]
    [Tooltip("Text showing active warnings (Low Fuel, Critical Damage, Overheat, etc.) as fallback.")]
    [SerializeField] private TMP_Text statusText;

    // Warning Images (PNG Indicators)
    [Header("Warning Images (PNG Indicators)")]
    [Tooltip("Pulsing PNG indicator displayed during low fuel (< 20%).")]
    [SerializeField] private Image lowFuelWarningImage;
    [Tooltip("Pulsing PNG indicator displayed during critical hull damage (< 30%).")]
    [SerializeField] private Image criticalDamageWarningImage;
    [Tooltip("Pulsing PNG indicator displayed during critical engine damage (< 20%).")]
    [SerializeField] private Image engineCriticalWarningImage;
    [Tooltip("Pulsing PNG indicator displayed during engine overheating (> 80%).")]
    [SerializeField] private Image engineOverheatWarningImage;

    // Checkpoint Notification Images (PNGs)
    [Header("Checkpoint Notification Images (PNGs)")]
    [Tooltip("PNG banner pulsed when a checkpoint is reached.")]
    [SerializeField] private Image checkpointReachedImage;
    [Tooltip("PNG banner pulsed when a checkpoint is missed / bypassed.")]
    [SerializeField] private Image missedCheckpointImage;

    // Colors
    [Header("Warning Colors")]
    [SerializeField] private Color criticalWarningColor = new Color(1f, 0.22f, 0.22f, 1f); // Vibrant Red
    [SerializeField] private Color warningColor         = new Color(1f, 0.75f, 0.15f, 1f); // Amber / Yellow-Orange
    [SerializeField] private Color normalStatusColor    = Color.white;

    // Pulse Animation Settings
    [Header("Text & Image Animation Settings")]
    [Tooltip("Minimum scale multiplier for status/warning pulse.")]
    [SerializeField] private float statusPulseMinScale = 0.95f;
    [Tooltip("Maximum scale multiplier for status/warning pulse.")]
    [SerializeField] private float statusPulseMaxScale = 1.15f;
    [Tooltip("Speed/frequency of status/warning pulse.")]
    [SerializeField] private float statusPulseSpeed    = 4f;

    // Private State
    private string    customStatusMessage;
    private Color     customStatusColor = Color.white;
    private Coroutine statusPulseCoroutine;
    private Coroutine warningImagePulseCoroutine;
    private Coroutine checkpointNotificationCoroutine;
    private Coroutine checkpointReachedCoroutine;
    private Vector2   checkpointReachedInitialPos;
    private Vector3   checkpointReachedInitialScale = Vector3.one;
    private Vector3   originalStatusTextScale = Vector3.one;
    private Image     currentActiveWarningImage;

    // Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        // Auto-discover statusText if unassigned 
        if (statusText == null)
        {
            GameObject gamePanel = GameObject.Find("GamePanel");
            if (gamePanel != null)
            {
                TMP_Text[] texts = gamePanel.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in texts)
                {
                    string n = t.gameObject.name.ToLower();
                    if (n.Contains("warning") || n.Contains("status") || n.Contains("alert"))
                    {
                        statusText = t;
                        break;
                    }
                }
            }

            if (statusText == null)
            {
                TMP_Text[] allTexts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var t in allTexts)
                {
                    string n = t.gameObject.name.ToLower();
                    if (n.Contains("warning") || n.Contains("status") || n.Contains("alert"))
                    {
                        statusText = t;
                        break;
                    }
                }
            }
        }

        // Cache initial scale and initialize statusText
        if (statusText != null)
        {
            statusText.gameObject.SetActive(true);
            statusText.enabled = true;
            originalStatusTextScale = statusText.transform.localScale;
            if (originalStatusTextScale == Vector3.zero)
                originalStatusTextScale = Vector3.one;
            statusText.text = "";
        }

        // Overlay Images Init 
        InitOverlayImage(lowFuelWarningImage);
        InitOverlayImage(criticalDamageWarningImage);
        InitOverlayImage(engineCriticalWarningImage);
        InitOverlayImage(engineOverheatWarningImage);
        InitOverlayImage(checkpointReachedImage);
        if (checkpointReachedImage != null)
        {
            checkpointReachedInitialPos = checkpointReachedImage.rectTransform.anchoredPosition;
            checkpointReachedInitialScale = checkpointReachedImage.transform.localScale;
            if (checkpointReachedInitialScale == Vector3.zero)
                checkpointReachedInitialScale = Vector3.one;
        }
        InitOverlayImage(missedCheckpointImage);

        RefreshWarningDisplay();
    }

    private void OnDestroy()
    {
        StopStatusTextPulse();
        StopWarningImagePulse();
        StopCheckpointReachedAnimation();
        if (checkpointNotificationCoroutine != null)
        {
            StopCoroutine(checkpointNotificationCoroutine);
            checkpointNotificationCoroutine = null;
        }
    }

    // Warning & Status Priority Management --------------------->

 
    /// Evaluates current active warnings and displays the highest priority warning indicator/text.
    /// Priority: Critical Hull Damage > Engine Critical > Overheating > Low Fuel > Custom Status.

    public void RefreshWarningDisplay()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing)
        {
            HideAllWarningImages();
            ClearWarning();
            SoundEffectsManager.Instance?.StopWarningSiren();
            return;
        }

        bool hullCrit   = UIStats.Instance != null && UIStats.Instance.IsHullCritical;
        bool engineCrit = UIStats.Instance != null && UIStats.Instance.IsEngineCritical;
        bool overheat   = UIStats.Instance != null && UIStats.Instance.IsEngineOverheating;
        bool lowFuel    = UIStats.Instance != null && UIStats.Instance.IsLowFuel;

        if (hullCrit)
        {
            UIStats.Instance?.StartVignette();
            DisplayActiveWarning(criticalDamageWarningImage, "CRITICAL DAMAGE!", criticalWarningColor);
            SoundEffectsManager.Instance?.StartWarningSiren();
        }
        else if (engineCrit)
        {
            UIStats.Instance?.StartVignette();
            DisplayActiveWarning(engineCriticalWarningImage, "ENGINE CRITICAL!", criticalWarningColor);
            SoundEffectsManager.Instance?.StartWarningSiren();
        }
        else if (overheat)
        {
            UIStats.Instance?.StopVignette();
            DisplayActiveWarning(engineOverheatWarningImage, "ENGINE OVERHEATING!", warningColor);
            SoundEffectsManager.Instance?.StopWarningSiren();
        }
        else if (lowFuel)
        {
            UIStats.Instance?.StopVignette();
            DisplayActiveWarning(lowFuelWarningImage, "LOW FUEL!", warningColor);
            SoundEffectsManager.Instance?.StartWarningSiren();
        }
        else if (!string.IsNullOrEmpty(customStatusMessage))
        {
            UIStats.Instance?.StopVignette();
            HideAllWarningImages();
            ShowWarning(customStatusMessage, customStatusColor);
            SoundEffectsManager.Instance?.StopWarningSiren();
        }
        else
        {
            UIStats.Instance?.StopVignette();
            HideAllWarningImages();
            ClearWarning();
            SoundEffectsManager.Instance?.StopWarningSiren();
        }
    }

    private void DisplayActiveWarning(Image warningImg, string fallbackText, Color color)
    {
        // 1. If warning PNG image is assigned, show and pulse it
        if (warningImg != null)
        {
            HideAllWarningImagesExcept(warningImg);
            StartWarningImagePulse(warningImg);

            // Hide fallback text when image is active
            if (statusText != null)
            {
                statusText.text = "";
                StopStatusTextPulse();
            }
        }
        else
        {
            // 2. Otherwise fall back to pulsing text
            HideAllWarningImages();
            ShowWarning(fallbackText, color);
        }
    }

    /// Displays a custom warning or status message with text pulsing.
    public void ShowWarning(string message, Color color)
    {
        if (statusText == null) return;

        if (!statusText.gameObject.activeSelf) statusText.gameObject.SetActive(true);
        if (!statusText.enabled) statusText.enabled = true;

        statusText.text  = message;
        statusText.color = color;

        StartStatusTextPulse();
    }

    /// Clears status/warning text and stops its pulse.
    public void ClearWarning()
    {
        if (statusText != null)
        {
            statusText.text = "";
            StopStatusTextPulse();
        }
    }

   
    /// Clears all warning flags in UIStats, hides all warning indicators, stops text pulse and siren.

    public void ClearAllWarnings()
    {
        UIStats.Instance?.ResetWarningFlags();
        customStatusMessage = null;
        HideAllWarningImages();
        ClearWarning();
        SoundEffectsManager.Instance?.StopWarningSiren();
    }

    private void HideAllWarningImages()
    {
        StopWarningImagePulse();
        SetImageActive(lowFuelWarningImage, false);
        SetImageActive(criticalDamageWarningImage, false);
        SetImageActive(engineCriticalWarningImage, false);
        SetImageActive(engineOverheatWarningImage, false);
    }

    private void HideAllWarningImagesExcept(Image keepImg)
    {
        if (lowFuelWarningImage != keepImg)        SetImageActive(lowFuelWarningImage, false);
        if (criticalDamageWarningImage != keepImg) SetImageActive(criticalDamageWarningImage, false);
        if (engineCriticalWarningImage != keepImg) SetImageActive(engineCriticalWarningImage, false);
        if (engineOverheatWarningImage != keepImg) SetImageActive(engineOverheatWarningImage, false);
    }

    private void SetImageActive(Image img, bool active)
    {
        if (img == null) return;
        img.enabled = active;
        if (img.gameObject.activeSelf != active)
            img.gameObject.SetActive(active);

        if (!active)
        {
            Color c = img.color;
            c.a = 0f;
            img.color = c;
        }
    }

    //  Checkpoint Notifications (PNG Banner Display) -------------->

    /// <summary>
    /// Displays a dedicated "Checkpoint Reached" banner with zoom-in punch, upward floating motion,
    /// and slow fade-out over the specified duration.
    /// </summary>
    public void ShowCheckpointReached(float duration = 2.5f)
    {
        if (checkpointReachedCoroutine != null)
            StopCoroutine(checkpointReachedCoroutine);

        if (checkpointReachedImage != null)
        {
            checkpointReachedCoroutine = StartCoroutine(AnimateCheckpointReachedCoroutine(duration));
        }
        else
        {
            if (checkpointNotificationCoroutine != null)
                StopCoroutine(checkpointNotificationCoroutine);

            checkpointNotificationCoroutine = StartCoroutine(AnimateNotificationBanner(
                null, "CHECKPOINT REACHED", Color.green, duration));
        }
    }

    private void StopCheckpointReachedAnimation()
    {
        if (checkpointReachedCoroutine != null)
        {
            StopCoroutine(checkpointReachedCoroutine);
            checkpointReachedCoroutine = null;
        }

        if (checkpointReachedImage != null)
        {
            SetImageActive(checkpointReachedImage, false);
            checkpointReachedImage.rectTransform.anchoredPosition = checkpointReachedInitialPos;
            checkpointReachedImage.transform.localScale = checkpointReachedInitialScale;
        }
    }

    /// <summary>
    /// Dedicated animation coroutine for Checkpoint Reached:
    /// 1. Zoom punch in on activation (starts at 0.65x, zooms to 1.15x, settles to 1.0x).
    /// 2. Moves upward continuously (smooth floating motion).
    /// 3. Fades in quickly, holds, and fades out slowly.
    /// </summary>
    private IEnumerator AnimateCheckpointReachedCoroutine(float duration)
    {
        if (checkpointReachedImage == null) yield break;

        checkpointReachedImage.enabled = true;
        checkpointReachedImage.gameObject.SetActive(true);

        RectTransform rect = checkpointReachedImage.rectTransform;
        Vector2 startPos = checkpointReachedInitialPos;
        Vector3 baseScale = checkpointReachedInitialScale;
        float floatDistance = 75f; // pixels moved upward
        float elapsed = 0f;

        // Initial setup
        Color c = checkpointReachedImage.color;
        c.a = 0f;
        checkpointReachedImage.color = c;
        rect.anchoredPosition = startPos;
        checkpointReachedImage.transform.localScale = baseScale * 0.65f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            // 1. Zoom Effect: Zooms up to 1.15x then eases back to 1.0x
            float currentScaleMultiplier;
            if (elapsed < 0.3f)
            {
                float zoomT = elapsed / 0.3f;
                currentScaleMultiplier = Mathf.Lerp(0.65f, 1.15f, Mathf.SmoothStep(0f, 1f, zoomT));
            }
            else if (elapsed < 0.5f)
            {
                float settleT = (elapsed - 0.3f) / 0.2f;
                currentScaleMultiplier = Mathf.Lerp(1.15f, 1.0f, Mathf.SmoothStep(0f, 1f, settleT));
            }
            else
            {
                currentScaleMultiplier = 1.0f;
            }
            checkpointReachedImage.transform.localScale = baseScale * currentScaleMultiplier;

            // 2. Upward Movement: Smooth continuous float upward
            float moveProgress = Mathf.SmoothStep(0f, 1f, progress);
            rect.anchoredPosition = startPos + Vector2.up * (floatDistance * moveProgress);

            // 3. Alpha Fade: Fast fade-in (first 0.25s), holds until 40% duration, then slow fade-out
            float alpha = 1f;
            if (elapsed < 0.25f)
            {
                alpha = elapsed / 0.25f;
            }
            else if (progress > 0.4f)
            {
                float fadeProgress = (progress - 0.4f) / 0.6f;
                alpha = 1f - Mathf.SmoothStep(0f, 1f, fadeProgress);
            }

            c.a = Mathf.Clamp01(alpha);
            checkpointReachedImage.color = c;

            yield return null;
        }

        SetImageActive(checkpointReachedImage, false);
        rect.anchoredPosition = startPos;
        checkpointReachedImage.transform.localScale = baseScale;
        checkpointReachedCoroutine = null;
    }

    /// <summary>
    /// Displays a pulsing "Missed Checkpoint" warning banner PNG for the specified duration.
    /// </summary>
    public void ShowMissedCheckpoint(float duration = 3.0f)
    {
        if (checkpointNotificationCoroutine != null)
            StopCoroutine(checkpointNotificationCoroutine);

        checkpointNotificationCoroutine = StartCoroutine(AnimateNotificationBanner(
            missedCheckpointImage, "MISSED CHECKPOINT!", warningColor, duration));
    }

    private IEnumerator AnimateNotificationBanner(Image bannerImg, string fallbackText, Color fallbackColor, float duration)
    {
        if (bannerImg != null)
        {
            bannerImg.enabled = true;
            bannerImg.gameObject.SetActive(true);

            Vector3 baseScale = Vector3.one;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float alpha = 1f;

                // Fade in (first 0.3s)
                if (elapsed < 0.3f)
                    alpha = elapsed / 0.3f;
                // Fade out (last 0.5s)
                else if (elapsed > duration - 0.5f)
                    alpha = (duration - elapsed) / 0.5f;

                // Subtle pulsing scale
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * statusPulseSpeed) * 0.08f;
                bannerImg.transform.localScale = baseScale * pulse;

                Color c = bannerImg.color;
                c.a = Mathf.Clamp01(alpha);
                bannerImg.color = c;

                yield return null;
            }

            SetImageActive(bannerImg, false);
            bannerImg.transform.localScale = baseScale;
        }
        else
        {
            // Fallback to temporary status text
            SetStatusText(fallbackText);
            yield return new WaitForSeconds(duration);
            SetStatusText(null);
        }

        checkpointNotificationCoroutine = null;
    }

    /// <summary>
    /// Updates custom status text. If message is empty, restores active game warnings.
    /// </summary>
    public void SetStatusText(string message, bool pulse = true)
    {
        customStatusMessage = message;
        customStatusColor   = normalStatusColor;

        if (!string.IsNullOrEmpty(message))
        {
            ShowWarning(message, normalStatusColor);
        }
        else
        {
            RefreshWarningDisplay();
        }
    }

    // ── Warning Image Pulse Animation ────────────────────────────────────────

    private void StartWarningImagePulse(Image img)
    {
        if (img == null) return;
        if (currentActiveWarningImage == img && warningImagePulseCoroutine != null) return;

        StopWarningImagePulse();
        currentActiveWarningImage = img;
        img.enabled = true;
        img.gameObject.SetActive(true);

        warningImagePulseCoroutine = StartCoroutine(PulseWarningImageCoroutine(img));
    }

    private void StopWarningImagePulse()
    {
        if (warningImagePulseCoroutine != null)
        {
            StopCoroutine(warningImagePulseCoroutine);
            warningImagePulseCoroutine = null;
        }

        if (currentActiveWarningImage != null)
        {
            currentActiveWarningImage.transform.localScale = Vector3.one;
            currentActiveWarningImage = null;
        }
    }

    private IEnumerator PulseWarningImageCoroutine(Image img)
    {
        Vector3 baseScale = Vector3.one;

        while (img != null && img.enabled)
        {
            float t = (Mathf.Sin(Time.unscaledTime * statusPulseSpeed) + 1f) * 0.5f;
            float scale = Mathf.Lerp(statusPulseMinScale, statusPulseMaxScale, t);
            img.transform.localScale = baseScale * scale;

            Color c = img.color;
            c.a = Mathf.Lerp(0.6f, 1f, t);
            img.color = c;

            yield return null;
        }
    }

    // ── Text Pulse Animation ─────────────────────────────────────────────────

    private void StartStatusTextPulse()
    {
        if (statusText == null) return;
        if (statusPulseCoroutine != null) StopCoroutine(statusPulseCoroutine);
        statusPulseCoroutine = StartCoroutine(UIAnimUtils.PulseTextScale(
            statusText, originalStatusTextScale, statusPulseMinScale, statusPulseMaxScale, statusPulseSpeed));
    }

    private void StopStatusTextPulse()
    {
        if (statusPulseCoroutine != null)
        {
            StopCoroutine(statusPulseCoroutine);
            statusPulseCoroutine = null;
        }

        if (statusText != null)
            statusText.transform.localScale = originalStatusTextScale;
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private void InitOverlayImage(Image img)
    {
        if (img == null) return;
        img.enabled       = false;
        Color c           = img.color;
        c.a               = 0f;
        img.color         = c;
        img.raycastTarget = false; // never block UI clicks
    }
}
