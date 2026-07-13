using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class SalonInterstitialAd : MonoBehaviour
{
    [Header("Media Renderers")]
    public Image image;
    public RawImage videoDisplay;
    public VideoPlayer videoPlayer;

    public Button button;

    [Header("Close Button & Timer")]
    public Button closeButton;
    public float closeButtonDelay = 5f;

    [Tooltip("The parent GameObject containing the timer text/graphics. Deactivates when done.")]
    public GameObject timerBody;
    public Text timerText;

    [Space]
    [Tooltip("If true, the time scale and audio volume will be reverted to their previous states when the ad is closed.")]
    public bool revertStatesOnClose = true;

    // Add variables to cache the prior state
    private float _previousTimeScale;
    private float _previousAudioVolume;

    private void OnEnable()
    {
        _previousTimeScale = Time.timeScale;
        _previousAudioVolume = AudioListener.volume;

        if (closeButton != null)
            closeButton.gameObject.SetActive(false);

        Time.timeScale = 0;
        AudioListener.volume = 0;
        _ = DelayShowingCloseButton();
    }

    private void Start()
    {
        if (button != null)
            button.onClick.AddListener(OnInterstitialClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseButtonClicked);
    }

    public void SetupImage(Sprite sprite)
    {
        image.gameObject.SetActive(true);
        if (videoDisplay != null) videoDisplay.gameObject.SetActive(false);
        if (videoPlayer != null) videoPlayer.gameObject.SetActive(false);

        image.sprite = sprite;
    }

    public void SetupVideo(VideoClip clip, string url = null)
    {
        image.gameObject.SetActive(false);
        if (videoDisplay != null) videoDisplay.gameObject.SetActive(true);

        if (videoPlayer != null)
        {
            videoPlayer.gameObject.SetActive(true);

            // Tell the VideoPlayer to use a URL if one exists, otherwise fallback to the local clip
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

            PlayVideo();
        }
    }

    public async void PlayVideo()
    {
        await Task.Delay(500);
        videoPlayer.Play();
    }

    private void OnInterstitialClicked()
    {
        AdsCallerSalon instance = AdsCallerSalon.Instance;
        if (instance != null)
        {
            instance.OnInterstitialClicked?.Invoke(instance.currentInterstitialIndex);
        }
    }

    private async Awaitable DelayShowingCloseButton()
    {
        Logger.Note($"Waiting for {closeButtonDelay} seconds before showing the close button...");

        try
        {
            if (timerBody != null) timerBody.SetActive(true);

            float remainingTime = closeButtonDelay;

            while (remainingTime > 0)
            {
                if (timerText != null)
                {
                    timerText.text = Mathf.CeilToInt(remainingTime).ToString();
                }

                await Awaitable.NextFrameAsync(destroyCancellationToken);
                remainingTime -= Time.unscaledDeltaTime;
            }

            if (timerBody != null) timerBody.SetActive(false);

            if (closeButton != null)
            {
                Logger.Note("Showing the close button now.");

                CanvasGroup canvasGroup = closeButton.GetComponent<CanvasGroup>();

                if (canvasGroup == null) canvasGroup = closeButton.gameObject.AddComponent<CanvasGroup>();

                canvasGroup.alpha = 0f;
                closeButton.gameObject.SetActive(true);

                float fadeDuration = 0.3f;
                float t = 0f;

                while (t < fadeDuration)
                {
                    t += Time.unscaledDeltaTime;
                    canvasGroup.alpha = t / fadeDuration;
                    await Awaitable.NextFrameAsync(destroyCancellationToken);
                }

                canvasGroup.alpha = 1f;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Note("Close button delay was cancelled because the object was destroyed.");
        }
    }

    private void OnCloseButtonClicked()
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.Stop();

        gameObject.SetActive(false);
    }

    private void OnDisable()
    {

        if (revertStatesOnClose)
        {
            Time.timeScale = _previousTimeScale;
            AudioListener.volume = _previousAudioVolume;
        }
        else
        {
            Time.timeScale = 1;
            AudioListener.volume = 1;
        }


        if (videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.Stop();
    }

    private void OnDestroy()
    {
        if (revertStatesOnClose)
        {
            Time.timeScale = _previousTimeScale;
            AudioListener.volume = _previousAudioVolume;
        }
        else
        {
            Time.timeScale = 1;
            AudioListener.volume = 1;
        }


        if (button != null)
            button.onClick.RemoveListener(OnInterstitialClicked);

        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
    }
}