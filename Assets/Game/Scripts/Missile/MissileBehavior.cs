
public enum MissileBehavior
{
    Hold,        // Default: steady follow, maintains safe distance
    Surge,       // Aggressive: player low fuel/health or tight corridor
    Stalk,       // Menacing: missile very close — match speed, build tension
    Retreat,     // False security: post wall-hit or post-pickup — pulls back
    CornerTrap,  // Predictive: player dwelling in same corridor segment
    Attack       // Urgent: distance <= 10m, direct chase and detonation
}
