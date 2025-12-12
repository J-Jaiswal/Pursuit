using UnityEngine;

public class Fuel : MonoBehaviour
{
    [SerializeField] float maxfuel;
    [SerializeField] float burnRate;
    
     
    float sideBoosterRate = 0.5f;
    float currentFuel;

    void Start()
    {
      currentFuel = maxfuel;
    }

    void Update()

    {
        if (currentFuel > 0)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                currentFuel -= burnRate;
                Debug.Log("(Main THrust) Current Fuel: " + currentFuel);
            }
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.B))
            {
                currentFuel -= burnRate * sideBoosterRate;
                Debug.Log("(Side thrust)Current Fuel: " + currentFuel);
            }
          currentFuel = Mathf.Clamp(currentFuel, 0, maxfuel);
           
        }

       
    }
}
