using System;
using UnityEngine;
using UnityEngine.UI;

public class LevelUI : MonoBehaviour
{
    CollisionHandler collisionHandler;
    Movements movements;
    Fuel Fuel;

    [SerializeField] Text rocketHealt;
    [SerializeField] Text currentFuel;
    [SerializeField] Text healthEff;
    [SerializeField] Text currentThrust;
    [SerializeField] Text death;



    private void Awake()
    {
        collisionHandler = FindFirstObjectByType<CollisionHandler>();
        movements = FindFirstObjectByType<Movements>();
        Fuel = FindFirstObjectByType<Fuel>();
        death.text = "";
    }
    private void Update()
    {

        if (!movements.RockeAlive())
        {
            death.text = "Rocket Destroyed! Restarting Level";
        }
        rocketHealt.text = "Rocket Health: " +  collisionHandler.currentHealth  ;
        currentFuel.text = "Current Fuel: " + Fuel.currentFuel; 
        healthEff.text = "Health Efficiency: " + collisionHandler.HealthEfficency();
        currentFuel.text = "Current Thrust: " + movements.currentThrust;

    }



}
