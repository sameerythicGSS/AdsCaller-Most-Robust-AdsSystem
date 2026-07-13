using UnityEngine;
using UnityEngine.UI;

public class SalonPopAd : MonoBehaviour
{
    [Header("Pop Ad Configuration")]
    [Tooltip("Unique ID for this specific Pop Ad location. Give different IDs to avoid showing the exact same ad at the same time.")]
    public int popAdSlotID = 0;

    [Header("UI References")]
    public Image popImage;
    public Button popButton;

    private int currentGameIndex = -1;

    private void Awake()
    {
        if (popImage != null)
        {
            // Set image to transparent initially
            Color startColor = popImage.color;
            startColor.a = 0f;
            popImage.color = startColor;
        }

        if (popButton != null)
        {
            popButton.onClick.AddListener(OnPopButtonClicked);
        }
        else
        {
            this.Error($"[SalonPopAd] Button missing on Slot ID {popAdSlotID}");
        }
    }

    private async void OnEnable()
    {
        // 1. Wait for the Instance to exist in the scene
        while (AdsCallerSalon.Instance == null)
        {
            if (destroyCancellationToken.IsCancellationRequested) return;
            await Awaitable.EndOfFrameAsync();
        }

        // 2. Check if it's already initialized
        if (AdsCallerSalon.Instance.isInitialized)
        {
            await StartOperationsWithDelayAsync();
        }
        else
        {
            // 3. If it exists but isn't ready yet, subscribe to the event!
            AdsCallerSalon.Instance.OnAdsCallerInitialized += HandleAdsInitialized;
        }
    }

    private void OnDisable()
    {
        if (AdsCallerSalon.Instance != null)
        {
            AdsCallerSalon.Instance.OnAdsCallerInitialized -= HandleAdsInitialized;
            AdsCallerSalon.Instance.OnPopAdRefreshed -= HandlePopAdRefreshed;
            AdsCallerSalon.Instance.UnregisterPopAdSlot(popAdSlotID);
        }
    }

    // This catches the event broadcast
    private async void HandleAdsInitialized()
    {
        // Unsubscribe immediately so we don't accidentally run this twice
        AdsCallerSalon.Instance.OnAdsCallerInitialized -= HandleAdsInitialized;

        // Push to our delayed start
        await StartOperationsWithDelayAsync();
    }

    private async Awaitable StartOperationsWithDelayAsync()
    {
        // The "little after" buffer you requested
        await Awaitable.WaitForSecondsAsync(0.5f, destroyCancellationToken);

        if (AdsCallerSalon.Instance != null)
        {
            if (AdsCallerSalon.Instance.IsAdsRemoved)
            {
                gameObject.SetActive(false);
                return;
            }

            AdsCallerSalon.Instance.OnPopAdRefreshed += HandlePopAdRefreshed;
            TryRegisterSlot();
        }
    }

    private void HandlePopAdRefreshed(int broadcastSlotID, int gameIndex, Sprite newSprite)
    {
        if (AdsCallerSalon.Instance.IsAdsRemoved)
        {
            gameObject.SetActive(false);
            return;
        }

        if (broadcastSlotID == popAdSlotID)
        {
            currentGameIndex = gameIndex;
            if (popImage != null)
            {
                popImage.sprite = newSprite;

                Color loadedColor = popImage.color;
                loadedColor.a = 1f;
                popImage.color = loadedColor;
            }
        }
    }

    private void OnPopButtonClicked()
    {
        if (currentGameIndex != -1 && AdsCallerSalon.Instance != null)
        {
            AdsCallerSalon.Instance.OnPopAdClicked?.Invoke(currentGameIndex);
        }
    }

    private void TryRegisterSlot()
    {
        if (AdsCallerSalon.Instance != null && AdsCallerSalon.Instance.isInitialized)
        {
            AdsCallerSalon.Instance.RegisterPopAdSlot(popAdSlotID);
        }
    }
}