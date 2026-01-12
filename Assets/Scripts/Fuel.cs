using UnityEngine;

public class Fuel : MonoBehaviour
{
    [SerializeField] float burnRate = 5;
    float sideBoosterRate = 0.5f;
    
    public float maxfuel = 50f;
    public float currentFuel;


    private void Awake()
    {
        currentFuel = maxfuel;  
    }
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
