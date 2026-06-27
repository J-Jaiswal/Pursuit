using UnityEngine;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [SerializeField] private Rigidbody playerRigidbody;

    private Vector3 respawnPosition;
    private bool hasCheckpoint;
    private int attemptCount = 1;

    public bool HasCheckpoint => hasCheckpoint;
    public int AttemptCount => attemptCount;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        GameManager.OnPlayerDied += HandlePlayerDied;
    }

    private void OnDestroy()
    {
        GameManager.OnPlayerDied -= HandlePlayerDied;
    }

    private void HandlePlayerDied()
    {
        attemptCount++;
    }

    public void SetCheckpoint(Vector3 position)
    {
        respawnPosition = position;
        hasCheckpoint = true;
    }

    public void RespawnPlayer()
    {
        if (!hasCheckpoint || playerRigidbody == null) return;

        // Reset positions
        playerRigidbody.transform.position = respawnPosition;
        playerRigidbody.transform.rotation = Quaternion.identity;

        // Reset physics velocities
        playerRigidbody.linearVelocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;

        // Fully restore player health and fuel
        CollisionHandler ch = playerRigidbody.GetComponent<CollisionHandler>();
        if (ch != null)
        {
            ch.ResetHealth();
        }

        Fuel fuel = playerRigidbody.GetComponent<Fuel>();
        if (fuel != null)
        {
            fuel.ResetFuel();
        }

        // Return game state to Playing
        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdateState(GameState.Playing);
        }
    }
}
