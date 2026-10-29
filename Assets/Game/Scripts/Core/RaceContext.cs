using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton state aggregator, Subscribes to existing script events.
/// and synthesises the full game-state snapshot that drives all LLM commentary.
/// </summary>
public class RaceContext : MonoBehaviour
{
   
    public static RaceContext Instance { get; private set; }

   
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

    [Header("Live Debugging")]
    [Tooltip("Optional CommentaryDebugSO to monitor live parameters and trigger logs in the Inspector.")]
    [SerializeField] private CommentaryDebugSO debugSO;

    // Public Live State (read by MissileAI, MissileVoice, MissionControl) 
    public float           PlayerFuelNormalized        { get; private set; } = 1f;
    public float           PlayerHealthNormalized      { get; private set; } = 1f;   // Rocket HP
    public float           PlayerEngineHealthNormalized { get; private set; } = 1f;  // Engine HP
    public string          CorridorWidthAhead          { get; private set; } = "open";
    public float           PlayerSpeed                 { get; private set; }
    public string          PlayerSpeedTrend            { get; private set; } = "steady";
    public float           MissileDistanceToPlayer     { get; private set; } = 999f;
    public float           ClosestMissileDistance      { get; private set; } = 999f;
    public int             PlayerCrashCount            { get; private set; }
    public int             MissileCatchCount           { get; private set; }
    public float           RaceElapsedTime             { get; private set; }
    public DeathCause      LastDeathCause              { get; private set; } = DeathCause.Hazard;
    public float           PlayerEngineHeatNormalized  { get; private set; } = 0f;
    public string          LastNovaLine                { get; set; } = "";
    public string          LastMCLine                  { get; set; } = "";

    // ── Pickup Tracking ───────────────────────────────────────────────────────
    /// <summary>Straight-line distance to the nearest uncollected fuel pickup. 999f if none present.</summary>
    public float NearestFuelPickupDistance   { get; private set; } = 999f;
    /// <summary>Straight-line distance to the nearest uncollected repair pickup. 999f if none present.</summary>
    public float NearestRepairPickupDistance { get; private set; } = 999f;
    /// <summary>Seconds elapsed since the player last collected a fuel pickup.</summary>
    public float TimeSinceLastFuelPickup     { get; private set; } = 0f;
    /// <summary>Distance (units) the player has travelled since the last fuel pickup.</summary>
    public float DistanceSinceLastFuelPickup { get; private set; } = 0f;

    // ── New Telemetry & Zone Tracking ─────────────────────────────────────────
    public string          CurrentZoneName             { get; private set; } = "Start";
    public string          MissileCurrentZoneName      { get; private set; } = "Start";
    public bool            MissileHasLineOfSight       { get; private set; } = false;
    public float           SpatialProgressPercent      { get; private set; } = 0f;
    public int             PhaseAttemptCount           { get; private set; } = 1;
    public string          ZoneSituationContext        { get; private set; } = "";
    public CommentaryZoneTrigger.ZoneSpeakerTarget ZoneSpeakerTarget { get; private set; } = CommentaryZoneTrigger.ZoneSpeakerTarget.Both;
    public CommentaryQueue.CommentPriority ZoneCommentPriority { get; private set; } = CommentaryQueue.CommentPriority.Low;

    public DeathCause PendingDeathCause => pendingDeathCause;

    // ── Events ────────────────────────────────────────────────────────────────
    public static event Action<CommentTrigger, GameStateSnapshot> OnCommentTrigger;
    public static event Action<float>                             OnMissileDistanceUpdated;

    // ── Types ─────────────────────────────────────────────────────────────────

    public enum DeathCause { None, Missile, Hazard, Fuel, Overheat, EngineFailure, WallCrash, LaserGate, HydraulicCrusher, RotatingHazard, Asteroid }

    public struct GameStateSnapshot
    {
        public float  PlayerHealthNormalized;        // Rocket / hull HP
        public float  PlayerEngineHealthNormalized;  // Engine HP (NEW)
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

        public string currentPhaseName;
        public int    phaseNumber;
        public int    totalPhases;

        // Extended Zone & Telemetry
        public string CurrentZoneName;
        public string MissileCurrentZoneName;
        public bool   MissileHasLineOfSight;
        public float  SpatialProgressPercent;
        public int    attemptCount;
        public string ZoneSituationContext;
        public CommentaryZoneTrigger.ZoneSpeakerTarget ZoneSpeakerTarget;
        public CommentaryQueue.CommentPriority ZoneCommentPriority;

