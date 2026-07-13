using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// Defines the globally accessible finite states for the minigame's runtime cycle.
/// </summary>
public enum GameState { Intro, Playing, BlooperRescue, GameOver, Victory }

/// <summary>
/// Core state machine manager that orchestrates color roulettes, progressive difficulty, 
/// cinematic camera transitions, and synchronized UI audio triggers.
/// </summary>
public class MushroomGameManager : MonoBehaviour
{
    [Header("Global Game State")]
    public GameState currentState = GameState.Intro;

    [Header("Arena Configuration")]
    public MushroomPlatform[] allPlatforms;
    public Image uiColorIndicator;

    [Header("Cinematics & UI Objects")]
    public GameObject blooperPrefab;
    public Transform mainCamera;
    public Transform cameraStartPos;
    public Transform cameraPlayPos;
    public GameObject startTextUI;
    public GameObject gameOverTextUI;
    public GameObject victoryTextUI;

    [Header("UI Audio Assets")]
    [SerializeField] private AudioClip startSound;
    [SerializeField] private AudioClip gameOverSound;
    [SerializeField] private AudioClip victorySound;

    // Dedicated 2D interface channel and background music tracking interceptor
    private AudioSource uiSfxSource;
    private AudioSource bgmSource;

    [Header("Timing Loops Settings")]
    [SerializeField] private float timeBeforeSink = 4f;
    [SerializeField] private float timeSpentSunken = 3f;
    [SerializeField] private float timeBetweenRounds = 2f;

    [Header("Progressive Difficulty Thresholds")]
    [SerializeField] private float minTimeBeforeSink = 0.8f;
    [SerializeField] private float minTimeSpentSunken = 0.8f;
    [SerializeField] private float minTimeBetweenRounds = 0.4f;

    [Header("Difficulty Dynamic Timers Step (Subtractions)")]
    [SerializeField] private float sinkDifficultyStep = 0.3f;
    [SerializeField] private float sunkenDifficultyStep = 0.3f;
    [SerializeField] private float roundDifficultyStep = 0.2f;

    [Header("Platform Speed Acceleration Settings")]
    [SerializeField] private float initialPlatformSpeed = 3.5f;
    [SerializeField] private float maxPlatformSpeed = 15.0f;
    [SerializeField] private float speedDifficultyStep = 1.8f;

    // Numerical runtime counters
    private float currentPlatformSpeed;
    private MushroomColor currentSafeColor = MushroomColor.Black;
    private int activeNPCCount;

    [Header("UI Canvas Color Definitions")]
    public Color redColor = Color.red;
    public Color greenColor = Color.green;
    public Color pinkColor = Color.magenta;
    public Color blueColor = Color.blue;
    public Color lightBlueColor = Color.cyan;
    public Color yellowColor = Color.yellow;
    public Color blackColor = Color.black;

    void Start()
    {
        // Enforce a stable update lock rate for consistent physical simulation ticks
        Application.targetFrameRate = 60;

        // Initialize structural constants
        currentPlatformSpeed = initialPlatformSpeed;
        ApplySpeedToPlatforms();

        // Dynamically compile the total count of active NPC components present on the field
        NPCPhysicsController[] npcs = Object.FindObjectsByType<NPCPhysicsController>(FindObjectsSortMode.None);
        activeNPCCount = npcs.Length;

        // Ensure canvas element states are reset upon entering the scene
        if (startTextUI != null) startTextUI.SetActive(false);
        if (gameOverTextUI != null) gameOverTextUI.SetActive(false);
        if (victoryTextUI != null) victoryTextUI.SetActive(false);

        // EXTRACTION PUENTE: Intercept the main camera to capture its background music component
        if (mainCamera != null)
        {
            bgmSource = mainCamera.GetComponent<AudioSource>();
        }

        // SFX CHANNEL CONFIG: Generate an independent 2D audio channel for clean UI clip mixing
        uiSfxSource = gameObject.AddComponent<AudioSource>();
        uiSfxSource.spatialBlend = 0f;
        uiSfxSource.playOnAwake = false;

        // Initialize the opening cinematic sequence
        StartCoroutine(IntroSequence());
    }

