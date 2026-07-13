using System;
using UnityEngine;
using UnityEngine.UI;

public class SalonBannerAd : MonoBehaviour
{
    [Tooltip("The actual UI element (Panel or Image) inside the Canvas to snap.")]
    public RectTransform snapContainer;

    public Image bannerImage; // To be changed from Ads Caller Salon, based on the current game being advertised
    public Button bannerButton;

    public Button closeButton;

    private void Start()
    {
        bannerButton.onClick.AddListener(OnBannerClicked);

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseButtonClicked);
        }
    }

    private void OnBannerClicked()
    {
        AdsCallerSalon instance = AdsCallerSalon.Instance;

        if (instance != null)
        {
            instance.OnBannerClicked?.Invoke(instance.currentBannerIndex); // Assuming 0 is the ID for the game in the game catalog
        }
    }

    private void OnCloseButtonClicked()
    {
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        Time.timeScale = 1;
    }
}
