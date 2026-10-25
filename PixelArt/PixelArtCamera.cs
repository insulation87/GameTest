using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Point-filtered world presentation. Overlay UI stays readable at the display resolution.</summary>
[RequireComponent(typeof(Camera)), DisallowMultipleComponent]
[DefaultExecutionOrder(11000)]
public sealed class PixelArtCamera : MonoBehaviour
{
    [Tooltip("Render at the window's full resolution for clear distant geometry. Disable for the original coarse pixel presentation.")]
    public bool nativeResolution = true;
    public Rect outputRect = new Rect(0, 0, 1, 1);
    [Range(180, 540)] public int referenceHeight = 360;
    Camera view;
    RenderTexture pixels;
    GameObject display;
    RawImage image;
    Camera screenOutput;
    UniversalAdditionalCameraData data;
    bool oldHDR, oldMSAA, oldPost;
    AntialiasingMode oldAA;
    float oldAspect;
    Rect oldRect;
    bool ownsView;

    void OnEnable()
    {
        view = GetComponent<Camera>();
        // Never take over an application-owned render target.
        if (view.targetTexture != null) { enabled = false; return; }
        ownsView = true;
        oldHDR = view.allowHDR; oldMSAA = view.allowMSAA; oldAspect = view.aspect;
        oldRect = view.rect;
        view.allowHDR = view.allowMSAA = false;
        data = GetComponent<UniversalAdditionalCameraData>();
        if (data != null)
        {
            oldPost = data.renderPostProcessing; oldAA = data.antialiasing;
            data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
        }
        // Game View requires an enabled camera targeting the display even when overlay UI
        // presents the world texture. Keep this camera empty; it only clears the backbuffer.
        var output = new GameObject("Pixel Screen Output", typeof(Camera));
        output.transform.SetParent(transform, false);
        screenOutput = output.GetComponent<Camera>();
        screenOutput.cullingMask = 0;
        screenOutput.clearFlags = CameraClearFlags.SolidColor;
        screenOutput.backgroundColor = GameUITheme.Ink;
        screenOutput.allowHDR = screenOutput.allowMSAA = false;
        screenOutput.depth = view.depth + 1;
        screenOutput.targetDisplay = view.targetDisplay;
        var outputData = output.AddComponent<UniversalAdditionalCameraData>();
        outputData.renderType = CameraRenderType.Base;
        outputData.renderPostProcessing = false;
        outputData.antialiasing = AntialiasingMode.None;
        outputData.renderShadows = false;
        outputData.requiresDepthTexture = outputData.requiresColorTexture = false;
        display = new GameObject("Pixel World Display", typeof(RectTransform), typeof(Canvas));
        display.transform.SetParent(transform, false);
        Canvas canvas = display.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MinValue;
        canvas.pixelPerfect = true;
        canvas.targetDisplay = view.targetDisplay;
        var node = new GameObject("World Pixels", typeof(RectTransform), typeof(RawImage));
        node.transform.SetParent(display.transform, false);
        image = node.GetComponent<RawImage>();
        image.raycastTarget = false;
        GameUITheme.Stretch(image.rectTransform);
        Resize();
    }

    void LateUpdate()
    {
        if (image == null) return;
        Camera gameplayCamera = PixelArtDirector.ActiveGameplayCamera;
        bool presented = view.isActiveAndEnabled && (CoopSession.IsMultiplayer ? CoopSession.Presents(view) : gameplayCamera == null || gameplayCamera == view);
        if (display.activeSelf != presented) display.SetActive(presented);
        if (screenOutput != null && screenOutput.gameObject.activeSelf != presented) screenOutput.gameObject.SetActive(presented);
        if (presented) Resize();
    }

    void Resize()
    {
        // An integer multiplier gives every texel equal screen coverage, also at 4:3 and ultrawide.
        float viewportWidth = Screen.width * outputRect.width, viewportHeight = Screen.height * outputRect.height;
        int multiplier = nativeResolution ? 1 : Mathf.Max(1, Mathf.RoundToInt(viewportHeight / Mathf.Max(1,referenceHeight)));
        int width = Mathf.Max(1, Mathf.CeilToInt(viewportWidth / multiplier));
        int height = Mathf.Max(1, Mathf.CeilToInt(viewportHeight / multiplier));
        if (pixels == null || pixels.width != width || pixels.height != height)
        {
            ReleaseTexture();
            pixels = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Pixel World " + width + "x" + height,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1, useMipMap = false, hideFlags = HideFlags.DontSave
            };
            pixels.Create();
            view.targetTexture = pixels;
            image.texture = pixels;
        }
        view.rect = new Rect(0, 0, 1, 1);
        view.aspect = viewportWidth / Mathf.Max(1, viewportHeight);
        screenOutput.rect = outputRect;
        // Overscan by at most one texel so uneven window dimensions do not stretch texels.
        RectTransform rect = image.rectTransform;
        rect.anchorMin = outputRect.min; rect.anchorMax = outputRect.max;
        float extraWidth = width * multiplier - viewportWidth;
        float extraHeight = height * multiplier - viewportHeight;
        rect.offsetMin = new Vector2(-extraWidth * .5f, -extraHeight * .5f);
        rect.offsetMax = new Vector2(extraWidth * .5f, extraHeight * .5f);
    }

    void ReleaseTexture()
    {
        if (pixels == null) return;
        if (view != null && view.targetTexture == pixels) view.targetTexture = null;
        pixels.Release(); Destroy(pixels); pixels = null;
    }

    void OnDisable()
    {
        if (screenOutput != null) { screenOutput.gameObject.SetActive(false); Destroy(screenOutput.gameObject); }
        screenOutput = null;
        if (display != null) { display.SetActive(false); Destroy(display); }
        display = null; image = null;
        ReleaseTexture();
        if (view == null || !ownsView) return;
        view.allowHDR = oldHDR; view.allowMSAA = oldMSAA; view.aspect = oldAspect;
        view.rect = oldRect;
        if (data != null) { data.renderPostProcessing = oldPost; data.antialiasing = oldAA; }
        ownsView = false;
    }
}