    /// <summary>
    /// Custom smart timing buffer. Pauses timer execution loops 
    /// if the match sequence gets interrupted by rescue events.
    /// </summary>
    private IEnumerator SmartWait(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (currentState == GameState.Playing)
            {
                elapsed += Time.deltaTime;
            }
            yield return null;
        }
    }

    /// <summary>
    /// Handles the introductory panning interpolation matrix of the main camera transform.
    /// Triggers start UI flags and broadcasts the core match state.
    /// </summary>
    private IEnumerator IntroSequence()
    {
        currentState = GameState.Intro;

        // Lock camera initial orientation boundaries
        if (mainCamera != null && cameraStartPos != null)
        {
            mainCamera.position = cameraStartPos.position;
            mainCamera.rotation = cameraStartPos.rotation;
        }

        yield return new WaitForSeconds(1.0f);

        float timer = 0f;
        float duration = 2.0f;

        // Smoothly interpolate spatial coordinates toward play position boundaries
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, timer / duration);

            if (mainCamera != null && cameraStartPos != null && cameraPlayPos != null)
            {
                mainCamera.position = Vector3.Lerp(cameraStartPos.position, cameraPlayPos.position, t);
                mainCamera.rotation = Quaternion.Slerp(cameraStartPos.rotation, cameraPlayPos.rotation, t);
            }
            yield return null;
        }

        // Activate notification overlay graphics
        if (startTextUI != null) startTextUI.SetActive(true);

        // Play the interface trigger jingle
        if (uiSfxSource != null && startSound != null) uiSfxSource.PlayOneShot(startSound, 0.6f);

        yield return new WaitForSeconds(1.0f);
        if (startTextUI != null) startTextUI.SetActive(false);

        // Open core systems loop
        currentState = GameState.Playing;
        StartCoroutine(GameLoopRoutine());
    }

    /// <summary>
    /// Infinite loop managing color selection roulettes, broadcasting platform evaluation orders, 
    /// resetting matrix layers, and compounding difficulty over time.
    /// </summary>
    private IEnumerator GameLoopRoutine()
    {
        while (true)
        {
            // Pick a pseudo-random enum value to dictate the current round's safe location
            currentSafeColor = (MushroomColor)Random.Range(0, 7);
            UpdateUI(currentSafeColor);

            // Wait during the announcement window
            yield return StartCoroutine(SmartWait(timeBeforeSink));

            // Issue translation commands to all tracking platform elements
            foreach (MushroomPlatform platform in allPlatforms)
            {
                platform.EvaluatePlatform(currentSafeColor);
            }

            // Maintain the hazardous layout geometry active
            yield return StartCoroutine(SmartWait(timeSpentSunken));

            // Return HUD parameters to default white balance
            if (uiColorIndicator != null) uiColorIndicator.color = Color.white;

            // Return all platforms back to safe surface boundaries
            foreach (MushroomPlatform platform in allPlatforms)
            {
                platform.ResetPlatform();
            }

            // DIFFICULTY SCALING CALCULATIONS: Subtract cycle times and clamp to minimum safe margins
            timeBeforeSink = Mathf.Max(minTimeBeforeSink, timeBeforeSink - sinkDifficultyStep);
            timeBetweenRounds = Mathf.Max(minTimeBetweenRounds, timeBetweenRounds - roundDifficultyStep);
            timeSpentSunken = Mathf.Max(minTimeSpentSunken, timeSpentSunken - sunkenDifficultyStep);

            // Platform physical speed addition clamped to maximum boundaries
            currentPlatformSpeed = Mathf.Min(maxPlatformSpeed, currentPlatformSpeed + speedDifficultyStep);
            ApplySpeedToPlatforms();

            // Interval delay before initializing the next roulette loop iteration
            yield return StartCoroutine(SmartWait(timeBetweenRounds));
        }
    }

    /// <summary>
    /// Intercepts entity death reports to lock inputs and execute game over coroutines.
    /// </summary>
    public void TriggerGameOver(Transform playerTransform)
    {
        if (currentState == GameState.Playing || currentState == GameState.BlooperRescue || currentState == GameState.Victory)
        {
            StartCoroutine(GameOverRoutine(playerTransform));
        }
    }

    /// <summary>
    /// Asynchronous defeat routine. Cuts background ambient loops, activates visual assets, 
    /// plays dedicated defeat clips, and reloads the current baseline scene index.
    /// </summary>
    private IEnumerator GameOverRoutine(Transform playerTransform)
    {
        currentState = GameState.GameOver;

        // CUT CHANNEL: Halt background tracks to isolate the dedicated defeat clip
        if (bgmSource != null) bgmSource.Stop();

        // Dispatch animated capture entities
        if (blooperPrefab != null)
        {
            Vector3 spawnPos = playerTransform.position + new Vector3(15f, 0, 0);
            GameObject blooper = Instantiate(blooperPrefab, spawnPos, Quaternion.identity);
            blooper.GetComponent<BlooperPlaceholder>().StartCapture(playerTransform);
        }

        if (gameOverTextUI != null) gameOverTextUI.SetActive(true);

        // Execute the UI audio play command
        if (uiSfxSource != null && gameOverSound != null) uiSfxSource.PlayOneShot(gameOverSound, 0.8f);

        // TIMING SYNC FIX: Holds the scene context active for 5.5s to play the entire track duration perfectly
        yield return new WaitForSeconds(5.5f);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>
    /// Decreases alive tracking index counters upon NPC hazard connection.
    /// Manages instant conditional checks to catch simultaneous collision edge-cases.
    /// </summary>
    public void TriggerNPCRescue(float rescueDuration)
    {
        activeNPCCount--;

        // CONCURRENCY SAFEGUARD: Checks victory conditions even if a previous rescue loop was active
        if (activeNPCCount <= 0 && (currentState == GameState.Playing || currentState == GameState.BlooperRescue))
        {
            StartCoroutine(VictoryRoutine());
        }
        else if (currentState == GameState.Playing)
        {
            StartCoroutine(RescueRoutine(rescueDuration));
        }
    }

    /// <summary>
    /// Intermediary freeze state that holds standard progression arrays while a rescue sequence clears out.
    /// </summary>
    private IEnumerator RescueRoutine(float duration)
    {
        currentState = GameState.BlooperRescue;
        yield return new WaitForSeconds(duration);
        if (currentState != GameState.GameOver && currentState != GameState.Victory)
        {
            currentState = GameState.Playing;
        }
    }

    /// <summary>
    /// Asynchronous victory routine. Halts main ambient layers, displays success graphic panels, 
    /// plays winning tracking sound clips, and resets the simulation.
    /// </summary>
    private IEnumerator VictoryRoutine()
    {
        currentState = GameState.Victory;

        // CUT CHANNEL: Silences the main camera audio source to isolate the victory jingle cleanly
        if (bgmSource != null) bgmSource.Stop();

        if (victoryTextUI != null) victoryTextUI.SetActive(true);

        // Execute victory trigger play command
        if (uiSfxSource != null && victorySound != null) uiSfxSource.PlayOneShot(victorySound, 0.8f);

        // TIMING SYNC FIX: Matches the exact 3.122s clip profile with a comfortable 3.5s wrap-up window
        yield return new WaitForSeconds(3.5f);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>
    /// Iterates across the platform tracking array to feed updated velocity values.
    /// </summary>
    private void ApplySpeedToPlatforms()
    {
        foreach (MushroomPlatform platform in allPlatforms)
        {
            platform.SetMoveSpeed(currentPlatformSpeed);
        }
    }

    /// <summary>
    /// Updates the target canvas image parameter to match color enumerator indexes.
    /// </summary>
    private void UpdateUI(MushroomColor safeColor)
    {
        if (uiColorIndicator == null) return;

        switch (safeColor)
        {
            case MushroomColor.Red: uiColorIndicator.color = redColor; break;
            case MushroomColor.Green: uiColorIndicator.color = greenColor; break;
            case MushroomColor.Pink: uiColorIndicator.color = pinkColor; break;
            case MushroomColor.Blue: uiColorIndicator.color = blueColor; break;
            case MushroomColor.LightBlue: uiColorIndicator.color = lightBlueColor; break;
            case MushroomColor.Yellow: uiColorIndicator.color = yellowColor; break;
            case MushroomColor.Black: uiColorIndicator.color = blackColor; break;
            default: uiColorIndicator.color = Color.white; break;
        }
    }

    /// <summary>
    /// Standard getter method to expose the current round's safe color code to pathfinding AI systems.
    /// </summary>
    public MushroomColor GetCurrentSafeColor()
    {
        return currentSafeColor;
    }
}