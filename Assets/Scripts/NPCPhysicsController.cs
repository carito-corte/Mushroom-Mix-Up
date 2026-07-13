using UnityEngine;
using System.Collections;

/// <summary>
/// Controls NPC movement, physics, decision-making AI, and contextual audio feedback.
/// Includes arcade-style gravity modification and an advanced audio randomization system.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class NPCPhysicsController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float baseMoveSpeed = 3.2f;
    [SerializeField] private float rotationSpeed = 10.0f;
    [SerializeField] private float jumpForce = 23.76f;

    [Header("Arcade Physics (Jump)")]
    [SerializeField] private float gravityScale = 3.0f;
    [SerializeField] private float groundCheckDistance = 0.3f;
    [SerializeField] private float seamBufferThreshold = 0.2f;

    [Header("AI Personality")]
    [SerializeField] private float targetRadiusOffset = 0.45f;
    [SerializeField] private float aggroRadius = 1.0f;

    [Header("Squish Effect (Arepa)")]
    [SerializeField] private float squishDuration = 2.0f;
    private bool isSquished = false;
    private Vector3 originalScale;

    [Header("AI Decision Timers")]
    [SerializeField] private float minDecisionTime = 0.2f;
    [SerializeField] private float maxDecisionTime = 0.5f;

    [Header("Mushroom SFX Assets")]
    [SerializeField] private AudioClip[] runSounds;
    [SerializeField] private AudioClip jumpSound;
    [SerializeField] private AudioClip fallingScreamSound;
    [SerializeField] private AudioClip splashSound;
    [SerializeField] private AudioClip squishSound;
    [SerializeField] private AudioClip voiceSound;

    [Header("Audio Settings & Thresholds")]
    [SerializeField] private float runStepInterval = 0.35f;
    [SerializeField] private float fallScreamYThreshold = -0.5f;
    [SerializeField] private float minPitch = 0.88f;
    [SerializeField] private float maxPitch = 1.12f;

    // Core Components
    private Rigidbody rb;
    private CapsuleCollider capsuleCollider;
    private Animator anim;

    // Movement Vectors and Logic Flags
    private Vector3 moveInput;
    private Vector3 targetMoveInput;
    private bool inWater = false;
    private bool isGrounded = false;
    private bool wantsToJump = false;

    // AI Targeting Systems
    private MushroomGameManager gameManager;
    private Transform targetPlatform;
    private Vector3 specificTargetPoint;

    // AI Memory Variables
    private MushroomColor lastKnownSafeColor = MushroomColor.Black;
    private float actualMoveSpeed;
    private float pushBias;
    private float decisionTimer = 0f;
    private float seamBufferTimer = 0f;

    // Audio Control Variables
    private AudioSource sfxSource;
    private float footstepTimer = 0f;
    private bool isFallingScreamPlayed = false;

    void Start()
    {
        // Cache original transformation and structural component references
        originalScale = transform.localScale;
        rb = GetComponent<Rigidbody>();
        rb.maxDepenetrationVelocity = 5.0f; // Prevents clipping out of geometry at high velocities
        capsuleCollider = GetComponent<CapsuleCollider>();
        anim = GetComponentInChildren<Animator>();

        // Lock rotation axes to prevent the capsule from tipping over during physics interactions
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // Establish connection to the game loop manager
        gameManager = Object.FindAnyObjectByType<MushroomGameManager>();

        // Randomize speed and push direction slightly to give each NPC unique behavioral variance
        actualMoveSpeed = baseMoveSpeed * Random.Range(0.9f, 1.1f);
        pushBias = (Random.value > 0.5f) ? 0.6f : -0.6f;

        // Apply a zero-friction physics material to prevent NPCs from sticking to platform walls
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

        // Initialize and configure the local dedicated AudioSource component
        sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();

        // Forced to 0f (2D Sound) to match arcade mixing rules where all entities sound equidistant
        sfxSource.spatialBlend = 0f;
    }

    void Update()
    {
        // Continuously poll ground physics data
        CheckGrounded();

        // CONTEXTUAL FREE FALL DETECTION: Triggers a scream if falling past the height threshold
        if (!isGrounded && !inWater && !isFallingScreamPlayed && rb.linearVelocity.y < -0.1f)
        {
            if (transform.position.y < fallScreamYThreshold)
            {
                if (sfxSource != null && fallingScreamSound != null)
                {
                    sfxSource.pitch = 1.0f; // Force original vocal pitch
                    sfxSource.PlayOneShot(fallingScreamSound, 0.85f);
                    isFallingScreamPlayed = true; // Lock flag to prevent audio stuttering frame by frame
                }
            }
        }

        // GLOBAL GAME STATE LOCK: Halts all inputs if the game is in Intro or GameOver state
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

        // Safety validation to regain reference if the GameManager resets
        if (this is NPCPhysicsController && gameManager == null)
        {
            gameManager = Object.FindAnyObjectByType<MushroomGameManager>();
        }

        // Update basic animator parameter fields
        if (anim != null)
        {
            anim.SetBool("inWater", inWater);
            if (isSquished) anim.SetFloat("Speed", 0f);
        }

        // Complete state bypass if dead or currently flattened
        if (inWater || isSquished) return;

        CheckGrounded();

        // TARGETING LOGIC: If target is lost, acquire a random available platform
        if (targetPlatform == null && gameManager != null && gameManager.allPlatforms != null)
        {
            foreach (MushroomPlatform platform in gameManager.allPlatforms)
            {
                targetPlatform = platform.transform;
                if (Random.value > 0.4f) break; // Adds pathfinding entropy
            }

            if (targetPlatform != null)
            {
                // Calculate a specific offset target point inside the platform radius to avoid stacking
                Vector2 randomCircle = Random.insideUnitCircle * targetRadiusOffset;
                specificTargetPoint = targetPlatform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
            }
        }

        // REACTION LOGIC: Recalculate safe destination immediately when the GameManager color changes
        if (gameManager != null)
        {
            MushroomColor currentManagerColor = gameManager.GetCurrentSafeColor();

            if (currentManagerColor != lastKnownSafeColor)
            {
                lastKnownSafeColor = currentManagerColor;
                FindSafePlatform(currentManagerColor);

                // Low random chance to hop reactively when a new safe target color is picked
                if (isGrounded && Random.value < 0.3f)
                {
                    wantsToJump = true;
                }
            }
        }

        // DECISION LOOP TIMER: Forces recalculation of spatial AI tactics based on randomized intervals
        decisionTimer -= Time.deltaTime;
        if (decisionTimer <= 0f && targetPlatform != null)
        {
            decisionTimer = Random.Range(minDecisionTime, maxDecisionTime);
            CalculateHumanLogic();
        }

        // Smooth spatial velocity changes to emulate organic movement acceleration
        moveInput = Vector3.Lerp(moveInput, targetMoveInput, Time.deltaTime * 15f);

        if (moveInput.magnitude < 0.02f)
        {
            moveInput = Vector3.zero;
        }

        if (anim != null)
        {
            anim.SetFloat("Speed", moveInput.magnitude);
            anim.SetBool("isGrounded", isGrounded);
        }

        // ADVANCED AUDIO RANDOMIZATION FOOTSTEPS LOOP (DICE ROLL IMPLEMENTATION)
        if (isGrounded && moveInput.magnitude > 0.1f && gameManager != null && gameManager.currentState == GameState.Playing)
        {
            footstepTimer -= Time.deltaTime;
            if (footstepTimer <= 0f)
            {
                if (sfxSource != null && runSounds != null && runSounds.Length > 0)
                {
                    // DICE ROLL 1: Pick a random sound clip asset out of the array
                    int diceRollClip = Random.Range(0, runSounds.Length);
                    AudioClip selectedStepClip = runSounds[diceRollClip];

                    // DICE ROLL 2: Randomize audio pitch dynamically to prevent acoustic brain fatigue
                    sfxSource.pitch = Random.Range(minPitch, maxPitch);

                    // DICE ROLL 3: Microvariance on volume for organic footfall feedback
                    float randomVolume = Random.Range(0.80f, 0.90f);

                    if (selectedStepClip != null) sfxSource.PlayOneShot(selectedStepClip, randomVolume);
                }
                footstepTimer = runStepInterval; // Reset running timer sequence
            }
        }
        else
        {
            footstepTimer = 0f; // Instant footstep execution upon starting movement next frame
        }
    }

    /// <summary>
    /// Processes combat and navigation AI priorities. 
    /// Handles platform routing and player pushing/aggro vectors.
    /// </summary>
    private void CalculateHumanLogic()
    {
        Vector3 directionToSafePoint = (specificTargetPoint - transform.position);
        directionToSafePoint.y = 0;

        bool isAttacking = false;
        float distToPlatformCenter = Vector3.Distance(transform.position, targetPlatform.position);

        // STRATEGY: If far away from safety, prioritize pure navigation routing
        if (distToPlatformCenter > 0.7f)
        {
            targetMoveInput = (targetPlatform.position - transform.position).normalized;
            isAttacking = false;

            // Jump to clear platform gap seams if moving far enough away
            if (isGrounded && distToPlatformCenter > 0.85f)
            {
                if (Random.value < 0.6f)
                {
                    wantsToJump = true;
                }
            }
        }
        else
        {
            // STRATEGY: If safe inside a platform, search for nearby entities to push out of bounds
            Collider[] hitColliders = Physics.OverlapSphere(transform.position, aggroRadius);
            Transform closestTarget = null;
            float closestDistance = Mathf.Infinity;

            foreach (Collider hit in hitColliders)
            {
                if (hit.transform == this.transform) continue;
                if (hit.CompareTag("Player") || hit.CompareTag("NPC"))
                {
                    float dist = Vector3.Distance(transform.position, hit.transform.position);
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        closestTarget = hit.transform;
                    }
                }
            }

            // If an enemy is in push range, calculate combat trajectory vectors with push bias offsets
            if (closestTarget != null)
            {
                Vector3 vectorToTarget = (closestTarget.position - transform.position);
                vectorToTarget.y = 0;

                Vector3 sideStep = Vector3.Cross(vectorToTarget.normalized, Vector3.up);
                Vector3 finalAttackVector = vectorToTarget.normalized + (sideStep * pushBias);

                targetMoveInput = finalAttackVector.normalized;
                isAttacking = true;

                // Occasional defensive jump during close-quarter engagement
                if (isGrounded && distToPlatformCenter < 0.3f && Random.value < 0.1f)
                {
                    wantsToJump = true;
                }
            }
            else
            {
                // Idle roaming toward specific assigned safety coordinates
                targetMoveInput = directionToSafePoint.normalized;
            }
        }

        // Hard brake dead-zone logic to prevent micro-stuttering at target center
        if (!isAttacking && directionToSafePoint.magnitude < 0.2f)
        {
            targetMoveInput = Vector3.zero;
        }
    }

    void FixedUpdate()
    {
        // Cancel all momentum loops if falling into fluid volume
        if (inWater)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        // Cancel horizontal movement if squished into a pancake (arepa)
        if (isSquished)
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        // CUSTOM GRAVITY SCALING: Multiplies standard engine acceleration for responsive arcade falling
        if (!isGrounded)
        {
            Vector3 extraGravityForce = Physics.gravity * (gravityScale - 1f);
            rb.AddForce(extraGravityForce, ForceMode.Acceleration);
        }

        // State lock on physical velocity during UI cinemática routines
        if (gameManager != null && (gameManager.currentState == GameState.Intro || gameManager.currentState == GameState.GameOver))
        {
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            return;
        }

        // Apply target tracking physics forces on the horizontal plane
        Vector3 targetVelocity = moveInput * actualMoveSpeed;
        rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);

        // Smoothly interpolate heading rotation angle over time towards input trajectory vectors
        if (moveInput.magnitude > 0.01f)
        {
            Vector3 lookDirection = moveInput;
            lookDirection.y = 0f;

            if (lookDirection.magnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
            }
        }

        // Process jump impulse instructions
        if (wantsToJump && isGrounded)
        {
            if (sfxSource != null && jumpSound != null)
            {
                sfxSource.pitch = 1.0f; // Clamp jump to original frequency
                sfxSource.PlayOneShot(jumpSound, 0.6f);
            }

            rb.position += Vector3.up * 0.06f; // Lift slightly out of ground check range to clear seams cleanly
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
            wantsToJump = false;
        }
    }

    /// <summary>
    /// Executes complex SphereCast profiling to check ground alignment.
    /// Employs a time-based seam buffer threshold to stop micro-ground loss when crossing platform borders.
    /// </summary>
    private void CheckGrounded()
    {
        float worldScaleY = transform.lossyScale.y;
        float worldScaleX = transform.lossyScale.x;
        float scaledRadius = capsuleCollider.radius * worldScaleX;
        float scaledHeight = capsuleCollider.height * worldScaleY;

        float localOffsetToBottomCenter = (scaledHeight * 0.5f) - scaledRadius;
        Vector3 worldColliderCenter = transform.position + (capsuleCollider.center * worldScaleY);
        Vector3 sphereCastOrigin = worldColliderCenter + (Vector3.down * localOffsetToBottomCenter);
        float checkRadius = scaledRadius * 0.9f;

        bool rayHit = Physics.SphereCast(sphereCastOrigin, checkRadius, Vector3.down, out RaycastHit hit, groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);

        if (rayHit)
        {
            isGrounded = true;
            seamBufferTimer = 0f;
            isFallingScreamPlayed = false; // Reset fall flag safety upon securing footing
        }
        else
        {
            // If geometry connection is broken, buffer state for a short grace window before declaring true airborne state
            seamBufferTimer += Time.deltaTime;
            if (seamBufferTimer > seamBufferThreshold)
            {
                isGrounded = false;
            }
        }
    }

    /// <summary>
    /// Traverses all available platform instances to track down coords matching the safe manager key color.
    /// </summary>
    private void FindSafePlatform(MushroomColor newColor)
    {
        if (gameManager != null && gameManager.allPlatforms != null)
        {
            foreach (MushroomPlatform platform in gameManager.allPlatforms)
            {
                if (platform.myColor == newColor)
                {
                    targetPlatform = platform.transform;
                    Vector2 randomCircle = Random.insideUnitCircle * targetRadiusOffset;
                    specificTargetPoint = targetPlatform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
                    break;
                }
            }
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        if (isSquished) return;
        if (!isGrounded) return; // Anvils require a hard surface to compress objects. Air collision prevents flattening.

        if (collision.gameObject.CompareTag("Player") || collision.gameObject.CompareTag("NPC"))
        {
            float heightDifference = collision.transform.position.y - transform.position.y;
            Vector3 horizontalOffset = collision.transform.position - transform.position;
            horizontalOffset.y = 0f;
            float horizontalDistance = horizontalOffset.magnitude;

            // CRITERIA 1: "Goomba Stomp" implementation (Direct vertical compression check)
            // If the rival is standing squarely on top of our head geometry bounds, skip contacts and flatten instantly
            if (heightDifference > 0.45f && horizontalDistance < 0.45f)
            {
                GetSquished();
                return;
            }

            // CRITERIA 2: Original lateral crushing validation (Platform wall crushing physics)
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

    private void OnTriggerEnter(Collider other)
    {
        // Check for fluid boundary volume interaction
        if (other.gameObject.name.ToLower().Contains("water") || other.CompareTag("Water"))
        {
            if (inWater) return;
            inWater = true;

            // AUDIO LAYER: Instantly trigger the splash audio event upon water surface contact
            if (sfxSource != null && splashSound != null)
            {
                sfxSource.pitch = 1.0f; // Fixes pitch warping caused by running dice mod
                sfxSource.PlayOneShot(splashSound, 0.85f);
            }

            // Disable entity control loops and lock physics kinematics
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
            if (capsuleCollider != null) capsuleCollider.enabled = false;

            // Notify Manager to decrease target survivor arrays and dispatch rescue entities
            if (gameManager != null) gameManager.TriggerNPCRescue(3.0f);

            // Spawn visual placeholder capture effect
            if (gameManager != null && gameManager.blooperPrefab != null)
            {
                Vector3 spawnPos = transform.position + new Vector3(15f, 0, 0);
                GameObject blooper = Instantiate(gameManager.blooperPrefab, spawnPos, Quaternion.identity);
                blooper.GetComponent<BlooperPlaceholder>().StartCapture(this.transform);
            }
        }
    }

    public void GetSquished()
    {
        if (isSquished || inWater) return;
        StartCoroutine(SquishRoutine());
    }

    /// <summary>
    /// Performs asynchronous flattening transformation matrix deformation.
    /// Plays layered impact effects combined with local character audio.
    /// </summary>
    private IEnumerator SquishRoutine()
    {
        isSquished = true;
        moveInput = Vector3.zero;

        // LAYERED AUDIO EVENT: Fires splat structural impact and vocal audio channels simultaneously
        if (sfxSource != null)
        {
            sfxSource.pitch = 1.0f; // Protect voice track frequencies
            if (squishSound != null) sfxSource.PlayOneShot(squishSound, 0.70f);
            if (voiceSound != null) sfxSource.PlayOneShot(voiceSound, 0.80f);
        }

        if (GetComponent<Rigidbody>() != null)
        {
            GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        }

        // Store standard geometric scale properties
        float originalRadius = capsuleCollider.radius;
        float originalHeight = capsuleCollider.height;

        // Shrink the physical collision volume boundary boxes to prevent launcher tunneling issues
        capsuleCollider.radius = originalRadius * 0.2f;
        capsuleCollider.height = originalHeight * 0.15f;

        // Deform the visual mesh container down into a flat pancake shape
        transform.localScale = new Vector3(originalScale.x * 1.2f, originalScale.y * 0.15f, originalScale.z * 1.2f);

        // Hold flattened state
        yield return new WaitForSeconds(squishDuration);

        // Lerp reconstruction loop: Smoothly restore original scale geometries
        float timer = 0f;
        while (timer < 0.3f)
        {
            timer += Time.deltaTime;
            float progress = timer / 0.3f;
            float previousHeight = capsuleCollider.height;

            transform.localScale = Vector3.Lerp(transform.localScale, originalScale, progress);
            capsuleCollider.radius = Mathf.Lerp(originalRadius * 0.2f, originalRadius, progress);
            capsuleCollider.height = Mathf.Lerp(originalHeight * 0.15f, originalHeight, progress);

            // Mathematical lift calculation: Moves the entity upward slightly as it expands to stop clipping errors through the floor mesh
            float heightGrowth = (capsuleCollider.height - previousHeight) * transform.lossyScale.y;
            rb.position += new Vector3(0f, heightGrowth * 0.5f, 0f);

            yield return null;
        }

        // Hard lock exact structural dimensions back to base values
        transform.localScale = originalScale;
        capsuleCollider.radius = originalRadius;
        capsuleCollider.height = originalHeight;
        isSquished = false;
    }
}