// MissileBehavior.cs
// Five deterministic behavior states for the missile state machine.
// Transitions are driven entirely by code — no LLM involvement.

public enum MissileBehavior
{
    Hold,        // Default: steady follow, maintains safe distance
    Surge,       // Aggressive: player low fuel/health or tight corridor
    Stalk,       // Menacing: missile very close — match speed, build tension
    Retreat,     // False security: post wall-hit or post-pickup — pulls back
    CornerTrap   // Predictive: player dwelling in same corridor segment
}
