using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Trigger placed past a checkpoint landing pad or along bypass routes.
/// When the player passes through without having reached/activated the checkpoint,
/// it triggers a pulsing "Missed Checkpoint" notification on the UI.
/// </summary>
[RequireComponent(typeof(Collider))]
public class MissedCheckpointTrigger : MonoBehaviour
{
    [Tooltip("The checkpoint index that should have been activated before entering this area.")]
    [SerializeField] private int targetCheckpointIndex = 0;

    [Tooltip("How long the 'Missed Checkpoint' warning PNG stays on screen.")]
    [SerializeField] private float warningDuration = 3f;

    private Collider triggerCollider;
    private bool hasTriggered;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
            triggerCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered) return;

        bool isPlayer = other.CompareTag("Player")
                     || (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player"))
                     || (other.transform.root != null && other.transform.root.CompareTag("Player"));

        if (!isPlayer) return;

        // Check if the checkpoint was registered
        string sceneName = SceneManager.GetActiveScene().name;
        int sceneIdx     = SceneManager.GetActiveScene().buildIndex;
        int savedIndex   = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneName);
        if (savedIndex < 0)
            savedIndex = CheckpointSaveSystem.GetSavedCheckpointIndex(sceneIdx);

        // If the player did not activate this checkpoint before passing
        if (savedIndex < targetCheckpointIndex)
        {
            hasTriggered = true;
            Debug.Log($"[MissedCheckpointTrigger] Player missed checkpoint {targetCheckpointIndex + 1}!");

            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowMissedCheckpoint(warningDuration);
            }

            if (triggerCollider != null)
                triggerCollider.enabled = false;
        }
    }
}