        // Pickup Tracking (for MC guidance prompts)
        public float  NearestFuelPickupDistance;
        public float  NearestRepairPickupDistance;
        public float  TimeSinceLastFuelPickup;
        public float  DistanceSinceLastFuelPickup;
    }

    // ── Internal Tracking ─────────────────────────────────────────────────────
    private float       raceStartTime;
    private GameState   previousState = GameState.Playing;
    private bool        fuelLowTriggered;
    private bool        healthCriticalTriggered;
    private bool        engineHealthCriticalTriggered;
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
    private float prevFuelNormalized         = 1f;
    private float prevHealthNormalized       = 1f;
    private float prevEngineHealthNormalized = 1f;
    private const float PickupDeltaThreshold = 0.04f; // 4% jump = a pickup collected

    // Pickup distance tracking
    private float          lastFuelPickupTime     = -999f;
    private Vector3        lastFuelPickupPosition;
    private readonly System.Collections.Generic.List<Transform> activeFuelPickups   = new();
    private readonly System.Collections.Generic.List<Transform> activeRepairPickups = new();

    // New Tier 2 MC trigger gates
    private int  corridorTightDwellCount;   // consecutive 0.5s samples with tight corridor
    private bool corridorAdviceTriggered;   // gates corridor-tight trigger per approach
    private bool fuelDroughtTriggered;      // gates overdue-for-fuel trigger per drought

    // Final snapshot (set at race end, read by PostRaceDebrief) 
    public GameStateSnapshot FinalSnapshot { get; private set; }

    // Unity Lifecycle --->

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

        // Subscribe to player events 
        if (playerCollisionHandler != null)
        {
            playerCollisionHandler.OnHealthChanged        += HandleHealthChanged;
            playerCollisionHandler.OnEngineHealthChanged  += HandleEngineHealthChanged;
            playerCollisionHandler.OnCriticalDamage       += HandleCriticalDamage;
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
        GameManager.OnLevelComplete  += HandleLevelComplete;
        GameManager.OnPlayerDied     += HandlePlayerDied;
        MissileAI.OnBehaviorChanged  += HandleBehaviorChanged;
        MissileAI.OnMissileCaughtPlayer += HandleMissileCaught;
        MissileAI.OnMissileActivated += HandleMissileActivated;

        // Begin periodic state sampling (distance, corridor, speed trend)
        InvokeRepeating(nameof(SamplePeriodicState), 0.5f, 0.5f);

        // Initialise lastFuelPickupTime so TimeSinceLastFuelPickup starts counting from 0
        lastFuelPickupTime     = Time.time;
        lastFuelPickupPosition = playerTransform != null ? playerTransform.position : Vector3.zero;
    }

