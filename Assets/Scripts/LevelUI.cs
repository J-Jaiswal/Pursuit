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
    //[SerializeField] Slider healthBar;
    //[SerializeField] Slider fuelBar;
    //[SerializeField] Slider thrustBar;

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

    }
    private void Update()
    {
      
        if (!movements.RockeAlive())
        {
            death.text = "Rocket Destroyed! Restarting Level";
        }

        healthEff.text = "Health Efficiency: " + collisionHandler.HealthEfficency();


    }


   

}
