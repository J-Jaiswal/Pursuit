using UnityEngine;

/// <summary>
/// Orchestrates a set of LaserGate hazards using one of three group-level
/// behaviour modes. LaserGate remains fully self-contained; this script only
/// uses LaserGate's public API: SetPhaseOffset, SetExternalControl, ForceOn, ForceOff.
/// No internal LaserGate state is accessed or assumed.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// GROUP CONTROL MODES
/// ─────────────────────────────────────────────────────────────────────────────
///
///   PHASE STAGGER
///     Gates self-run their own timers (Pulsing or Sweeping). The group sets each
///     gate's starting phase offset once in Awake, then steps back entirely — the
///     group has no per-frame cost in this mode. Works with both gate modes.
///
///     Three stagger orders (PhaseStaggerOrder):
///       AllTogether — all gates start in perfect sync (phase = 0 for all)
///       Forward     — gate[0] leads; each subsequent gate is offset by phaseStep
///       Reverse     — last gate leads; offset increases backwards through the list
///
///     phaseStep is a 0–1 fraction of one full cycle (e.g. 0.1 = each gate starts
///     10% of its cycle behind the previous one).
///
///   SEQUENTIAL
///     The group cycles one gate on at a time, turning each off before the next
///     activates. Gates have their own pulsing suppressed; the group drives
///     ForceOn/ForceOff directly.
///
///     sequentialOnDuration  — how long each gate stays active
///     sequentialGapDuration — dark pause between one gate off and next gate on
///                             (0 = instant hand-off)
///     randomOrder           — shuffles the activation order each full cycle
///
///   ALTERNATING
///     Gates are split into two sets by index parity: even-indexed and odd-indexed.
///     The group swaps the active set on a shared timer, creating a see-saw rhythm.
///     Gates have their own pulsing suppressed.
///
///     alternatingDuration — how long each set stays active before swapping
/// </summary>
public class LaserGateGroup : MonoBehaviour
{
    // Enums 

    public enum GroupControlMode
    {
        PhaseStagger,   // Gates self-run from a staggered start; no per-frame group logic
        Sequential,     // Group cycles one gate on at a time
        Alternating     // Group swaps even/odd gate sets on a shared timer
    }

    public enum PhaseStaggerOrder
    {
        AllTogether,    // All gates in perfect sync
        Forward,        // Gate[0] leads, phase increases down the list
        Reverse         // Last gate leads, phase increases up the list
    }

    // Inspector

    [Header("Gates")]
    [Tooltip("LaserGate instances in this group. Order matters for Sequential " +
             "and PhaseStagger Forward/Reverse.")]
    [SerializeField] private LaserGate[] gates;

    [Header("Group Control Mode")]
    [Tooltip("PhaseStagger: gates self-run from a staggered start phase.\n" +
             "Sequential: one gate active at a time, cycling through the list.\n" +
             "Alternating: even/odd gate sets swap on a shared timer.")]
    [SerializeField] private GroupControlMode controlMode = GroupControlMode.PhaseStagger;

    // Phase Stagger Settings

    [Header("Phase Stagger Settings")]
    [Tooltip("AllTogether: all gates start in sync (phaseStep is ignored).\n" +
             "Forward: gate[0] leads, each next gate offset by phaseStep.\n" +
             "Reverse: last gate leads, stagger runs backwards through the list.")]
    [SerializeField] private PhaseStaggerOrder staggerOrder = PhaseStaggerOrder.Forward;

    [Tooltip("Phase offset step per gate as a 0–1 fraction of one full cycle. " +
             "0.1 = each gate starts 10% of its cycle behind the previous one. " +
             "Only used for Forward and Reverse orders.")]
    [Range(0f, 1f)]
    [SerializeField] private float phaseStep = 0.1f;

    // Sequential Settings

    [Header("Sequential Settings")]
    [Tooltip("How long (seconds) each gate stays active before the group moves to the next.")]
    [Min(0.05f)]
    [SerializeField] private float sequentialOnDuration = 1.0f;

    [Tooltip("Dark gap (seconds) between one gate turning off and the next turning on. " +
             "0 = instant hand-off with no dark frame.")]
    [Min(0f)]
    [SerializeField] private float sequentialGapDuration = 0.1f;

    [Tooltip("If true, the activation order is shuffled randomly at the start of each new cycle.")]
    [SerializeField] private bool randomOrder = false;

    // Alternating Settings

    [Header("Alternating Settings")]
    [Tooltip("How long (seconds) each gate set (even or odd) stays active before swapping.")]
    [Min(0.05f)]
    [SerializeField] private float alternatingDuration = 1.0f;

    // Private State 

    private float groupTimer;

    // Sequential
    private int[] sequenceOrder;        // gate indices in current activation order
    private int sequentialStep;         // current index into sequenceOrder
    private bool sequentialInGap;       // true during the dark gap between two active gates

    // Alternating
    private bool evenSetActive;         // true = even-indexed gates on, odd-indexed off

    // Lifecycle --------------------------->

    private void Awake()
    {
        if (gates == null || gates.Length == 0) return;

        switch (controlMode)
        {
            case GroupControlMode.PhaseStagger:
                // Phase offsets are written once; gates self-run from here on.
                InitPhaseStagger();
                break;

            case GroupControlMode.Sequential:
            case GroupControlMode.Alternating:
                // Suppress each gate's own pulsing — this group drives on/off directly.
                foreach (LaserGate gate in gates)
                {
                    if (gate == null) continue;
                    gate.SetExternalControl(true);
                    gate.ForceOff(instant: true);
                }
                break;
        }
    }

