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


    private Rigidbody rb;
    CollisionHandler collision;
    private void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        handleThrust();
        handleRotation();
    }


    void handleThrust()
    {
        if (collision.criticalDamage()) { return; }

        if (Input.GetKey(KeyCode.Space))
        {

            rb.AddRelativeForce(Vector3.up * thrust * Time.fixedDeltaTime);
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
        if (collision.criticalDamage()) { return; }

        if (Input.GetKey(KeyCode.A))
        {
            
            transform.Rotate(Vector3.forward * rotateSpeed * Time.fixedDeltaTime);
            if ( mainEngine != null && !mainEngine.isPlaying) mainEngine.Play();

            rightThrust.Play();
          
        }
        else if (Input.GetKey(KeyCode.D))
        {

            rb.freezeRotation = true;
            transform.Rotate(Vector3.forward * -rotateSpeed * Time.fixedDeltaTime);
            if (mainEngine != null && !mainEngine.isPlaying) mainEngine.Play();

            leftThrust.Play();
            rb.freezeRotation = false;

        }
        else
        {
            if ( leftThrust.isPlaying) leftThrust.Stop();
            if (rightThrust.isPlaying) rightThrust.Stop();
        }
    }
}
