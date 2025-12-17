using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CollisionHandler: MonoBehaviour
{

    [SerializeField] float maxhealth = 10f;
    [SerializeField] float damageRate = 1f;
    [SerializeField] ParticleSystem crashEffect;
    float currentHealth;
    int currentSceneIndex ;
    bool criDamage = false;    
    Movements movement;
    private void Start()
    {
        currentHealth = maxhealth;
        movement = GetComponent<Movements>();
        currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
    }


    private void Update()
    {
        death();
    }


    private void OnCollisionEnter(Collision obj)
    {
        ContactPoint contact = obj.contacts[0];
        switch (obj.gameObject.tag)
        {
            case "Start":
                Debug.Log(" Starting point");
                break;
            case "Finish":
                LoadNextScene(currentSceneIndex);
                //Debug.Log(" Loading next level");
                break;
            default:
                Debug.Log(" Rocket crashed");
                Instantiate(crashEffect, contact.point, Quaternion.LookRotation(contact.normal));
                TakeDamge(damageRate);  
                break;
        }
    }

    void LoadNextScene(int sceneIndex)
    {
       int totalScenes =  SceneManager.sceneCountInBuildSettings;
 
        if ( sceneIndex < totalScenes)
        {
            SceneManager.LoadScene(sceneIndex + 1);
            //LoadNextScene(sceneIndex + 1);
            currentSceneIndex = sceneIndex + 1 ;
            Debug.Log(" Loading next level"); 


        }
        else if ( sceneIndex == totalScenes)
        {
            SceneManager.LoadScene(0);
        }
    }

    public bool criticalDamage ()
    {
        criDamage  = true;

        return criDamage;
    }

    void death() { 
    
        if ( currentHealth == 0f  )

        {
             movement.enabled = false;
            
            Debug.Log(" Rocket destroyed");
        }
    
    }
    void TakeDamge(float damage)
    {
        if (movement != null)
        {
            currentHealth -= damage;

            if(currentHealth < 5f)
            {
                criticalDamage();
                Debug.Log(" Critical Damage");
             
            }
            currentHealth = Mathf.Clamp(currentHealth, 0f,  maxhealth);
            Debug.Log($"{currentHealth}");
        }
    }
}
