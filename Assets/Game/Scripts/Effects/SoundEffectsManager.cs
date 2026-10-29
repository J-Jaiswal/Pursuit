using UnityEngine;

public enum SFXType
{
    // Health & Combat Mechanics
    PlayerDamage,
    Explosion,

    // Resource & Pickup Mechanics
    FuelPickup,
    RepairPickup,

    // Alert & Warning Mechanics
    WarningSiren,

    // Run Lifecycle Mechanics
    LevelStart,
    LevelComplete
   
}

/// <summary>
/// Central manager for global sound effects, resource pickups, cockpit sirens, and level background music.
/// Hazardous world elements (like LaserGate) manage their own localized 3D audio directly.
/// </summary>
public class SoundEffectsManager : MonoBehaviour
{
    private static SoundEffectsManager instance;
    private static bool warnedMissingInstance = false;

    public static SoundEffectsManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<SoundEffectsManager>();
                if (instance == null)
                {
                    // Check if there is a configured prefab in Resources
                    SoundEffectsManager prefab = Resources.Load<SoundEffectsManager>("SoundEffectsManager");
                    if (prefab != null)
                    {
                        instance = Instantiate(prefab);
                        instance.name = "SoundEffectsManager";
                    }
                    else if (!warnedMissingInstance)
                    {
                        warnedMissingInstance = true;
                        Debug.LogWarning("[SoundEffectsManager] No SoundEffectsManager instance found in the active scene or Resources! Ensure a configured SoundEffectsManager is present in the scene.");
                    }
                }
            }
            return instance;
        }
    }

    //  1. Health & Combat Mechanics
    [Header("1. Health & Combat Mechanics")]
    [Tooltip("Played when the rocket collides with cave walls or takes hazard damage.")]
    [SerializeField] private AudioClip playerDamageClip;
    [Tooltip("Played when missile strikes the player or on fatal explosions.")]
    [SerializeField] private AudioClip explosionClip;

    //  2. Resource & Pickup Mechanics 
    [Header("2. Resource & Pickup Mechanics")]
    [Tooltip("Played when collecting a fuel tank.")]
    [SerializeField] private AudioClip fuelPickupClip;
    [Tooltip("Played when collecting a hull repair kit.")]
    [SerializeField] private AudioClip repairPickupClip;

    //  3. Alert & Cockpit Warning Mechanics
    [Header("3. Alert & Cockpit Warning Mechanics")]
    [Tooltip("Looping siren triggered during critical hull/engine health or low fuel.")]
    [SerializeField] private AudioClip warningSirenClip;
    [Range(0f, 1f)] [SerializeField] private float warningSirenVolume = 0.8f;

    //  4. Run Lifecycle Mechanics 
    [Header("4. Run Lifecycle Mechanics")]
    [Tooltip("Played when the rocket spawns at the start of a level or respawns at a checkpoint.")]
    [SerializeField] private AudioClip levelStartClip;
    [Tooltip("Played when the player successfully completes a level (crosses the finish line).")]
    [SerializeField] private AudioClip levelCompleteClip;
    

    //  5. Level Ambience & Background Music (BGM) 
    [Header("5. Level Ambience & Background Music (BGM)")]
    [Tooltip("Background music track looping continuously throughout the level.")]
    [SerializeField] private AudioClip bgmClip;
    [Range(0f, 1f)] [SerializeField] private float bgmVolume = 0.5f;
    [Tooltip("Automatically begin BGM playback when the scene starts.")]
    [SerializeField] private bool playBgmOnStart = true;

    //  General Audio Pool Settings 
    [Header("General Audio Pool Settings")]
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;
    [SerializeField] private int poolSize = 10;

    private AudioSource[] sourcePool;
    private int poolIndex = 0;

    private AudioSource bgmSource;
    private AudioSource warningSirenSource;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        InitializePool();
        InitializeDedicatedSources();
    }

    private void Start()
    {
        GameManager.OnStateChanged += HandleGameStateChanged;

        if (playBgmOnStart && bgmClip != null)
        {
            PlayBGM(bgmClip);
        }
    }

    private void OnDestroy()
    {
        GameManager.OnStateChanged -= HandleGameStateChanged;
    }

    private void InitializePool()
    {
        if (sourcePool != null) return;

        sourcePool = new AudioSource[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = new GameObject($"SFXSource_{i}");
            go.transform.SetParent(transform);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.minDistance = 20f;
            source.maxDistance = 100f;
            source.rolloffMode = AudioRolloffMode.Linear;
            sourcePool[i] = source;
        }
    }

    private void InitializeDedicatedSources()
    {
        if (bgmSource == null)
        {
            GameObject bgmGO = new GameObject("BGM_Source");
            bgmGO.transform.SetParent(transform);
            bgmSource = bgmGO.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.loop = true;
            bgmSource.spatialBlend = 0f; // 2D Stereo
            bgmSource.volume = bgmVolume;
        }

        if (warningSirenSource == null)
        {
            GameObject sirenGO = new GameObject("WarningSiren_Source");
            sirenGO.transform.SetParent(transform);
            warningSirenSource = sirenGO.AddComponent<AudioSource>();
            warningSirenSource.playOnAwake = false;
            warningSirenSource.loop = true;
            warningSirenSource.spatialBlend = 0f; // 2D Stereo
            warningSirenSource.volume = warningSirenVolume;
        }
    }

    private AudioSource GetAvailableSource()
    {
        if (sourcePool == null || sourcePool.Length == 0)
        {
            InitializePool();
        }

        for (int i = 0; i < poolSize; i++)
        {
            int idx = (poolIndex + i) % poolSize;
            if (sourcePool[idx] != null && !sourcePool[idx].isPlaying)
            {
                poolIndex = (idx + 1) % poolSize;
                return sourcePool[idx];
            }
        }

        // Fallback: overwrite the oldest active one in rotation
        AudioSource fallbackSource = sourcePool[poolIndex];
        if (fallbackSource != null)
        {
            fallbackSource.Stop();
        }
        poolIndex = (poolIndex + 1) % poolSize;
        return fallbackSource;
    }

    //  Public SFX Play API ------------------>
    /// Plays a sound effect globally in 2D stereo (crystal clear, un-attenuated).
 
    public void PlaySFX(SFXType type)
    {
        AudioClip clip = GetClip(type);
        if (clip == null)
        {
            Debug.LogWarning($"[SoundEffectsManager] No clip assigned in Inspector for SFXType: {type}");
            return;
        }

        AudioSource source = GetAvailableSource();
        if (source == null) return;

        source.transform.position = Vector3.zero;
        source.spatialBlend = 0f; // 2D Stereo
        source.clip = clip;
        source.volume = sfxVolume;
        source.loop = false;
        source.Play();
    }

    /// Plays a sound effect at a specific 3D location in space with linear distance falloff.  
    public void PlaySFX(SFXType type, Vector3 position)
    {
        AudioClip clip = GetClip(type);
        if (clip == null)
        {
            Debug.LogWarning($"[SoundEffectsManager] No clip assigned in Inspector for SFXType: {type}");
            return;
        }

        AudioSource source = GetAvailableSource();
        if (source == null) return;

        source.transform.position = position;
        source.spatialBlend = 1f; // 3D Spatialized
        source.minDistance = 20f;
        source.maxDistance = 100f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.clip = clip;
        source.volume = sfxVolume;
        source.loop = false;
        source.Play();
    }

    //  Warning Siren System ------------------>

    /// Starts the looping warning siren audio.

    public void StartWarningSiren()
    {
        if (warningSirenSource == null) InitializeDedicatedSources();
        if (warningSirenClip == null) return;

        if (!warningSirenSource.isPlaying || warningSirenSource.clip != warningSirenClip)
        {
            warningSirenSource.clip = warningSirenClip;
            warningSirenSource.volume = warningSirenVolume;
            warningSirenSource.loop = true;
            warningSirenSource.Play();
        }
    }

    /// Stops the looping warning siren audio.

    public void StopWarningSiren()
    {
        if (warningSirenSource != null && warningSirenSource.isPlaying)
        {
            warningSirenSource.Stop();
        }
    }

    public bool IsWarningSirenPlaying => warningSirenSource != null && warningSirenSource.isPlaying;

    public void SetWarningSirenVolume(float volume)
    {
        warningSirenVolume = Mathf.Clamp01(volume);
        if (warningSirenSource != null)
            warningSirenSource.volume = warningSirenVolume;
    }

    //  Background Music (BGM) System ---------------->

    /// Plays background music in a continuous loop.>
    public void PlayBGM(AudioClip clip = null)
    {
        if (bgmSource == null) InitializeDedicatedSources();

        AudioClip targetClip = clip != null ? clip : bgmClip;
        if (targetClip == null) return;

        bgmSource.clip = targetClip;
        bgmSource.volume = bgmVolume;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        if (bgmSource != null && bgmSource.isPlaying)
            bgmSource.Stop();
    }

    public void PauseBGM()
    {
        if (bgmSource != null && bgmSource.isPlaying)
            bgmSource.Pause();
    }

    public void ResumeBGM()
    {
        if (bgmSource != null && !bgmSource.isPlaying)
            bgmSource.UnPause();
    }

    public void SetBGMVolume(float volume)
    {
        bgmVolume = Mathf.Clamp01(volume);
        if (bgmSource != null)
            bgmSource.volume = bgmVolume;
    }

    //  Game State Response --------------------->

    private void HandleGameStateChanged(GameState state)
    {
        switch (state)
        {
            case GameState.Paused:
                PauseBGM();
                StopWarningSiren();
                break;

            case GameState.Playing:
                ResumeBGM();
                break;

            case GameState.Dead:
                StopBGM();
                StopWarningSiren();
                break;

            case GameState.LevelComplete:
                StopBGM();
                StopWarningSiren();
                PlaySFX(SFXType.LevelComplete);
                break;
        }
    }

    //  Clip Resolution ----------------------->

    public AudioClip GetClip(SFXType type)
    {
        return type switch
        {
            SFXType.PlayerDamage  => playerDamageClip,
            SFXType.Explosion     => explosionClip,
            SFXType.FuelPickup    => fuelPickupClip,
            SFXType.RepairPickup  => repairPickupClip,
            SFXType.WarningSiren  => warningSirenClip,
            SFXType.LevelStart    => levelStartClip,
            SFXType.LevelComplete => levelCompleteClip,
            _                     => null
        };
    }
}
