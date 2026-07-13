using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

[Serializable]
public class RemoteGameEntry
{
    public string gameName;
    public string packageName;
    public string asin;

    public string bannerImages;

    public string interstitialImages;
    public string interstitialVideos;

    public string rewardedImages;
    public string rewardedVideos;

    public string bigbannerImage;
    public string popImages;

    public string Screenorientation;
}

[Serializable]
public class CrossPromoEntry
{
    public string packageName;
    public List<string> asins;
}

/// <summary>
/// Handles downloading and caching cross-promo game catalogs and their associated ad media.
/// </summary>
public class CatalogLoader : MonoBehaviour
{
    public static CatalogLoader Instance;

    #region Inspector Configurations

    [Header("Local Backup Catalog")]
    public GameCatalog localCatalog;

    [Header("Google Sheet JSON URLs")]
    public string sheet1CrossPromoURL;
    public string sheet2GameDataURL;

    [Header("Game Settings")]
    public bool isPortrait = true;

    [Header("Video Priority Settings")]
    [Tooltip("If true, searches for remote Interstitial URLs first. If false, searches local VideoClips first.")]
    public bool prioritizeRemoteInterstitial = true;

    [Tooltip("If true, searches for remote Rewarded URLs first. If false, searches local VideoClips first.")]
    public bool prioritizeRemoteRewarded = true;

    [Tooltip("If remote data is missing videos, allow it to pull local VideoClips from the local backup catalog instead of failing.")]
    public bool allowLocalVideoFallback = true;

    #endregion

    #region Runtime Data

    public List<GameAdEntry> runtimeCatalog = new List<GameAdEntry>();
    private List<GameAdEntry> tempRemoteCatalog = new List<GameAdEntry>();

    [HideInInspector]
    public bool IsCatalogReady { get; private set; } = false;

    #endregion

    #region Initialization

    private async void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Logger.Note("Initializing RemoteGameCatalogLoader with local backup catalog data.");

            runtimeCatalog = new List<GameAdEntry>(localCatalog.gameEntries);
            IsCatalogReady = true;

            if (AdsCallerSalon.Instance != null)
            {
                AdsCallerSalon.Instance.CheckAndInitialize();
            }

            await LoadCatalogAsync();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    #region Catalog Loading

