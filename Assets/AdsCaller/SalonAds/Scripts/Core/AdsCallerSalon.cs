using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Core system for custom-made ads. Loads UI prefabs dynamically from Resources.
/// </summary>
public class AdsCallerSalon : MonoBehaviour, IOutAdsProvider
{
    public static AdsCallerSalon Instance;

    private CancellationTokenSource popAdCTS;
    private CancellationTokenSource bannerCTS;

    #region Inspector Configs
    [Header("General")]
    public EnumsJar_Salon.PlatformType platformType;
    private List<GameAdEntry> runtimeCatalog;
    public bool canInitialize;
    public string removeAdsPlayerPrefKey = "RemoveAds";

    [Header("Banner Ads")]
    [Tooltip("(in seconds) How long a banner ad stays before showing other content")]
    public int bannerCycleSpeed = 7;
    public bool showBannerOnStart = true;

    [Header("Pop Ads")]
    [Tooltip("(in seconds) How long a pop ad stays before cycling to the next game")]
    public int popAdCycleSpeed = 3;
    public bool startPopAdCycleOnStart = true;

    [Header("Level Ads")]
    public int levelAdCycleSpeed = 5;
    public bool startLevelAdCycleOnStart = true;


    [Header("Ad Sequencing")]
    public EnumsJar_Salon.AdSequenceType bannerSequence = EnumsJar_Salon.AdSequenceType.Sequential;
    public EnumsJar_Salon.AdSequenceType interstitialSequence = EnumsJar_Salon.AdSequenceType.Random;
    public EnumsJar_Salon.AdSequenceType largeBannerSequence = EnumsJar_Salon.AdSequenceType.Random;
    public EnumsJar_Salon.AdSequenceType popAdSequence = EnumsJar_Salon.AdSequenceType.Sequential;

    [Header("Ad Positioning")]
    [Tooltip("Where should the regular banner be pinned?")]
    public EnumsJar_Salon.AdPosition bannerPosition = EnumsJar_Salon.AdPosition.BottomCenter;

    [Tooltip("Where should the large banner be pinned?")]
    public EnumsJar_Salon.AdPosition largeBannerPosition = EnumsJar_Salon.AdPosition.BottomCenter;

    [Header("Interstitial Settings")]
    public EnumsJar_Salon.AdMediaType interstitialMediaType = EnumsJar_Salon.AdMediaType.Image;

    [Header("Rewarded Settings")]
    public EnumsJar_Salon.AdSequenceType rewardedSequence = EnumsJar_Salon.AdSequenceType.Random;
    public EnumsJar_Salon.AdMediaType rewardedMediaType = EnumsJar_Salon.AdMediaType.Video;
    #endregion

    [HideInInspector] public bool isInitialized = false;
    public Action OnAdsCallerInitialized;

    [HideInInspector] public bool bannerCycleActive = false;
    [HideInInspector] public bool popAdCycleActive = false;
    [HideInInspector] public bool ArePrefabsLoaded { get; private set; } = false;

    [HideInInspector] public int currentBannerIndex = 0;
    [HideInInspector] public int currentInterstitialIndex = 0;
    [HideInInspector] public int currentRewardedIndex = 0;
    [HideInInspector] public int currentLargeBannerIndex = 0;
    [HideInInspector] public int currentPopAdIndex = 0;

    // Trackers for per-game image indices
    private int[] gameBannerImageIndices;

    private int[] gameInterstitialImageIndices;
    private int[] gameInterstitialVideoIndices;

    private int[] gameRewardedImageIndices;
    private int[] gameRewardedVideoIndices;

    private int[] gameLargeBannerImageIndices;

    private int[] gamePopAdImageIndices;
    // -----------------------------------

    private Dictionary<int, int> activePopAdSlots = new Dictionary<int, int>();

    public Action<int> OnBannerClicked;
    public Action<int> OnInterstitialClicked;
    public Action<int> OnRewardedClicked;
    public Action<int> OnLargeBannerClicked;
    public Action<int> OnPopAdClicked;

    public Action<int, int, Sprite> OnPopAdRefreshed;

    public bool IsBannerAdShowing => bannerAdUI != null && bannerAdUI.gameObject.activeSelf;
    public bool IsLargeBannerAdShowing => largeBannerAdUI != null && largeBannerAdUI.gameObject.activeSelf;
    public bool IsRewardedAdShowing => rewardedAdUI != null && rewardedAdUI.gameObject.activeInHierarchy;

    public bool IsAdsRemoved => PlayerPrefs.GetInt(removeAdsPlayerPrefKey, 0) == 1;

    private AsyncOperationHandle<GameObject> bannerHandle;
    private AsyncOperationHandle<GameObject> largeBannerHandle;
    private AsyncOperationHandle<GameObject> interstitialHandle;
    private AsyncOperationHandle<GameObject> rewardedHandle;

