using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// Handles the main player's physics, inputs, and state management.
/// Features enhanced arcade gravity, zero friction walls, smooth rotation, 
/// moving platform velocity inheritance, and dynamic audio feedback.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerPhysicsController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float jumpForce = 7.92f;
    [SerializeField] private float rotationSpeed = 10.0f;

    [Header("Arcade Physics")]
    [SerializeField] private float gravityScale = 3.0f;
    [SerializeField] private float groundCheckDistance = 0.15f;
    [SerializeField] private float seamBufferThreshold = 0.12f;

    [Header("Squish Effect (Flattening)")]
    [SerializeField] private float squishDuration = 2.0f;

    [Header("Audio SFX Assets")]
    [SerializeField] private AudioClip[] runSounds;
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip fallingScreamSound;
    [SerializeField] private AudioClip splashSound;
    [SerializeField] private AudioClip squishSound;
    [SerializeField] private AudioClip marioSquishVoice;

    [Header("Audio Settings & Thresholds")]
    [SerializeField] private float runStepInterval = 0.32f;
    [SerializeField] private float fallScreamYThreshold = -0.5f; // Altitude threshold to trigger the falling scream
    [SerializeField] private float minPitch = 0.88f;
    [SerializeField] private float maxPitch = 1.12f;

    // State Flags
    private bool isSquished = false;
    private bool isFallingScreamPlayed = false; // Prevents the scream audio from triggering multiple times per frame
    private bool jumpRequested = false;
    private bool isGrounded = false;
    private bool inWater = false;

    // Component References
    private Vector3 originalScale;
    private MushroomGameManager gameManager;
    private Rigidbody rb;
    private CapsuleCollider capsuleCollider;
    private Animator anim;
    private AudioSource sfxSource;

    // Input and Timers
    private Vector3 moveInput;
    private float seamBufferTimer = 0f;
    private float jumpDisableGroundTimer = 0f;
    private float footstepTimer = 0f;

    void Start()
    {
        // Cache external managers and local structural components
        gameManager = Object.FindAnyObjectByType<MushroomGameManager>();
        originalScale = transform.localScale;
        rb = GetComponent<Rigidbody>();

        // Limit depenetration velocity to prevent extreme physics explosions when colliders overlap
        rb.maxDepenetrationVelocity = 5.0f;
        capsuleCollider = GetComponent<CapsuleCollider>();

        // Lock rotation on X and Z axes to keep the character upright at all times
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        anim = GetComponentInChildren<Animator>();

        // Apply a zero-friction physics material to avoid sticking to walls while jumping
        if (capsuleCollider != null)
        {
            PhysicsMaterial slipperyMaterial = new PhysicsMaterial("Slippery")
            {
                staticFriction = 0f,
                dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
            capsuleCollider.material = slipperyMaterial;
        }

        // Initialize audio source and force spatial blend to 0 (2D sound) for consistent player feedback
        sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.spatialBlend = 0f;
    }

    void Update()
    {
        // Update basic animation states
        if (anim != null)
        {
            anim.SetBool("inWater", inWater);
            if (isSquished) anim.SetFloat("Speed", 0f);
        }

        // Suspend all input and logic processing if the player is dead or incapacitated
        if (inWater || isSquished) return;

        Keyboard currentKeyboard = Keyboard.current;

        if (currentKeyboard != null)
        {
            // 1. GROUND DETECTION LOGIC
            // Delay ground checking immediately after jumping to prevent false positives
            if (jumpDisableGroundTimer > 0f)
            {
                jumpDisableGroundTimer -= Time.deltaTime;
                isGrounded = false;
                seamBufferTimer = 0f;
            }
            else
            {
                // Calculate dynamic dimensions to cast a sphere precisely below the capsule
                float worldScaleY = transform.lossyScale.y;
                float worldScaleX = transform.lossyScale.x;
                float scaledRadius = capsuleCollider.radius * worldScaleX;
                float scaledHeight = capsuleCollider.height * worldScaleY;
                float localOffsetToBottomCenter = (scaledHeight * 0.5f) - scaledRadius;
                Vector3 worldColliderCenter = transform.position + (capsuleCollider.center * worldScaleY);
                Vector3 sphereCastOrigin = worldColliderCenter + (Vector3.down * localOffsetToBottomCenter);
                float checkRadius = scaledRadius * 0.9f;

                bool rayHit = Physics.SphereCast(sphereCastOrigin, checkRadius, Vector3.down, out RaycastHit hit, groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);

                // Handle ground detection with a seam buffer to ignore micro-gaps between platforms
                if (rayHit)
                {
                    isGrounded = true;
                    seamBufferTimer = 0f;
                    isFallingScreamPlayed = false; // Reset the scream lock when landing safely
                }
                else
                {
                    seamBufferTimer += Time.deltaTime;
                    if (seamBufferTimer > seamBufferThreshold) isGrounded = false;
                }
            }

            // 2. FREE FALL AUDIO DETECTION
            // Triggers a specific audio clip if the player falls below a threshold with negative vertical velocity
            if (!isGrounded && !inWater && !isFallingScreamPlayed && rb.linearVelocity.y < -0.1f)
            {
                if (transform.position.y < fallScreamYThreshold)
                {
                    if (sfxSource != null && fallingScreamSound != null)
                    {
                        sfxSource.pitch = 1.0f; // Force normal pitch for the voice
                        sfxSource.PlayOneShot(fallingScreamSound, 0.8f);
                        isFallingScreamPlayed = true;
                    }
                }
            }

            // 3. CINEMATIC STATE LOCK
            // Ignore movement commands during intros or game over sequences
            if (gameManager != null && (gameManager.currentState == GameState.Intro || gameManager.currentState == GameState.GameOver))
            {
                moveInput = Vector3.zero;
                if (anim != null)
                {
                    anim.SetFloat("Speed", 0f);
                    anim.SetBool("isGrounded", isGrounded);
                }
                return;
            }

            // 4. INPUT READING
            float moveX = 0f;
            float moveZ = 0f;

            if (currentKeyboard.wKey.isPressed || currentKeyboard.upArrowKey.isPressed) moveZ = 1f;
            if (currentKeyboard.sKey.isPressed || currentKeyboard.downArrowKey.isPressed) moveZ = -1f;
            if (currentKeyboard.aKey.isPressed || currentKeyboard.leftArrowKey.isPressed) moveX = -1f;
            if (currentKeyboard.dKey.isPressed || currentKeyboard.rightArrowKey.isPressed) moveX = 1f;

            // Normalize input vector to prevent faster diagonal movement
            moveInput = new Vector3(moveX, 0f, moveZ).normalized;

            // Register jump intent
            if (currentKeyboard.spaceKey.wasPressedThisFrame && isGrounded)
            {
                jumpRequested = true;
                isGrounded = false;
                jumpDisableGroundTimer = 0.15f;
            }
        }

        // Update locomotion animations
        if (anim != null)
        {
            anim.SetFloat("Speed", moveInput.magnitude);
            anim.SetBool("isGrounded", isGrounded);
        }

        // 5. FOOTSTEP AUDIO RANDOMIZATION LOOP
        // Plays organic footstep sounds only when grounded, moving, and the game is active
        if (isGrounded && moveInput.magnitude > 0.1f && gameManager != null && gameManager.currentState == GameState.Playing)
        {
            footstepTimer -= Time.deltaTime;
            if (footstepTimer <= 0f)
            {
                if (sfxSource != null && runSounds != null && runSounds.Length > 0)
                {
                    // Select a random audio clip from the array and randomize pitch/volume for variety
                    int diceRollClip = Random.Range(0, runSounds.Length);
                    AudioClip selectedStepClip = runSounds[diceRollClip];

                    sfxSource.pitch = Random.Range(minPitch, maxPitch);
                    float randomVolume = Random.Range(0.9f, 1.2f);

                    if (selectedStepClip != null) sfxSource.PlayOneShot(selectedStepClip, randomVolume);
                }
                footstepTimer = runStepInterval; // Reset timer for the next step
            }
        }
        else
        {
            footstepTimer = 0f; // Instant sound playback on next movement start
        }
    }

    void FixedUpdate()
    {
        // Suspend physics operations if submerged
        if (inWater)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        // Suspend lateral physics operations if flattened (retain gravity)
        if (isSquished)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        // 1. ARCADE GRAVITY
        // Apply extra downward force when airborne to make jumps feel heavier and less floaty
        if (!isGrounded)
        {
            Vector3 extraGravityForce = Physics.gravity * (gravityScale - 1f);
            rb.AddForce(extraGravityForce, ForceMode.Acceleration);
        }

        // Cinematic safety lock for physics
        if (gameManager != null && (gameManager.currentState == GameState.Intro || gameManager.currentState == GameState.GameOver))
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        // 2. MOVEMENT AND VELOCITY INHERITANCE
        Vector3 targetVelocity = moveInput * moveSpeed;
        float finalVelocityY = rb.linearVelocity.y;

        // If grounded, inherit the vertical velocity of the platform beneath to avoid jittering when it sinks
        if (isGrounded)
        {
            float worldScaleY = transform.lossyScale.y;
            float worldScaleX = transform.lossyScale.x;
            float scaledRadius = capsuleCollider.radius * worldScaleX;
            float scaledHeight = capsuleCollider.height * worldScaleY;
            float localOffsetToBottomCenter = (scaledHeight * 0.5f) - scaledRadius;
            Vector3 worldColliderCenter = transform.position + (capsuleCollider.center * worldScaleY);
            Vector3 sphereCastOrigin = worldColliderCenter + (Vector3.down * localOffsetToBottomCenter);
            float checkRadius = scaledRadius * 0.9f;

            if (Physics.SphereCast(sphereCastOrigin, checkRadius, Vector3.down, out RaycastHit hit, groundCheckDistance + 0.05f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != null) finalVelocityY = hit.rigidbody.linearVelocity.y;
            }
        }

        // Apply final calculated velocities
        rb.linearVelocity = new Vector3(targetVelocity.x, finalVelocityY, targetVelocity.z);

        // 3. SMOOTH ROTATION
        // Interpolate character heading towards the input direction
        if (moveInput.magnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveInput);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }

        // 4. JUMP EXECUTION
        if (jumpRequested)
        {
            if (sfxSource != null && jumpSound != null)
            {
                sfxSource.pitch = 1.0f; // Maintain original jump sound integrity
                sfxSource.PlayOneShot(jumpSound, 0.55f);
            }

            // Shift position slightly upwards to cleanly disconnect from ground colliders
            rb.position += Vector3.up * 0.06f;

            // Nullify current vertical velocity before applying the jump impulse to ensure consistent jump heights
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
            jumpRequested = false;
        }
    }

    /// <summary>
    /// Processes physical collisions with other entities to determine squish logic.
    /// Analyzes vertical and horizontal spatial relationships upon contact.
    /// </summary>
    private void OnCollisionStay(Collision collision)
    {
        if (isSquished) return;
        if (!isGrounded) return; // Anvils require a hard surface beneath to compress an object

        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("NPC"))
        {
            float heightDifference = collision.transform.position.y - transform.position.y;
            Vector3 horizontalOffset = collision.transform.position - transform.position;
            horizontalOffset.y = 0f;
            float horizontalDistance = horizontalOffset.magnitude;

            // SCENARIO 1: "Goomba Stomp" (Direct top-down compression)
            // If the opposing entity is clearly above the shoulders and centered over the head
            if (heightDifference > 0.45f && horizontalDistance < 0.45f)
            {
                GetSquished();
                return;
            }

            // SCENARIO 2: Lateral crushing (Platform shifting mechanics)
            // Checks individual contact points to detect pressure from moving side boundaries
            if (heightDifference > 0.20f && horizontalDistance < 0.32f)
            {
                foreach (ContactPoint contact in collision.contacts)
                {
                    if (contact.point.y > transform.position.y + 0.15f)
                    {
                        GetSquished();
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Detects entry into hazardous volumes (e.g., water).
    /// Halts player physics entirely and initiates the game over sequence.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.name.ToLower().Contains("water") || other.CompareTag("Water"))
        {
            if (inWater) return;
            inWater = true;

            // Trigger splash audio event
            if (sfxSource != null && splashSound != null)
            {
                sfxSource.pitch = 1.0f;
                sfxSource.PlayOneShot(splashSound, 0.50f);
            }

            // Disable all physics calculations to freeze the character in the fluid
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
            if (capsuleCollider != null) capsuleCollider.enabled = false;

            // Notify the main game loop to trigger defeat conditions
            MushroomGameManager manager = Object.FindAnyObjectByType<MushroomGameManager>();
            if (manager != null) manager.TriggerGameOver(this.transform);
        }
    }

    /// <summary>
    /// Delays the spawn of the rescue entity (Blooper) after falling into the water.
    /// </summary>
    private IEnumerator WaterDeathSequence()
    {
        yield return new WaitForSeconds(1.0f);
        MushroomGameManager manager = Object.FindAnyObjectByType<MushroomGameManager>();
        if (manager != null && manager.blooperPrefab != null)
        {
            Vector3 spawnPos = transform.position + new Vector3(15f, 0, 0);
            GameObject blooper = Instantiate(manager.blooperPrefab, spawnPos, Quaternion.identity);
            blooper.GetComponent<BlooperPlaceholder>().StartCapture(this.transform);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.name.ToLower().Contains("water") || other.CompareTag("Water"))
        {
            inWater = false;
        }
    }

    public void GetSquished()
    {
        if (isSquished) return;
        StartCoroutine(SquishRoutine());
    }

    /// <summary>
    /// Asynchronous routine that mathematically flattens the character geometry.
    /// Plays layered impact and vocal audio, shrinks colliders, and restores size over time.
    /// </summary>
    private IEnumerator SquishRoutine()
    {
        isSquished = true;
        moveInput = Vector3.zero;

        // LAYERED AUDIO: Fire physical impact crunch and vocal reaction simultaneously
        if (sfxSource != null)
        {
            sfxSource.pitch = 1.0f;
            if (squishSound != null) sfxSource.PlayOneShot(squishSound, 0.70f);
            if (marioSquishVoice != null) sfxSource.PlayOneShot(marioSquishVoice, 0.85f);
        }

        // Hard stop any lingering kinetic energy
        if (GetComponent<Rigidbody>() != null)
        {
            GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        }

        // Store standard geometric scale boundaries
        float originalRadius = capsuleCollider.radius;
        float originalHeight = capsuleCollider.height;

        // Shrink the physical collision volume significantly to prevent geometric clipping
        capsuleCollider.radius = originalRadius * 0.2f;
        capsuleCollider.height = originalHeight * 0.15f;

        // Flatten the visual 3D mesh model down into a pancake layout
        transform.localScale = new Vector3(originalScale.x * 1.2f, originalScale.y * 0.15f, originalScale.z * 1.2f);

        // Maintain flattened state
        yield return new WaitForSeconds(squishDuration);

        // LERP RECONSTRUCTION: Gradually interpolate scale values back to their starting dimensions
        float timer = 0f;
        while (timer < 0.3f)
        {
            timer += Time.deltaTime;
            float progress = timer / 0.3f;
            float previousHeight = capsuleCollider.height;

            transform.localScale = Vector3.Lerp(transform.localScale, originalScale, progress);
            capsuleCollider.radius = Mathf.Lerp(originalRadius * 0.2f, originalRadius, progress);
            capsuleCollider.height = Mathf.Lerp(originalHeight * 0.15f, originalHeight, progress);

            // Shift position upwards relative to the expanding height to prevent falling through the floor
            float heightGrowth = (capsuleCollider.height - previousHeight) * transform.lossyScale.y;
            rb.position += new Vector3(0f, heightGrowth * 0.5f, 0f);

            yield return null;
        }

        // Hard lock exact dimensions to base values to eliminate floating point errors
        transform.localScale = originalScale;
        capsuleCollider.radius = originalRadius;
        capsuleCollider.height = originalHeight;
        isSquished = false;
    }
}