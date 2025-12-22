using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelUI : MonoBehaviour
{
    CollisionHandler collisionHandler;
    Movements movements;
    Fuel Fuel;

    [SerializeField] TextMeshProUGUI rocketHealth;
    [SerializeField] TextMeshProUGUI currentFuel;
    [SerializeField] TextMeshProUGUI healthEff;
    [SerializeField] TextMeshProUGUI currentThrust;
    [SerializeField] TextMeshProUGUI death;



    private void Awake()
    {
        collisionHandler = FindFirstObjectByType<CollisionHandler>();
        movements = FindFirstObjectByType<Movements>();
        Fuel = FindFirstObjectByType<Fuel>();
        death.text = "";
    }
    private void Update()
    {
        if ( !collisionHandler || !movements || !Fuel)
        {
            Debug.LogError("Missing component references in LevelUI");
        }

        if (!movements.RockeAlive())
        {
            death.text = "Rocket Destroyed! Restarting Level";
        }
        rocketHealth.text = "Rocket Health: " +  collisionHandler.currentHealth  ;
        currentFuel.text = "Current Fuel: " + Fuel.currentFuel; 
        healthEff.text = "Health Efficiency: " + collisionHandler.HealthEfficency();
        currentThrust.text = "Current Thrust: " + movements.currentThrust;

    }



}
