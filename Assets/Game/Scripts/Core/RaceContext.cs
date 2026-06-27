using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton state aggregator. Subscribes to existing script events (zero modifications to those scripts)
/// and synthesises the full game-state snapshot that drives all LLM commentary.
///
/// SETUP:
///   1. Place this on a dedicated "RaceContext" GameObject in each level scene.
///   2. Assign playerCollisionHandler, playerFuel, playerTransform, finishLineTransform in the Inspector.
///   3. Set wallLayerMask to the same mask used by MissileAI.
///   4. Optionally set estimatedLevelDuration for level-progress normalisation.
/// </summary>
public class RaceContext : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static RaceContext Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Player References")]
    [SerializeField] private CollisionHandler playerCollisionHandler;
    [SerializeField] private Fuel             playerFuel;
    [SerializeField] private Transform        playerTransform;
    [SerializeField] private EngineHeat       playerEngineHeat;

    [Header("Level References")]
    [SerializeField] private Transform finishLineTransform;
    [Tooltip("Used to normalise level progress. Set to an estimate of how long the level takes.")]
    [SerializeField] private float estimatedLevelDuration = 120f;

    [Header("Corridor Detection")]
    [SerializeField] private LayerMask wallLayerMask;
    [Tooltip("Left + right raycast distance. Both hit → corridor is 'tight'.")]
    [SerializeField] private float corridorTightThreshold = 8f;

    // ── Public Live State (read by MissileAI, MissileVoice, MissionControl) ───
    public float           PlayerFuelNormalized   { get; private set; } = 1f;
    public float           PlayerHealthNormalized { get; private set; } = 1f;
    public string          CorridorWidthAhead     { get; private set; } = "open";
    public float           PlayerSpeed            { get; private set; }
    public string          PlayerSpeedTrend       { get; private set; } = "steady";
    public float           MissileDistanceToPlayer { get; private set; } = 999f;
    public float           ClosestMissileDistance  { get; private set; } = 999f;
    public int             PlayerCrashCount        { get; private set; }
    public int             MissileCatchCount       { get; private set; }
    public float           RaceElapsedTime         { get; private set; }
    public DeathCause      LastDeathCause          { get; private set; } = DeathCause.Hazard;
    public float           PlayerEngineHeatNormalized { get; private set; } = 0f;
    public string          LastNovaLine            { get; set; } = "";
    public string          LastMCLine              { get; set; } = "";

    // ── Events ────────────────────────────────────────────────────────────────
    public static event Action<CommentTrigger, GameStateSnapshot> OnCommentTrigger;
    public static event Action<float>                             OnMissileDistanceUpdated;

    // ── Types ─────────────────────────────────────────────────────────────────

    public enum DeathCause { None, Missile, Hazard, Fuel, Overheat }

    public struct GameStateSnapshot
    {
        public float  PlayerHealthNormalized;
        public float  PlayerFuelNormalized;
        public float  PlayerEngineHeatNormalized;
        public float  MissileDistance;
        public string MissileBehaviorName;
        public float  LevelProgress;
        public int    CrashCount;
        public int    MissileCatchCount;
        public string CorridorWidthAhead;
        public string PlayerSpeedTrend;
        public string LastNovaLine;
        public string LastMCLine;
        public float  RaceElapsedTime;
        public float  ClosestMissileDistance;
        public DeathCause DeathCause;
        public float  FuelAtEnd;
        public float  HealthAtEnd;
        public string MissileBehaviorAtDeath; // behavior active at moment player died (lose debrief)
        public int    LevelNumber;            // scene build index — included in all LLM context
    }

    // ── Internal Tracking ─────────────────────────────────────────────────────
    private float       raceStartTime;
    private GameState   previousState = GameState.Playing;
    private bool        fuelLowTriggered;
    private bool        healthCriticalTriggered;
    private bool        nearFinishTriggered;
    private bool        engineOverheatingTriggered;
    private bool        engineOverheatedTriggered;
    private int         lastCrashCountForThirdCrashTrigger;
    private DeathCause  pendingDeathCause = DeathCause.Hazard;
    private float       missileCaughtTimestamp = -99f;

    // Player speed tracking (samples over time)
    private Vector3 playerPosSample1;
    private Vector3 playerPosSample2;
    private Vector3 playerPosSample3;
    private float   speedSampleTimer;

    // Pickup detection (positive delta approach — no script modifications needed)
    private float prevFuelNormalized   = 1f;
    private float prevHealthNormalized = 1f;
    private const float PickupDeltaThreshold = 0.04f; // 4% jump = a pickup collected

    // Final snapshot (set at race end, read by PostRaceDebrief)
    public GameStateSnapshot FinalSnapshot { get; private set; }

    // ── Unity Lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (wallLayerMask == 0)
            wallLayerMask = LayerMask.GetMask("Default");
    }

    private void Start()
    {
        raceStartTime = Time.time;

        // Subscribe to player events (zero modifications to those scripts)
        if (playerCollisionHandler != null)
        {
            playerCollisionHandler.OnHealthChanged   += HandleHealthChanged;
            playerCollisionHandler.OnCriticalDamage  += HandleCriticalDamage;
        }
        if (playerFuel != null)
        {
            playerFuel.OnFuelChanged += HandleFuelChanged;
            playerFuel.OnFuelEmpty   += HandleFuelEmpty;
        }
        if (playerEngineHeat == null && playerCollisionHandler != null)
        {
            playerEngineHeat = playerCollisionHandler.GetComponent<EngineHeat>();
        }

        if (playerEngineHeat != null)
        {
            playerEngineHeat.OnHeatChanged += HandleHeatChanged;
        }

        GameManager.OnStateChanged   += HandleStateChanged;
        GameManager.OnPlayerDied     += HandlePlayerDied;
        GameManager.OnLevelComplete  += HandleLevelComplete;
        MissileAI.OnBehaviorChanged  += HandleBehaviorChanged;
        MissileAI.OnMissileCaughtPlayer += HandleMissileCaught;

        // Begin periodic state sampling (distance, corridor, speed trend)
        InvokeRepeating(nameof(SamplePeriodicState), 0.5f, 0.5f);
    }

    private void OnDestroy()
    {
        if (playerCollisionHandler != null)
        {
            playerCollisionHandler.OnHealthChanged  -= HandleHealthChanged;
            playerCollisionHandler.OnCriticalDamage -= HandleCriticalDamage;
        }
        if (playerFuel != null)
        {
            playerFuel.OnFuelChanged -= HandleFuelChanged;
            playerFuel.OnFuelEmpty   -= HandleFuelEmpty;
        }
        if (playerEngineHeat != null)
        {
            playerEngineHeat.OnHeatChanged -= HandleHeatChanged;
        }

        GameManager.OnStateChanged   -= HandleStateChanged;
        GameManager.OnPlayerDied     -= HandlePlayerDied;
        GameManager.OnLevelComplete  -= HandleLevelComplete;
        MissileAI.OnBehaviorChanged  -= HandleBehaviorChanged;
        MissileAI.OnMissileCaughtPlayer -= HandleMissileCaught;
    }

    // ── Periodic Sampling ─────────────────────────────────────────────────────

    private void SamplePeriodicState()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        RaceElapsedTime = Time.time - raceStartTime;

        // Missile distance
        if (MissileAI.Instance != null && playerTransform != null)
        {
            MissileDistanceToPlayer = Vector3.Distance(MissileAI.Instance.transform.position, playerTransform.position);
            if (MissileDistanceToPlayer < ClosestMissileDistance)
                ClosestMissileDistance = MissileDistanceToPlayer;
            OnMissileDistanceUpdated?.Invoke(MissileDistanceToPlayer);
        }

        // Corridor width (raycasts left and right from player)
        if (playerTransform != null)
        {
            bool leftHit  = Physics.Raycast(playerTransform.position, -playerTransform.right,
                                             corridorTightThreshold, wallLayerMask);
            bool rightHit = Physics.Raycast(playerTransform.position,  playerTransform.right,
                                             corridorTightThreshold, wallLayerMask);
            CorridorWidthAhead = (leftHit && rightHit) ? "tight" : "open";
        }

        // Player speed (instantaneous from Rigidbody, or positional delta)
        if (playerTransform != null)
        {
            SamplePlayerSpeed();
        }

        // Finish proximity check (once)
        if (!nearFinishTriggered && finishLineTransform != null && playerTransform != null)
        {
            float distToFinish = Vector3.Distance(playerTransform.position, finishLineTransform.position);
            if (distToFinish < 20f)
            {
                nearFinishTriggered = true;
                FireTrigger(CommentTrigger.PlayerNearFinish);
            }
        }
    }

    private void SamplePlayerSpeed()
    {
        speedSampleTimer += 0.5f; // called every 0.5s

        // Shift samples
        playerPosSample1 = playerPosSample2;
        playerPosSample2 = playerPosSample3;
        playerPosSample3 = playerTransform.position;

        float dist1 = Vector3.Distance(playerPosSample1, playerPosSample2);
        float dist2 = Vector3.Distance(playerPosSample2, playerPosSample3);

        PlayerSpeed = dist2 / 0.5f; // units/sec

        if (speedSampleTimer < 1.0f) return; // need at least 2 samples

        if (dist2 < dist1 * 0.8f)
            PlayerSpeedTrend = "slowing";
        else if (dist2 > dist1 * 1.2f)
            PlayerSpeedTrend = "accelerating";
        else
            PlayerSpeedTrend = "steady";
    }

    // ── Event Handlers ────────────────────────────────────────────────────────

    private void HandleHealthChanged(float normalizedHealth)
    {
        // Pickup detection: significant positive delta = repair pickup collected
        if (normalizedHealth > prevHealthNormalized + PickupDeltaThreshold)
        {
            FireTrigger(CommentTrigger.PlayerCollectedRepair);
        }

        PlayerHealthNormalized = normalizedHealth;

        if (!healthCriticalTriggered && normalizedHealth < 0.30f)
        {
            healthCriticalTriggered = true;
            FireTrigger(CommentTrigger.PlayerHealthCritical);
        }
        else if (normalizedHealth >= 0.35f)
        {
            healthCriticalTriggered = false; // reset if repaired above threshold
        }

        prevHealthNormalized = normalizedHealth;
    }

    private void HandleCriticalDamage()
    {
        PlayerCrashCount++;

        if (PlayerCrashCount == 3)
        {
            FireTrigger(CommentTrigger.PlayerOnThirdCrash);
        }
    }

    private void HandleFuelChanged(float normalizedFuel)
    {
        // Pickup detection: significant positive delta = fuel pickup collected
        if (normalizedFuel > prevFuelNormalized + PickupDeltaThreshold)
        {
            FireTrigger(CommentTrigger.PlayerCollectedFuel);
        }

        PlayerFuelNormalized = normalizedFuel;

        if (!fuelLowTriggered && normalizedFuel < 0.20f)
        {
            fuelLowTriggered = true;
            FireTrigger(CommentTrigger.PlayerFuelLow);
        }
        else if (normalizedFuel >= 0.25f)
        {
            fuelLowTriggered = false; // reset when refuelled
        }

        prevFuelNormalized = normalizedFuel;
    }

    private void HandleFuelEmpty()
    {
        pendingDeathCause = DeathCause.Fuel;
    }

    private void HandleHeatChanged(float normalizedHeat)
    {
        PlayerEngineHeatNormalized = normalizedHeat;

        // Trigger warnings based on thresholds (warning at 80%)
        float warningThreshold = 0.8f;
        if (!engineOverheatingTriggered && normalizedHeat >= warningThreshold)
        {
            engineOverheatingTriggered = true;
            FireTrigger(CommentTrigger.PlayerEngineOverheating);
        }
        else if (normalizedHeat < warningThreshold - 0.05f)
        {
            engineOverheatingTriggered = false; // cool down hysteresis
        }

        // Trigger overheat damage state at 100%
        if (!engineOverheatedTriggered && normalizedHeat >= 1.0f)
        {
            engineOverheatedTriggered = true;
            pendingDeathCause = DeathCause.Overheat; // If they die now, it's due to overheating
            FireTrigger(CommentTrigger.PlayerEngineOverheated);
        }
        else if (normalizedHeat < 0.95f)
        {
            engineOverheatedTriggered = false; // cool down hysteresis
            if (pendingDeathCause == DeathCause.Overheat)
            {
                pendingDeathCause = DeathCause.Hazard; // reset to default
            }
        }
    }

    private void HandleBehaviorChanged(MissileBehavior behavior)
    {
        switch (behavior)
        {
            case MissileBehavior.Surge:
                FireTrigger(CommentTrigger.MissileSurgeActivated);
                break;
            case MissileBehavior.Retreat:
                FireTrigger(CommentTrigger.MissileRetreatActivated);
                break;
        }
    }

    private void HandleMissileCaught()
    {
        MissileCatchCount++;
        missileCaughtTimestamp = Time.time;
        pendingDeathCause      = DeathCause.Missile;
        FireTrigger(CommentTrigger.MissileCaughtPlayer);

        // Forward to GameManager for debrief
        if (GameManager.Instance != null)
            GameManager.Instance.MissileDidCatchOnFinalHit = true;

        // Clear the missile flag after a delay (only meaningful if that catch killed the player)
        StartCoroutine(ClearMissileFlagAfterDelay(2f));
    }

    private IEnumerator ClearMissileFlagAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        // If player hasn't died in this time, that catch was non-lethal
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
        {
            if (Time.time - missileCaughtTimestamp >= delay)
            {
                pendingDeathCause = DeathCause.Hazard; // reset to default
                if (GameManager.Instance != null)
                    GameManager.Instance.MissileDidCatchOnFinalHit = false;
            }
        }
    }

    private void HandlePlayerDied()
    {
        LastDeathCause = pendingDeathCause;
        // Capture missile behavior at the moment of death for debrief context
        string behaviorAtDeath = MissileAI.Instance != null
            ? MissileAI.Instance.CurrentBehavior.ToString().ToLower()
            : "unknown";
        FinalSnapshot  = BuildSnapshot(behaviorAtDeath);
        pendingDeathCause = DeathCause.Hazard;
    }

    private void HandleLevelComplete()
    {
        FinalSnapshot = BuildSnapshot();
    }

    private void HandleStateChanged(GameState newState)
    {
        if (newState == GameState.Playing && previousState == GameState.Dead
            && LevelManager.Instance != null && LevelManager.Instance.HasCheckpoint)
        {
            // Respawn
            ResetPerRunState();
            FireTrigger(CommentTrigger.PlayerRespawned);
        }
        else if (newState == GameState.Playing && previousState != GameState.Playing)
        {
            // Fresh start (new scene / first load)
            raceStartTime       = Time.time;
            ClosestMissileDistance = 999f;
        }

        previousState = newState;
    }

    private void ResetPerRunState()
    {
        // Reset threshold triggers so they fire again after respawn
        fuelLowTriggered        = false;
        healthCriticalTriggered = false;
        nearFinishTriggered     = false;
        engineOverheatingTriggered = false;
        engineOverheatedTriggered  = false;
        pendingDeathCause       = DeathCause.Hazard;

        if (GameManager.Instance != null)
            GameManager.Instance.MissileDidCatchOnFinalHit = false;
    }

    // ── Snapshot Builder ──────────────────────────────────────────────────────

    public GameStateSnapshot BuildSnapshot(string missileBehaviorAtDeath = null)
    {
        string behaviorName = MissileAI.Instance != null
            ? MissileAI.Instance.CurrentBehavior.ToString().ToLower()
            : "unknown";

        float levelProgress = estimatedLevelDuration > 0f
            ? Mathf.Clamp01(RaceElapsedTime / estimatedLevelDuration)
            : 0f;

        return new GameStateSnapshot
        {
            PlayerHealthNormalized = PlayerHealthNormalized,
            PlayerFuelNormalized   = PlayerFuelNormalized,
            PlayerEngineHeatNormalized = PlayerEngineHeatNormalized,
            MissileDistance        = MissileDistanceToPlayer,
            MissileBehaviorName    = behaviorName,
            LevelProgress          = levelProgress,
            CrashCount             = PlayerCrashCount,
            MissileCatchCount      = MissileCatchCount,
            CorridorWidthAhead     = CorridorWidthAhead,
            PlayerSpeedTrend       = PlayerSpeedTrend,
            LastNovaLine           = LastNovaLine,
            LastMCLine             = LastMCLine,
            RaceElapsedTime        = RaceElapsedTime,
            ClosestMissileDistance = ClosestMissileDistance,
            DeathCause             = LastDeathCause,
            FuelAtEnd              = PlayerFuelNormalized,
            HealthAtEnd            = PlayerHealthNormalized,
            MissileBehaviorAtDeath = missileBehaviorAtDeath ?? behaviorName,
            LevelNumber            = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex,
        };
    }

    // ── Trigger Helper ────────────────────────────────────────────────────────

    private void FireTrigger(CommentTrigger trigger)
    {
        OnCommentTrigger?.Invoke(trigger, BuildSnapshot());
    }
}

// ── Trigger Enum ─────────────────────────────────────────────────────────────

public enum CommentTrigger
{
    PlayerFuelLow,
    PlayerHealthCritical,
    MissileCaughtPlayer,
    MissileSurgeActivated,
    MissileRetreatActivated,
    PlayerCollectedFuel,
    PlayerCollectedRepair,
    PlayerNearFinish,
    PlayerRespawned,
    PlayerOnThirdCrash,
    PlayerEngineOverheating,
    PlayerEngineOverheated,
}
