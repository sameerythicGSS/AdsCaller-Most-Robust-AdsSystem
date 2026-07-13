using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Video;

[System.Serializable]
public struct GameAdEntry
{
    [Header("Info Section")]
    [Tooltip("The display name of the game.")]
    public string gameName;

    [Tooltip("The bundle/package identifier (e.g., com.studio.gamename).")]
    public string gamePackageName;

    [Tooltip("Asin is required when it belongs to Amazon App Store.")]
    public string asin;

    [Space(10)]
    [Header("Ad Flow")]
    [Tooltip("Determines how images for this specific game are sequenced.")]
    public EnumsJar_Salon.AdSequenceType gameImageSequence;

    [Space(10)]
    [Header("Images Section (Multi-Image Support)")]
    public List<Sprite> gameBannerImages;

    [Space]
    public List<Sprite> gameInterstitialImages;
    [Tooltip("[Optional] Salon Ads Caller uses it if its Interstitial Media Type is set to Video")]
    public List<VideoClip> gameInterstitialVideos;
    [Space]

    [Space(10)]
    [Header("Rewarded Section")]
    public List<Sprite> gameRewardedImages;
    [Tooltip("[Optional] Salon Ads Caller uses it if its Rewarded Media Type is set to Video")]
    public UnityEngine.Video.VideoClip[] gameRewardedVideos;
    [Space]

    public List<Sprite> gameLargeBannerImages;
    public List<Sprite> gamePopAdImages;
    //public List<Sprite> gameLevelAdImages;

    [Space(10)]
    [Header("Remote Media URLs")]
    public List<string> remoteInterstitialVideoURLs;
    public List<string> remoteRewardedVideoURLs;

}