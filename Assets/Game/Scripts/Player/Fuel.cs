using System;
using UnityEngine;

public class Fuel : MonoBehaviour
{
    [SerializeField] private RocketConfigSO config;

    public event Action<float> OnFuelChanged;
    public event Action OnFuelEmpty;

 
    /// Fired when fuel reaches zero. Movements subscribes to this to start the
    /// tumble death sequence — the tumble coroutine then calls PlayerDied after
    /// the animation plays. This is the ONLY death path that uses a tumble.
   
    public event Action OnFuelEmptyTumble;

    private float currentFuel;
    private bool isEmpty;
    private Movements movements; // BUG-02 FIX: cached to detect critical tumble state

    public bool HasFuel => currentFuel > 0f;

    private void Start()
    {
        currentFuel = config != null ? config.MaxFuel : 100f;
        movements   = GetComponent<Movements>(); // BUG-02 FIX
    }

    private void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;
        if (!HasFuel) return;

      
        // Movements.isCriticalTumbling is true for ~1.5s before PlayerDied fires,
        // and the player cannot control the rocket during this window anyway.
        if (movements != null && movements.IsCriticalTumbling) return;

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
            // Don't call PlayerDied here — fire the tumble event instead.
            // Movements will start the tumble coroutine and call PlayerDied
            // after the animation completes (fuel is the only tumble death).
            OnFuelEmptyTumble?.Invoke();
        }
    }

    
    /// Restores fuel to maximum and clears the empty flag.
    /// Called by PlayerRespawnHandler on checkpoint respawn.

    public void ResetFuel()
    {
        isEmpty     = false;
        currentFuel = config != null ? config.MaxFuel : 100f;
        OnFuelChanged?.Invoke(1f);
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
}
