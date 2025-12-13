using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CollisionHandler: MonoBehaviour
{

    [SerializeField] float maxhealth = 15f;
    [SerializeField] float damageRate = 1f;
    [SerializeField] float currentHealth;
    int currentSceneIndex = 0;
   
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
                LoadNextScene(currentSceneIndex);
                Debug.Log(" Loading next level");
                break;
            default:
                Debug.Log(" Rocket crashed");
                TakeDamge(damageRate);  
                break;
        }
    }

    void LoadNextScene(int sceneIndex)
    {
        if ( sceneIndex < 3)

        {
            LoadNextScene(sceneIndex + 1);
            currentSceneIndex = sceneIndex +1 ;

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
