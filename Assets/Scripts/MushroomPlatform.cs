using UnityEngine;

// Handles physics-aligned kinematic translation to prevent high-speed collision tunneling
[RequireComponent(typeof(Rigidbody))]
public class MushroomPlatform : MonoBehaviour
{
    public MushroomColor myColor;

    [SerializeField] private float moveSpeed = 3.5f;

    private Rigidbody rb;
    private Vector3 originalPosition;
    private Vector3 sunkenPosition;
    private Vector3 VectorTargetPosition;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // CRITICAL: Kinematic allows programmatic movement while maintaining solid physics sweeps
        rb.isKinematic = true;

        originalPosition = transform.position;
        sunkenPosition = originalPosition + Vector3.down * 3f;
        VectorTargetPosition = originalPosition;
    }

    // Moved to FixedUpdate to stay perfectly in sync with Unity's internal physics loop
    void FixedUpdate()
    {
        // Smoothly calculate next step and apply via Rigidbody sweeping instead of transform teleportation
        Vector3 nextPosition = Vector3.MoveTowards(rb.position, VectorTargetPosition, moveSpeed * Time.fixedDeltaTime);
        rb.MovePosition(nextPosition);
    }

    public void EvaluatePlatform(MushroomColor safeColor)
    {
        if (myColor != safeColor)
        {
            VectorTargetPosition = sunkenPosition;
        }
    }

    public void ResetPlatform()
    {
        VectorTargetPosition = originalPosition;
    }

    public void SetMoveSpeed(float newSpeed)
    {
        moveSpeed = newSpeed;
    }
}