using UnityEngine;

public class RocketEngineFX : MonoBehaviour
{
    [Header("Particle Systems")]
    public ParticleSystem mainFlame;
    public ParticleSystem engineSmoke;

    // ---- Audio: Main Engine ----
    [Header("Audio - Main Engine")]
    public AudioSource engineAudioSource;
    public AudioClip normalEngineLoop;

    [Header("Audio - Main Engine Dynamics")]
    [Tooltip("Pitch when thrust is at zero (idle rumble).")]
    public float minEnginePitch  = 0.75f;
    [Tooltip("Pitch when thrust is at full power.")]
    public float maxEnginePitch  = 1.35f;
    [Tooltip("Volume when thrust is at zero.")]
    public float minEngineVolume = 0.25f;
    [Tooltip("Volume when thrust is at full power.")]
    public float maxEngineVolume = 1.0f;
    [Tooltip("Lerp speed for smooth pitch/volume transitions (higher = snappier).")]
    public float audioDampSpeed  = 4f;

    // ---- Audio: Side Thrusters ----
    [Header("Audio - Side Thrusters")]
    [Tooltip("AudioSource for A/D thruster puffs. Set Spatial Blend=1, Loop=true, Play-on-Awake=false.")]
    public AudioSource sideThrusterAudioSource;
    [Tooltip("Looping thruster-puff clip assigned to the side thruster AudioSource.")]
    public AudioClip   sideThrusterClip;
    public float sideThrusterMinPitch = 0.9f;
    public float sideThrusterMaxPitch = 1.1f;
    public float sideThrusterVolume   = 0.5f;

    [Header("Thrust Charge Tuning")]
    public float maxHoldTime = 2f;
    public float minStartSpeed = 4f;
    public float maxStartSpeed = 14f;
    public float minLifetime = 0.25f;
    public float maxLifetime = 0.7f;
    public float minFlameEmissionRate = 15f;
    public float maxFlameEmissionRate = 60f;

    [Header("Health Color/Noise Tuning")]
    public Gradient healthyToDamagedColor;
    [Range(0f, 1f)] public float noiseStartThreshold = 0.6f;
    public float maxNoiseStrength = 1.5f;
    public float maxNoiseFrequency = 1.2f;

    [Header("Smoke Tuning")]
    public float maxSmokeEmissionRate = 30f;

    [Header("Thrust Force")]
    public float baseMaxThrust = 20f;

    // ---- Private state ----
    private ParticleSystem.MainModule     flameMain;
    private ParticleSystem.EmissionModule flameEmission;
    private ParticleSystem.NoiseModule    flameNoise;
    private ParticleSystem.EmissionModule smokeEmission;

    private float holdTimer      = 0f;
    private float health01       = 1f;

    private bool  isThrustHeld      = false;
    private float currentThrustNorm = 0f; // 0-1, drives audio pitch & volume

    void Start()
    {
        flameMain     = mainFlame.main;
        flameEmission = mainFlame.emission;
        flameNoise    = mainFlame.noise;
        smokeEmission = engineSmoke.emission;

        ApplyCharge();
        ApplyHealthVisuals();

        flameEmission.enabled = false;
        smokeEmission.enabled = false;

        mainFlame.Play();
    }

    void Update()
    {
        ApplyCharge();
        UpdateMainEngineAudio();
    }

    // ---------- PUBLIC API ----------

    public void OnThrustHeld(float deltaTime, float thrustNormalized01 = 1f)
    {
        holdTimer = Mathf.Min(holdTimer + deltaTime, maxHoldTime);
        currentThrustNorm = Mathf.Clamp01(thrustNormalized01);
        ApplyCharge();

        if (!isThrustHeld)
        {
            isThrustHeld = true;
            flameEmission.enabled = true;
            ApplyHealthVisuals(); // FIX: re-sync smoke now that thrust is active
            PlayEngineAudio();
        }
    }

    public void OnThrustReleased()
    {
        holdTimer = 0f;
        currentThrustNorm = 0f;
        ApplyCharge();
        if (isThrustHeld)
        {
            isThrustHeld = false;
            flameEmission.enabled = false;
            ApplyHealthVisuals(); // FIX: turn smoke off when thrust stops
            StopEngineAudio();
        }
    }

    public void SetHealth(float h)
    {
        health01 = Mathf.Clamp01(h);
        ApplyHealthVisuals();
    }

    public float GetAppliedThrust()
    {
        return GetCharge01() * baseMaxThrust * health01;
    }

    // ---------- PRIVATE HELPERS ----------

