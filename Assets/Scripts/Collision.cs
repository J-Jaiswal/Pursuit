using Unity.VisualScripting;
using UnityEngine;

public class CollisionHandler: MonoBehaviour
{

    [SerializeField] float maxhealth = 15f;
    [SerializeField] float damageRate = 1f;
    [SerializeField] float currentHealth;

    Movements movement;


    private void Start()
    {
        currentHealth = maxhealth;
        movement = GetComponent<Movements>();
    }


    private void Update()
    {
        if (currentHealth == 0f)
        {
          movement.enabled = false;
            Debug.Log(" Rocket destroyed");
        }
    }


    private void OnCollisionEnter(Collision obj)
    {
        switch (obj.gameObject.tag)
        {
            case "Start":
                Debug.Log(" Starting point");
                break;
            case "Finish":
                Debug.Log(" Complete the level");
                break;
            default:
                Debug.Log(" Rocket crashed");
                TakeDamge(damageRate);  
                break;
        }
    }

    void TakeDamge(float damage)
    {
        if (movement != null)
        {
            currentHealth -= damage;
            currentHealth = Mathf.Clamp(currentHealth, 0f,  maxhealth);
        }
    }
}
