using UnityEngine;
using UnityEngine.Networking;

public class SpriteDownloader
{
    // Changed return type to Awaitable<Sprite> and removed the callback parameter
    public static async Awaitable<Sprite> LoadSpriteAsync(string url)
    {
        // Wrapped the UnityWebRequest in a using block to ensure proper memory disposal
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            await request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(request);

                Sprite sprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f)
                );

                return sprite; // Return the created sprite directly
            }
            else
            {
                // Added request.error to provide more context on why it failed
                Debug.LogError($"Failed to download sprite: {url}. Error: {request.error}");
                return null;   // Return null if the download fails
            }
        }
    }
}