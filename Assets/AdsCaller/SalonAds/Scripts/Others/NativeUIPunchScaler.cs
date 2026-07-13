using System.Collections;
using UnityEngine;

public class NativeUIPunchScaler : MonoBehaviour
{
    [Header("Target & Settings")]
    [SerializeField] private RectTransform targetRect;
    [SerializeField] private float delayBetweenPunches = 3f;

    [Header("Punch Configuration")]
    [Tooltip("Maximum scale increase (not more than 0.2)")]
    [SerializeField] private float maxScaleOffset = 0.2f;
    [Tooltip("How fast a single punch (in and out) takes")]
    [SerializeField] private float singlePunchDuration = 0.3f;

    private Vector3 originalScale;

    private void Start()
    {
        if (targetRect == null) targetRect = GetComponent<RectTransform>();

        originalScale = targetRect.localScale;
        StartCoroutine(PunchRoutine());
    }

    private IEnumerator PunchRoutine()
    {
        while (true)
        {
            // Wait for the 3-second delay
            yield return new WaitForSeconds(delayBetweenPunches);

            // Execute the punch twice
            yield return StartCoroutine(DoSinglePunch());
            yield return StartCoroutine(DoSinglePunch());
        }
    }

    private IEnumerator DoSinglePunch()
    {
        Vector3 targetScale = originalScale + new Vector3(maxScaleOffset, maxScaleOffset, maxScaleOffset);
        float halfDuration = singlePunchDuration / 2f;
        float elapsed = 0f;

        // Scale Up (The "Punch" Out)
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            targetRect.localScale = Vector3.Lerp(originalScale, targetScale, elapsed / halfDuration);
            yield return null;
        }

        elapsed = 0f;

        // Scale Down (The Return)
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            targetRect.localScale = Vector3.Lerp(targetScale, originalScale, elapsed / halfDuration);
            yield return null;
        }

        // Hard reset to original scale to prevent floating point inaccuracies over time
        targetRect.localScale = originalScale;
    }
}