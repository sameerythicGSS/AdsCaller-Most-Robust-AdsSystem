using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Handles rewarded ad displays (Image/Video) using modern Awaitable async patterns.
/// Safely manages global engine states and UI animations.
/// </summary>
public class OutSalonRewardedAd : MonoBehaviour
{
    #region Variables

    [Header("Media Renderers")]
    public Image imageDisplay;
    public RawImage videoDisplay;
    public VideoPlayer videoPlayer;

    [Header("UI Controls")]
    public Button mainAdButton;
    public Button closeButton;
    public RectTransform textBody;
    public Text timerText;

    [Header("Timer Settings")]
    [Tooltip("Duration required to watch the image ad (in seconds) before granting reward.")]
    public float imageRewardDuration = 5f;

    [Header("Text Body Animation")]
    public float minWidth = 120f;
    public float maxWidth = 550f;
    public float minHeight = 120f;
    public float maxHeight = 250f;
    public float animationDuration = 0.5f;

    private Action onSuccess;
    private Action onFail;

    private bool rewardEarned = false;
    private float currentTimer;

    // Explicit cancellation tokens to handle script reuse and state changes safely
    private CancellationTokenSource adRoutineCts;

    #endregion

    #region Initialization & Setup

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);

        if (mainAdButton != null)
            mainAdButton.onClick.AddListener(OnRewardedAdClicked);
    }

    /// <summary>
    /// Configures and starts an image-based rewarded ad.
    /// </summary>
    public void SetupImageAd(Sprite sprite, Action successCallback, Action failCallback)
    {
        // 1. Hard safety check to prevent blank screens
        if (sprite == null)
        {
            Debug.LogError("[OutSalonRewardedAd] SetupImageAd called with a null sprite! Check your catalog. Aborting and firing failCallback.");
            failCallback?.Invoke();
            return;
        }

        ResetAdState(successCallback, failCallback);

        if (imageDisplay != null) imageDisplay.gameObject.SetActive(true);
        if (videoDisplay != null) videoDisplay.gameObject.SetActive(false);
        if (videoPlayer != null) videoPlayer.gameObject.SetActive(false);

        if (imageDisplay != null) imageDisplay.sprite = sprite;

        StartAdRoutine(ImageTimerRoutine);
    }

    /// <summary>
    /// Configures and starts a video-based rewarded ad via URL or AudioClip.
    /// </summary>
    public void SetupVideoAd(VideoClip clip, Action successCallback, Action failCallback, string url = null)
    {
        ResetAdState(successCallback, failCallback);

        if (imageDisplay != null) imageDisplay.gameObject.SetActive(false);
        if (videoDisplay != null) videoDisplay.gameObject.SetActive(true);

        if (videoPlayer != null)
        {
            videoPlayer.gameObject.SetActive(true);

            if (!string.IsNullOrEmpty(url))
            {
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = url;
            }
            else if (clip != null)
            {
                videoPlayer.source = VideoSource.VideoClip;
                videoPlayer.clip = clip;
            }

            videoPlayer.loopPointReached += OnVideoFinished;
            videoPlayer.errorReceived += OnVideoError;
            videoPlayer.Play();

            StartAdRoutine(VideoTimerUpdateRoutine);
        }
        else
        {
            Debug.LogWarning("[OutSalonRewardedAd] VideoPlayer missing. Falling back to image timer logic.");
            StartAdRoutine(ImageTimerRoutine);
        }
    }

    private void OnRewardedAdClicked()
    {
        if (AdsCallerSalon.Instance != null)
        {
            AdsCallerSalon.Instance.OnRewardedClicked?.Invoke(AdsCallerSalon.Instance.currentRewardedIndex);
        }
    }

    #endregion

    #region Core Ad Logic

    private void GrantReward()
    {
        if (rewardEarned) return;

        rewardEarned = true;

        // Kill the timer loop so we can reuse the token system for the UI animation
        ResetCancellationToken();

        if (timerText != null)
            timerText.text = "Reward Granted!";

        if (closeButton != null)
            closeButton.gameObject.SetActive(true);

        // Fire off the width animation async routine
        StartAdRoutine(AnimateTextBodySizeAsync);
    }

    private void OnVideoFinished(VideoPlayer vp)
    {
        UnsubscribeVideoEvents();
        GrantReward();
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError($"[OutSalonRewardedAd] Video runtime error: {message}. Granting reward as fallback.");
        UnsubscribeVideoEvents();
        GrantReward();
    }

    private void OnCloseClicked()
    {
        ResetCancellationToken();

        if (rewardEarned)
            onSuccess?.Invoke();
        else
            onFail?.Invoke();

        gameObject.SetActive(false);
    }

    #endregion

    #region Async Routines

    private void StartAdRoutine(Func<CancellationToken, Awaitable> routine)
    {
        adRoutineCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        _ = routine(adRoutineCts.Token);
    }

    private async Awaitable ImageTimerRoutine(CancellationToken token)
    {
        currentTimer = imageRewardDuration;

        while (currentTimer > 0)
        {
            if (token.IsCancellationRequested) return;

            if (timerText != null)
                timerText.text = $"Reward in: {Mathf.CeilToInt(currentTimer)}s";

            try
            {
                await Awaitable.NextFrameAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Using unscaledDeltaTime because timeScale is 0
            currentTimer -= Time.unscaledDeltaTime;
        }

        GrantReward();
    }

    private async Awaitable VideoTimerUpdateRoutine(CancellationToken token)
    {
        while (!rewardEarned && videoPlayer != null && videoPlayer.isPlaying)
        {
            if (token.IsCancellationRequested) return;

            if (timerText != null)
            {
                float videoLength = (float)videoPlayer.length;
                float videoTime = (float)videoPlayer.time;
                float timeRemaining = Mathf.Max(0, videoLength - videoTime);

                timerText.text = $"Reward in: {Mathf.CeilToInt(timeRemaining)}s";
            }

            try
            {
                await Awaitable.NextFrameAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Awaitable AnimateTextBodySizeAsync(CancellationToken token)
    {
        if (textBody == null) return;

        float elapsedTime = 0f;
        Vector2 startSize = textBody.sizeDelta;

        // Target vectors now lock onto both your maximum limits
        Vector2 targetSize = new Vector2(maxWidth, maxHeight);

        while (elapsedTime < animationDuration)
        {
            if (token.IsCancellationRequested) return;

            elapsedTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsedTime / animationDuration);

            textBody.sizeDelta = Vector2.Lerp(startSize, targetSize, t);

            try
            {
                await Awaitable.NextFrameAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // Snap to the absolute target to prevent floating-point slop
        textBody.sizeDelta = targetSize;
    }

    #endregion

    #region State Management & Cleanup

    private void ResetAdState(Action successCallback, Action failCallback)
    {
        ResetCancellationToken();

        onSuccess = successCallback;
        onFail = failCallback;
        rewardEarned = false;

        if (closeButton != null)
            closeButton.gameObject.SetActive(false);

        // Reset Text Body width and height to minWidth immediately
        if (textBody != null)
        {
            textBody.sizeDelta = new Vector2(minWidth, minHeight);
        }

        Time.timeScale = 0;
        AudioListener.volume = 0;
    }

    private void ResetCancellationToken()
    {
        if (adRoutineCts != null)
        {
            adRoutineCts.Cancel();
            adRoutineCts.Dispose();
            adRoutineCts = null;
        }
    }

    private void UnsubscribeVideoEvents()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.errorReceived -= OnVideoError;
        }
    }

    private void RestoreGlobalStates()
    {
        Time.timeScale = 1;
        AudioListener.volume = 1;
    }

    private void OnDisable()
    {
        ResetCancellationToken();
        UnsubscribeVideoEvents();
        RestoreGlobalStates();

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }
    }

    private void OnDestroy()
    {
        ResetCancellationToken();
        UnsubscribeVideoEvents();
        RestoreGlobalStates();

        if (mainAdButton != null)
            mainAdButton.onClick.RemoveListener(OnRewardedAdClicked);
    }

    #endregion
}