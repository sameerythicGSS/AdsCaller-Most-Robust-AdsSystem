using System;
using UnityEngine;
using UnityEngine.UI;

public class SalonLargeBannerAd : MonoBehaviour
{
    [Tooltip("The actual UI element (Panel or Image) inside the Canvas to snap.")]
    public RectTransform snapContainer;
    
    public Image image;
    public Button button;
    public Button closeButton; // Optional, often Large Banners just close when the parent menu closes

    private void Start()
    {
        button.onClick.AddListener(OnLargeBannerClicked);

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseButtonClicked);
        }
    }

    private void OnLargeBannerClicked()
    {
        AdsCallerSalon instance = AdsCallerSalon.Instance;

        if (instance != null)
        {
            instance.OnLargeBannerClicked?.Invoke(instance.currentLargeBannerIndex);
        }
    }

    private void OnCloseButtonClicked()
    {
        // Route the close action through the main caller to keep state centralized
        AdsCallerSalon.Instance?.HideLargeBanner();
    }
}