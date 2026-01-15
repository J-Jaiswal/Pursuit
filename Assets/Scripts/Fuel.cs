using UnityEngine;

public class Fuel : MonoBehaviour
{
    [SerializeField] float engineBurnRate = 1.5f;
    [SerializeField] float sideBoostBurnRate = 0.5f;
    
    public float maxFuel = 50f;
    public float currentFuel;


    private void Awake()
    {
        currentFuel = maxFuel;  
    }
    void Start()
    {
      currentFuel = maxFuel;
    }

    void Update()
    {

        if ( currentFuel <= 0) return;
        
            if (Input.GetKeyDown(KeyCode.Space))
            {
                currentFuel -= engineBurnRate;
                Debug.Log("(Main THrust) Current Fuel: " + currentFuel);
            }
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D))
            {
                currentFuel -= sideBoostBurnRate;
                Debug.Log("(Side thrust)Current Fuel: " + currentFuel);
            }
          currentFuel = Mathf.Clamp(currentFuel, 0, maxFuel);
           
        

       
    }
}
