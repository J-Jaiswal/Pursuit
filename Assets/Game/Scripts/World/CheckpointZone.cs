using UnityEngine;

public class CheckpointZone : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.SetCheckpoint(transform.position);
            }
        }
    }
}
