using UnityEngine;

public class Oscillator : MonoBehaviour
{
    [Tooltip("Direction and distance of oscillation in local space.")]
    [SerializeField] private Vector3 movementVector;

    [Tooltip("Speed of the back-and-forth oscillation.")]
    [Range(0.01f, 10f)]
    [SerializeField] private float movementSpeed = 1f;

    private Vector3 startingPos;
    private Vector3 endingPos;
    private float movementFactor;

    private void Start()
    {
        startingPos = transform.position;
        endingPos   = transform.position + movementVector;
    }

    private void Update()
    {
      
        if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameState.Playing) return;

        movementFactor    = Mathf.PingPong(Time.time * movementSpeed, 1f);
        transform.position = Vector3.Lerp(startingPos, endingPos, movementFactor);
    }
}

