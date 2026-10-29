using TMPro;
using UnityEngine;

/// <summary>
/// Controls always-on HUD elements that poll or update every frame,
/// such as current mission phase name, timers, or score indicators.
/// </summary>
public class UICore : MonoBehaviour
{
    public static UICore Instance { get; private set; }

    [Header("Always-On HUD")]
    [Tooltip("Text element displaying current level phase name (e.g. 'Phase 1 - Asteroid Belt').")]
    [SerializeField] private TMP_Text phaseReachedText;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Update()
    {
        UpdatePhaseIndicator();
    }

    private void UpdatePhaseIndicator()
    {
        if (phaseReachedText == null) return;

        if (LevelManager.Instance != null)
        {
            string phaseName = LevelManager.Instance.GetCurrentPhaseName();
            phaseReachedText.text = !string.IsNullOrEmpty(phaseName) ? phaseName : "";
        }
    }
}
