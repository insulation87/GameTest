using UnityEngine;
using UnityEngine.UI;

/// <summary>Screen overlay HUD with the same layout and scaling as a player's viewport.</summary>
[RequireComponent(typeof(Canvas)), DisallowMultipleComponent, DefaultExecutionOrder(12000)]
public sealed class GameUIViewport : MonoBehaviour
{
    Canvas canvas;
    CanvasScaler scaler;
    RectTransform content;
    Rect viewport = new Rect(0, 0, 1, 1);

    public void SetViewport(Rect rect, int display)
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        if (content == null)
        {
            scaler = GetComponent<CanvasScaler>();
            var node = new GameObject("Player UI Viewport", typeof(RectTransform), typeof(RectMask2D));
            content = node.GetComponent<RectTransform>();
            content.SetParent(transform, false);
        }
        viewport = rect;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.targetDisplay = display;
        // The world texture is on the default sorting layer at short.MinValue.
        canvas.sortingLayerID = 0;
        // Also adopt UI created later, such as upgrade choices and dropdowns.
        content.SetAsLastSibling();
        while (transform.childCount > 1)
        {
            Transform child = transform.GetChild(0);
            var rectTransform = child as RectTransform;
            Vector3 position = rectTransform != null ? rectTransform.anchoredPosition3D : Vector3.zero;
            child.SetParent(content, false);
            // SetParent preserves local position, but anchors must stay relative
            // to the new viewport rather than the previous full-screen center.
            if (rectTransform != null) rectTransform.anchoredPosition3D = position;
        }
        RefreshLayout();
    }

    void LateUpdate() { if (canvas != null && content != null) RefreshLayout(); }

    void RefreshLayout()
    {
        content.anchorMin = viewport.min;
        content.anchorMax = viewport.max;
        content.offsetMin = content.offsetMax = Vector2.zero;
        if (scaler == null) return;

        // CanvasScaler normally measures the whole display. Measure the player's
        // half instead, so switching from camera UI does not change its layout.
        scaler.enabled = false;
        Vector2 size = new Vector2(Screen.width * viewport.width, Screen.height * viewport.height);
        float scale = scaler.scaleFactor;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            float width = size.x / Mathf.Max(1, scaler.referenceResolution.x);
            float height = size.y / Mathf.Max(1, scaler.referenceResolution.y);
            switch (scaler.screenMatchMode)
            {
                case CanvasScaler.ScreenMatchMode.Expand: scale = Mathf.Min(width, height); break;
                case CanvasScaler.ScreenMatchMode.Shrink: scale = Mathf.Max(width, height); break;
                default:
                    scale = Mathf.Pow(2, Mathf.Lerp(Mathf.Log(Mathf.Max(.001f, width), 2),
                        Mathf.Log(Mathf.Max(.001f, height), 2), scaler.matchWidthOrHeight));
                    break;
            }
        }
        canvas.scaleFactor = Mathf.Max(.001f, scale);
        canvas.referencePixelsPerUnit = scaler.referencePixelsPerUnit;
    }
}
