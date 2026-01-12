using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelUI : MonoBehaviour
{
    CollisionHandler collisionHandler;
    Movements movements;
    Fuel fuel;

    [SerializeField] TMP_Text currentFuel;
    [SerializeField] TMP_Text healthEff;
    [SerializeField] TMP_Text currentThrust;
    [SerializeField] TMP_Text death;
    [SerializeField] TMP_Text rocketHealth;



    private void Awake()
    {
        collisionHandler = FindFirstObjectByType<CollisionHandler>();
        movements = FindFirstObjectByType<Movements>();
        fuel = FindFirstObjectByType<Fuel>();
        if (death != null)
            death.text = "";
        else
            Debug.LogError("Death TextMeshPro is not assigned!");
    }
    private void Update()
    {
        if (!collisionHandler || !movements || !fuel ||
       !rocketHealth || !death)
        {
        
            return;
        }
        Debug.Log("Rocket Active " + movements.RockeAlive());

        if (!movements.RockeAlive())
        {
            death.text = "Rocket Destroyed! Restarting Level";
        }
        rocketHealth.text = "Rocket Health: " +  collisionHandler.currentHealth.ToString();
        currentFuel.text = "Current Fuel: " + fuel.currentFuel;
        healthEff.text = "Health Efficiency: " + collisionHandler.HealthEfficency();
        currentThrust.text = "Current Thrust: " + movements.currentThrust;

    }

}
