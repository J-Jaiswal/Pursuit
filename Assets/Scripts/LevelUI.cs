using System;
using UnityEngine;

public class LevelUI : MonoBehaviour
{

     float mainThrust_fuel = 1500f;
    float sideThrust_fuel = 100f;



    private void Update()
    {
        if (Input.GetKey(KeyCode.Space))
        {
            mainThrust_fuel -= 1f * Time.deltaTime;
        }
        else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D))
        {
            sideThrust_fuel -= 5f * Time.deltaTime;
        }  
        
        Debug.Log("Main Thrust Fuel: " + mainThrust_fuel);
        Debug.Log("Side Thrust Fuel: " + sideThrust_fuel);

    }



}
