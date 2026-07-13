using UnityEngine;
using System.Collections;

/// <summary>
/// Controls the cinematic capture sequence when an entity falls into a hazard.
/// Manages the movement interpolation, parenting mechanics, and memory cleanup.
/// </summary>
public class BlooperPlaceholder : MonoBehaviour
{
    [Header("Cinematic Settings")]
    [SerializeField] private float dragSpeed = 8f;

    /// <summary>
    /// Initiates the capture sequence for a specified victim.
    /// </summary>
    /// <param name="victim">The transform of the player or NPC that triggered the hazard.</param>
    public void StartCapture(Transform victim)
    {
        StartCoroutine(CaptureSequence(victim));
    }

    /// <summary>
    /// Asynchronous routine that handles the four-phase capture animation using kinematic interpolation.
    /// </summary>
    private IEnumerator CaptureSequence(Transform victim)
    {
        // PHASE 1: ALIGNMENT
        // Calculate the exact grab coordinate slightly above the victim's origin to avoid geometric clipping
        Vector3 grabPosition = victim.position + new Vector3(0f, 0.5f, 0f);

        // Interpolate position smoothly until the capturer reaches the grab threshold
        while (Vector3.Distance(transform.position, grabPosition) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, grabPosition, dragSpeed * Time.deltaTime);
            yield return null;
        }

        // PHASE 2: ATTACHMENT
        // Parent the victim to the capturer. Because positions are aligned, this prevents sudden snapping or teleportation bugs.
        victim.SetParent(this.transform);

        // Brief dramatic pause to emphasize the impact before the drag begins
        yield return new WaitForSeconds(0.5f);

        // PHASE 3: EXTRACTION
        // Calculate the off-screen exit coordinates
        Vector3 exitPoint = transform.position + new Vector3(25f, -1f, 0f);

        // Interpolate towards the exit point, carrying the parented victim along automatically
        while (Vector3.Distance(transform.position, exitPoint) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, exitPoint, dragSpeed * Time.deltaTime);
            yield return null;
        }

        // PHASE 4: MEMORY CLEANUP
        // Safely destroy both entity GameObjects to free up system memory resources once they are off-screen
        Destroy(victim.gameObject);
        Destroy(gameObject);
    }
}