    private void OnDestroy()
    {
        if (playerCollisionHandler != null)
        {
            playerCollisionHandler.OnHealthChanged       -= HandleHealthChanged;
            playerCollisionHandler.OnEngineHealthChanged -= HandleEngineHealthChanged;
            playerCollisionHandler.OnCriticalDamage      -= HandleCriticalDamage;
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
        GameManager.OnLevelComplete  -= HandleLevelComplete;
        GameManager.OnPlayerDied     -= HandlePlayerDied;
        MissileAI.OnBehaviorChanged  -= HandleBehaviorChanged;
        MissileAI.OnMissileCaughtPlayer -= HandleMissileCaught;
        MissileAI.OnMissileActivated -= HandleMissileActivated;
    }

    // ── Pickup Registration (called by pickup scripts in their Awake) ────────────────

    /// Fuel pickup scripts call this in Awake to register for nearest-distance tracking.
    public void RegisterFuelPickup(Transform t)   { if (t != null) activeFuelPickups.Add(t); }
    /// Repair pickup scripts call this in Awake to register for nearest-distance tracking.
    public void RegisterRepairPickup(Transform t) { if (t != null) activeRepairPickups.Add(t); }
    /// <summary>Fuel pickup scripts call this OnDestroy/OnDisable when collected to deregister.
    public void UnregisterFuelPickup(Transform t)   => activeFuelPickups.Remove(t);
    /// Repair pickup scripts call this OnDestroy/OnDisable when collected to deregister.
    public void UnregisterRepairPickup(Transform t) => activeRepairPickups.Remove(t);

    // ── Periodic Sampling ─────────────────────────────────────────────────────

    private void SamplePeriodicState()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        RaceElapsedTime = Time.time - raceStartTime;

        // Missile distance & Line of Sight (only when missile is actively in pursuit)
        if (MissileAI.Instance != null && MissileAI.Instance.IsActive && playerTransform != null)
        {
            Vector3 missilePos = MissileAI.Instance.transform.position;
            Vector3 playerPos  = playerTransform.position;

            MissileDistanceToPlayer = Vector3.Distance(missilePos, playerPos);
            if (MissileDistanceToPlayer < ClosestMissileDistance)
                ClosestMissileDistance = MissileDistanceToPlayer;
            OnMissileDistanceUpdated?.Invoke(MissileDistanceToPlayer);

            // Line of Sight (LOS) raycast against wallLayerMask
            MissileHasLineOfSight = !Physics.Linecast(missilePos, playerPos, wallLayerMask);
        }
        else
        {
            MissileDistanceToPlayer = 999f;
            MissileHasLineOfSight   = false;
        }

        // Spatial / Phase Progress
        if (LevelManager.Instance != null && LevelManager.Instance.TotalPhases > 0)
        {
            SpatialProgressPercent = Mathf.Clamp01((float)LevelManager.Instance.CurrentPhaseIndex / LevelManager.Instance.TotalPhases);
            PhaseAttemptCount = LevelManager.Instance.AttemptCount;
        }
        else if (estimatedLevelDuration > 0f)
        {
            SpatialProgressPercent = Mathf.Clamp01(RaceElapsedTime / estimatedLevelDuration);
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

        // Pickup distances --->
        NearestFuelPickupDistance   = ComputeNearestPickup(activeFuelPickups);
        NearestRepairPickupDistance = ComputeNearestPickup(activeRepairPickups);

        if (playerTransform != null)
        {
            TimeSinceLastFuelPickup     = Time.time - lastFuelPickupTime;
            DistanceSinceLastFuelPickup = Vector3.Distance(playerTransform.position, lastFuelPickupPosition);
        }

        // Corridor-tight-ahead MC Tier 2 trigger ----->
        // Condition: tight corridor + hot engine + missile close, held for ≥ 1s (2 sample ticks).
        if (CorridorWidthAhead == "tight" &&
            PlayerEngineHeatNormalized > 0.45f &&
            MissileDistanceToPlayer < 25f)
        {
            corridorTightDwellCount++;
            if (!corridorAdviceTriggered && corridorTightDwellCount >= 2)
            {
                corridorAdviceTriggered = true;
                FireTrigger(CommentTrigger.CorridorTightAhead);
            }
        }
        else
        {
            corridorTightDwellCount = 0;
            if (CorridorWidthAhead == "open") corridorAdviceTriggered = false; // reset for next tight section
        }

        // Fuel-drought proactive MC Tier 2 trigger --------->
        // Fires once when player hasn't collected fuel in 45s and is running low.
        if (!fuelDroughtTriggered &&
            TimeSinceLastFuelPickup > 45f &&
            PlayerFuelNormalized < 0.35f &&
            NearestFuelPickupDistance < 999f) // only if a pickup exists to guide toward
        {
            fuelDroughtTriggered = true;
            FireTrigger(CommentTrigger.FuelDrought);
        }
        else if (PlayerFuelNormalized >= 0.40f)
        {
            fuelDroughtTriggered = false; // reset when refuelled
        }

        // Live Debug SO Update
        if (debugSO != null)
        {
            float cd = CommentaryQueue.Instance != null ? CommentaryQueue.Instance.CooldownRemaining : 0f;
            bool isProc = CommentaryQueue.Instance != null && CommentaryQueue.Instance.IsProcessing;
            int qCount = CommentaryQueue.Instance != null ? CommentaryQueue.Instance.PendingCount : 0;
            debugSO.UpdateSnapshot(BuildSnapshot(), MissileHasLineOfSight, CurrentZoneName, MissileCurrentZoneName, cd, isProc, qCount);
        }
    }

    private float ComputeNearestPickup(System.Collections.Generic.List<Transform> pickups)
    {
        if (pickups.Count == 0 || playerTransform == null) return 999f;
        float min = float.MaxValue;
        foreach (var t in pickups)
            if (t != null) min = Mathf.Min(min, Vector3.Distance(playerTransform.position, t.position));
        return min == float.MaxValue ? 999f : min;
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

    private void HandleHealthChanged(float normalizedRocketHealth)
    {
        // Pickup detection: significant positive delta on either pool = repair pickup collected
        if (normalizedRocketHealth > prevHealthNormalized + PickupDeltaThreshold)
        {
            FireTrigger(CommentTrigger.PlayerCollectedRepair);
        }

        PlayerHealthNormalized = normalizedRocketHealth;

        if (!healthCriticalTriggered && normalizedRocketHealth < 0.30f)
        {
            healthCriticalTriggered = true;
            FireTrigger(CommentTrigger.PlayerHealthCritical);
        }
        else if (normalizedRocketHealth >= 0.35f)
        {
            healthCriticalTriggered = false; // reset if repaired above threshold
        }

        prevHealthNormalized = normalizedRocketHealth;
    }

    private void HandleEngineHealthChanged(float normalizedEngineHealth)
    {
        PlayerEngineHealthNormalized = normalizedEngineHealth;

        if (!engineHealthCriticalTriggered && normalizedEngineHealth <= 0.20f)
        {
            engineHealthCriticalTriggered = true;
            // Reuse PlayerHealthCritical trigger — LLM will know context from snapshot
            FireTrigger(CommentTrigger.PlayerEngineHealthCritical);
        }
        else if (normalizedEngineHealth > 0.25f)
        {
            engineHealthCriticalTriggered = false; // reset if repaired above threshold
        }

        prevEngineHealthNormalized = normalizedEngineHealth;
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
            // Record pickup position/time for drought tracking
            lastFuelPickupTime     = Time.time;
            lastFuelPickupPosition = playerTransform != null ? playerTransform.position : Vector3.zero;
            fuelDroughtTriggered   = false; // reset drought gate on collection
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
        SetDeathCause(DeathCause.Fuel);
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
            SetDeathCause(DeathCause.Overheat); // If they die now, it's due to overheating
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
        SetDeathCause(DeathCause.Missile);
        FireTrigger(CommentTrigger.MissileCaughtPlayer);

        // Clear the missile flag after a delay (only meaningful if that catch was non-lethal)
        StartCoroutine(ClearMissileFlagAfterDelay(2f));
    }

    private IEnumerator ClearMissileFlagAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // RISK-09 FIX: The original check (Time.time - timestamp >= delay) is always true
        // here because we just waited that long. The meaningful guard is whether the player
        // is still alive — if they are, the missile catch was non-lethal and we reset flags.
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
        {
            pendingDeathCause = DeathCause.Hazard; // reset to default
        }
    }


    public void SetDeathCause(DeathCause cause)
    {
        // Prioritize specific causes of death over generic Hazard
        if (cause == DeathCause.Hazard && pendingDeathCause != DeathCause.None && pendingDeathCause != DeathCause.Hazard)
        {
            return;
        }
        pendingDeathCause = cause;
    }

    public void CaptureFinalSnapshot()
    {
        LastDeathCause = pendingDeathCause;
        // Capture missile behavior at the moment of death for debrief context
        string behaviorAtDeath = MissileAI.Instance != null
            ? MissileAI.Instance.CurrentBehavior.ToString().ToLower()
            : "unknown";
        FinalSnapshot  = BuildSnapshot(behaviorAtDeath);
        pendingDeathCause = DeathCause.Hazard;
    }

    private void HandleMissileActivated()
    {
        FireTrigger(CommentTrigger.MissileSpawnActivated);
    }

    private void HandlePlayerDied()
    {
        CaptureFinalSnapshot();
        FireTrigger(CommentTrigger.PlayerDied);
    }

    private void HandleLevelComplete()
    {
        FinalSnapshot = BuildSnapshot();
        FireTrigger(CommentTrigger.LevelCompleted);
    }

    private void HandleStateChanged(GameState newState)
    {
        if (newState == GameState.Playing && previousState != GameState.Playing)
        {
            // Fresh start (new scene / first load)
            raceStartTime       = Time.time;
            ClosestMissileDistance = 999f;
        }

        previousState = newState;
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

        string cPhaseName = "";
        int pNum = 0;
        int tPhases = 0;
        if (LevelManager.Instance != null)
        {
            cPhaseName = LevelManager.Instance.GetCurrentPhaseName();
            pNum = LevelManager.Instance.CurrentPhaseIndex + 1;
            tPhases = LevelManager.Instance.TotalPhases;
        }

        return new GameStateSnapshot
        {
            PlayerHealthNormalized       = PlayerHealthNormalized,
            PlayerEngineHealthNormalized = PlayerEngineHealthNormalized,
            PlayerFuelNormalized         = PlayerFuelNormalized,
            PlayerEngineHeatNormalized   = PlayerEngineHeatNormalized,
            MissileDistance              = MissileDistanceToPlayer,
            MissileBehaviorName          = behaviorName,
            LevelProgress                = levelProgress,
            CrashCount                   = PlayerCrashCount,
            MissileCatchCount            = MissileCatchCount,
            CorridorWidthAhead           = CorridorWidthAhead,
            PlayerSpeedTrend             = PlayerSpeedTrend,
            LastNovaLine                 = LastNovaLine,
            LastMCLine                   = LastMCLine,
            RaceElapsedTime              = RaceElapsedTime,
            ClosestMissileDistance       = ClosestMissileDistance,
            DeathCause                   = LastDeathCause,
            FuelAtEnd                    = PlayerFuelNormalized,
            HealthAtEnd                  = PlayerHealthNormalized,
            MissileBehaviorAtDeath       = missileBehaviorAtDeath ?? behaviorName,
            LevelNumber                  = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex,
            currentPhaseName             = cPhaseName,
            phaseNumber                  = pNum,
            totalPhases                  = tPhases,

            CurrentZoneName              = CurrentZoneName,
            MissileCurrentZoneName       = MissileCurrentZoneName,
            MissileHasLineOfSight        = MissileHasLineOfSight,
            SpatialProgressPercent       = SpatialProgressPercent,
            attemptCount                 = PhaseAttemptCount,
            ZoneSituationContext         = ZoneSituationContext,
            ZoneSpeakerTarget            = ZoneSpeakerTarget,
            ZoneCommentPriority          = ZoneCommentPriority,

            NearestFuelPickupDistance    = NearestFuelPickupDistance,
            NearestRepairPickupDistance  = NearestRepairPickupDistance,
            TimeSinceLastFuelPickup      = TimeSinceLastFuelPickup,
            DistanceSinceLastFuelPickup  = DistanceSinceLastFuelPickup,
        };
    }

    public void NotifyPlayerEnteredZone(string zone, string situation, bool fireCommentary,
                                        CommentaryQueue.CommentPriority priority,
                                        CommentaryZoneTrigger.ZoneSpeakerTarget targetSpeaker)
    {
        CurrentZoneName = zone;
        ZoneSituationContext = situation;
        ZoneCommentPriority = priority;
        ZoneSpeakerTarget = targetSpeaker;

        if (fireCommentary)
        {
            FireTrigger(CommentTrigger.PlayerEnteredZone);
        }
    }

    public void NotifyMissileEnteredZone(string zone)
    {
        MissileCurrentZoneName = zone;
    }

    public void FirePhaseAdvanced(string phaseName, int phaseNumber, int totalPhases)
    {
        GameStateSnapshot snapshot = BuildSnapshot();
        snapshot.currentPhaseName = phaseName;
        snapshot.phaseNumber = phaseNumber;
        snapshot.totalPhases = totalPhases;
        OnCommentTrigger?.Invoke(CommentTrigger.PlayerAdvancedPhase, snapshot);
    }

    // ── Trigger Helper ────────────────────────────────────────────────────────

    private void FireTrigger(CommentTrigger trigger)
    {
        GameStateSnapshot snapshot = BuildSnapshot();
        if (debugSO != null)
        {
            debugSO.LogTriggerEvent(trigger.ToString(), "Normal", $"Fired in zone '{CurrentZoneName}'");
        }
        OnCommentTrigger?.Invoke(trigger, snapshot);
    }
}

// ── Trigger Enum ─────────────────────────────────────────────────────────────

public enum CommentTrigger
{
    PlayerFuelLow,
    PlayerHealthCritical,
    PlayerEngineHealthCritical,   // Engine HP ≤ 20%
    MissileCaughtPlayer,
    MissileSurgeActivated,
    MissileRetreatActivated,
    PlayerCollectedFuel,
    PlayerCollectedRepair,
    PlayerNearFinish,
    PlayerOnThirdCrash,
    PlayerEngineOverheating,
    PlayerEngineOverheated,
    PlayerAdvancedPhase,
    PlayerEnteredZone,
    MissileSpawnActivated,
    PlayerDied,
    LevelCompleted,
    CorridorTightAhead,           // MC Tier 2: tight corridor + hot engine + missile close
    FuelDrought,                  // MC Tier 2: proactive fuel warning after 45s without pickup
}