    private void Start()
    {
        if (gates == null || gates.Length == 0) return;

        switch (controlMode)
        {
            case GroupControlMode.Sequential:
                BuildSequenceOrder();
                sequentialStep = 0;
                sequentialInGap = false;
                groupTimer = 0f;
                ActivateCurrentGate(instant: true);   // first gate on immediately (silent at spawn)
                break;

            case GroupControlMode.Alternating:
                evenSetActive = true;
                groupTimer = 0f;
                ApplyAlternating(instant: true);      // even gates on immediately (silent at spawn)
                break;
        }
    }

    private void Update()
    {
        if (!IsPlaying()) return;
        if (gates == null || gates.Length == 0) return;

        switch (controlMode)
        {
            case GroupControlMode.Sequential:
                UpdateSequential();
                break;
            case GroupControlMode.Alternating:
                UpdateAlternating();
                break;
                // PhaseStagger: zero per-frame cost — gates handle themselves.
        }
    }

    // Phase Stagger ----------------------------->

    private void InitPhaseStagger()
    {
        for (int i = 0; i < gates.Length; i++)
        {
            if (gates[i] == null) continue;

            float phase01 = staggerOrder switch
            {
                PhaseStaggerOrder.AllTogether => 0f,
                PhaseStaggerOrder.Forward => Mathf.Clamp01(i * phaseStep),
                PhaseStaggerOrder.Reverse => Mathf.Clamp01((gates.Length - 1 - i) * phaseStep),
                _ => 0f
            };

            gates[i].SetPhaseOffset(phase01);
        }
    }

    // Sequential ------------------------------>

    private void UpdateSequential()
    {
        groupTimer += Time.deltaTime;

        if (sequentialInGap)
        {
            // Waiting in the dark gap before activating the next gate.
            if (groupTimer >= sequentialGapDuration)
            {
                groupTimer = 0f;
                sequentialInGap = false;
                ActivateCurrentGate(instant: false);
            }
        }
        else
        {
            // A gate is currently active — wait for its on-duration to expire.
            if (groupTimer >= sequentialOnDuration)
            {
                groupTimer = 0f;
                DeactivateCurrentGate(instant: false);

                // Advance to the next gate; reshuffle if a full cycle just completed.
                sequentialStep++;
                if (sequentialStep >= sequenceOrder.Length)
                {
                    sequentialStep = 0;
                    if (randomOrder) ShuffleSequenceOrder();
                }

                if (sequentialGapDuration > 0f)
                    sequentialInGap = true;  // enter the dark gap
                else
                    ActivateCurrentGate(instant: false);   // instant hand-off, no gap
            }
        }
    }

    private void BuildSequenceOrder()
    {
        sequenceOrder = new int[gates.Length];
        for (int i = 0; i < gates.Length; i++)
            sequenceOrder[i] = i;

        if (randomOrder) ShuffleSequenceOrder();
    }

    private void ShuffleSequenceOrder()
    {
        // Fisher-Yates shuffle
        for (int i = sequenceOrder.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (sequenceOrder[i], sequenceOrder[j]) = (sequenceOrder[j], sequenceOrder[i]);
        }
    }

    private void ActivateCurrentGate(bool instant = false)
    {
        if (sequenceOrder == null) return;
        if (sequentialStep < 0 || sequentialStep >= sequenceOrder.Length) return;
        gates[sequenceOrder[sequentialStep]]?.ForceOn(instant);
    }

    private void DeactivateCurrentGate(bool instant = false)
    {
        if (sequenceOrder == null) return;
        if (sequentialStep < 0 || sequentialStep >= sequenceOrder.Length) return;
        gates[sequenceOrder[sequentialStep]]?.ForceOff(instant);
    }

    // Alternating --------------------------->

    private void UpdateAlternating()
    {
        groupTimer += Time.deltaTime;

        if (groupTimer >= alternatingDuration)
        {
            groupTimer = 0f;
            evenSetActive = !evenSetActive;
            ApplyAlternating(instant: false);
        }
    }

    private void ApplyAlternating(bool instant = false)
    {
        for (int i = 0; i < gates.Length; i++)
        {
            if (gates[i] == null) continue;

            // Even-indexed (0, 2, 4…) vs odd-indexed (1, 3, 5…)
            bool isEven = (i % 2 == 0);
            bool shouldBeOn = (isEven == evenSetActive);

            if (shouldBeOn) gates[i].ForceOn(instant);
            else gates[i].ForceOff(instant);
        }
    }

    // Utility --------------------------------->

    private bool IsPlaying() =>
        GameManager.Instance != null &&
        GameManager.Instance.CurrentState == GameState.Playing;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (gates == null) return;

        for (int i = 0; i < gates.Length; i++)
        {
            if (gates[i] == null) continue;

            // Colour-code connections by mode to make group roles obvious in Scene view.
            Gizmos.color = controlMode switch
            {
                GroupControlMode.PhaseStagger => Color.yellow,
                GroupControlMode.Sequential => Color.green,
                GroupControlMode.Alternating => i % 2 == 0 ? Color.cyan : Color.magenta,
                _ => Color.white
            };

            Gizmos.DrawLine(transform.position, gates[i].transform.position);
            Gizmos.DrawSphere(gates[i].transform.position, 0.1f);
        }
    }
#endif
}