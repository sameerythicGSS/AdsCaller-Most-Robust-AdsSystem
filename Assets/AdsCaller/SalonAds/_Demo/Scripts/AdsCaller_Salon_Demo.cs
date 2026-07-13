using UnityEngine;
using UnityEngine.UI;

public class AdsCaller_Salon_Demo : MonoBehaviour
{
    [SerializeField]
    int delayShowInterstitial = 10;
    public Button BannerToggleBTN;
    public Button LBannerToggleBTN;
    public Button rewardedAdBTN;

    private void Start()
    {
        _ = ShowInterstitialAfterDelay();
        BannerToggleBTN.onClick.AddListener(() =>
        {
            if (AdsCallerSalon.Instance.IsBannerAdShowing)
            {
                AdsCallerSalon.Instance.HideBanner();
            }
            else
            {
                AdsCallerSalon.Instance.ShowBanner();
            }
        });

        LBannerToggleBTN.onClick.AddListener(() =>
        {
            if (AdsCallerSalon.Instance.IsLargeBannerAdShowing)
            {
                AdsCallerSalon.Instance.HideLargeBanner();
            }
            else
            {
                AdsCallerSalon.Instance.ShowLargeBanner();
            }
        });

        rewardedAdBTN.onClick.AddListener(() =>
        {
            AdsCallerSalon.Instance.ShowRewardedAd(
                onSuccess: () => Logger.Note("Rewarded Ad Success!"),
                onFail: () => Logger.Note("Rewarded Ad Failed or Skipped.")
            );
        });
    }

    private async Awaitable ShowInterstitialAfterDelay()
    {
        while (true)
        {
            await Awaitable.WaitForSecondsAsync(delayShowInterstitial);
            AdsCallerSalon.Instance.ShowInterstitial();
        }
    }
}
