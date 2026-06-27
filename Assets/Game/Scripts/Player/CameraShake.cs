using UnityEngine;
using Unity.Cinemachine;


/// <summary>
/// Cinemachine-compatible camera shake manager.
/// 
/// WHY MANUAL SHAKE FAILS WITH CINEMACHINE:
/// Cinemachine overrides the camera's Position and Rotation every frame during LateUpdate.
/// Directly modifying 'transform.localPosition' on the camera will be instantly overwritten.
/// 
/// HOW THIS SCRIPT WORKS (Method A - Cinemachine Impulse):
/// This script delegates camera shake to Unity's native Cinemachine Impulse system. Shakes are
/// generated in 3D world space as "impulses", which any virtual camera listening for impulses
/// will capture, apply to its position, and decay naturally.
/// 
/// SETUP INSTRUCTIONS IN UNITY EDITOR:
/// 1. Place this script on a persistent manager GameObject or on the Main Camera.
///    - It will automatically attach a 'CinemachineImpulseSource' component because of [RequireComponent].
/// 2. In the inspector for the attached 'Cinemachine Impulse Source' component:
///    - Expand 'Impulse Definition'.
///    - Assign a noise profile to 'Raw Signal' (e.g. click the gear or circle button and select '6D Shake' or a handheld profile).
///    - Feel free to tune the 'Time Envelope' (Attack, Sustain, Decay times) which determines how long the shake lasts.
/// 3. In the inspector for your active Cinemachine Virtual Camera (Vcam):
///    - Go to the bottom 'Extensions' section.
///    - Add the 'Cinemachine Impulse Listener' extension.
/// 4. Ensure any event triggering a shake calls 'CameraShake.Shake(intensity, duration)'.
/// </summary>
[RequireComponent(typeof(CinemachineImpulseSource))]
public class CameraShake : MonoBehaviour
{
    // Static singleton instance so other classes can trigger shakes globally
    public static CameraShake Instance { get; private set; }

    private CinemachineImpulseSource impulseSource;

    private void Awake()
    {
        // Enforce Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Cache the attached CinemachineImpulseSource
        impulseSource = GetComponent<CinemachineImpulseSource>();
    }

    /// <summary>
    /// Static utility method to trigger a shake from any script (e.g., CollisionHandler, MissileAI).
    /// </summary>
    /// <param name="intensity">The strength multiplier of the shake.</param>
    /// <param name="duration">
    /// The duration parameter (retained for signature compatibility, though Cinemachine Impulse
    /// defaults to the Envelope's Decay time configured on the Impulse Source inspector).
    /// </param>
    public static void Shake(float intensity, float duration)
    {
        if (Instance != null)
        {
            Instance.TriggerImpulse(intensity);
        }
    }

    /// <summary>
    /// Triggers a physical impulse vector that the Cinemachine Brain listens for.
    /// </summary>
    /// <param name="intensity">Multiplier scaled against the source noise vector.</param>
    public void TriggerImpulse(float intensity)
    {
        if (impulseSource != null)
        {
            // GenerateImpulse scales the Default Velocity vector set in the inspector.
            // This is processed natively in Cinemachine and decay is managed by the envelope profile.
            impulseSource.GenerateImpulse(intensity);
        }
    }
}

