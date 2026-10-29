using System.Collections;
using UnityEngine;

public class FuelPickup : MonoBehaviour
{
    [Header("Refill Settings")]
    [SerializeField] private float refillAmount = 25f;
    [SerializeField] private ParticleSystem collectParticle;
    [SerializeField] private bool destroyOnCollect = false;

    [Header("Dynamic Idle Animation")]
    [Tooltip("Enable continuous rotation to make the pickup feel dynamic.")]
    [SerializeField] private bool enableRotation = true;
    [Tooltip("Rotation speed in degrees per second.")]
    [SerializeField] private float rotationSpeed = 90f;
    [Tooltip("Axis of rotation.")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up;

    [Tooltip("Enable vertical hovering / bobbing motion.")]
    [SerializeField] private bool enableBobbing = true;
    [SerializeField] private float bobAmplitude = 0.12f;
    [SerializeField] private float bobFrequency = 2f;

    private Collider col;
    private Renderer[] renderers;
    private Vector3 initialPosition;
    private bool isCollected;

    private void Awake()
    {
        col = GetComponent<Collider>();
        renderers = GetComponentsInChildren<Renderer>();
        initialPosition = transform.position;
    }

    private void Update()
    {
        if (isCollected) return;

        // Dynamic rotation
        if (enableRotation)
        {
            transform.Rotate(rotationAxis * (rotationSpeed * Time.deltaTime), Space.Self);
        }

        // Dynamic vertical hover bobbing
        if (enableBobbing)
        {
            float newY = initialPosition.y + Mathf.Sin(Time.time * bobFrequency) * bobAmplitude;
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        // Check if the collider belongs to the Player
        bool isPlayer = other.CompareTag("Player")
                     || (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player"))
                     || (other.transform.root != null && other.transform.root.CompareTag("Player"));

        if (!isPlayer) return;

        Fuel fuel = other.GetComponentInParent<Fuel>()
                 ?? other.GetComponentInChildren<Fuel>()
                 ?? other.GetComponent<Fuel>();

        if (fuel != null)
        {
            isCollected = true;
            fuel.AddFuel(refillAmount);
            StartCoroutine(CollectSequence());
        }
    }

    private IEnumerator CollectSequence()
    {
        // Disable collision to prevent double triggering
        if (col != null)
        {
            col.enabled = false;
        }

        // Hide visuals
        foreach (Renderer r in renderers)
        {
            if (r != null)
            {
                r.enabled = false;
            }
        }

        // Play feedback
        bool hasParticle = collectParticle != null;

        if (hasParticle)
        {
            collectParticle.Play();
        }

        // Play crisp unattenuated pickup sound effect
        if (SoundEffectsManager.Instance != null)
        {
            SoundEffectsManager.Instance.PlaySFX(SFXType.FuelPickup);
        }

        // Only delay if there are particle effects to finish
        if (hasParticle)
        {
            yield return new WaitForSeconds(1.5f);
        }

        // Always deactivate instead of destroying so the pickup can be restored on checkpoint respawn
        gameObject.SetActive(false);
    }


    /// Restores the pickup to its initial uncollected state and position,
    /// re-enabling its collider, renderers, and GameObject.

    public void ResetPickup()
    {
        StopAllCoroutines();
        isCollected = false;
        transform.position = initialPosition;
        if (col == null)
        {
            col = GetComponent<Collider>();
        }
        if (col != null)
        {
            col.enabled = true;
        }
        if (renderers == null || renderers.Length == 0)
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }
        if (renderers != null)
        {
            foreach (Renderer r in renderers)
            {
                if (r != null) r.enabled = true;
            }
        }
        gameObject.SetActive(true);
    }

    /// Resets all FuelPickup instances in the active scene (including currently inactive ones).
    public static void ResetAll()
    {
        FuelPickup[] pickups = FindObjectsByType<FuelPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (FuelPickup p in pickups)
        {
            if (p != null)
            {
                p.ResetPickup();
            }
        }
    }
}
