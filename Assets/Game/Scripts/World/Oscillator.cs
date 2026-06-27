using UnityEngine;

public class Oscillator : MonoBehaviour
{

    [SerializeField] Vector3 movementVector;
    [SerializeField] float movementSpeed;    
    Vector3 startingPos;
    Vector3 endingPos;
    float movementFactor;
    void Start()
    {
        startingPos = transform.position;
        endingPos = transform.position + movementVector;    
    }

    void Update()
    {
        movementFactor =  Mathf.PingPong(Time.time * movementSpeed, 1f);
        transform.position = Vector3.Lerp(startingPos, endingPos,movementFactor );

    }
}
