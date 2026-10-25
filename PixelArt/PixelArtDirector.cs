using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Camera and UI presentation. Map materials are authored and saved in Edit Mode.</summary>
[DefaultExecutionOrder(10000)]
public sealed class PixelArtDirector : MonoBehaviour
{
    readonly HashSet<Canvas> styledCanvases = new HashSet<Canvas>();
    float nextScan;
    static PixelArtDirector instance;
    CameraSwitch gameplayViews;

    public static Camera ActiveGameplayCamera => instance != null && instance.gameplayViews != null
        ? instance.gameplayViews.ActiveCamera : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<PixelArtDirector>() != null) return;
        var node = new GameObject("Pixel Art Presentation", typeof(PixelArtDirector));
        DontDestroyOnLoad(node);
    }

    void Awake()
    {
        instance = this;
        SceneManager.sceneLoaded += SceneLoaded;
        Scan();
    }

    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        styledCanvases.RemoveWhere(c => c == null);
        nextScan = 0;
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + .75f;
        Scan();
    }

    void Scan()
    {
        gameplayViews = FindFirstObjectByType<CameraSwitch>();
        if (gameplayViews != null && (gameplayViews.fpsCamera == null || gameplayViews.tpsCamera == null)) gameplayViews = null;
        styledCanvases.RemoveWhere(c => c == null);
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Map1 also has a stationary Main Camera. A full-screen texture from that
            // camera must never cover the player's moving first/third-person view.
            if (CoopSession.IsMultiplayer)
            {
                bool owned = false;
                foreach (Player player in CoopSession.Players())
                    if (player.GetComponent<CoopActor>() != null && player.GetComponent<CoopActor>().Local &&
                        (camera == player.fpsCamera || camera == player.tpsCamera)) owned = true;
                if (!owned) continue;
            }
            else if (gameplayViews != null && camera != gameplayViews.fpsCamera && camera != gameplayViews.tpsCamera) continue;
            if (camera.cameraType != CameraType.Game || camera.targetTexture != null ||
                camera.GetComponentInParent<PixelArtCamera>(true) != null) continue;
            // Render textures used by minimaps and previews remain owned by their original components.
            if (camera.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data) &&
                data.renderType == UnityEngine.Rendering.Universal.CameraRenderType.Overlay) continue;
            camera.gameObject.AddComponent<PixelArtCamera>();
        }
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!styledCanvases.Add(canvas) || canvas.GetComponentInParent<PixelArtCamera>() != null) continue;
            canvas.pixelPerfect = true;
            // Retain semantic colors (rarity, health, minimap fog) and existing event handlers.
            foreach (Button button in canvas.GetComponentsInChildren<Button>(true))
                if (button.transform.Find("Theme Frame") == null) GameUITheme.StyleButton(button);
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        SceneManager.sceneLoaded -= SceneLoaded;
    }
}
