using System;
using UnityEngine;

public class EngineHeat : MonoBehaviour
{
    [SerializeField] private RocketConfigSO config;

    public event Action<float> OnHeatChanged;

    private Movements movements;
    private CollisionHandler collisionHandler;

    private float currentHeat = 0f;
    private bool isOverheated = false;

    public float CurrentHeatNormalized
    {
        get
        {
            float maxH = config != null ? config.MaxHeat : 100f;
            return maxH > 0f ? currentHeat / maxH : 0f;
        }
    }

    public bool IsOverheated => isOverheated;

    private void Start()
    {
        movements = GetComponent<Movements>();
        collisionHandler = GetComponent<CollisionHandler>();
        GameManager.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        GameManager.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        float maxHeat = config != null ? config.MaxHeat : 100f;
        float heatAcc = config != null ? config.HeatAccumulationRate : 25f;
        float heatDec = config != null ? config.HeatDecayRate : 20f;
        float dmgRate = config != null ? config.OverheatDamageRate : 15f;

        // Check if thrust is active (smoothed thrust represents throttle)
        bool isThrusting = movements != null && movements.NormalizedThrust > 0.05f;

        if (isThrusting)
        {
            currentHeat = Mathf.Clamp(currentHeat + heatAcc * Time.deltaTime, 0f, maxHeat);
        }
        else
        {
            currentHeat = Mathf.Clamp(currentHeat - heatDec * Time.deltaTime, 0f, maxHeat);
        }

        float normalizedHeat = maxHeat > 0f ? currentHeat / maxHeat : 0f;
        OnHeatChanged?.Invoke(normalizedHeat);

        if (currentHeat >= maxHeat)
        {
            isOverheated = true;
            if (collisionHandler != null)
            {
                float totalDmg  = dmgRate * Time.deltaTime;
                float engShare  = config != null ? config.OverheatEngineShare : 0.8f;

                // 80% of overheat damage burns the engine, 20% bleeds into the rocket hull.
                collisionHandler.TakeDamage(totalDmg, transform.position,
                                            playCrashEffect: false,
                                            engineShare: engShare,
                                            deathCause: RaceContext.DeathCause.EngineFailure);
            }
        }
        else
        {
            isOverheated = false;
        }
    }

    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.Playing)
        {
            ResetHeat();
        }
    }

    public void ResetHeat()
    {
        currentHeat = 0f;
        isOverheated = false;
        OnHeatChanged?.Invoke(0f);
    }
}
