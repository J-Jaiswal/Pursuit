using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelUI : MonoBehaviour
{
    CollisionHandler collisionHandler;
    Movements movements;
    Fuel fuel;

    [SerializeField] TMP_Text healthEff;
    [SerializeField] TMP_Text death;
    [SerializeField] Slider healthBar;
    [SerializeField] Slider fuelBar;
    [SerializeField] Slider thrustBar;

    //float currentHealth;
    //float currentThrust;
    //float currentFuel;  



    private void Awake()
    {
        collisionHandler = FindFirstObjectByType<CollisionHandler>();
        movements = FindFirstObjectByType<Movements>();
        fuel = FindFirstObjectByType<Fuel>();
        if (death != null)
            death.text = "";
        else
        {
            Debug.LogError("Death TextMeshPro is not assigned!");
        }

        //currentFuel = collisionHandler.currentHealth;   
        //currentHealth = fuel.currentFuel;   
        //currentThrust = movements.currentThrust;    

    }
    private void Update()
    {
      
        if (!movements.RockeAlive())
        {
            death.text = "Rocket Destroyed! Restarting Level";
        }

        healthEff.text = "Health Efficiency: " + collisionHandler.HealthEfficency();


        HealthBar(collisionHandler.currentHealth, collisionHandler.maxHealth);  
        FuelBar(fuel.currentFuel, fuel.maxfuel); 
        ThrustBar(movements.currentThrust, movements.maxThrust);  
    }


    public void HealthBar ( float currentHealth , float MaxHealth)
    {
        healthBar.value = currentHealth / MaxHealth;
    } 
    
    public void FuelBar ( float currentFuel , float MaxFuel)
    {
        fuelBar.value = currentFuel / MaxFuel;
    }

    public void ThrustBar ( float currentThrust , float MaxThrust)
    {
        thrustBar.value = currentThrust / MaxThrust;
    }   

}
