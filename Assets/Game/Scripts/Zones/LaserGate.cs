using UnityEngine;

/// <summary>
/// Self-contained single laser gate hazard for cave-tunnel zones.
///
/// WHAT IT DOES:
///   Two modes, selectable from the Inspector:
///
///   PULSING  — The laser blinks on/off on a configurable timer.
///              Renderer + Collider are toggled. Emissive glow fades in/out using
///              a cached base colour (so the fade never drifts to black).
///              phaseOffset (0–1) staggers this gate's starting position in its
///              pulse cycle — set manually in the Inspector.
///
///   SWEEPING — The laser bar moves back-and-forth using a MovementPattern:
///              • Horizontal  — sweeps left/right across the tunnel (dodge up or down)
///              • Vertical    — sweeps up/down (dodge left or right)
///              • Rotating    — rotates continuously on local Z while sweeping,
///                             forcing the player to read both position AND angle.
///
///   Both modes deal smooth per-second damage while the player is inside the laser.
///   Damage is Sharp-type: mostly hull damage, small engine share.
///
/// AUDIO SYSTEM (Self-Contained 3D Spatial):
///   - Master Volume, individual volume sliders for Hum, Open, Close, and Beam Burn.
///   - Dedicated AudioSource for continuous laser burn loop strictly played while inside.
///   - Full 3D spatial attenuation (spatialBlend = 1.0) with min/max distance roll-off.
///
/// EXTERNAL CONTROL (used by LaserGateGroup):
///   - SetPhaseOffset(float)   — set starting phase before Start() computes its cycle timing.
///   - SetExternalControl(bool) — suppresses the internal Pulsing timer so a group can drive
///                                 on/off state directly via ForceOn/ForceOff.
///   - ForceOn(bool) / ForceOff(bool) — immediately set laser state, reusing ApplyLaserState
///                                       so renderer/collider/glow/audio all stay in sync.
/// </summary>
public class LaserGate : MonoBehaviour
{
    // ── Mode & Pattern ────────────────────────────────────────────────────────

    public enum LaserMode { Pulsing, Sweeping }
    public enum MovementPattern { Horizontal, Vertical, Rotating }

    [Header("Mode")]
    [Tooltip("Pulsing: laser blinks on/off.\nSweeping: laser physically moves through the cave.")]
    [SerializeField] private LaserMode mode = LaserMode.Pulsing;

    // Pulsing Settings

    [Header("Pulsing Mode")]
    [Tooltip("How long (seconds) the laser stays ON.")]
    [SerializeField] private float onDuration = 1.2f;

    [Tooltip("How long (seconds) the laser stays OFF.")]
    [SerializeField] private float offDuration = 0.8f;

    [Tooltip("Cycle offset (0–1). 0 = start at the beginning of the ON phase.")]
    [Range(0f, 1f)]
    [SerializeField] private float phaseOffset = 0f;

    [Tooltip("How quickly the emissive glow fades in/out during pulse transitions.")]
    [SerializeField] private float glowFadeSpeed = 6f;

    // Sweeping Settings

    [Header("Sweeping Mode")]
    [Tooltip("Horizontal: slides left/right.\nVertical: slides up/down.\nRotating: spins on local Z while sweeping horizontally.")]
    [SerializeField] private MovementPattern movementPattern = MovementPattern.Horizontal;

    [Tooltip("How far (units) the laser travels from its origin in each sweep direction.")]
    [Range(0.5f, 20f)]
    [SerializeField] private float sweepDistance = 4f;

    [Tooltip("Speed of the back-and-forth sweep cycle.")]
    [Range(0.1f, 10f)]
    [SerializeField] private float sweepSpeed = 1.5f;

    [Tooltip("Eased: smoothly decelerates/accelerates at each end of the sweep.\n" +
             "Linear: constant speed, sharp direction change at each end.")]
    [SerializeField] private bool easeSweepMotion = false;

    [Tooltip("For Rotating pattern: degrees per second the laser bar spins on its local Z axis.")]
    [SerializeField] private float rotationSpeed = 90f;

    // References

    [Header("References")]
    [Tooltip("The Renderer of the laser beam mesh.")]
    [SerializeField] private Renderer laserRenderer;

    [Tooltip("The Collider on the laser beam (must be Is Trigger = true).")]
    [SerializeField] private Collider laserCollider;

