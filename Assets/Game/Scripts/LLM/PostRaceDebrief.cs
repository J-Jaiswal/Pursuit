// ══════════════════════════════════════════════════════════════════════════════
// [DEPRECATED — FULLY DISABLED]
// PostRaceDebrief has been replaced by the DeathPanel section in UIManager.cs.
// The LLM-driven debrief flow (TTS, typewriter, NOVA voice line) is no longer used.
// This file is kept for reference only. All code is commented out below.
// ══════════════════════════════════════════════════════════════════════════════

/*
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [DEPRECATED — DISABLED FOR NOW]
///
/// Post-run debrief. Triggered on win or loss. Shows race stats, plays a 2-3 sentence
/// LLM-generated debrief via TTS, and runs a typewriter text effect that syncs with audio.
///
/// Speaker selection:
///   Win  → Mission Control (professional, data-focused)
///   Lose → NOVA (gloating, specific to cause of death)
///
/// SETUP:
///   1. Create a dark overlay CanvasGroup panel.
///   2. Wire up all serialized UI fields in the Inspector.
///   3. Wire up LLMService, LLMSecretsSO references.
///   4. Assign Continue and Restart buttons.
///
/// NOTE: This component is deprecated. Death screen is now handled by DeathPanel
/// in UIManager. Level-complete flow returns to the main menu directly.
/// </summary>
[System.Obsolete("PostRaceDebrief is deprecated. Death screen is now in UIManager.DeathPanel.")]
public class PostRaceDebrief : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static PostRaceDebrief Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Dependencies")]
    [SerializeField] private LLMService   llmService;
    [SerializeField] private LLMSecretsSO secrets;

    [Header("Debrief Panel UI")]
    [SerializeField] private GameObject  debriefPanel;
    [SerializeField] private TMP_Text     debriefText;
    [SerializeField] private TMP_Text     statsText;
    [SerializeField] private TMP_Text     speakerLabel;    // "NOVA" or "MISSION CONTROL"
    [SerializeField] private GameObject   continueButton;  // shown after debrief complete
    [SerializeField] private GameObject   restartButton;

    [Header("Typewriter")]
    [Tooltip("Characters revealed per second during typewriter effect.")]
    [SerializeField] private float typewriterSpeed = 40f;

    [Header("Fade")]
    [SerializeField] private float panelFadeInDuration = 0.8f;

    [Header("Debrief Audio")]
    [Tooltip("AudioSource for the debrief TTS. 2D (Spatial Blend = 0).")]
    [SerializeField] private AudioSource debriefAudioSource;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool isShowing;

    // ── System Prompts ────────────────────────────────────────────────────────

    private const string WinSystemPrompt =
        "You are Mission Control. The pilot just completed the level. " +
        "Give a 2-3 sentence professional debrief using the race stats provided. " +
        "Data-focused. Note the closest call. One improvement suggestion. No markdown.";

    private const string LoseSystemPrompt =
        "You are NOVA, the missile that just caught the pilot. " +
        "Give a 2-3 sentence gloating debrief using the race stats. " +
        "Be specific about what killed them. Cold and precise, not cartoonish. No markdown.";

    // ── Win Fallbacks ─────────────────────────────────────────────────────────
    private static readonly string[] WinFallbacks =
    {
        "Run complete. Fuel and hull held. Missile fell behind at the last corridor. Solid execution.",
        "Level cleared. Watch the hazard clusters on the next run. You slowed unnecessarily.",
        "Mission accomplished. The missile was close at the end. Study your route.",
    };

    // ── Lose Fallbacks ────────────────────────────────────────────────────────
    private static readonly string[] LoseFallbacks =
    {
        "You slowed down. I don't slow down. Try again.",
        "The cave did half my work. You did the rest.",
        "I caught you at the bend. Same mistake, every run.",
    };

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Hide panel initially
        if (debriefPanel != null)
        {
            debriefPanel.SetActive(false);
        }

        if (continueButton != null) continueButton.SetActive(false);
        if (restartButton  != null) restartButton.SetActive(false);
    }

    private void Start()
    {
        GameManager.OnPlayerDied    += HandlePlayerDied;
        GameManager.OnLevelComplete += HandleLevelComplete;

        BindButton(restartButton,  OnRestartClicked,  "RestartButton");
        BindButton(continueButton, OnContinueClicked, "ContinueButton");
    }

    private void BindButton(GameObject buttonGO, UnityEngine.Events.UnityAction callback, string debugName)
    {
        if (buttonGO == null)
        {
            Debug.LogWarning($"[PostRaceDebrief] {debugName} reference is NULL in Inspector — button will not work.");
            return;
        }

        Button btn = buttonGO.GetComponentInChildren<Button>(includeInactive: true);
        if (btn == null)
        {
            Debug.LogWarning($"[PostRaceDebrief] {debugName}: No Button component found on '{buttonGO.name}' or its children.");
            return;
        }

        btn.onClick.RemoveListener(callback);
        btn.onClick.AddListener(callback);
        Debug.Log($"[PostRaceDebrief] {debugName} bound to '{btn.gameObject.name}'.");
    }


    private void OnDestroy()
    {
        GameManager.OnPlayerDied    -= HandlePlayerDied;
        GameManager.OnLevelComplete -= HandleLevelComplete;
    }

    // ── Event Handlers ────────────────────────────────────────────────────────

    private void HandlePlayerDied()
    {
        if (isShowing) return;
        // Small delay so the death sequence (camera shake, fade) runs first
        StartCoroutine(ShowDebriefAfterDelay(1.5f, isWin: false));
    }

    private void HandleLevelComplete()
    {
        if (isShowing) return;
        StartCoroutine(ShowDebriefAfterDelay(1.0f, isWin: true));
    }

    // ── Debrief Flow ──────────────────────────────────────────────────────────

    private IEnumerator ShowDebriefAfterDelay(float delay, bool isWin)
    {
        yield return new WaitForSeconds(delay);
        StartCoroutine(ShowDebrief(isWin));
    }

    private IEnumerator ShowDebrief(bool isWin)
    {
        isShowing = true;

        RaceContext.GameStateSnapshot snap = RaceContext.Instance != null
            ? RaceContext.Instance.FinalSnapshot
            : default;

        yield return StartCoroutine(FadePanelIn());

        if (statsText   != null) statsText.text   = BuildStatsText(snap, isWin);
        if (speakerLabel != null) speakerLabel.text = isWin ? "MISSION CONTROL" : "N O V A";
        if (debriefText  != null) debriefText.text  = "";

        string sysPrompt  = isWin ? WinSystemPrompt : LoseSystemPrompt;
        string userPrompt = BuildDebriefPrompt(snap, isWin);
        string[] fallbacks = isWin ? WinFallbacks : LoseFallbacks;

        string debriefLine = null;
        bool   llmDone     = false;

        if (llmService != null)
        {
            int maxTokens = secrets != null ? secrets.MaxTokensDebrief : 120;
            llmService.RequestLLMLine(sysPrompt, userPrompt, fallbacks, timeoutSeconds: 8f,
                onSuccess: text => { debriefLine = text; llmDone = true; },
                onFallback: text => { debriefLine = text; llmDone = true; },
                maxTokensOverride: maxTokens);
        }
        else
        {
            debriefLine = fallbacks[UnityEngine.Random.Range(0, fallbacks.Length)];
            llmDone     = true;
        }

        float elapsed = 0f;
        while (!llmDone && elapsed < 8f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (string.IsNullOrEmpty(debriefLine))
            debriefLine = fallbacks[UnityEngine.Random.Range(0, fallbacks.Length)];

        AudioClip debriefClip = null;
        bool      ttsDone     = false;

        if (llmService != null && secrets != null)
        {
            string voiceId = isWin ? secrets.MissionControlVoiceId : secrets.NovaVoiceId;
            llmService.RequestTTS(debriefLine, voiceId, timeoutSeconds: 10f,
                clip => { debriefClip = clip; ttsDone = true; });
        }
        else
        {
            ttsDone = true;
        }

        Coroutine typewriterCoroutine = StartCoroutine(TypewriterEffect(debriefLine));

        elapsed = 0f;
        while (!ttsDone && elapsed < 10f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (debriefClip != null && debriefAudioSource != null)
        {
            debriefAudioSource.clip = debriefClip;
            debriefAudioSource.Play();
        }

        float waitForAudio = debriefClip != null ? debriefClip.length : 0f;
        yield return new WaitForSeconds(Mathf.Max(waitForAudio, debriefLine.Length / typewriterSpeed));

        if (continueButton != null) continueButton.SetActive(isWin);
        if (restartButton  != null) restartButton.SetActive(true);
    }

    // ── Typewriter Effect ─────────────────────────────────────────────────────

    private IEnumerator TypewriterEffect(string fullText)
    {
        if (debriefText == null) yield break;

        debriefText.text = "";
        float delay = 1f / Mathf.Max(typewriterSpeed, 1f);

        foreach (char c in fullText)
        {
            debriefText.text += c;
            yield return new WaitForSeconds(delay);
        }
    }

    // ── Panel Fade ────────────────────────────────────────────────────────────

    private IEnumerator FadePanelIn()
    {
        if (debriefPanel == null) yield break;

        debriefPanel.SetActive(true);

        CanvasGroup cg = debriefPanel.GetComponent<CanvasGroup>();
        if (cg == null) yield break;

        cg.alpha = 0f;
        float t = 0f;

        while (t < panelFadeInDuration)
        {
            t        += Time.deltaTime;
            cg.alpha  = Mathf.Clamp01(t / panelFadeInDuration);
            yield return null;
        }

        cg.alpha = 1f;
    }

    // ── Stats Text Builder ────────────────────────────────────────────────────

    private string BuildStatsText(RaceContext.GameStateSnapshot s, bool isWin)
    {
        string result = isWin ? "MISSION COMPLETE\n" : "MISSION FAILED\n";
        string deathStr = isWin ? "—" : FormatDeathCause(s.DeathCause);
        result += $"Phase reached: {s.currentPhaseName} ({s.phaseNumber}/{s.totalPhases})\n";
        result += $"Attempts: {s.attemptCount}\n";
        result += $"Cause: {deathStr}\n";
        result += $"Fuel: {s.playerFuelNormalized * 100f:F0}%\n";
        result += $"Hull: {s.playerHealthNormalized * 100f:F0}%\n";
        result += $"Engine: {s.playerEngineHealthNormalized * 100f:F0}%\n";
        return result;
    }

    // ── Debrief Prompt Builder ────────────────────────────────────────────────

    private string BuildDebriefPrompt(RaceContext.GameStateSnapshot s, bool isWin)
    {
        if (isWin)
        {
            return $"race stats:\n" +
                   $"phase reached: {s.currentPhaseName} ({s.phaseNumber} of {s.totalPhases})\n" +
                   $"attempts: {s.attemptCount}\n" +
                   $"final fuel: {s.playerFuelNormalized * 100f:F0}%\n" +
                   $"final hull: {s.playerHealthNormalized * 100f:F0}%\n" +
                   $"final engine: {s.playerEngineHealthNormalized * 100f:F0}%\n";
        }

        string tauntContext = s.DeathCause switch
        {
            RaceContext.DeathCause.Missile  => $"Missile closed to kill range, made direct contact, and detonated on the rocket. Direct hit. Player reached {s.currentPhaseName} (Phase {s.phaseNumber} of {s.totalPhases}).",
            RaceContext.DeathCause.Hazard   => "Rocket destroyed by environmental hazard.",
            RaceContext.DeathCause.Fuel     => "Rocket ran out of fuel and went dead in space.",
            RaceContext.DeathCause.Overheat => "Engine overheated and tore the rocket apart.",
            _                              => "Rocket was destroyed.",
        };

        string prompt = $"What happened: {tauntContext}\n";
        prompt += $"cause of death: {FormatDeathCause(s.DeathCause)}\n" +
                  $"phase reached: {s.currentPhaseName} ({s.phaseNumber}/{s.totalPhases})\n" +
                  $"attempts: {s.attemptCount}\n" +
                  $"fuel remaining: {s.playerFuelNormalized * 100f:F0}%\n" +
                  $"hull remaining: {s.playerHealthNormalized * 100f:F0}%\n" +
                  $"engine remaining: {s.playerEngineHealthNormalized * 100f:F0}%\n";
        return prompt;
    }

    private string FormatDeathCause(RaceContext.DeathCause cause)
    {
        return cause switch
        {
            RaceContext.DeathCause.Missile   => "caught by missile",
            RaceContext.DeathCause.Fuel      => "fuel depleted",
            RaceContext.DeathCause.Hazard    => "hazard collision",
            RaceContext.DeathCause.Overheat  => "engine overheat",
            _                               => "unknown",
        };
    }

    // ── Button Callbacks (wire to Unity Button OnClick) ───────────────────────

    public void OnContinueClicked()
    {
        if (SoundEffectsManager.Instance != null)
        {
            SoundEffectsManager.Instance.PlaySFX(SFXType.UIClick);
        }

        if (debriefPanel != null)
        {
            debriefPanel.SetActive(false);
        }
        if (GameManager.Instance != null)
            GameManager.Instance.ReturnToMenu();
    }

    public void OnRestartClicked()
    {
        if (SoundEffectsManager.Instance != null)
        {
            SoundEffectsManager.Instance.PlaySFX(SFXType.UIClick);
        }

        if (LevelManager.Instance != null)
        {
            LevelManager.Instance.ReloadScene();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }
}
*/
