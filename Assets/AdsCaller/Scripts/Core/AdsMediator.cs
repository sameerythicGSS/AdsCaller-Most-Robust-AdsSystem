using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Central hub for ad requests. Routes calls blindly to the injected active provider.
/// </summary>
public class AdsMediator : MonoBehaviour
{
    #region Singleton & the Ghost
    private static AdsMediator _instance;
    public static AdsMediator Instance
    {
        get
        {
            if (_instance == null)
            {
                Logger.Warn("Missing in scene! Spawning an Ghost for testing.");
                Logger.Note("Ghost will not show ads, but will allow testing of the code flow.");
                GameObject ghostObj = new GameObject("AdsMediator_Ghost");
                _instance = ghostObj.AddComponent<AdsMediator>();
                _instance.adsMode = EnumsJar_Salon.AdsMode.None;
            }
            return _instance;
        }
        private set { _instance = value; }
    }
    #endregion

    #region Fields & Properties
    private IOutAdsProvider _activeProvider;
    private IOutAdsProvider _rewardedProvider;

    [Header("UI References")]
    public Text textStatus;

    [Header("Configuration")]
    public EnumsJar_Salon.AdsMode adsMode = EnumsJar_Salon.AdsMode.Online;
    public bool testAds;
    public bool bypassRewardedAd = false;
    public bool useAdsCallerRewarded = true;

    [Space]
    public string removeAdsPlayerPrefKey = "RemoveAds";
    private int _backPressCount = 0;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        InitializeProviders();
    }
    #endregion

    #region Initialization
    private void InitializeProviders()
    {
        this.Note("[AdsMediator] Initializing active providers...");

        if (adsMode == EnumsJar_Salon.AdsMode.CrossPromo)
        {
            _activeProvider = AdsCallerSalon.Instance;
            _rewardedProvider = useAdsCallerRewarded ? null /* AdsCaller.Instance */ : _activeProvider;
        }
        else
        {
            _activeProvider = null; /* AdsCaller.Instance */
            _rewardedProvider = null; /* AdsCaller.Instance */
        }

        if (_activeProvider == null && adsMode != EnumsJar_Salon.AdsMode.None)
            this.Warn("[AdsMediator] Active provider is null! Standard ads will fail.");
    }
    #endregion

    #region Standard Ad Controls
    public void ShowInterstitial()
    {
        if (IsAdsRemoved() || _activeProvider == null) return;

        this.Note("ShowInterstitial");
        _activeProvider.ShowInterstitial();
    }

    public void ShowBanner()
    {
        if (IsAdsRemoved() || _activeProvider == null) return;

        this.Note("ShowBanner");
        _activeProvider.ShowBanner();
    }

    public void HideBanner() => _activeProvider?.HideBanner();

    public void ShowLargeBanner()
    {
        if (IsAdsRemoved() || _activeProvider == null) return;

        this.Note("ShowLargeBanner");
        _activeProvider.ShowLargeBanner();
    }

    public void HideLargeBanner() => _activeProvider?.HideLargeBanner();

    /// <summary>
    /// Instantly kills both standard and large banners.
    /// </summary>
    public void HideAllBanners()
    {
        this.Note("HideAllBanners");
        _activeProvider?.HideBanner();
        _activeProvider?.HideLargeBanner();
    }

    public void SwitchBanners(bool largeBanner = false)
    {
        if (IsAdsRemoved() || _activeProvider == null) return;

        if (_activeProvider.IsBannerAdShowing)
        {
            if (largeBanner)
            {
                _activeProvider.HideBanner();
                _activeProvider.ShowLargeBanner();
            }
        }
        else
        {
            _activeProvider.HideLargeBanner();
            _activeProvider.ShowBanner();
        }
    }
    #endregion

    #region Rewarded Ad Logic
    public void ShowRewardedAd(Action successCallback, Action failureCallback = null)
    {
        this.Note("RewardedAd Initiated");

        if (bypassRewardedAd)
        {
            this.Note("Bypassing Rewarded Ad - Granting reward directly");
            RewardedSuccess(successCallback);
            return;
        }

        if (_rewardedProvider == null)
        {
#if UNITY_EDITOR
            this.Note("No Ads Provider found. Bypassing the ad (Editor Only, actual build will execute reward failure).");
            RewardedSuccess(successCallback);
            return;
#else
            Debug.LogError("[AdsMediator] Rewarded Provider is null. Reward not granted.");
            RewardedFailure(failureCallback);
            return;
#endif
        }

        _rewardedProvider.ShowRewardedAd(
            onSuccess: () => RewardedSuccess(successCallback),
            onFail: () => RewardedFailure(failureCallback)
        );
    }

    private void RewardedSuccess(Action callback)
    {
        this.Note("Rewarded Ad Success - Reward Granted");
        if (textStatus) textStatus.text = "Reward Granted";
        callback?.Invoke();
    }

    private void RewardedFailure(Action callback)
    {
        if (textStatus) textStatus.text = "Ad not available";
        callback?.Invoke();
    }
#endregion

    #region Utilities
    public static bool IsAdsRemoved() => PlayerPrefs.GetInt(Instance.removeAdsPlayerPrefKey, 0) == 1;

    public static void RemoveAds() => PlayerPrefs.SetInt(Instance.removeAdsPlayerPrefKey, 1);

    public void BackBtnAd()
    {
        _backPressCount++;
        if (_backPressCount >= 3)
        {
            ShowInterstitial();
            _backPressCount = 0;
        }
    }
    #endregion
}