    float GetCharge01() => holdTimer / maxHoldTime;

   

void ApplyCharge()
{
    float t = GetCharge01();

    // Health now scales down the flame's physical presence, not just its color/noise.
    // At low health the flame is visibly smaller/weaker even at full charge.
    float healthScaledMaxSpeed     = Mathf.Lerp(minStartSpeed, maxStartSpeed, health01);
    float healthScaledMaxEmission  = Mathf.Lerp(minFlameEmissionRate, maxFlameEmissionRate, health01);

    flameMain.startSpeed    = Mathf.Lerp(minStartSpeed, healthScaledMaxSpeed, t);
    flameMain.startLifetime = Mathf.Lerp(minLifetime,   maxLifetime,   t);

    var rate = flameEmission.rateOverTime;
    rate.constant = Mathf.Lerp(minFlameEmissionRate, healthScaledMaxEmission, t);
    flameEmission.rateOverTime = rate;
}

       void ApplyHealthVisuals()
    {
        // Flame color
        if (healthyToDamagedColor != null)
        {
            float damaged01 = 1f - health01;
            flameMain.startColor = healthyToDamagedColor.Evaluate(damaged01);
        }

        // Noise on damage
        float noiseT = Mathf.InverseLerp(noiseStartThreshold, 0f, health01);
        flameNoise.strengthMultiplier = Mathf.Lerp(0f, maxNoiseStrength,  noiseT);
        flameNoise.frequency          = Mathf.Lerp(0f, maxNoiseFrequency, noiseT);
        flameNoise.enabled            = health01 < noiseStartThreshold;

        // Damage smoke
        smokeEmission.enabled = isThrustHeld && health01 < noiseStartThreshold;
        var smokeRate = smokeEmission.rateOverTime;
        smokeRate.constant = Mathf.Lerp(0f, maxSmokeEmissionRate, 1f - health01);
        smokeEmission.rateOverTime = smokeRate;

        // Re-sync flame size/emission immediately, since ApplyCharge now depends on health01
        ApplyCharge();
    }  
    // ---- Main engine audio ----

    void PlayEngineAudio()
    {
        if (engineAudioSource == null || normalEngineLoop == null) return;
        engineAudioSource.clip   = normalEngineLoop;
        engineAudioSource.loop   = true;
        engineAudioSource.pitch  = minEnginePitch;
        engineAudioSource.volume = minEngineVolume;
        engineAudioSource.Play();
    }

    void StopEngineAudio()
    {
        if (engineAudioSource == null) return;
        engineAudioSource.Stop();
        engineAudioSource.pitch  = minEnginePitch;
        engineAudioSource.volume = minEngineVolume;
    }

    void UpdateMainEngineAudio()
    {
        if (engineAudioSource == null || !engineAudioSource.isPlaying) return;

        float t = currentThrustNorm;

        float targetPitch  = Mathf.Lerp(minEnginePitch,  maxEnginePitch,  t);
        float targetVolume = Mathf.Lerp(minEngineVolume, maxEngineVolume, t);

        engineAudioSource.pitch  = Mathf.Lerp(engineAudioSource.pitch,  targetPitch,  Time.deltaTime * audioDampSpeed);
        engineAudioSource.volume = Mathf.Lerp(engineAudioSource.volume, targetVolume, Time.deltaTime * audioDampSpeed);
    }

    // ---- Side thruster audio API ----

    public void OnSideThrusterActive(float normalizedMagnitude = 1f)
    {
        if (sideThrusterAudioSource == null || sideThrusterClip == null) return;

        if (!sideThrusterAudioSource.isPlaying)
        {
            sideThrusterAudioSource.clip   = sideThrusterClip;
            sideThrusterAudioSource.loop   = true;
            sideThrusterAudioSource.volume = sideThrusterVolume;
            sideThrusterAudioSource.pitch  = Mathf.Lerp(sideThrusterMinPitch, sideThrusterMaxPitch, normalizedMagnitude);
            sideThrusterAudioSource.Play();
        }
        else
        {
            float targetPitch = Mathf.Lerp(sideThrusterMinPitch, sideThrusterMaxPitch, normalizedMagnitude);
            sideThrusterAudioSource.pitch = Mathf.Lerp(
                sideThrusterAudioSource.pitch,
                targetPitch,
                Time.deltaTime * audioDampSpeed);
        }
    }

    public void OnSideThrusterReleased()
    {
        if (sideThrusterAudioSource != null && sideThrusterAudioSource.isPlaying)
            sideThrusterAudioSource.Stop();
    }
}