using UnityEngine;
using UnityEngine.Video; // Don't forget this, or Unity will throw a fit

public class RenderTexture_Switch : MonoBehaviour
{
    [Tooltip("The RenderTexture you want to resize dynamically.")]
    public RenderTexture targetRenderTexture;

    [Tooltip("The VideoPlayer we are feeding the texture to.")]
    public VideoPlayer targetVideoPlayer;

    [Tooltip("If true, the render texture will automatically resize to match the screen resolution.")]
    public bool changeResolutionOnRuntime = true;

    private int lastScreenWidth;
    private int lastScreenHeight;

    void Start()
    {
        // Auto-fetch VideoPlayer if it's sitting on the same GameObject
        if (targetVideoPlayer == null)
        {
            targetVideoPlayer = GetComponent<VideoPlayer>();
        }

        if (targetRenderTexture != null)
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            if (changeResolutionOnRuntime)
            {
                UpdateRenderTextureResolution();
            }
            else
            {
                // Even if we don't resize, we still need to assign it at startup
                AssignToVideoPlayer();
            }
        }
        else
        {
            Debug.LogWarning("You forgot to assign a RenderTexture to the script!");
        }
    }

    //void Update()
    //{
    //    if (changeResolutionOnRuntime && targetRenderTexture != null)
    //    {
    //        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
    //        {
    //            UpdateRenderTextureResolution();
    //        }
    //    }
    //}

    private void UpdateRenderTextureResolution()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        targetRenderTexture.Release();
        targetRenderTexture.width = lastScreenWidth;
        targetRenderTexture.height = lastScreenHeight;
        targetRenderTexture.Create();

        Debug.Log($"RenderTexture resized to: {lastScreenWidth}x{lastScreenHeight}");

        AssignToVideoPlayer();
    }

    private void AssignToVideoPlayer()
    {
        if (targetVideoPlayer != null && targetRenderTexture != null)
        {
            // Force the video player to use Render Texture mode and hand it the goods
            targetVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            targetVideoPlayer.targetTexture = targetRenderTexture;
        }
    }
}