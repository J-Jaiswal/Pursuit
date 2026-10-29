using UnityEngine;
using Unity.Cinemachine;


/// <summary>
/// Cinemachine-compatible camera shake manager.
/// This script delegates camera shake to Unity's native Cinemachine Impulse system. 
/// Shakes are generated in 3D world space as "impulses", which any virtual camera listening for impulses
/// will capture, apply to its position, and decay naturally.
/// </summary>


[RequireComponent(typeof(CinemachineImpulseSource))]
public class CameraShake : MonoBehaviour
{
    // Static singleton instance so other classes can trigger shakes globally
    public static CameraShake Instance { get; private set; }

    private CinemachineImpulseSource impulseSource;

    private void Awake()
    {
        
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        impulseSource = GetComponent<CinemachineImpulseSource>();
    }


    // Static utility method to trigger a shake from any script (e.g., CollisionHandler, MissileAI).

    
    public static void Shake(float intensity, float duration)
    {
        if (Instance != null)
        {
            Instance.TriggerImpulse(intensity);
        }
    }


    // Triggers a physical impulse vector that the Cinemachine Brain listens for.
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

