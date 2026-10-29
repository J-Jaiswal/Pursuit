using UnityEngine;

/// <summary>
/// Placed on 3D trigger colliders across track sectors and landmarks (e.g. "Laser Cavern", "Crusher Gauntlet").
/// Detects player and missile entry, notifying RaceContext to update sector tracking and trigger contextual commentary.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CommentaryZoneTrigger : MonoBehaviour
{
    public enum ZoneSpeakerTarget
    {
        Both,
        NovaOnly,
        MissionControlOnly
    }

    [Header("Zone Identity")]
    [Tooltip("Human-readable name of this level zone/landmark (e.g. 'Laser Cavern', 'Crusher Sector', 'Crystal Chasm').")]
    [SerializeField] private string zoneName = "Sector";

    [Header("Commentary Trigger")]
    [Tooltip("If true, entering this zone will fire a commentary trigger immediately.")]
    [SerializeField] private bool triggerCommentaryOnEnter = true;

    [Tooltip("Target speaker for this zone's trigger.")]
    [SerializeField] private ZoneSpeakerTarget targetSpeaker = ZoneSpeakerTarget.Both;

    [Tooltip("Priority for this zone commentary.")]
    [SerializeField] private CommentaryQueue.CommentPriority priority = CommentaryQueue.CommentPriority.Low;

    [TextArea(1, 3)]
    [Tooltip("Contextual description injected into the LLM prompt (e.g. 'Entering tight corridor flanked by pulsing laser gates').")]
    [SerializeField] private string situationContext = "Entered a dangerous track sector.";

    [Tooltip("Only trigger the voice line once per race run.")]
    [SerializeField] private bool triggerOncePerRun = true;

    private bool hasTriggeredThisRun;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            bool shouldFireLine = triggerCommentaryOnEnter && (!triggerOncePerRun || !hasTriggeredThisRun);
            if (shouldFireLine)
            {
                hasTriggeredThisRun = true;
            }

            if (RaceContext.Instance != null)
            {
                RaceContext.Instance.NotifyPlayerEnteredZone(
                    zoneName,
                    situationContext,
                    shouldFireLine,
                    priority,
                    targetSpeaker
                );
            }
        }
        else if (other.CompareTag("Missile"))
        {
            if (RaceContext.Instance != null)
            {
                RaceContext.Instance.NotifyMissileEnteredZone(zoneName);
            }
        }
    }

    /// Resets one-shot trigger state when reloading/restarting the scene.
    public void ResetTrigger()
    {
        hasTriggeredThisRun = false;
    }
}
