using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
    [Header("Appearance")]
    public Color color = Color.white;
    [Min(1f)] public float lineLength = 10f;
    [Min(1f)] public float thickness = 2f;
    [Min(0f)] public float centerGap = 5f;
    public bool showCenterDot = true;
    [Min(1f)] public float centerDotSize = 2f;

    [Header("Visibility")]
    public bool onlyWhenCursorLocked = true;

    private Canvas crosshairCanvas;
    private CanvasGroup canvasGroup;

    void Awake()
    {
        BuildCrosshair();
    }

    void Update()
    {
        if (canvasGroup != null)
        {
            bool visible = !onlyWhenCursorLocked ||
                           Cursor.lockState == CursorLockMode.Locked;
            canvasGroup.alpha = visible ? 1f : 0f;
        }
    }

    void BuildCrosshair()
    {
        GameObject canvasObject = new GameObject(
            "CrosshairCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup)
        );

        crosshairCanvas = canvasObject.GetComponent<Canvas>();
        crosshairCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        crosshairCanvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        CreateLine("Top", new Vector2(0f, centerGap + lineLength * 0.5f),
            new Vector2(thickness, lineLength));
        CreateLine("Bottom", new Vector2(0f, -centerGap - lineLength * 0.5f),
            new Vector2(thickness, lineLength));
        CreateLine("Left", new Vector2(-centerGap - lineLength * 0.5f, 0f),
            new Vector2(lineLength, thickness));
        CreateLine("Right", new Vector2(centerGap + lineLength * 0.5f, 0f),
            new Vector2(lineLength, thickness));

        if (showCenterDot)
            CreateLine("CenterDot", Vector2.zero,
                new Vector2(centerDotSize, centerDotSize));
    }

    void CreateLine(string objectName, Vector2 position, Vector2 size)
    {
        GameObject lineObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image)
        );

        RectTransform rectTransform = lineObject.GetComponent<RectTransform>();
        rectTransform.SetParent(crosshairCanvas.transform, false);
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;

        Image image = lineObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    void OnDestroy()
    {
        if (crosshairCanvas != null)
            Destroy(crosshairCanvas.gameObject);
    }

    void OnValidate()
    {
        lineLength = Mathf.Max(1f, lineLength);
        thickness = Mathf.Max(1f, thickness);
        centerGap = Mathf.Max(0f, centerGap);
        centerDotSize = Mathf.Max(1f, centerDotSize);
    }
}
