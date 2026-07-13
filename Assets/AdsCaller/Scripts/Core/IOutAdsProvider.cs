using System;

public interface IOutAdsProvider
{
    bool IsBannerAdShowing { get; }
    void ShowInterstitial();
    void ShowBanner();
    void HideBanner();
    void ShowLargeBanner();
    void HideLargeBanner();
    void ShowRewardedAd(Action onSuccess, Action onFail = null);
}