    // Audio Settings

    [Header("Laser Audio Clips & Volumes")]
    [Tooltip("Master volume multiplier for this laser gate.")]
    [Range(0f, 1f)][SerializeField] private float masterLaserVolume = 1f;

    [Tooltip("Continuous laser hum loop played while the beam is powered.")]
    [SerializeField] private AudioClip laserHumClip;
    [Range(0f, 1f)][SerializeField] private float humVolume = 0.6f;
    [Range(0.5f, 2f)][SerializeField] private float humPitch = 1.0f;

    [Tooltip("Sound played when the laser beam ignites / turns on.")]
    [SerializeField] private AudioClip laserOpenClip;
    [Range(0f, 1f)][SerializeField] private float openVolume = 0.8f;

    [Tooltip("Sound played when the laser beam deactivates / turns off.")]
    [SerializeField] private AudioClip laserCloseClip;
    [Range(0f, 1f)][SerializeField] private float closeVolume = 0.8f;

    [Tooltip("Continuous sizzle/burn sound played strictly while the player stays inside the beam.")]
    [SerializeField] private AudioClip laserBurnClip;
    [Range(0f, 1f)][SerializeField] private float burnVolume = 0.85f;

    [Header("Laser 3D Attenuation & Falloff")]
    [Tooltip("Spatial Blend: 0 = 2D Stereo, 1 = Full 3D Spatial Audio (default: 1 for realistic 3D).")]
    [Range(0f, 1f)][SerializeField] private float spatialBlend = 1.0f;

    [Tooltip("Distance at which the sound plays at 100% volume before starting to attenuate.")]
    [Range(1f, 50f)][SerializeField] private float minDistance = 15f;

    [Tooltip("Distance beyond which the laser sound is inaudible.")]
    [Range(10f, 200f)][SerializeField] private float maxDistance = 70f;

