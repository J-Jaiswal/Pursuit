using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns all ship telemetry bar/slider updates and the critical hull vignette FX.
/// The only component that directly subscribes to CollisionHandler, Fuel, and EngineHeat events.
/// </summary>
public class UIStats : MonoBehaviour
{
    public static UIStats Instance { get; private set; }

    // Dependencies 
    [SerializeField] private RocketConfigSO   config;
    [SerializeField] private CollisionHandler collisionHandler;
    [SerializeField] private Fuel             fuel;
    [SerializeField] private EngineHeat       engineHeat;

    // Rocket Hull Health UI 
    [Header("Rocket Hull Health UI")]
    [Tooltip("Slider for rocket hull HP (death condition).")]
    [SerializeField] private Slider   healthSlider;
    [SerializeField] private Image    healthFill;
    [SerializeField] private Gradient healthGradient;

    // Engine Health UI
    [Header("Engine Health UI")]
    [Tooltip("Slider for engine HP (thrust performance + engine-failure death).")]
    [SerializeField] private Slider   engineHealthSlider;
    [SerializeField] private Image    engineHealthFill;
    [SerializeField] private Gradient engineHealthGradient;

    // Fuel UI 
    [Header("Fuel UI")]
    [SerializeField] private Slider   fuelSlider;
    [SerializeField] private Image    fuelFill;
    [SerializeField] private Gradient fuelGradient;

    // Thrust / Heat UI 
    [Header("Thrust / Heat UI")]
    [SerializeField] private Slider   thrustSlider;
    [SerializeField] private Image    thrustFill;
    [SerializeField] private Gradient thrustGradient;

    // Critical Vignette
    [Header("Critical Vignette")]
    [Tooltip("Full-screen red overlay pulsed when hull or engine reaches critical state.")]
    [SerializeField] private Image criticalVignette;
    [Tooltip("Sine-wave frequency of the vignette pulse.")]
    [SerializeField] private float vignettePulseSpeed = 3f;
    [Tooltip("Maximum alpha of the vignette at peak of pulse.")]
    [SerializeField] private float vignetteMaxAlpha   = 0.6f;

    // Public Warning State (read-only; consumed by UIWarnings) 
    public bool IsHullCritical      { get; private set; }
    public bool IsEngineCritical    { get; private set; }
    public bool IsEngineOverheating { get; private set; }
    public bool IsLowFuel           { get; private set; }
    public bool IsFuelEmpty         { get; private set; }

    // Vignette Private State
    private bool      isPulseVignette;
    private Coroutine vignetteCoroutine;

    // Unity Lifecycle ---------------->

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        // Auto-discover player components if not assigned in Inspector
        if (collisionHandler == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                collisionHandler = playerObj.GetComponent<CollisionHandler>()
                                ?? playerObj.GetComponentInChildren<CollisionHandler>()
                                ?? playerObj.GetComponentInParent<CollisionHandler>();
            }

