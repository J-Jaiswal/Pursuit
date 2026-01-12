using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CollisionHandler: MonoBehaviour
{

    [SerializeField] float maxhealth = 10f;
    [SerializeField] float damageRate = 1f;
    [SerializeField] ParticleSystem crashEffect;
    [SerializeField] AudioSource damageAudio;

    public float currentHealth;
    int currentSceneIndex ;
    Movements movement;
    Fuel fuel;
    private void Awake()
    {
        currentHealth = maxhealth;
        movement = GetComponent<Movements>();
        fuel = GetComponent<Fuel>();
        currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
    }


    private void Update()
    {
        Death();
    }


    private void OnCollisionEnter(Collision obj)
    {
        if( !movement.RockeAlive() ) return;
        ContactPoint contact = obj.contacts[0];

        crashEffect.transform.position = contact.point;
        switch (obj.gameObject.tag)
        {
            case "Start":
                Debug.Log(" Starting point");
                break;
            case "Finish":
                LoadNextScene(currentSceneIndex);
                break;
            default:
                Debug.Log(" Rocket crashed");
                
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
        bool returnDamage = false ;
        if ( currentHealth < 5f )
        {
            returnDamage = true;
        }   
        return returnDamage;
    }

    void Death() { 
    
        if ( currentHealth == 0f  )

        {
             movement.enabled = false;     
            Debug.Log(" Rocket destroyed");
        }
    
    }

    public float HealthEfficency()
    {
        float currenthealthPer = currentHealth / maxhealth;


        return currenthealthPer;

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
       if( crashEffect != null) crashEffect.Play();
       if( damageAudio != null) damageAudio.Play();
    }
}
