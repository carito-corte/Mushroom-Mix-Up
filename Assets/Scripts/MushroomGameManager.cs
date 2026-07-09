using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// Controls the core gameplay loop, managing progressive acceleration for ALL timers and physical platform translation speeds
public class MushroomGameManager : MonoBehaviour
{
    public MushroomPlatform[] allPlatforms;
    public Image uiColorIndicator;

    [Header("Timing Settings")]
    [SerializeField] private float timeBeforeSink = 4f;
    [SerializeField] private float timeSpentSunken = 3f;
    [SerializeField] private float timeBetweenRounds = 2f;

    [Header("Progressive Difficulty Limits")]
    [SerializeField] private float minTimeBeforeSink = 0.8f;    // Fast reaction limit
    [SerializeField] private float minTimeSpentSunken = 0.8f;   // FIX: Allows mushrooms to bounce back up almost instantly
    [SerializeField] private float minTimeBetweenRounds = 0.4f; // Fast next-round transition limit

    [Header("Acceleration Steps (Timers)")]
    [SerializeField] private float sinkDifficultyStep = 0.3f;
    [SerializeField] private float sunkenDifficultyStep = 0.3f; // FIX: Reduces the time spent downstairs each round
    [SerializeField] private float roundDifficultyStep = 0.2f;

    [Header("Platform Speed Acceleration")]
    [SerializeField] private float initialPlatformSpeed = 3.5f;
    [SerializeField] private float maxPlatformSpeed = 15.0f;
    [SerializeField] private float speedDifficultyStep = 1.8f;

    private float currentPlatformSpeed;

    [Header("UI Color Customization")]
    public Color redColor = Color.red;
    public Color greenColor = Color.green;
    public Color pinkColor = Color.magenta;
    public Color blueColor = Color.blue;
    public Color lightBlueColor = Color.cyan;
    public Color yellowColor = Color.yellow;
    public Color blackColor = Color.black;

    void Start()
    {
        Application.targetFrameRate = 60;

        currentPlatformSpeed = initialPlatformSpeed;
        ApplySpeedToPlatforms();

        StartCoroutine(GameLoopRoutine());
    }

    private IEnumerator GameLoopRoutine()
    {
        while (true)
        {
            if (uiColorIndicator != null) uiColorIndicator.color = Color.white;

            foreach (MushroomPlatform platform in allPlatforms)
            {
                platform.ResetPlatform();
            }

            yield return new WaitForSeconds(timeBetweenRounds);

            MushroomColor safeColor = (MushroomColor)Random.Range(0, 7);
            UpdateUI(safeColor);

            yield return new WaitForSeconds(timeBeforeSink);

            foreach (MushroomPlatform platform in allPlatforms)
            {
                platform.EvaluatePlatform(safeColor);
            }

            // This wait duration now dynamically shrinks every single round
            yield return new WaitForSeconds(timeSpentSunken);

            // STEP 5: Apply dynamic progressive difficulty adjustments to ALL parameters simultaneously
            timeBeforeSink = Mathf.Max(minTimeBeforeSink, timeBeforeSink - sinkDifficultyStep);
            timeBetweenRounds = Mathf.Max(minTimeBetweenRounds, timeBetweenRounds - roundDifficultyStep);
            timeSpentSunken = Mathf.Max(minTimeSpentSunken, timeSpentSunken - sunkenDifficultyStep); // Dynamic shrink applied

            currentPlatformSpeed = Mathf.Min(maxPlatformSpeed, currentPlatformSpeed + speedDifficultyStep);
            ApplySpeedToPlatforms();

            Debug.Log($"LOOP SPEEDUP -> Break: {timeBetweenRounds}s | Reaction: {timeBeforeSink}s | Sunken Time: {timeSpentSunken}s | Physics: {currentPlatformSpeed}m/s");
        }
    }

    private void ApplySpeedToPlatforms()
    {
        foreach (MushroomPlatform platform in allPlatforms)
        {
            platform.SetMoveSpeed(currentPlatformSpeed);
        }
    }

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
}