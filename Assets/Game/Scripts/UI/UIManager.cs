using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [SerializeField] private RocketConfigSO config;
    [SerializeField] private CollisionHandler collisionHandler;
    [SerializeField] private Fuel fuel;
    [SerializeField] private Movements movements;
    [SerializeField] private EngineHeat engineHeat;

    [Header("Health UI")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Image healthFill;
    [SerializeField] private Gradient healthGradient;
    [SerializeField] private TMP_Text healthEffText;

    [Header("Fuel UI")]
    [SerializeField] private Slider fuelSlider;
    [SerializeField] private Image fuelFill;
    [SerializeField] private Gradient fuelGradient;

    [Header("Thrust UI")]
    [SerializeField] private Slider thrustSlider;
    [SerializeField] private Image thrustFill;
    [SerializeField] private Gradient thrustGradient;

    [Header("Overlay UI")]
    [SerializeField] private Image criticalVignette;
    [SerializeField] private GameObject fadePanel;
    [SerializeField] private TMP_Text statusText;

    [Header("Missile Proximity Indicator")]
    [Tooltip("UI element that appears when missile is within 15 units. Assign a warning text/icon.")]
    [SerializeField] private GameObject missileWarningPanel;
    [Tooltip("Text label inside the warning group (e.g. '⚠ MISSILE CLOSE').")]
    [SerializeField] private TMP_Text    missileWarningText;
    [Tooltip("Distance at which the warning starts fading in.")]
    [SerializeField] private float       missileWarningShowDistance = 15f;
    [Tooltip("Distance at which warning turns red and pulses urgently.")]
    [SerializeField] private float       missileWarningUrgentDistance = 8f;

    [Header("Post-Race Debrief")]
    [Tooltip("The GameObject panel of the PostRaceDebrief overlay panel for coordination.")]
    [SerializeField] private GameObject debriefPanel;

    private bool isPulseVignette;
    private float currentMissileDistance = 999f;

    private void Start()
    {
        if (collisionHandler != null)
        {
            collisionHandler.OnHealthChanged += HandleHealthChanged;
            collisionHandler.OnCriticalDamage += HandleCriticalDamage;
            HandleHealthChanged(1f);
        }

        if (fuel != null)
        {
            fuel.OnFuelChanged += HandleFuelChanged;
            fuel.OnFuelEmpty += HandleFuelEmpty;
            HandleFuelChanged(1f);
        }

        if (engineHeat == null && collisionHandler != null)
        {
            engineHeat = collisionHandler.GetComponent<EngineHeat>();
        }

        if (engineHeat != null)
        {
            engineHeat.OnHeatChanged += HandleHeatChanged;
            HandleHeatChanged(0f);
        }

        GameManager.OnPlayerDied += HandlePlayerDied;
        GameManager.OnLevelComplete += HandleLevelComplete;

        if (statusText != null)
        {
            statusText.text = "";
        }

        if (fadePanel != null)
        {
            fadePanel.SetActive(false);
        }

        if (criticalVignette != null)
        {
            Color c = criticalVignette.color;
            c.a = 0f;
            criticalVignette.color = c;
        }

        if (missileWarningPanel != null)
        {
            missileWarningPanel.SetActive(false);
        }

        RaceContext.OnMissileDistanceUpdated += HandleMissileDistanceUpdated;
    }

    private void OnDestroy()
    {
        if (collisionHandler != null)
        {
            collisionHandler.OnHealthChanged -= HandleHealthChanged;
            collisionHandler.OnCriticalDamage -= HandleCriticalDamage;
        }

        if (fuel != null)
        {
            fuel.OnFuelChanged -= HandleFuelChanged;
            fuel.OnFuelEmpty -= HandleFuelEmpty;
        }

        if (engineHeat != null)
        {
            engineHeat.OnHeatChanged -= HandleHeatChanged;
        }

        GameManager.OnPlayerDied -= HandlePlayerDied;
        GameManager.OnLevelComplete -= HandleLevelComplete;
        RaceContext.OnMissileDistanceUpdated -= HandleMissileDistanceUpdated;
    }

    private void Update()
    {
        UpdateMissileProximityIndicator();
    }

    private void UpdateMissileProximityIndicator()
    {
        if (missileWarningPanel == null) return;

        float dist = currentMissileDistance;

        if (dist > missileWarningShowDistance)
        {
            missileWarningPanel.SetActive(false);
            return;
        }

        missileWarningPanel.SetActive(true);

        if (missileWarningText != null)
        {
            if (dist < missileWarningUrgentDistance)
            {
                // Urgent: pulse red
                float pulse = (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f;
                missileWarningText.color = Color.Lerp(new Color(1f, 0.3f, 0.3f), Color.red, pulse);
                missileWarningText.text  = $"⚠ MISSILE {dist:F0}m";
            }
            else
            {
                missileWarningText.color = new Color(1f, 0.6f, 0.2f); // orange
                missileWarningText.text  = $"MISSILE {dist:F0}m";
            }
        }
    }

    private void HandleMissileDistanceUpdated(float distance)
    {
        currentMissileDistance = distance;
    }

    private void HandleHealthChanged(float normalizedHealth)
    {
        if (healthSlider != null)
        {
            healthSlider.value = normalizedHealth;
        }

        if (healthFill != null && healthGradient != null)
        {
            healthFill.color = healthGradient.Evaluate(normalizedHealth);
        }

        if (healthEffText != null)
        {
            healthEffText.text = $"Engine Health: {(normalizedHealth * 100f):F0}%";
        }

        float threshold = config != null ? config.CriticalHealthThreshold : 0.3f;
        if (normalizedHealth >= threshold)
        {
            isPulseVignette = false;
            if (criticalVignette != null)
            {
                Color c = criticalVignette.color;
                c.a = 0f;
                criticalVignette.color = c;
            }
        }
    }

    private void HandleHeatChanged(float normalizedHeat)
    {
        if (thrustSlider != null)
        {
            thrustSlider.value = normalizedHeat;
        }

        if (thrustFill != null && thrustGradient != null)
        {
            thrustFill.color = thrustGradient.Evaluate(normalizedHeat);
        }
    }

    private void HandleCriticalDamage()
    {
        if (!isPulseVignette)
        {
            isPulseVignette = true;
            StartCoroutine(PulseVignetteCoroutine());
        }
    }

    private IEnumerator PulseVignetteCoroutine()
    {
        while (isPulseVignette)
        {
            if (criticalVignette != null)
            {
                Color c = criticalVignette.color;
                c.a = Mathf.PingPong(Time.time * 2f, 0.5f);
                criticalVignette.color = c;
            }
            yield return null;
        }
    }

    private void HandleFuelChanged(float normalizedFuel)
    {
        if (fuelSlider != null) fuelSlider.value = normalizedFuel;
        if (fuelFill != null && fuelGradient != null)
            fuelFill.color = fuelGradient.Evaluate(normalizedFuel);

        // only write if no higher priority message is showing
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        float threshold = config != null ? config.CriticalFuelThreshold : 0.2f;
        if (statusText != null && normalizedFuel <= threshold && normalizedFuel > 0f)
            statusText.text = "Low Fuel!";
        else if (statusText != null && normalizedFuel > threshold)
            statusText.text = "";
    }

    private void HandleFuelEmpty()
    {
        if (statusText != null)
        {
            statusText.text = "Fuel Empty!";
        }
    }

    private void HandlePlayerDied()
    {
        if (statusText != null)
        {
            statusText.text = "Rocket Destroyed! Restarting...";
        }
        float duration = config != null ? config.DeathSequenceDuration : 1.5f;
        StartCoroutine(FadeScreenCoroutine(0f, 1f, duration));
    }

    private void HandleLevelComplete()
    {
        if (statusText != null)
        {
            statusText.text = "Level Complete! Proceeding...";
        }
        float duration = config != null ? config.LandingSequenceDuration : 1.0f;
        StartCoroutine(FadeScreenCoroutine(0f, 1f, duration));
    }

    private IEnumerator FadeScreenCoroutine(float startAlpha, float endAlpha, float duration)
    {
        if (fadePanel == null) yield break;

        if (startAlpha < endAlpha)
        {
            fadePanel.SetActive(true);
        }

        Image fadeImage = fadePanel.GetComponent<Image>();
        if (fadeImage != null)
        {
            Color color = fadeImage.color;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                color.a = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
                fadeImage.color = color;
                elapsed += Time.deltaTime;
                yield return null;
            }
            color.a = endAlpha;
            fadeImage.color = color;
        }
        else
        {
            yield return new WaitForSeconds(duration);
        }

        if (endAlpha <= 0f)
        {
            fadePanel.SetActive(false);
        }
    }
}