            if (collisionHandler == null)
                collisionHandler = FindAnyObjectByType<CollisionHandler>();
        }

        if (collisionHandler != null)
        {
            GameObject playerGO = collisionHandler.gameObject;
            if (fuel == null)
                fuel = playerGO.GetComponent<Fuel>()
                    ?? playerGO.GetComponentInChildren<Fuel>()
                    ?? playerGO.GetComponentInParent<Fuel>();

            if (engineHeat == null)
                engineHeat = playerGO.GetComponent<EngineHeat>()
                          ?? playerGO.GetComponentInChildren<EngineHeat>()
                          ?? playerGO.GetComponentInParent<EngineHeat>();
        }
        else
        {
            if (fuel == null)       fuel       = FindAnyObjectByType<Fuel>();
            if (engineHeat == null) engineHeat = FindAnyObjectByType<EngineHeat>();
        }

        // Init vignette overlay
        InitOverlayImage(criticalVignette);

        // Subscribe events & seed initial bar values
        if (collisionHandler != null)
        {
            collisionHandler.OnHealthChanged       += HandleRocketHealthChanged;
            collisionHandler.OnEngineHealthChanged += HandleEngineHealthChanged;
            collisionHandler.OnCriticalDamage      += HandleCriticalDamage;
            collisionHandler.OnEngineCritical      += HandleEngineCritical;

            float initRocket = collisionHandler.NormalizedRocketHealth > 0f
                ? collisionHandler.NormalizedRocketHealth : 1f;
            float initEngine = collisionHandler.NormalizedEngineHealth > 0f
                ? collisionHandler.NormalizedEngineHealth : 1f;

            HandleRocketHealthChanged(initRocket);
            HandleEngineHealthChanged(initEngine);
        }
        else
        {
            HandleRocketHealthChanged(1f);
            HandleEngineHealthChanged(1f);
        }

        if (fuel != null)
        {
            fuel.OnFuelChanged += HandleFuelChanged;
            fuel.OnFuelEmpty   += HandleFuelEmpty;
            HandleFuelChanged(1f);
        }
        else
        {
            HandleFuelChanged(1f);
        }

        if (engineHeat != null)
        {
            engineHeat.OnHeatChanged += HandleHeatChanged;
            HandleHeatChanged(0f);
        }
        else
        {
            HandleHeatChanged(0f);
        }
    }

    private void OnDestroy()
    {
        if (collisionHandler != null)
        {
            collisionHandler.OnHealthChanged       -= HandleRocketHealthChanged;
            collisionHandler.OnEngineHealthChanged -= HandleEngineHealthChanged;
            collisionHandler.OnCriticalDamage      -= HandleCriticalDamage;
            collisionHandler.OnEngineCritical      -= HandleEngineCritical;
        }

        if (fuel != null)
        {
            fuel.OnFuelChanged -= HandleFuelChanged;
            fuel.OnFuelEmpty   -= HandleFuelEmpty;
        }

        if (engineHeat != null)
            engineHeat.OnHeatChanged -= HandleHeatChanged;
    }

    // Health Handlers --------------->

    private void HandleRocketHealthChanged(float normalizedRocketHealth)
    {
        if (healthSlider != null) healthSlider.value = normalizedRocketHealth;
        if (healthFill != null && healthGradient != null)
            healthFill.color = healthGradient.Evaluate(normalizedRocketHealth);

        float threshold = config != null ? config.CriticalHealthThreshold : 0.3f;
        IsHullCritical  = normalizedRocketHealth <= threshold && normalizedRocketHealth > 0f;

        if (IsHullCritical) StartVignette();
        else                StopVignette();

        UIWarnings.Instance?.RefreshWarningDisplay();
    }

    private void HandleEngineHealthChanged(float normalizedEngineHealth)
    {
        if (engineHealthSlider != null) engineHealthSlider.value = normalizedEngineHealth;
        if (engineHealthFill != null && engineHealthGradient != null)
            engineHealthFill.color = engineHealthGradient.Evaluate(normalizedEngineHealth);

        float threshold  = config != null ? config.EngineCriticalThreshold : 0.2f;
        IsEngineCritical = normalizedEngineHealth <= threshold && normalizedEngineHealth > 0f;

        UIWarnings.Instance?.RefreshWarningDisplay();
    }

    private void HandleHeatChanged(float normalizedHeat)
    {
        if (thrustSlider != null) thrustSlider.value = normalizedHeat;
        if (thrustFill != null && thrustGradient != null)
            thrustFill.color = thrustGradient.Evaluate(normalizedHeat);

        float threshold     = config != null ? config.OverheatWarningThreshold : 0.8f;
        IsEngineOverheating = normalizedHeat >= threshold;

        UIWarnings.Instance?.RefreshWarningDisplay();
    }

    private void HandleCriticalDamage()
    {
        IsHullCritical = true;
        StartVignette();
        UIWarnings.Instance?.RefreshWarningDisplay();
    }

    private void HandleEngineCritical()
    {
        IsEngineCritical = true;
        UIWarnings.Instance?.RefreshWarningDisplay();
    }

    // Fuel Handlers -------------->

    private void HandleFuelChanged(float normalizedFuel)
    {
        if (fuelSlider != null) fuelSlider.value = normalizedFuel;
        if (fuelFill != null && fuelGradient != null)
            fuelFill.color = fuelGradient.Evaluate(normalizedFuel);

        // Only update warning state while actively playing — bar still updates visually
        if (GameManager.Instance != null &&
            GameManager.Instance.CurrentState != GameState.Playing) return;

        float threshold = config != null ? config.CriticalFuelThreshold : 0.2f;
        IsLowFuel   = normalizedFuel <= threshold && normalizedFuel > 0f;
        IsFuelEmpty = normalizedFuel <= 0f;

        UIWarnings.Instance?.RefreshWarningDisplay();
    }

    private void HandleFuelEmpty()
    {
        IsFuelEmpty = true;
        UIWarnings.Instance?.RefreshWarningDisplay();
    }

    // Vignette FX ----------------->
    // Public so UIWarnings.RefreshWarningDisplay() and UIManager state machine can control it.

    ///Starts the red full-screen vignette pulse for hull/engine critical states.
    public void StartVignette()
    {
        if (criticalVignette == null) return;

        criticalVignette.enabled = true;
        if (!criticalVignette.gameObject.activeSelf)
            criticalVignette.gameObject.SetActive(true);

        if (isPulseVignette) return;
        isPulseVignette = true;

        if (vignetteCoroutine != null) StopCoroutine(vignetteCoroutine);
        vignetteCoroutine = StartCoroutine(PulseVignetteCoroutine());
    }

    /// Stops the vignette pulse and snaps alpha to zero
    public void StopVignette()
    {
        if (!isPulseVignette && (criticalVignette == null || !criticalVignette.enabled)) return;
        isPulseVignette = false;

        if (vignetteCoroutine != null)
        {
            StopCoroutine(vignetteCoroutine);
            vignetteCoroutine = null;
        }

        UIAnimUtils.SnapAlphaToZero(criticalVignette);
    }

    private IEnumerator PulseVignetteCoroutine()
    {
        float speed    = vignettePulseSpeed;
        float maxAlpha = vignetteMaxAlpha;

        if (criticalVignette != null)
        {
            criticalVignette.enabled = true;
            if (!criticalVignette.gameObject.activeSelf)
                criticalVignette.gameObject.SetActive(true);
        }

        while (isPulseVignette && criticalVignette != null)
        {
            float t = (Mathf.Sin(Time.unscaledTime * speed) + 1f) * 0.5f;
            Color c = criticalVignette.color;
            c.a     = t * maxAlpha;
            criticalVignette.color = c;
            yield return null;
        }

        UIAnimUtils.SnapAlphaToZero(criticalVignette);
    }

    // Public Flag Reset   ------------->

  
    /// Resets all warning-state flags to false.
    /// Called by UIWarnings.ClearAllWarnings() — the flags are owned here but

    public void ResetWarningFlags()
    {
        IsHullCritical      = false;
        IsEngineCritical    = false;
        IsEngineOverheating = false;
        IsLowFuel           = false;
        IsFuelEmpty         = false;
    }

    // Private Helpers --------------->

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