    [Tooltip("Audio falloff roll-off mode (Linear produces clear hearing in side-view camera setups).")]
    [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Linear;

    [Tooltip("Doppler pitch shift when flying past the laser gate at speed.")]
    [Range(0f, 2f)][SerializeField] private float dopplerLevel = 0.4f;

    // Damage

    [Header("Damage")]
    [Tooltip("Damage per second applied smoothly while the player stays inside the active laser.")]
    [SerializeField] private float damagePerSecond = 35f;

    [Tooltip("Engine HP share of laser damage (0–1). Laser burns hull primarily.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float engineShare = 0.08f;

    // Private State

    private bool isLaserOn = true;
    private float pulseTimer;
    private float currentGlow;
    private float targetGlow;

    private float sweepTimeOffset;
    private Vector3 originPosition;
    private CollisionHandler playerInside;

    private Color baseEmissionColor;
    private bool emissionCached;
    private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private AudioSource laserHumSource;
    private AudioSource laserSfxSource;
    private AudioSource laserBurnSource;

    private bool externalControl = false; // true when a LaserGateGroup is driving this gate directly

    //  Lifecycle ------------------------>

    private void Awake()
    {
        if (laserRenderer == null) laserRenderer = GetComponentInChildren<Renderer>();
        if (laserCollider == null) laserCollider = GetComponentInChildren<Collider>();

        // Ensure collider is a trigger
        if (laserCollider != null)
        {
            laserCollider.isTrigger = true;

            // If collider is on a child GameObject, attach a proxy to route trigger events to this parent
            if (laserCollider.gameObject != gameObject)
            {
                LaserTriggerProxy proxy = laserCollider.GetComponent<LaserTriggerProxy>();
                if (proxy == null)
                    proxy = laserCollider.gameObject.AddComponent<LaserTriggerProxy>();
                proxy.Init(this);
            }
        }

        // Ensure a kinematic Rigidbody exists on root so Unity routes trigger queries properly
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        InitLaserAudio();
    }

    private void InitLaserAudio()
    {
        // 1. Looping Hum AudioSource
        if (laserHumSource == null)
            laserHumSource = GetComponent<AudioSource>();

        if (laserHumSource == null)
            laserHumSource = gameObject.AddComponent<AudioSource>();

        ConfigureAudioSource(laserHumSource, isLoop: true);
        laserHumSource.pitch = humPitch;
        laserHumSource.volume = 0f;

        if (laserHumClip != null)
            laserHumSource.clip = laserHumClip;

        // 2. Dedicated One-Shot SFX AudioSource (Open, Close)
        if (laserSfxSource == null)
        {
            Transform existing = transform.Find("Laser_SFX_Source");
            if (existing != null)
                laserSfxSource = existing.GetComponent<AudioSource>();

            if (laserSfxSource == null)
            {
                GameObject sfxGO = new GameObject("Laser_SFX_Source");
                sfxGO.transform.SetParent(transform, false);
                laserSfxSource = sfxGO.AddComponent<AudioSource>();
            }
        }

        ConfigureAudioSource(laserSfxSource, isLoop: false);

        // 3. Dedicated Looping Burn AudioSource (strictly plays while inside)
        if (laserBurnSource == null)
        {
            Transform existing = transform.Find("Laser_Burn_Source");
            if (existing != null)
                laserBurnSource = existing.GetComponent<AudioSource>();

            if (laserBurnSource == null)
            {
                GameObject burnGO = new GameObject("Laser_Burn_Source");
                burnGO.transform.SetParent(transform, false);
                laserBurnSource = burnGO.AddComponent<AudioSource>();
            }
        }

        ConfigureAudioSource(laserBurnSource, isLoop: true);
        if (laserBurnClip != null)
            laserBurnSource.clip = laserBurnClip;
    }

    private void ConfigureAudioSource(AudioSource src, bool isLoop)
    {
        if (src == null) return;
        src.playOnAwake = false;
        src.loop = isLoop;
        src.spatialBlend = spatialBlend;
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;
        src.rolloffMode = rolloffMode;
        src.dopplerLevel = dopplerLevel;
    }

    private void Start()
    {
        originPosition = transform.position;
        CacheBaseEmission();

        if (laserHumSource != null && laserHumClip != null)
        {
            laserHumSource.clip = laserHumClip;
        }

        if (mode == LaserMode.Pulsing)
        {
            float totalCycle = onDuration + offDuration;
            pulseTimer = phaseOffset * totalCycle;
            isLaserOn = pulseTimer < onDuration;
            ApplyLaserState(instant: true);
        }
        else
        {
            sweepTimeOffset = phaseOffset * (2f / sweepSpeed);
        }

        currentGlow = isLaserOn ? 1f : 0f;
        targetGlow = currentGlow;
    }

    private void Update()
    {
        if (!IsPlaying())
        {
            if (laserHumSource != null && laserHumSource.isPlaying)
                laserHumSource.Pause();
            StopBurnEffects();
            return;
        }
        else
        {
            if (laserHumSource != null && !laserHumSource.isPlaying && currentGlow > 0.02f)
                laserHumSource.UnPause();
        }

        if (mode == LaserMode.Pulsing)
        {
            if (!externalControl)
                UpdatePulsing();
        }
        else
        {
            UpdateSweeping();
        }

        UpdateGlow();
        UpdateContinuousDamage();
    }

    //  Pulsing Logic ----------------------------->

    private void UpdatePulsing()
    {
        pulseTimer += Time.deltaTime;
        float threshold = isLaserOn ? onDuration : offDuration;

        if (pulseTimer >= threshold)
        {
            pulseTimer = 0f;
            isLaserOn = !isLaserOn;
            ApplyLaserState(instant: false);
        }
    }

    private void ApplyLaserState(bool instant)
    {
        if (isLaserOn)
        {
            if (laserRenderer != null) laserRenderer.enabled = true;
            if (laserCollider != null) laserCollider.enabled = true;
            targetGlow = 1f;

            if (instant) currentGlow = 1f;

            // Play open ignition sound only after level has loaded (prevents initial spawn audio blast)
            if (!instant && Time.timeSinceLevelLoad > 0.15f && laserSfxSource != null && laserOpenClip != null)
            {
                laserSfxSource.PlayOneShot(laserOpenClip, openVolume * masterLaserVolume);
            }
        }
        else
        {
            if (laserCollider != null) laserCollider.enabled = false;
            targetGlow = 0f;

            if (instant)
            {
                currentGlow = 0f;
                if (laserRenderer != null) laserRenderer.enabled = false;
            }

            // Play close deactivation sound only after level has loaded
            if (!instant && Time.timeSinceLevelLoad > 0.15f && laserSfxSource != null && laserCloseClip != null)
            {
                laserSfxSource.PlayOneShot(laserCloseClip, closeVolume * masterLaserVolume);
            }

            StopBurnEffects();
            playerInside = null;
        }
    }

    //  External Control API (used by LaserGateGroup) ---------------------------->

    /// Sets this gate's starting phase offset (0–1) within its own pulse/sweep cycle.
    /// Only takes effect on the next Start() cycle-timing calculation — call before Start() runs
    /// (e.g. from a group's Awake) for it to apply on spawn
    public void SetPhaseOffset(float phase01)
    {
        phaseOffset = Mathf.Clamp01(phase01);
    }

    ///Enables or disables this gate's own internal pulsing timer.
    /// While true, UpdatePulsing() is suppressed and only ForceOn/ForceOff change state —
    /// used by LaserGateGroup's Sequential and Alternating modes.
    public void SetExternalControl(bool enabled)
    {
        externalControl = enabled;
    }

    /// Forces the laser on immediately, bypassing the internal pulse timer.
    public void ForceOn(bool instant = false)
    {
        isLaserOn = true;
        pulseTimer = 0f;
        ApplyLaserState(instant);
    }

    /// Forces the laser off immediately, bypassing the internal pulse timer.
    public void ForceOff(bool instant = false)
    {
        isLaserOn = false;
        pulseTimer = 0f;
        ApplyLaserState(instant);
    }

    // Sweeping Logic ----------------------->

    private void UpdateSweeping()
    {
        float t = Mathf.PingPong((Time.time + sweepTimeOffset) * sweepSpeed, 1f);
        float factor = easeSweepMotion ? Mathf.SmoothStep(0f, 1f, t) : t;

        switch (movementPattern)
        {
            case MovementPattern.Horizontal:
                transform.position = Vector3.Lerp(
                    originPosition,
                    originPosition + Vector3.right * sweepDistance,
                    factor);
                break;

            case MovementPattern.Vertical:
                transform.position = Vector3.Lerp(
                    originPosition,
                    originPosition + Vector3.up * sweepDistance,
                    factor);
                break;

            case MovementPattern.Rotating:
                transform.position = Vector3.Lerp(
                    originPosition,
                    originPosition + Vector3.right * sweepDistance,
                    factor);
                transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime, Space.Self);
                break;
        }
    }

    // Glow Fade & Hum Modulation ---------------------------->

    private void CacheBaseEmission()
    {
        if (emissionCached || laserRenderer == null) return;
        Material mat = laserRenderer.material;
        if (mat.HasProperty(EmissionColorID))
            baseEmissionColor = mat.GetColor(EmissionColorID);
        emissionCached = true;
    }

    private void UpdateGlow()
    {
        if (laserRenderer == null) return;

        if (!laserRenderer.enabled && Mathf.Approximately(currentGlow, 0f)) return;

        currentGlow = Mathf.MoveTowards(currentGlow, targetGlow, Time.deltaTime * glowFadeSpeed);

        CacheBaseEmission();
        Material mat = laserRenderer.material;
        if (mat.HasProperty(EmissionColorID))
            mat.SetColor(EmissionColorID, baseEmissionColor * currentGlow);

        if (!isLaserOn && Mathf.Approximately(currentGlow, 0f))
            laserRenderer.enabled = false;

        // Attenuated spatial laser hum volume modulation tied directly to current glow
        if (laserHumSource != null && laserHumClip != null)
        {
            if (laserHumSource.clip != laserHumClip)
                laserHumSource.clip = laserHumClip;

            laserHumSource.volume = humVolume * masterLaserVolume * currentGlow;

            if (currentGlow > 0.02f && !laserHumSource.isPlaying && IsPlaying())
            {
                laserHumSource.Play();
            }
            else if (currentGlow <= 0.02f && laserHumSource.isPlaying)
            {
                laserHumSource.Stop();
            }
        }
    }

    // Continuous Smooth Per-Second Damage ----------------------------->

    private void UpdateContinuousDamage()
    {
        if (playerInside == null || !isLaserOn || !IsPlaying())
        {
            StopBurnEffects();
            return;
        }

        // Apply smooth per-second damage without sudden instant tick spikes
        float damageThisFrame = damagePerSecond * Time.deltaTime;
        if (damageThisFrame > 0f)
        {
            Vector3 anchorPos = playerInside.DamageEffectPoint != null
                ? playerInside.DamageEffectPoint.position
                : playerInside.transform.position;

            Vector3 impactPoint = laserCollider != null
                ? laserCollider.ClosestPoint(anchorPos)
                : anchorPos;

            // playCrashEffect: false prevents generic crash audio clip and unwanted camera shake
            playerInside.TakeDamage(
                damageThisFrame,
                impactPoint,
                playCrashEffect: false,
                engineShare: engineShare,
                deathCause: RaceContext.DeathCause.Hazard
            );

            // Update continuous laser burn VFX on player
            playerInside.SetLaserBurnVFX(true, impactPoint);
        }

        // Play burn audio loop strictly while inside
        UpdateBurnAudio();
    }

    private void UpdateBurnAudio()
    {
        if (laserBurnClip == null || laserBurnSource == null) return;

        if (laserBurnSource.clip != laserBurnClip)
            laserBurnSource.clip = laserBurnClip;

        laserBurnSource.volume = burnVolume * masterLaserVolume;

        if (!laserBurnSource.isPlaying)
            laserBurnSource.Play();
    }

    private void StopBurnAudio()
    {
        if (laserBurnSource != null && laserBurnSource.isPlaying)
            laserBurnSource.Stop();
    }

    private void StopBurnEffects()
    {
        StopBurnAudio();
        if (playerInside != null)
        {
            playerInside.SetLaserBurnVFX(false);
        }
    }

    // ── Trigger Events (Direct & Proxy Handlers) ──────────────────────────────

    public void HandleTriggerEnter(Collider other)
    {
        if (!IsPlaying()) return;
        if (!isLaserOn && mode == LaserMode.Pulsing) return;

        CollisionHandler handler = other.GetComponentInParent<CollisionHandler>()
                                ?? other.GetComponentInChildren<CollisionHandler>()
                                ?? other.GetComponent<CollisionHandler>()
                                ?? (other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<CollisionHandler>() : null);

        if (handler == null) return;

        playerInside = handler;
        UpdateBurnAudio();
    }

    public void HandleTriggerStay(Collider other)
    {
        if (!IsPlaying()) return;
        if (!isLaserOn && mode == LaserMode.Pulsing) return;
        if (playerInside != null) return;

        CollisionHandler handler = other.GetComponentInParent<CollisionHandler>()
                                ?? other.GetComponentInChildren<CollisionHandler>()
                                ?? other.GetComponent<CollisionHandler>()
                                ?? (other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<CollisionHandler>() : null);

        if (handler == null) return;

        playerInside = handler;
        UpdateBurnAudio();
    }

    public void HandleTriggerExit(Collider other)
    {
        CollisionHandler handler = other.GetComponentInParent<CollisionHandler>()
                                ?? other.GetComponentInChildren<CollisionHandler>()
                                ?? other.GetComponent<CollisionHandler>()
                                ?? (other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<CollisionHandler>() : null);

        if (handler != null && handler == playerInside)
        {
            StopBurnEffects();
            playerInside = null;
        }
    }

    private void OnTriggerEnter(Collider other) => HandleTriggerEnter(other);
    private void OnTriggerStay(Collider other) => HandleTriggerStay(other);
    private void OnTriggerExit(Collider other) => HandleTriggerExit(other);

    private bool IsPlaying()
    {
        return GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing;
    }

    private void OnDisable()
    {
        if (laserHumSource != null && laserHumSource.isPlaying)
            laserHumSource.Stop();
        if (laserSfxSource != null && laserSfxSource.isPlaying)
            laserSfxSource.Stop();
        StopBurnEffects();
        playerInside = null;
    }
}

/// <summary>
/// Lightweight trigger proxy automatically attached to child laser colliders
/// to forward Unity trigger physics events up to the parent LaserGate script.
/// </summary>
public class LaserTriggerProxy : MonoBehaviour
{
    private LaserGate parentGate;

    public void Init(LaserGate gate)
    {
        parentGate = gate;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (parentGate != null) parentGate.HandleTriggerEnter(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (parentGate != null) parentGate.HandleTriggerStay(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (parentGate != null) parentGate.HandleTriggerExit(other);
    }
}