    // Private references, populated at runtime
    private SalonBannerAd bannerAdUI;
    private SalonInterstitialAd interstitialAdUI;
    private OutSalonRewardedAd rewardedAdUI;
    private SalonLargeBannerAd largeBannerAdUI;
    private SalonPopAd popAdUI;


    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _ = LoadAdPrefabsAsync(); // Fire and forget the Awaitable
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        OnBannerClicked += HandleBannerClicked;
        OnInterstitialClicked += HandleInterstitialClicked;
        OnRewardedClicked += HandleRewardedClicked;
        OnLargeBannerClicked += HandleLargeBannerClicked;
        OnPopAdClicked += HandlePopAdClicked;
    }

    private void OnDisable()
    {
        OnBannerClicked -= HandleBannerClicked;
        OnInterstitialClicked -= HandleInterstitialClicked;
        OnRewardedClicked -= HandleRewardedClicked;
        OnLargeBannerClicked -= HandleLargeBannerClicked;
        OnPopAdClicked -= HandlePopAdClicked;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        OnBannerClicked -= HandleBannerClicked;
        OnInterstitialClicked -= HandleInterstitialClicked;
        OnRewardedClicked -= HandleRewardedClicked;
        OnLargeBannerClicked -= HandleLargeBannerClicked;
        OnPopAdClicked -= HandlePopAdClicked;

        popAdCTS?.Cancel();
        popAdCTS?.Dispose();
        popAdCTS = null;

        bannerCTS?.Cancel();
        bannerCTS?.Dispose();
        bannerCTS = null;

        if (bannerHandle.IsValid()) Addressables.Release(bannerHandle);
        if (interstitialHandle.IsValid()) Addressables.Release(interstitialHandle);
        if (rewardedHandle.IsValid()) Addressables.Release(rewardedHandle);
        if (largeBannerHandle.IsValid()) Addressables.Release(largeBannerHandle);
    }
    #endregion

    #region Event Handlers
    private void HandleBannerClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.Banner);
    private void HandleInterstitialClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.Inter);
    private void HandleRewardedClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.Rewarded);
    private void HandleLargeBannerClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.LBanner);
    private void HandlePopAdClicked(int index) => ProcessAdClick(index, EnumsJar_Salon.AdPlacementType.PopAd);
    #endregion

    #region Initialization

    private async Awaitable LoadAdPrefabsAsync()
    {
        try
        {
            // Load and instantiate Banner -----------------------------------------------------------------------------------------------------------------------------
            bannerHandle = Addressables.LoadAssetAsync<GameObject>("SalonAds_Banner");
            GameObject bannerPrefab = await bannerHandle.Task;

            if (bannerPrefab != null)
            {
                GameObject bannerObj = Instantiate(bannerPrefab, transform);
                bannerAdUI = bannerObj.GetComponent<SalonBannerAd>();

                if (bannerAdUI.snapContainer != null)
                {
                    SnapToPosition(bannerAdUI.snapContainer, bannerPosition);
                    bannerAdUI.bannerImage.color = Color.black;
                }
                else
                    Logger.Warn("Snap Container missing on Banner UI. Anchoring may behave unexpectedly.");

                bannerObj.SetActive(false);
            }
            else this.Error("Failed to load Banner prefab. Verify the Resources/AdsCaller_Resources/Banner path.");

            // Load and instantiate Interstitial based on orientation --------------------------------------------------------------------------------------------------
            GameObject interstitialPrefab;
            if (Screen.width > Screen.height)
            {
                //interstitialPrefab = Resources.Load<GameObject>("AdsCaller_Resources/Interstitial_landscape");
                interstitialHandle = Addressables.LoadAssetAsync<GameObject>("SalonAds_Interstitial_landscape");
                interstitialPrefab = await interstitialHandle.Task;
            }
            else
            {
                //interstitialPrefab = Resources.Load<GameObject>("AdsCaller_Resources/Interstitial");
                interstitialHandle = Addressables.LoadAssetAsync<GameObject>("SalonAds_Interstitial");
                interstitialPrefab = await interstitialHandle.Task;
            }

            if (interstitialPrefab != null)
            {
                GameObject interstitialObj = Instantiate(interstitialPrefab, transform);
                interstitialAdUI = interstitialObj.GetComponent<SalonInterstitialAd>();
                interstitialObj.SetActive(false);
            }
            else this.Error("Failed to load Interstitial prefab. Verify the Resources/AdsCaller_Resources/Interstitial path.");

            // Load and instantiate Rewarded Ad based on orientation --------------------------------------------------------------------------------------------------
            GameObject rewardedPrefab;
            if (Screen.width > Screen.height)
            {
                // Landscape
                rewardedHandle = Addressables.LoadAssetAsync<GameObject>("SalonAds_Rewarded_landscape");
                rewardedPrefab = await rewardedHandle.Task;
            }
            else
            {
                // Portrait
                rewardedHandle = Addressables.LoadAssetAsync<GameObject>("SalonAds_Rewarded");
                rewardedPrefab = await rewardedHandle.Task;
            }

            if (rewardedPrefab != null)
            {
                GameObject rewardedObj = Instantiate(rewardedPrefab, transform);
                rewardedAdUI = rewardedObj.GetComponent<OutSalonRewardedAd>();
                rewardedObj.SetActive(false);
            }
            else
            {
                this.Error("Failed to load Rewarded prefab. Go to Window>Asset Management>Addressables>Groups and check Addressable keys: SalonAds_Rewarded and SalonAds_Rewarded_landscape.");
            }

            // Load and instantiate Large Banner -------------------------------------------------------------------------------------------------------------------------------------
            largeBannerHandle = Addressables.LoadAssetAsync<GameObject>("SalonAds_LargeBanner");
            GameObject largeBannerPrefab = await largeBannerHandle.Task;

            //GameObject largeBannerPrefab = Resources.Load<GameObject>("AdsCaller_Resources/LargeBanner");
            if (largeBannerPrefab != null)
            {
                GameObject largeBannerObj = Instantiate(largeBannerPrefab, transform);
                largeBannerAdUI = largeBannerObj.GetComponent<SalonLargeBannerAd>();

                if (largeBannerAdUI.snapContainer != null)
                    SnapToPosition(largeBannerAdUI.snapContainer, largeBannerPosition);
                else
                    Logger.Warn("Snap Container missing on Large Banner UI. Anchoring may behave unexpectedly.");

                largeBannerObj.SetActive(false);
            }
            else this.Error("Failed to load Large Banner prefab. Verify the Resources/AdsCaller_Resources/LargeBanner path.");

            ArePrefabsLoaded = true;
            Logger.Note("AdsCaller_Salon prefabs loaded successfully.");
            CheckAndInitialize();
        }
        catch (System.Exception ex)
        {
            // Catches any unexpected errors during the fire-and-forget async operation
            this.Error($"[AdsCaller_Salon] Fatal error during LoadAdPrefabsAsync: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public void CheckAndInitialize()
    {
        if (!canInitialize) return;

        if (!ArePrefabsLoaded)
        {
            Logger.Note("AdsCaller_Salon waiting for prefabs to load...");
            return;
        }

        if (CatalogLoader.Instance == null || !CatalogLoader.Instance.IsCatalogReady)
        {
            Logger.Note("AdsCaller_Salon waiting for CatalogLoader...");
            return;
        }

        // Both are ready. Set catalog and initialize safely.
        SetRuntimeCatalog(CatalogLoader.Instance.runtimeCatalog);
        InitializeSystem();
    }

    public void InitializeSystem()
    {
        if (runtimeCatalog == null || runtimeCatalog.Count == 0)
        {
            Logger.Warn("Runtime catalog is empty. Aborting AdsCaller initialization.");
            return;
        }

        //activePopAdSlots.Clear();
        isInitialized = true;

        int gameCount = runtimeCatalog.Count;
        gameBannerImageIndices = new int[gameCount];
        gameInterstitialImageIndices = new int[gameCount];
        gameInterstitialVideoIndices = new int[gameCount];
        gameLargeBannerImageIndices = new int[gameCount];
        gamePopAdImageIndices = new int[gameCount];
        gameRewardedImageIndices = new int[gameCount];
        gameRewardedVideoIndices = new int[gameCount];

        // Reset current selection indices to 0 to prevent out-of-bounds errors
        currentBannerIndex = 0;
        currentInterstitialIndex = 0;
        currentRewardedIndex = 0;
        currentLargeBannerIndex = 0;
        currentPopAdIndex = 0;

        List<int> existingSlots = new List<int>(activePopAdSlots.Keys);
        foreach (int slot in existingSlots)
        {
            // Reset to 0 so it safely aligns with the new remote catalog length
            activePopAdSlots[slot] = 0;

            // Immediately refresh the UI to show the new remote image
            RefreshPopAdSlot(slot);
        }

        OnAdsCallerInitialized?.Invoke();

        if (showBannerOnStart && !IsAdsRemoved)
        {
            if (bannerAdUI != null)
                bannerAdUI.gameObject.SetActive(true);

            bannerCycleActive = true;
            StartBannerCycle();
        }

        if (startPopAdCycleOnStart && !IsAdsRemoved)
        {
            popAdCycleActive = true;
            StartPopAdCycle();
        }
    }

    public void SetRuntimeCatalog(List<GameAdEntry> catalog)
    {
        runtimeCatalog = catalog;
    }

    #endregion

    #region Pop Ad Logic

    public void StartPopAdCycle()
    {
        Logger.Note("Initializing Pop Ad cycle...");

        popAdCTS?.Cancel();
        popAdCTS?.Dispose();
        popAdCTS = null;
        popAdCTS = new CancellationTokenSource();

        _ = StartPopAdCycling(popAdCTS.Token);
    }

    private async Awaitable StartPopAdCycling(CancellationToken token)
    {
        try
        {
            // Removed the linked destroyCancellationToken. 
            // Your OnDestroy() already cleanly cancels popAdCTS.
            while (!token.IsCancellationRequested)
            {
                await Awaitable.WaitForSecondsAsync(popAdCycleSpeed, token);

                if (token.IsCancellationRequested) break;

                CycleAllPopAds();
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Note("Pop Ad cycle was successfully cancelled.");
        }
    }

    private void CycleAllPopAds()
    {
        if (runtimeCatalog == null || runtimeCatalog.Count == 0) return;

        Logger.Note("Cycling all active Pop Ads.");
        List<int> currentSlots = new List<int>(activePopAdSlots.Keys);

        foreach (int slotID in currentSlots)
        {
            int currentGame = activePopAdSlots[slotID];
            int nextGame = popAdSequence == EnumsJar_Salon.AdSequenceType.Sequential
                ? (currentGame + 1) % runtimeCatalog.Count
                : UnityEngine.Random.Range(0, runtimeCatalog.Count);

            activePopAdSlots[slotID] = nextGame;
            RefreshPopAdSlot(slotID);
        }
    }

    public void RegisterPopAdSlot(int slotID)
    {
        if (runtimeCatalog == null || runtimeCatalog.Count == 0) return;

        if (!activePopAdSlots.ContainsKey(slotID))
        {
            int startIndex = slotID % runtimeCatalog.Count;
            activePopAdSlots.Add(slotID, startIndex);
        }

        RefreshPopAdSlot(slotID);
    }

    public void UnregisterPopAdSlot(int slotID)
    {
        if (activePopAdSlots.ContainsKey(slotID))
        {
            activePopAdSlots.Remove(slotID);
        }
    }

    private void RefreshPopAdSlot(int slotID)
    {
        if (!activePopAdSlots.ContainsKey(slotID)) return;

        int targetGameIndex = activePopAdSlots[slotID];
        Sprite spriteToShow = GetAdSprite(targetGameIndex, EnumsJar_Salon.AdPlacementType.PopAd);

        if (spriteToShow != null)
        {
            OnPopAdRefreshed?.Invoke(slotID, targetGameIndex, spriteToShow);
        }
        else
        {
            Logger.Warn($"Missing Pop Ad image for game: {runtimeCatalog[targetGameIndex].gameName}");
        }
    }

    public void StopPopAdCycle()
    {
        popAdCycleActive = false;
        popAdCTS?.Cancel();
        popAdCTS?.Dispose();
        popAdCTS = null;
        Logger.Note("Pop ad cycle stopped.");
    }

    #endregion

    #region Banner Logic

    public void StartBannerCycle()
    {
        if (PlayerPrefs.GetInt(removeAdsPlayerPrefKey, 0) == 1) return;

        Logger.Note("Initializing banner ad cycle...");

        bannerCTS?.Cancel();
        bannerCTS?.Dispose();
        bannerCTS = null;
        bannerCTS = new CancellationTokenSource();

        _ = StartBannerCycling(bannerCTS.Token);
    }

    private async Awaitable StartBannerCycling(CancellationToken token)
    {
        Logger.Note("Starting banner ad cycle...");

        try
        {
            while (!token.IsCancellationRequested)
            {
                ChangeBannerAd();
                await Awaitable.WaitForSecondsAsync(bannerCycleSpeed, token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Note("Banner Ad cycle was successfully cancelled.");
        }
    }

    public void ChangeBannerAd()
    {
        if (IsAdsRemoved)
        {
            HideBanner();
            return;
        }

        if (bannerAdUI == null || runtimeCatalog.Count == 0 || PlayerPrefs.GetInt(removeAdsPlayerPrefKey, 0) == 1) return;

        currentBannerIndex = bannerSequence == EnumsJar_Salon.AdSequenceType.Sequential
            ? (currentBannerIndex + 1) % runtimeCatalog.Count
            : UnityEngine.Random.Range(0, runtimeCatalog.Count);

        Sprite spriteToShow = GetAdSprite(currentBannerIndex, EnumsJar_Salon.AdPlacementType.Banner);

        if (spriteToShow != null)
        {
            bannerAdUI.bannerImage.color = Color.white;
            bannerAdUI.bannerImage.sprite = spriteToShow;
            if (!bannerAdUI.gameObject.activeInHierarchy) bannerAdUI.gameObject.SetActive(true);

            Logger.Note($"Banner ad changed to: {runtimeCatalog[currentBannerIndex].gameName}");

            GameAdEntry entry = runtimeCatalog[currentBannerIndex];
            string gameName = entry.gameName;

            SEHandler.Instance.LogEvent("Shown" + gameName);
            FbAnalytics.Instance.LogEvent(gameName + "_Banner_S");
        }
        else
        {
            Logger.Warn($"Missing Banner image for game: {runtimeCatalog[currentBannerIndex].gameName}");
        }
    }

    public void ShowBanner()
    {
        if (PlayerPrefs.GetInt(removeAdsPlayerPrefKey, 0) == 1) return;

        if (!bannerCycleActive) StartBannerCycle();

        if (bannerAdUI != null)
            bannerAdUI.gameObject.SetActive(true);
        else this.Error("No Banner UI instance found while trying to use it");
    }

    public void HideBanner()
    {
        if (bannerAdUI != null)
            bannerAdUI.gameObject.SetActive(false);
        else this.Error("No Banner UI instance found while trying to use it");

        StopBannerCycle();
    }

    public void StopBannerCycle()
    {
        bannerCycleActive = false;
        bannerCTS?.Cancel();
        bannerCTS?.Dispose();
        bannerCTS = null;

        Logger.Note("Banner ad cycle stopped.");
    }

    #endregion

    #region Interstitial Logic

    public void ShowInterstitial()
    {
        bool abortCondition = runtimeCatalog == null || runtimeCatalog.Count == 0 || interstitialAdUI == null || IsAdsRemoved || IsRewardedAdShowing;
        if (abortCondition) return;

        // 1. Propose an initial index based on your sequencing rules
        int proposedIndex = interstitialSequence == EnumsJar_Salon.AdSequenceType.Sequential
            ? (currentInterstitialIndex + 1) % runtimeCatalog.Count
            : UnityEngine.Random.Range(0, runtimeCatalog.Count);

        if (interstitialMediaType == EnumsJar_Salon.AdMediaType.Image)
        {
            currentInterstitialIndex = proposedIndex;
            GameAdEntry entry = runtimeCatalog[currentInterstitialIndex];
            string gameName = entry.gameName;

            SEHandler.Instance.LogEvent("Shown" + gameName);
            FbAnalytics.Instance.LogEvent(gameName + "_Inter_S");

            Sprite spriteToShow = GetAdSprite(currentInterstitialIndex, EnumsJar_Salon.AdPlacementType.Inter);
            if (spriteToShow != null)
            {
                interstitialAdUI.SetupImage(spriteToShow);
                interstitialAdUI.gameObject.SetActive(true);
                Logger.Note($"Showing interstitial image ad for: {gameName}");
            }
            else
            {
                Logger.Warn($"Missing Interstitial image for game: {gameName}");
            }
        }
        else if (interstitialMediaType == EnumsJar_Salon.AdMediaType.Video)
        {
            // 2. Pass the proposed index. If it finds a video elsewhere, 'proposedIndex' gets updated automatically.
            if (TryGetVideoAdData(ref proposedIndex, false, out UnityEngine.Video.VideoClip localClip, out string remoteUrl))
            {
                currentInterstitialIndex = proposedIndex; // Sync the global index so clicks map correctly
                GameAdEntry entry = runtimeCatalog[currentInterstitialIndex];
                string gameName = entry.gameName;

                SEHandler.Instance.LogEvent("Shown" + gameName);
                FbAnalytics.Instance.LogEvent(gameName + "_Inter_S");

                interstitialAdUI.SetupVideo(localClip, remoteUrl);
                interstitialAdUI.gameObject.SetActive(true);
                Logger.Note($"Showing interstitial video ad for: {gameName}");
            }
            else
            {
                Logger.Warn("No Interstitial videos (remote or local) found in the ENTIRE catalog.");
            }
        }

        _ = WaitAfterInter();
    }

    private async Awaitable WaitAfterInter()
    {
        while (interstitialAdUI.gameObject.activeSelf == true)
        {
            await Awaitable.EndOfFrameAsync();
        }

        Logger.Note("Interstitial Ended! Ready to go...");
    }

    #endregion

    #region Rewarded Logic
    public void ShowRewardedAd(Action onSuccess, Action onFail)
    {
        if (runtimeCatalog == null || runtimeCatalog.Count == 0 || rewardedAdUI == null)
        {
            Logger.Warn("Cannot show Rewarded Ad. Catalog empty or UI missing.");
            onFail?.Invoke();
            return;
        }

        int proposedIndex = rewardedSequence == EnumsJar_Salon.AdSequenceType.Sequential
            ? (currentRewardedIndex + 1) % runtimeCatalog.Count
            : UnityEngine.Random.Range(0, runtimeCatalog.Count);

        if (rewardedMediaType == EnumsJar_Salon.AdMediaType.Image)
        {
            Sprite spriteToShow = null;
            int finalIndex = proposedIndex;
            int totalGames = runtimeCatalog.Count;

            // Loop through the catalog to find at least ONE game with a valid rewarded image
            for (int i = 0; i < totalGames; i++)
            {
                int checkIndex = (proposedIndex + i) % totalGames;
                spriteToShow = GetAdSprite(checkIndex, EnumsJar_Salon.AdPlacementType.Rewarded);

                if (spriteToShow != null)
                {
                    finalIndex = checkIndex;
                    break;
                }
            }

            if (spriteToShow != null)
            {
                currentRewardedIndex = finalIndex;
                GameAdEntry entry = runtimeCatalog[currentRewardedIndex];
                string gameName = entry.gameName;

                // Logs only fire when we CONFIRM we have a sprite to show
                SEHandler.Instance?.LogEvent("Shown" + gameName);
                FbAnalytics.Instance?.LogEvent(gameName + "_Rewarded_S");
                Logger.Note($"Showing rewarded image ad for: {gameName}");

                rewardedAdUI.SetupImageAd(spriteToShow, onSuccess, onFail);
                rewardedAdUI.gameObject.SetActive(true);
            }
            else
            {
                // Fails loudly if the entire catalog lacks rewarded images
                this.Error("No Rewarded images found in the ENTIRE catalog. Ad failed.");
                onFail?.Invoke();
            }
        }
        else if (rewardedMediaType == EnumsJar_Salon.AdMediaType.Video)
        {
            if (TryGetVideoAdData(ref proposedIndex, true, out UnityEngine.Video.VideoClip localClip, out string remoteUrl))
            {
                currentRewardedIndex = proposedIndex;
                GameAdEntry entry = runtimeCatalog[currentRewardedIndex];
                string gameName = entry.gameName;

                SEHandler.Instance?.LogEvent("Shown" + gameName);
                FbAnalytics.Instance?.LogEvent(gameName + "_Rewarded_S");

                rewardedAdUI.SetupVideoAd(localClip, onSuccess, onFail, remoteUrl);
                rewardedAdUI.gameObject.SetActive(true);
                Logger.Note($"Showing rewarded video ad for: {gameName}");
            }
            else
            {
                this.Error("No Rewarded videos (remote or local) found in the ENTIRE catalog. Ad failed.");
                onFail?.Invoke();
            }
        }
    }
    #endregion

    #region Large Banner Logic

    public void ShowLargeBanner()
    {
        if (runtimeCatalog == null || runtimeCatalog.Count == 0 || largeBannerAdUI == null || PlayerPrefs.GetInt(removeAdsPlayerPrefKey, 0) == 1) return;

        currentLargeBannerIndex = largeBannerSequence == EnumsJar_Salon.AdSequenceType.Sequential
            ? (currentLargeBannerIndex + 1) % runtimeCatalog.Count
            : UnityEngine.Random.Range(0, runtimeCatalog.Count);

        Sprite spriteToShow = GetAdSprite(currentLargeBannerIndex, EnumsJar_Salon.AdPlacementType.LBanner);

        GameAdEntry entry = runtimeCatalog[currentLargeBannerIndex];
        string gameName = entry.gameName;

        SEHandler.Instance.LogEvent("Shown" + gameName);
        FbAnalytics.Instance.LogEvent(gameName + "_BigBanner_S");

        if (spriteToShow != null)
        {
            largeBannerAdUI.image.sprite = spriteToShow;
            largeBannerAdUI.gameObject.SetActive(true);
        }
        else
        {
            Logger.Warn($"Missing Large Banner image for game: {runtimeCatalog[currentLargeBannerIndex].gameName}");
        }
    }
    public void HideLargeBanner()
    {
        _ = DelayHideLB();
    }

    public async Awaitable DelayHideLB()
    {
        while (largeBannerAdUI.gameObject.activeInHierarchy)
        {
            await Awaitable.EndOfFrameAsync();
            if (largeBannerAdUI != null && largeBannerAdUI.gameObject.activeInHierarchy)
            {
                largeBannerAdUI.gameObject.SetActive(false);
            }
        }
    }


    #endregion

    #region Redirection Logic

    private void ProcessAdClick(int index, EnumsJar_Salon.AdPlacementType adType)
    {
        if (runtimeCatalog == null || index < 0 || index >= runtimeCatalog.Count) return;

        GameAdEntry entry = runtimeCatalog[index];
        string gameName = entry.gameName;
        string package = entry.gamePackageName;

        string prefKey = $"AdClicks_{package}_{adType}";

        int currentClicks = PlayerPrefs.GetInt(prefKey, 0);
        currentClicks++;
        PlayerPrefs.SetInt(prefKey, currentClicks);
        PlayerPrefs.Save();

        string eventMsg = $"{gameName}_{adType}_C";

        FbAnalytics.Instance.LogEvent(eventMsg);
        Logger.Note($"Recorded ad click event: {eventMsg}");

        if (platformType == EnumsJar_Salon.PlatformType.Android)
        {
            Application.OpenURL($"market://details?id={package}");
        }
        else if (platformType == EnumsJar_Salon.PlatformType.Amazon)
        {
            if (SEHandler.Instance != null)
            {
                SEHandler.Instance.LogEvent(eventMsg);
            }
            else
            {
                Logger.Warn("Solar Engine Handler instance is null. Event not logged externally.");
            }

            Application.OpenURL($"amzn://apps/android?initiatePurchaseFlow=true&asin={entry.asin}");
        }
        else if (platformType == EnumsJar_Salon.PlatformType.iOS)
        {
            Application.OpenURL($"https://apps.apple.com/app/id{package}");
        }
    }

    #endregion

    #region Utility/Helpers

    private Sprite GetAdSprite(int gameIndex, EnumsJar_Salon.AdPlacementType placementType)
    {
        GameAdEntry entry = runtimeCatalog[gameIndex];
        List<Sprite> images = null;
        int currentIndex = 0;

        switch (placementType)
        {
            case EnumsJar_Salon.AdPlacementType.Banner:
                images = entry.gameBannerImages;
                currentIndex = gameBannerImageIndices[gameIndex];
                break;
            case EnumsJar_Salon.AdPlacementType.Inter:
                images = entry.gameInterstitialImages;
                currentIndex = gameInterstitialImageIndices[gameIndex];
                break;
            case EnumsJar_Salon.AdPlacementType.Rewarded:
                images = entry.gameRewardedImages;
                currentIndex = gameRewardedImageIndices[gameIndex];
                break;
            case EnumsJar_Salon.AdPlacementType.LBanner:
                images = entry.gameLargeBannerImages;
                currentIndex = gameLargeBannerImageIndices[gameIndex];
                break;
            case EnumsJar_Salon.AdPlacementType.PopAd:
                images = entry.gamePopAdImages;
                currentIndex = gamePopAdImageIndices[gameIndex];
                break;
        }

        if (images == null || images.Count == 0) return null;

        Sprite selectedSprite = null;

        if (entry.gameImageSequence == EnumsJar_Salon.AdSequenceType.Sequential)
        {
            selectedSprite = images[currentIndex];
            currentIndex = (currentIndex + 1) % images.Count;
        }
        else
        {
            int randomIndex = UnityEngine.Random.Range(0, images.Count);
            selectedSprite = images[randomIndex];
            currentIndex = randomIndex;
        }

        switch (placementType)
        {
            case EnumsJar_Salon.AdPlacementType.Banner:
                gameBannerImageIndices[gameIndex] = currentIndex;
                break;
            case EnumsJar_Salon.AdPlacementType.Inter:
                gameInterstitialImageIndices[gameIndex] = currentIndex;
                break;
            case EnumsJar_Salon.AdPlacementType.LBanner:
                gameLargeBannerImageIndices[gameIndex] = currentIndex;
                break;
            case EnumsJar_Salon.AdPlacementType.PopAd:
                gamePopAdImageIndices[gameIndex] = currentIndex;
                break;
        }

        return selectedSprite;
    }

    private bool TryGetVideoAdData(ref int gameIndex, bool isRewarded, out UnityEngine.Video.VideoClip localClip, out string remoteUrl)
    {
        localClip = null;
        remoteUrl = null;

        // Determine priority based on the CatalogLoader's inspector toggles
        bool prioritizeRemote = true;
        if (CatalogLoader.Instance != null)
        {
            prioritizeRemote = isRewarded
                ? CatalogLoader.Instance.prioritizeRemoteRewarded
                : CatalogLoader.Instance.prioritizeRemoteInterstitial;
        }

        // Execute searches in the order dictated by the Inspector
        if (prioritizeRemote)
        {
            if (CheckRemoteVideos(ref gameIndex, isRewarded, out remoteUrl)) return true;
            if (CheckLocalVideos(ref gameIndex, isRewarded, out localClip)) return true; // Fallback to local
        }
        else
        {
            if (CheckLocalVideos(ref gameIndex, isRewarded, out localClip)) return true;
            if (CheckRemoteVideos(ref gameIndex, isRewarded, out remoteUrl)) return true; // Fallback to remote
        }

        return false; // Total failure
    }

    private bool CheckRemoteVideos(ref int gameIndex, bool isRewarded, out string remoteUrl)
    {
        remoteUrl = null;
        int totalGames = runtimeCatalog.Count;

        for (int i = 0; i < totalGames; i++)
        {
            int checkIndex = (gameIndex + i) % totalGames;
            var remoteUrls = isRewarded ? runtimeCatalog[checkIndex].remoteRewardedVideoURLs : runtimeCatalog[checkIndex].remoteInterstitialVideoURLs;

            if (remoteUrls != null && remoteUrls.Count > 0)
            {
                gameIndex = checkIndex;
                int count = remoteUrls.Count;
                int subIndex = isRewarded ? gameRewardedVideoIndices[gameIndex] : gameInterstitialVideoIndices[gameIndex];

                if (runtimeCatalog[gameIndex].gameImageSequence != EnumsJar_Salon.AdSequenceType.Sequential)
                    subIndex = UnityEngine.Random.Range(0, count);
                else if (subIndex >= count)
                    subIndex = 0;

                remoteUrl = remoteUrls[subIndex];

                // Step the index forward for next time
                if (runtimeCatalog[gameIndex].gameImageSequence == EnumsJar_Salon.AdSequenceType.Sequential)
                {
                    if (isRewarded) gameRewardedVideoIndices[gameIndex] = (subIndex + 1) % count;
                    else gameInterstitialVideoIndices[gameIndex] = (subIndex + 1) % count;
                }
                return true;
            }
        }
        return false;
    }

    private bool CheckLocalVideos(ref int gameIndex, bool isRewarded, out UnityEngine.Video.VideoClip localClip)
    {
        localClip = null;
        int totalGames = runtimeCatalog.Count;

        for (int i = 0; i < totalGames; i++)
        {
            int checkIndex = (gameIndex + i) % totalGames;
            var entry = runtimeCatalog[checkIndex];

            var localVideos = isRewarded
                ? (entry.gameRewardedVideos != null ? new System.Collections.Generic.List<UnityEngine.Video.VideoClip>(entry.gameRewardedVideos) : null)
                : entry.gameInterstitialVideos;

            if (localVideos != null && localVideos.Count > 0)
            {
                gameIndex = checkIndex;
                int count = localVideos.Count;
                int subIndex = isRewarded ? gameRewardedVideoIndices[gameIndex] : gameInterstitialVideoIndices[gameIndex];

                if (runtimeCatalog[gameIndex].gameImageSequence != EnumsJar_Salon.AdSequenceType.Sequential)
                    subIndex = UnityEngine.Random.Range(0, count);
                else if (subIndex >= count)
                    subIndex = 0;

                localClip = localVideos[subIndex];

                // Step the index forward for next time
                if (runtimeCatalog[gameIndex].gameImageSequence == EnumsJar_Salon.AdSequenceType.Sequential)
                {
                    if (isRewarded) gameRewardedVideoIndices[gameIndex] = (subIndex + 1) % count;
                    else gameInterstitialVideoIndices[gameIndex] = (subIndex + 1) % count;
                }
                return true;
            }
        }
        return false;
    }

    private void SnapToPosition(RectTransform rectTransform, EnumsJar_Salon.AdPosition position)
    {
        if (rectTransform == null) return;

        Vector2 anchorAndPivot = Vector2.zero;

        switch (position)
        {
            // Top Row
            case EnumsJar_Salon.AdPosition.TopLeft: anchorAndPivot = new Vector2(0f, 1f); break;
            case EnumsJar_Salon.AdPosition.TopCenter: anchorAndPivot = new Vector2(0.5f, 1f); break;
            case EnumsJar_Salon.AdPosition.TopRight: anchorAndPivot = new Vector2(1f, 1f); break;

            // Middle Row
            case EnumsJar_Salon.AdPosition.LeftCenter: anchorAndPivot = new Vector2(0f, 0.5f); break;
            case EnumsJar_Salon.AdPosition.MiddleCenter: anchorAndPivot = new Vector2(0.5f, 0.5f); break;
            case EnumsJar_Salon.AdPosition.RightCenter: anchorAndPivot = new Vector2(1f, 0.5f); break;

            // Bottom Row
            case EnumsJar_Salon.AdPosition.BottomLeft: anchorAndPivot = new Vector2(0f, 0f); break;
            case EnumsJar_Salon.AdPosition.BottomCenter: anchorAndPivot = new Vector2(0.5f, 0f); break;
            case EnumsJar_Salon.AdPosition.BottomRight: anchorAndPivot = new Vector2(1f, 0f); break;
        }

        rectTransform.anchorMin = anchorAndPivot;
        rectTransform.anchorMax = anchorAndPivot;
        rectTransform.pivot = anchorAndPivot;

        rectTransform.anchoredPosition = Vector2.zero;
    }

    #endregion
}