    /// <summary>
    /// Fetches the mapping and game data sheets, filters for the current package, and triggers image caching.
    /// </summary>
    private async Awaitable LoadCatalogAsync()
    {
        using UnityWebRequest request1 = UnityWebRequest.Get(sheet1CrossPromoURL);
        await request1.SendWebRequest();

        Dictionary<string, List<string>> crossPromoMap = new Dictionary<string, List<string>>();

        if (request1.result == UnityWebRequest.Result.Success)
        {
            try
            {
                Logger.Note("Cross-promo mapping (Sheet 1) downloaded successfully.");
                crossPromoMap = ParseCrossPromoSheet(request1.downloadHandler.text);
            }
            catch (Exception e)
            {
                Logger.Warn("Failed to parse cross-promo mapping (Sheet 1): " + e.Message);
            }
        }
        else
        {
            Logger.Warn("Failed to download cross-promo mapping (Sheet 1). Proceeding with fallback.");
        }

        using UnityWebRequest request2 = UnityWebRequest.Get(sheet2GameDataURL);
        await request2.SendWebRequest();

        List<RemoteGameEntry> sheet2Entries = new List<RemoteGameEntry>();

        if (request2.result == UnityWebRequest.Result.Success)
        {
            try
            {
                Logger.Note("Remote game data (Sheet 2) downloaded successfully.");
                sheet2Entries = JsonConvert.DeserializeObject<List<RemoteGameEntry>>(request2.downloadHandler.text);
            }
            catch (Exception e)
            {
                Logger.Warn("Failed to parse remote game data (Sheet 2): " + e.Message);
            }
        }
        else
        {
            Logger.Warn("Failed to download remote game data (Sheet 2). Falling back to local catalog.");
            return;
        }

        Logger.Note("Filtering remote entries based on current package identifier.");

        if (sheet2Entries.Count > 0)
        {
            string currentPackage = Application.identifier;
            Logger.Note($"Current package identifier: {currentPackage}");

            if (crossPromoMap.TryGetValue(currentPackage, out var allowedAsins))
            {
                string targetOrientation = isPortrait ? "portrait" : "landscape";

                var filteredEntries = sheet2Entries
                    .Where(x => allowedAsins.Contains(x.asin) &&
                                x.Screenorientation != null &&
                                x.Screenorientation.ToLower() == targetOrientation)
                    .ToList();

                if (filteredEntries.Count == 0)
                {
                    Logger.Warn("No remote ads found matching the current orientation. Retaining local catalog.");
                    return;
                }

                tempRemoteCatalog.Clear();
                List<Awaitable> imageDownloadTasks = new List<Awaitable>();

                foreach (var remote in filteredEntries)
                {
                    GameAdEntry localMatch = new GameAdEntry();
                    bool hasLocalMatch = false;

                    if (localCatalog != null && localCatalog.gameEntries != null)
                    {
                        var match = localCatalog.gameEntries.FirstOrDefault(x => x.gamePackageName == remote.packageName);
                        if (!string.IsNullOrEmpty(match.gamePackageName))
                        {
                            localMatch = match;
                            hasLocalMatch = true;
                        }
                    }

                    GameAdEntry entry = new GameAdEntry
                    {
                        gameName = remote.gameName,
                        gamePackageName = remote.packageName,
                        asin = remote.asin,
                        gameBannerImages = new List<Sprite>(),
                        gameInterstitialImages = new List<Sprite>(),
                        gameRewardedImages = new List<Sprite>(),
                        gameLargeBannerImages = new List<Sprite>(),
                        gamePopAdImages = new List<Sprite>(),
                        remoteInterstitialVideoURLs = new List<string>(),
                        remoteRewardedVideoURLs = new List<string>()
                    };

                    if (!string.IsNullOrEmpty(remote.interstitialVideos))
                    {
                        string[] iVids = remote.interstitialVideos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var v in iVids) entry.remoteInterstitialVideoURLs.Add(v.Trim());
                    }
                    else
                    {
                        Logger.Warn($"[CatalogLoader] Remote data missing Interstitial Video for {remote.gameName}.");
                        if (allowLocalVideoFallback && hasLocalMatch && localMatch.gameInterstitialVideos != null && localMatch.gameInterstitialVideos.Count > 0)
                        {
                            entry.gameInterstitialVideos = localMatch.gameInterstitialVideos;
                        }
                    }

                    if (!string.IsNullOrEmpty(remote.rewardedVideos))
                    {
                        string[] rVids = remote.rewardedVideos.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var v in rVids) entry.remoteRewardedVideoURLs.Add(v.Trim());
                    }
                    else
                    {
                        Logger.Warn($"[CatalogLoader] Remote data missing Rewarded Video for {remote.gameName}.");
                        if (allowLocalVideoFallback && hasLocalMatch && localMatch.gameRewardedVideos != null && localMatch.gameRewardedVideos.Length > 0)
                        {
                            entry.gameRewardedVideos = localMatch.gameRewardedVideos;
                        }
                    }

                    tempRemoteCatalog.Add(entry);

                    Logger.Note($"Loading remote images for game: {remote.gameName}");

                    imageDownloadTasks.Add(LoadImagesAsync(remote.bannerImages, entry.gameBannerImages, remote.gameName, "Banners"));
                    imageDownloadTasks.Add(LoadImagesAsync(remote.interstitialImages, entry.gameInterstitialImages, remote.gameName, "Interstitials"));
                    imageDownloadTasks.Add(LoadImagesAsync(remote.rewardedImages, entry.gameRewardedImages, remote.gameName, "Rewarded"));
                    imageDownloadTasks.Add(LoadImagesAsync(remote.bigbannerImage, entry.gameLargeBannerImages, remote.gameName, "BigBanners"));
                    imageDownloadTasks.Add(LoadImagesAsync(remote.popImages, entry.gamePopAdImages, remote.gameName, "PopAds"));
                }

                foreach (var task in imageDownloadTasks)
                {
                    await task;
                }

                runtimeCatalog = new List<GameAdEntry>(tempRemoteCatalog);

                if (AdsCallerSalon.Instance != null)
                {
                    Logger.Note("Successfully applied remote catalog and re-initialized AdsCaller.");
                    AdsCallerSalon.Instance.SetRuntimeCatalog(runtimeCatalog);

                    if (AdsCallerSalon.Instance.canInitialize)
                        AdsCallerSalon.Instance.CheckAndInitialize();
                }
            }
            else
            {
                Logger.Warn("No cross-promo mapping found for this package. Retaining local catalog.");
            }
        }
    }

    private Dictionary<string, List<string>> ParseCrossPromoSheet(string json)
    {
        List<Dictionary<string, string>> rawList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
        Dictionary<string, List<string>> map = new Dictionary<string, List<string>>();

        foreach (var row in rawList)
        {
            foreach (var kv in row)
            {
                string pkg = kv.Key;
                string asinsRaw = kv.Value;
                if (!string.IsNullOrEmpty(pkg) && !string.IsNullOrEmpty(asinsRaw))
                {
                    var asins = asinsRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim())
                                        .ToList();

                    if (!map.ContainsKey(pkg))
                        map.Add(pkg, asins);
                }
            }
        }

        return map;
    }

    #endregion

    #region Image Loading & Caching

    /// <summary>
    /// Downloads images from the provided URLs. In the Editor, caches them locally for future use.
    /// Ensures files are saved with valid extensions and unique names to prevent overwriting.
    /// </summary>
    private async Awaitable LoadImagesAsync(string urls, List<Sprite> targetList, string gameName, string adForm)
    {
        if (string.IsNullOrEmpty(urls)) return;

        string[] split = urls.Split(',');

        for (int i = 0; i < split.Length; i++)
        {
            string url = split[i].Trim();
            if (string.IsNullOrEmpty(url)) continue;

            // Bulletproof the filename. Default to Image_i.png so we guarantee an extension and unique ID.
            string fileName = $"Image_{i}.png";
            try
            {
                Uri uri = new Uri(url);
                string extractedName = Path.GetFileName(uri.LocalPath);

                // Only use the extracted name if it actually looks like a real file (e.g., "banner.jpg").
                if (!string.IsNullOrEmpty(extractedName) && Path.HasExtension(extractedName))
                {
                    // Append the index anyway to completely kill the chance of a naming collision.
                    fileName = $"{Path.GetFileNameWithoutExtension(extractedName)}_{i}{Path.GetExtension(extractedName)}";
                }
            }
            catch { }

#if UNITY_EDITOR
            string safeGameName = string.Join("_", gameName.Split(Path.GetInvalidFileNameChars()));
            string dirPath = $"Assets/AdsCaller/SalonAds/Database/Games/{safeGameName}/{adForm}";
            string filePath = $"{dirPath}/{fileName}";

            if (File.Exists(filePath))
            {
                Sprite localSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(filePath);
                if (localSprite != null)
                {
                    targetList.Add(localSprite);
                    continue; // Skip the web request entirely if we already have it locally
                }
            }
#endif

            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                await request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    if (texture != null)
                    {
#if UNITY_EDITOR
                        try
                        {
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);

                            // texture.EncodeToPNG() physically writes a PNG format, so our forced .png extension is perfectly valid.
                            File.WriteAllBytes(filePath, texture.EncodeToPNG());
                        }
                        catch (Exception e)
                        {
                            Logger.Warn($"Failed to save local texture cache: {e.Message}");
                        }
#endif
                        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                        targetList.Add(sprite);
                    }
                }
                else
                {
                    Logger.Warn($"Failed to download sprite from URL: {url}");
                }
            }
        }
    }

    #endregion
}