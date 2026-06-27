using UnityEngine;

[CreateAssetMenu(fileName = "RocketConfig", menuName = "Config/RocketConfig")]
public class RocketConfigSO : ScriptableObject
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float damageRate = 20f;
    [SerializeField] private float maxFuel = 100f;
    [SerializeField] private float engineBurnRate = 10f;
    [SerializeField] private float sideBoostBurnRate = 5f;
    [SerializeField] private float rotateSpeed = 100f;
    [SerializeField] private float thrust = 1000f;
    [SerializeField] private float maxThrust = 2000f;
    [SerializeField] private float thrustSmoothing = 0.1f;
    [SerializeField] private float criticalHealthThreshold = 0.3f;
    [SerializeField] private float criticalFuelThreshold = 0.2f;
    [SerializeField] private float deathSequenceDuration = 1.5f;
    [SerializeField] private float landingSequenceDuration = 1.0f;
    [SerializeField] private float maxHeat = 100f;
    [SerializeField] private float heatAccumulationRate = 25f;
    [SerializeField] private float heatDecayRate = 20f;
    [SerializeField] private float overheatDamageRate = 15f;
    [SerializeField] private float overheatWarningThreshold = 0.8f;

    public float MaxHealth => maxHealth;
    public float DamageRate => damageRate;
    public float MaxFuel => maxFuel;
    public float EngineBurnRate => engineBurnRate;
    public float SideBoostBurnRate => sideBoostBurnRate;
    public float RotateSpeed => rotateSpeed;
    public float Thrust => thrust;
    public float MaxThrust => maxThrust;
    public float ThrustSmoothing => thrustSmoothing;
    public float CriticalHealthThreshold => criticalHealthThreshold;
    public float CriticalFuelThreshold => criticalFuelThreshold;
    public float DeathSequenceDuration => deathSequenceDuration;
    public float LandingSequenceDuration => landingSequenceDuration;
    public float MaxHeat => maxHeat;
    public float HeatAccumulationRate => heatAccumulationRate;
    public float HeatDecayRate => heatDecayRate;
    public float OverheatDamageRate => overheatDamageRate;
    public float OverheatWarningThreshold => overheatWarningThreshold;
}

