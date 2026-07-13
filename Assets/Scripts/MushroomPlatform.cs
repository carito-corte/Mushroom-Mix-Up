using UnityEngine;

/// <summary>
/// Controls the physical movement and state of individual hexagonal platforms.
/// Uses kinematic rigidbody sweeps to prevent physics clipping (tunneling) when players or NPCs stand on it.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MushroomPlatform : MonoBehaviour
{
    [Header("Platform Identity")]
    public MushroomColor myColor; // The assigned color of this specific platform

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 3.5f;

    // Component References
    private Rigidbody rb;

    // Spatial Coordinates
    private Vector3 originalPosition;
    private Vector3 sunkenPosition;
    private Vector3 targetPosition; // Corrected to standard camelCase naming convention

    void Start()
    {
        // Cache the Rigidbody component for physics operations
        rb = GetComponent<Rigidbody>();

        // CRITICAL SETUP: Kinematic mode allows the script to dictate movement mathematically 
        // while still forcing the physics engine to calculate sweeping collisions with characters standing on it.
        rb.isKinematic = true;

        // Establish the baseline coordinates and calculate the death-zone depth (3 units below the surface)
        originalPosition = transform.position;
        sunkenPosition = originalPosition + (Vector3.down * 3f);

        // Initialize the platform's destination to its current safe location
        targetPosition = originalPosition;
    }

    void FixedUpdate()
    {
        // PHYSICS LOOP: Calculate the incremental step needed to reach the target position.
        // We use FixedUpdate instead of Update to guarantee synchronization with Unity's internal physics tick rate.
        Vector3 nextPosition = Vector3.MoveTowards(rb.position, targetPosition, moveSpeed * Time.fixedDeltaTime);

        // Apply the movement. MovePosition physically pushes entities out of the way or carries them on top, 
        // unlike transform.position which teleports objects and breaks collision detection.
        rb.MovePosition(nextPosition);
    }

    /// <summary>
    /// Compares the platform's assigned color with the randomly chosen safe color.
    /// If there is a mismatch, the platform's target destination is set to the sunken hazard zone.
    /// </summary>
    /// <param name="safeColor">The color chosen by the GameManager as the safe zone for this round.</param>
    public void EvaluatePlatform(MushroomColor safeColor)
    {
        if (myColor != safeColor)
        {
            // Trigger the sinking mechanism for incorrect colors
            targetPosition = sunkenPosition;
        }
    }

    /// <summary>
    /// Restores the platform to its original vertical position for the next round.
    /// </summary>
    public void ResetPlatform()
    {
        targetPosition = originalPosition;
    }

    /// <summary>
    /// Dynamically adjusts the platform's vertical travel speed.
    /// Used by the GameManager to increase game difficulty and speed over time.
    /// </summary>
    /// <param name="newSpeed">The newly calculated speed multiplier.</param>
    public void SetMoveSpeed(float newSpeed)
    {
        moveSpeed = newSpeed;
    }
}