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
    private readonly RectTransform[] arms = new RectTransform[4];
    private readonly Image[] hitMarks = new Image[4];
    private float bloom, hitRemaining, killRemaining;
    private PlayerHealth health;
    private WeaponInventory inventory;

    void Awake()
    {
        BuildCrosshair();
    }

    void Start() { health = GetComponent<PlayerHealth>(); inventory = GetComponent<WeaponInventory>(); }

    public void NotifyShot() => bloom = Mathf.Min(5f, bloom + 2.5f);
    public void NotifyHit(bool killed)
    {
        hitRemaining = .14f;
        if (killed) killRemaining = .24f;
    }
    public void ResetFeedback()
    {
        bloom = hitRemaining = killRemaining = 0f;
        foreach (Image mark in hitMarks) if (mark != null) mark.color = Color.clear;
    }

    void OnDisable()
    {
        ResetFeedback();
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    void Update()
    {
        if (canvasGroup != null)
        {
            bool visible = !onlyWhenCursorLocked ||
                           Cursor.lockState == CursorLockMode.Locked;
            visible &= Time.timeScale > 0f && !DungeonVictoryUI.IsOpen &&
                (health == null || (!health.IsDead && !health.IsChoosingUpgrade)) &&
                (inventory == null || (!inventory.IsOpen && !inventory.IsPaused));
            canvasGroup.alpha = visible ? 1f : 0f;
        }
        bloom = Mathf.MoveTowards(bloom, 0f, Time.deltaTime * 30f);
        hitRemaining = Mathf.Max(0f, hitRemaining - Time.deltaTime);
        killRemaining = Mathf.Max(0f, killRemaining - Time.deltaTime);
        float gap = centerGap + lineLength * .5f + bloom;
        arms[0].anchoredPosition = new Vector2(0, gap);
        arms[1].anchoredPosition = new Vector2(0, -gap);
        arms[2].anchoredPosition = new Vector2(-gap, 0);
        arms[3].anchoredPosition = new Vector2(gap, 0);
        Color tint = killRemaining > 0f ? GameUITheme.Gold : GameUITheme.Ivory;
        tint.a = killRemaining > 0f ? Mathf.Clamp01(killRemaining / .08f) : Mathf.Clamp01(hitRemaining / .05f);
        foreach (Image mark in hitMarks) mark.color = tint;
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
        crosshairCanvas.transform.SetParent(transform, false);
        crosshairCanvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;

        arms[0] = CreateLine("Top", new Vector2(0f, centerGap + lineLength * 0.5f),
            new Vector2(thickness, lineLength));
        arms[1] = CreateLine("Bottom", new Vector2(0f, -centerGap - lineLength * 0.5f),
            new Vector2(thickness, lineLength));
        arms[2] = CreateLine("Left", new Vector2(-centerGap - lineLength * 0.5f, 0f),
            new Vector2(lineLength, thickness));
        arms[3] = CreateLine("Right", new Vector2(centerGap + lineLength * 0.5f, 0f),
            new Vector2(lineLength, thickness));

        if (showCenterDot)
            CreateLine("CenterDot", Vector2.zero,
                new Vector2(centerDotSize, centerDotSize));
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + i * 90f;
            Vector2 direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            RectTransform mark = CreateLine("Hit Marker", direction * 22f, new Vector2(9f, 2f));
            mark.localRotation = Quaternion.Euler(0, 0, angle);
            hitMarks[i] = mark.GetComponent<Image>();
            hitMarks[i].color = Color.clear;
        }
    }

    RectTransform CreateLine(string objectName, Vector2 position, Vector2 size)
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
        return rectTransform;
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
