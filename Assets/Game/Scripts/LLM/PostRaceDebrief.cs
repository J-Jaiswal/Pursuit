using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
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
/// </summary>
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

        // ── Fade panel in ──────────────────────────────────────────────────
        yield return StartCoroutine(FadePanelIn());

        // ── Display stats ─────────────────────────────────────────────────
        if (statsText   != null) statsText.text   = BuildStatsText(snap, isWin);
        if (speakerLabel != null) speakerLabel.text = isWin ? "MISSION CONTROL" : "N O V A";
        if (debriefText  != null) debriefText.text  = "";

        // ── Request LLM debrief ───────────────────────────────────────────
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

        // Wait for LLM (up to 8 seconds — no latency pressure for post-run)
        float elapsed = 0f;
        while (!llmDone && elapsed < 8f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (string.IsNullOrEmpty(debriefLine))
            debriefLine = fallbacks[UnityEngine.Random.Range(0, fallbacks.Length)];

        // ── TTS + Typewriter simultaneously ───────────────────────────────
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

        // Start typewriter immediately (doesn't wait for TTS)
        Coroutine typewriterCoroutine = StartCoroutine(TypewriterEffect(debriefLine));

        // Wait for TTS (up to 10 seconds)
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

        // Wait for whichever finishes last: audio or typewriter
        float waitForAudio = debriefClip != null ? debriefClip.length : 0f;
        yield return new WaitForSeconds(Mathf.Max(waitForAudio, debriefLine.Length / typewriterSpeed));

        // ── Show buttons ──────────────────────────────────────────────────
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

        Image panelImage = debriefPanel.GetComponent<Image>();
        if (panelImage != null)
        {
            Color color = panelImage.color;
            float elapsed = 0f;
            while (elapsed < panelFadeInDuration)
            {
                color.a = Mathf.Lerp(0f, 1f, elapsed / panelFadeInDuration);
                panelImage.color = color;
                elapsed += Time.deltaTime;
                yield return null;
            }
            color.a = 1f;
            panelImage.color = color;
        }
        else
        {
            yield return new WaitForSeconds(panelFadeInDuration);
        }
    }

    // ── Text Builders ─────────────────────────────────────────────────────────

    private string BuildStatsText(RaceContext.GameStateSnapshot s, bool isWin)
    {
        string outcome  = isWin ? "LEVEL COMPLETE" : "DESTROYED";
        int    mm       = Mathf.FloorToInt(s.RaceElapsedTime / 60f);
        int    ss       = Mathf.FloorToInt(s.RaceElapsedTime % 60f);
        string deathStr = isWin ? "—" : FormatDeathCause(s.DeathCause);

        string lines = $"{outcome}\n\n";
        lines += $"Time:              {mm:D2}:{ss:D2}\n";
        lines += $"Crashes:           {s.CrashCount}\n";
        lines += $"Missile catches:   {s.MissileCatchCount}\n";
        lines += $"Closest distance:  {s.ClosestMissileDistance:F1} units\n";

        if (isWin)
        {
            lines += $"Hull remaining:    {s.HealthAtEnd * 100f:F0}%\n";
            lines += $"Fuel remaining:    {s.FuelAtEnd   * 100f:F0}%\n";
        }
        else
        {
            lines += $"Cause of death:    {deathStr}\n";
        }

        return lines;
    }

    private string BuildDebriefPrompt(RaceContext.GameStateSnapshot s, bool isWin)
    {
        int mm = Mathf.FloorToInt(s.RaceElapsedTime / 60f);
        int ss = Mathf.FloorToInt(s.RaceElapsedTime % 60f);

        string prompt = $"outcome: {(isWin ? "win" : "lose")}\n" +
                        $"level: {s.LevelNumber + 1}\n" +
                        $"time elapsed: {mm:D2}:{ss:D2}\n" +
                        $"crash count: {s.CrashCount}\n" +
                        $"missile catch count: {s.MissileCatchCount}\n" +
                        $"closest missile distance during run: {s.ClosestMissileDistance:F1} units\n";

        if (isWin)
        {
            prompt += $"fuel remaining: {s.FuelAtEnd   * 100f:F0}%\n" +
                      $"health remaining: {s.HealthAtEnd * 100f:F0}%\n";
        }
        else
        {
            prompt += $"cause of death: {FormatDeathCause(s.DeathCause)}\n" +
                      $"behavior active when player died: {s.MissileBehaviorAtDeath}\n" +
                      $"fuel at death: {s.FuelAtEnd   * 100f:F0}%\n" +
                      $"health at death: {s.HealthAtEnd * 100f:F0}%\n";
        }

        return prompt;
    }

    private string FormatDeathCause(RaceContext.DeathCause cause)
    {
        return cause switch
        {
            RaceContext.DeathCause.Missile => "caught by missile",
            RaceContext.DeathCause.Fuel    => "fuel depleted",
            RaceContext.DeathCause.Hazard  => "hazard collision",
            _                              => "unknown",
        };
    }

    // ── Button Callbacks (wire to Unity Button OnClick) ───────────────────────

    public void OnContinueClicked()
    {
        // Hide panel then let GameManager handle scene transition
        if (debriefPanel != null)
        {
            debriefPanel.SetActive(false);
        }
        // GameManager.waitForDebrief = true by default — we own the scene transition on win
        if (GameManager.Instance != null)
            GameManager.Instance.LoadNextScene();
    }

    public void OnRestartClicked()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }
}
