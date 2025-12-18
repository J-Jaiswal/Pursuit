using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class Movements : MonoBehaviour
{
    [SerializeField] private float thrust = 1500f;
    [SerializeField] private float rotateSpeed = 100f;
    [SerializeField] ParticleSystem leftThrust ;
    [SerializeField] ParticleSystem rightThrust;
    [SerializeField] ParticleSystem mainThrust;
    [SerializeField] AudioSource mainEngine;
    float maxThrust = 1500f;    
    float currentThrust = 0f;
    float thrustSmoothing = 3f;


    private Rigidbody rb;
    CollisionHandler collision;
    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        collision = GetComponent<CollisionHandler>();
        rb.freezeRotation = true;
    }

    private void FixedUpdate()
    {

        if ( collision != null && collision.criticalDamage()) {
            rb.freezeRotation = false;
            Debug.Log("Critical Damage - No Movement");
        } else {
            handleThrust();
            handleRotation();
        }
        
    }


    void handleThrust()
    {

        
        float targetThrust = Input.GetKey(KeyCode.Space) ? maxThrust : 0f;
     
        currentThrust = Mathf.Lerp(currentThrust , targetThrust , Time.fixedDeltaTime * thrustSmoothing);
        rb.AddRelativeForce(Vector3.up * currentThrust * Time.fixedDeltaTime);


    

        if (Input.GetKey(KeyCode.Space))
        {

            if (mainThrust != null && !mainThrust.isPlaying) mainThrust.Play();
            if (mainEngine != null && !mainEngine.isPlaying) mainEngine.Play();
        }
        else
        {
            if (mainThrust != null && mainThrust.isPlaying) mainThrust.Stop();
            if (mainEngine != null && mainEngine.isPlaying) mainEngine.Stop();
        }

    }

    void handleRotation()
    {


        if (Input.GetKey(KeyCode.A))
        {
            
            transform.Rotate(Vector3.forward * rotateSpeed * Time.fixedDeltaTime);
            if ( mainEngine != null && !mainEngine.isPlaying) mainEngine.Play();

            rightThrust.Play();
          
        }
        else if (Input.GetKey(KeyCode.D))
        {

            transform.Rotate(Vector3.forward * -rotateSpeed * Time.fixedDeltaTime);
            if (mainEngine != null && !mainEngine.isPlaying) mainEngine.Play();

            leftThrust.Play();
          
        }
        else
        {
            if ( leftThrust.isPlaying) leftThrust.Stop();
            if (rightThrust.isPlaying) rightThrust.Stop();
        }
    }
}
