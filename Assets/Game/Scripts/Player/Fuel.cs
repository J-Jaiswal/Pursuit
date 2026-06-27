using System;
using UnityEngine;

public class Fuel : MonoBehaviour
{
    [SerializeField] private RocketConfigSO config;

    public event Action<float> OnFuelChanged;
    public event Action OnFuelEmpty;

    private float currentFuel;
    private bool isEmpty;

    public bool HasFuel => currentFuel > 0f;

    private void Start()
    {
        currentFuel = config != null ? config.MaxFuel : 100f;
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;
        if (!HasFuel) return;

        bool isSpacePressed = Input.GetKey(KeyCode.Space);
        bool isRotatePressed = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D);

        float burnAmount = 0f;
        if (isSpacePressed)
        {
            burnAmount += (config != null ? config.EngineBurnRate : 10f) * Time.deltaTime;
        }
        if (isRotatePressed)
        {
            burnAmount += (config != null ? config.SideBoostBurnRate : 5f) * Time.deltaTime;
        }

        if (burnAmount > 0f)
        {
            BurnFuel(burnAmount);
        }
    }

    private void BurnFuel(float amount)
    {
        if (isEmpty) return;

        float maxF = config != null ? config.MaxFuel : 100f;
        currentFuel = Mathf.Clamp(currentFuel - amount, 0f, maxF);
        
        float normalizedFuel = maxF > 0f ? currentFuel / maxF : 0f;
        OnFuelChanged?.Invoke(normalizedFuel);

        if (currentFuel <= 0f)
        {
            isEmpty = true;
            OnFuelEmpty?.Invoke();
            if (GameManager.Instance != null)
            {
                GameManager.Instance.PlayerDied();
            }
        }
    }

    public void AddFuel(float amount)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        float maxF = config != null ? config.MaxFuel : 100f;
        currentFuel = Mathf.Clamp(currentFuel + amount, 0f, maxF);

        float normalizedFuel = maxF > 0f ? currentFuel / maxF : 0f;
        OnFuelChanged?.Invoke(normalizedFuel);

        if (currentFuel > 0f)
        {
            isEmpty = false;
        }
    }

    public void ResetFuel()
    {
        currentFuel = config != null ? config.MaxFuel : 100f;
        isEmpty = false;
        OnFuelChanged?.Invoke(1f);
    }
}
