using UnityEngine;
using UnityEngine.InputSystem;

// Features enhanced arcade gravity, zero friction, and smooth input-based rotation mapping
[RequireComponent(typeof(Rigidbody))]
public class PlayerPhysicsController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float jumpForce = 5.2f;
    [SerializeField] private float rotationSpeed = 10.0f; // Added: Determines how fast the player faces the movement direction

    [Header("Arcade Physics")]
    [SerializeField] private float gravityScale = 3.5f;
    [SerializeField] private float groundCheckDistance = 1.05f;

    private Rigidbody rb;
    private Vector3 moveInput;
    private bool jumpRequested = false;
    private bool isGrounded = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        if (GetComponent<Collider>() != null)
        {
            PhysicsMaterial slipperyMaterial = new PhysicsMaterial("Slippery")
            {
                staticFriction = 0f,
                dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
            GetComponent<Collider>().material = slipperyMaterial;
        }
    }

    void Update()
    {
        Keyboard currentKeyboard = Keyboard.current;

        if (currentKeyboard != null)
        {
            isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);

            float moveX = 0f;
            float moveZ = 0f;

            if (currentKeyboard.wKey.isPressed || currentKeyboard.upArrowKey.isPressed) moveZ = 1f;
            if (currentKeyboard.sKey.isPressed || currentKeyboard.downArrowKey.isPressed) moveZ = -1f;
            if (currentKeyboard.aKey.isPressed || currentKeyboard.leftArrowKey.isPressed) moveX = -1f;
            if (currentKeyboard.dKey.isPressed || currentKeyboard.rightArrowKey.isPressed) moveX = 1f;

            moveInput = new Vector3(moveX, 0f, moveZ).normalized;

            if (currentKeyboard.spaceKey.wasPressedThisFrame && isGrounded && Mathf.Abs(rb.linearVelocity.y) < 0.1f)
            {
                jumpRequested = true;
            }
        }
    }

    void FixedUpdate()
    {
        // 1. Horizontal movement execution
        Vector3 targetVelocity = moveInput * moveSpeed;
        rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);

        // 2. Added: Smoothly rotate the player's transform toward the current movement vector
        if (moveInput.magnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveInput);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }

        // 3. Arcade Gravity calculation
        if (!isGrounded)
        {
            Vector3 extraGravityForce = Physics.gravity * (gravityScale - 1f);
            rb.AddForce(extraGravityForce, ForceMode.Acceleration);
        }

        // 4. Jump execution
        if (jumpRequested)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            jumpRequested = false;
        }
    }
}