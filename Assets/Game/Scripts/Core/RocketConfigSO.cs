using UnityEngine;

[CreateAssetMenu(fileName = "RocketConfig", menuName = "Config/RocketConfig")]
public class RocketConfigSO : ScriptableObject
{
    [Header("Rocket Health (Hull / Structural)")]
    [Tooltip("Maximum structural health the rocket hull can sustain before destruction.")]
    [SerializeField] private float maxRocketHealth = 100f;

    [Tooltip("Damage applied per wall/generic-obstacle collision.")]
    [SerializeField] private float damageRate = 20f;

    [Tooltip("Rocket hull health (normalized 0-1) at or below which the hull is considered critically damaged (vignette, missile surge).")]
    [SerializeField] private float criticalHealthThreshold = 0.3f;

    [Header("Engine Health")]
    [Tooltip("Maximum engine integrity. When this reaches 0 the engine explodes and the rocket is destroyed.")]
    [SerializeField] private float maxEngineHealth = 100f;

    [Tooltip("Engine health (normalized 0-1) at or below which the engine is considered critically damaged (warning pulse).")]
    [SerializeField] private float engineCriticalThreshold = 0.2f;

    [Header("Damage Split Ratios")]
    [Tooltip("Fraction of overheat damage that hits Engine Health (remainder hits Rocket Health). " +
             "Default 0.8 = 80% engine / 20% rocket.")]
    [Range(0f, 1f)]
    [SerializeField] private float overheatEngineShare = 0.8f;

    [Tooltip("Fraction of a generic wall/obstacle collision damage that hits Engine Health. " +
             "Default 0.1 = 10% engine / 90% rocket.")]
    [Range(0f, 1f)]
    [SerializeField] private float collisionEngineShare = 0.1f;

    [Header("Collision Recoil & Bounce")]
    [Tooltip("Impulse force kicking the rocket away when colliding with regular cave walls.")]
    [SerializeField] private float wallRecoilForce = 8f;

    [Tooltip("Impulse force kicking the rocket away when colliding with obstacles and hazards (spikes, crushing traps, asteroids).")]
    [SerializeField] private float obstacleRecoilForce = 15f;

    [Header("Fuel")]
    [Tooltip("Maximum fuel capacity.")]
    [SerializeField] private float maxFuel = 100f;

    [Tooltip("Fuel consumption rate while main engine is thrusting.")]
    [SerializeField] private float engineBurnRate = 10f;

    [Tooltip("Fuel consumption rate while side boosters (rotation) are active.")]
    [SerializeField] private float sideBoostBurnRate = 5f;

    [Tooltip("Fuel (normalized 0-1) at or below which fuel is considered critically low.")]
    [SerializeField] private float criticalFuelThreshold = 0.2f;

    [Header("Thrust")]
    [Tooltip("Maximum thrust force the engine can reach.")]
    [SerializeField] private float maxThrust = 2000f;

    [Tooltip("How quickly thrust ramps up/down toward its target value. Higher = snappier response.")]
    [SerializeField] private float thrustSmoothing = 0.1f;

    [Header("Rotation")]
    [Tooltip("Torque applied around Z when steering left/right with physics-based rotation.")]
    [SerializeField] private float rotationTorque = 200f;

    [Tooltip("Caps how fast the rocket can spin, preventing runaway rotation from torque.")]
    [SerializeField] private float maxAngularVelocity = 200f;

    [Header("Heat")]
    [Tooltip("Maximum heat the rocket can accumulate before overheating.")]
    [SerializeField] private float maxHeat = 100f;

    [Tooltip("Rate at which heat builds up (e.g. while thrusting).")]
    [SerializeField] private float heatAccumulationRate = 25f;

    [Tooltip("Rate at which heat dissipates when not generating heat.")]
    [SerializeField] private float heatDecayRate = 20f;

    [Tooltip("Total damage rate (HP/sec) applied while overheated. Split between engine and rocket by overheatEngineShare.")]
    [SerializeField] private float overheatDamageRate = 15f;

    [Tooltip("Heat (normalized 0-1) at or above which an overheat warning is triggered.")]
    [SerializeField] private float overheatWarningThreshold = 0.8f;

    [Header("Sequences")]
    [Tooltip("How long the critical tumble/death sequence lasts before the player officially dies (fuel death only).")]
    [SerializeField] private float deathSequenceDuration = 1.5f;

    // ---- Rocket Health ----
    public float MaxRocketHealth      => maxRocketHealth;
    public float DamageRate            => damageRate;
    public float CriticalHealthThreshold => criticalHealthThreshold;

    // ---- Engine Health ----
    public float MaxEngineHealth       => maxEngineHealth;
    public float EngineCriticalThreshold => engineCriticalThreshold;

    // ---- Damage Split ----
    public float OverheatEngineShare   => overheatEngineShare;
    public float CollisionEngineShare  => collisionEngineShare;

    // ---- Collision Recoil ----
    public float WallRecoilForce       => wallRecoilForce;
    public float ObstacleRecoilForce   => obstacleRecoilForce;

    // ---- Fuel ----
    public float MaxFuel               => maxFuel;
    public float EngineBurnRate        => engineBurnRate;
    public float SideBoostBurnRate     => sideBoostBurnRate;
    public float CriticalFuelThreshold => criticalFuelThreshold;

    // ---- Thrust ----
    public float MaxThrust             => maxThrust;
    public float ThrustSmoothing       => thrustSmoothing;

    // ---- Rotation ----
    public float RotationTorque        => rotationTorque;
    public float MaxAngularVelocity    => maxAngularVelocity;

    // ---- Heat ----
    public float MaxHeat               => maxHeat;
    public float HeatAccumulationRate  => heatAccumulationRate;
    public float HeatDecayRate         => heatDecayRate;
    public float OverheatDamageRate    => overheatDamageRate;
    public float OverheatWarningThreshold => overheatWarningThreshold;

    // ---- Sequences ----
    public float DeathSequenceDuration  => deathSequenceDuration;
}