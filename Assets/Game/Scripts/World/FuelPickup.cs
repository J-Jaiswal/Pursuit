using System.Collections;
using UnityEngine;

public class FuelPickup : MonoBehaviour
{
    [SerializeField] private float refillAmount = 25f;
    [SerializeField] private ParticleSystem collectParticle;
    [SerializeField] private AudioSource collectAudio;
    [SerializeField] private bool destroyOnCollect = false;

    private Collider col;
    private Renderer[] renderers;

    private void Awake()
    {
        col = GetComponent<Collider>();
        renderers = GetComponentsInChildren<Renderer>();
    }

    private void OnTriggerEnter(Collider other)
    {
        Fuel fuel = other.GetComponentInParent<Fuel>();
        if (fuel != null && fuel.CompareTag("Player"))
        {
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
        bool hasAudio = collectAudio != null;

        if (hasParticle)
        {
            collectParticle.Play();
        }

        if (hasAudio)
        {
            collectAudio.Play();
        }

        // Only delay if there are effects to play
        if (hasParticle || hasAudio)
        {
            yield return new WaitForSeconds(2.0f);
        }

        if (destroyOnCollect)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
