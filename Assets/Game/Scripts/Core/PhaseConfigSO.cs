using UnityEngine;

[CreateAssetMenu(fileName = "NewPhaseConfig", menuName = "Pursuit/Phase Config")]
public class PhaseConfigSO : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Name of this checkpoint phase. Used by the LLM commentary system " +
             "(RaceContext.GameStateSnapshot) to give the AI voice context about " +
             "which section of the level the player is in.")]
    [SerializeField] private string phaseName;

    [TextArea]
    [Tooltip("Optional description of this phase for design documentation.")]
    [SerializeField] private string phaseDescription;

    [Header("Missile Difficulty")]
    [Tooltip("Multiplier applied to the missile's base speed. " +
             "1.0 = default, 1.5 = 50% faster, 2.0 = double speed.")]
    [SerializeField] [Range(0.5f, 2f)] private float missileSpeedMultiplier = 1f;

    [Tooltip("Controls how aggressively the missile ignores walls when chasing. " +
             "Higher value = more reckless. " +
             "Internally inverts the avoidance weight: 1.0 = normal avoidance, 2.0 = half avoidance.")]
    [SerializeField] [Range(1f, 3f)] private float missileAggressionMultiplier = 1f;

    [Tooltip("Multiplier applied to the missile's catch (trigger) radius. " +
             "Higher = catches the player from further away.")]
    [SerializeField] [Range(0.5f, 1.5f)] private float missileCatchRadiusMultiplier = 1f;

    // Public Accessors ------------>

    public string PhaseName        => phaseName;
    public string PhaseDescription => phaseDescription;
    public float  MissileSpeedMultiplier       => missileSpeedMultiplier;

    /// Converts aggression to avoidance weight for MissileAI.ApplyDifficultyOverride.
    /// Higher aggression = lower avoidance weight (missile is more reckless through walls).
   
    public float  MissileAvoidanceWeightMultiplier => 1f / missileAggressionMultiplier;
    public float  MissileCatchRadiusMultiplier => missileCatchRadiusMultiplier;
}
