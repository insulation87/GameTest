using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerHealthBarUI : MonoBehaviour
{
    public Vector2 screenPosition = new Vector2(30f, -30f);
    public Vector2 barSize = new Vector2(300f, 24f);
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.75f);
    public Color healthColor = new Color(0.15f, 0.85f, 0.25f, 1f);
    public Color experienceColor = new Color(0.2f, 0.55f, 1f, 1f);
    public Color armorColor = new Color(0.2f, 0.75f, 0.95f, 1f);

    private PlayerHealth health;
    private RectTransform fillRect;
    private Canvas healthCanvas;
    private RectTransform experienceFillRect;
    private RectTransform armorFillRect;
    private Text levelText;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        BuildHealthBar();
    }

    void Update()
    {
        if (health == null || fillRect == null)
            return;

        fillRect.localScale = new Vector3(
            Mathf.Clamp01(health.HealthNormalized),
            1f,
            1f
        );

        if (experienceFillRect != null)
        {
            experienceFillRect.localScale = new Vector3(
                Mathf.Clamp01(health.ExperienceNormalized),
                1f,
                1f
            );
        }

        if (armorFillRect != null)
        {
            armorFillRect.localScale = new Vector3(
                Mathf.Clamp01(health.ArmorNormalized),
                1f,
                1f
            );
        }

        if (levelText != null)
        {
            levelText.text = health.CurrentLevel >= health.maxPlayerLevel
                ? $"等級 {health.CurrentLevel}　已達滿級"
                : $"等級 {health.CurrentLevel}　經驗 {health.CurrentExperience}/{health.ExperienceToNextLevel}";
        }
    }

    void BuildHealthBar()
    {
        GameObject canvasObject = new GameObject(
            "PlayerHealthCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup)
        );

        healthCanvas = canvasObject.GetComponent<Canvas>();
        healthCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        healthCanvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup group = canvasObject.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        RectTransform background = CreateImage(
            "Background",
            healthCanvas.transform,
            backgroundColor
        );
        background.anchorMin = new Vector2(0f, 1f);
        background.anchorMax = new Vector2(0f, 1f);
        background.pivot = new Vector2(0f, 1f);
        background.anchoredPosition = screenPosition;
        background.sizeDelta = barSize;

        fillRect = CreateImage("Fill", background, healthColor);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);

        RectTransform armorBackground = CreateImage(
            "ArmorBackground",
            healthCanvas.transform,
            backgroundColor
        );
        armorBackground.anchorMin = new Vector2(0f, 1f);
        armorBackground.anchorMax = new Vector2(0f, 1f);
        armorBackground.pivot = new Vector2(0f, 1f);
        armorBackground.anchoredPosition = screenPosition + new Vector2(0f, -30f);
        armorBackground.sizeDelta = new Vector2(barSize.x, 12f);

        armorFillRect = CreateImage("ArmorFill", armorBackground, armorColor);
        armorFillRect.anchorMin = Vector2.zero;
        armorFillRect.anchorMax = Vector2.one;
        armorFillRect.pivot = new Vector2(0f, 0.5f);
        armorFillRect.offsetMin = new Vector2(2f, 2f);
        armorFillRect.offsetMax = new Vector2(-2f, -2f);

        RectTransform experienceBackground = CreateImage(
            "ExperienceBackground",
            healthCanvas.transform,
            backgroundColor
        );
        experienceBackground.anchorMin = new Vector2(0f, 1f);
        experienceBackground.anchorMax = new Vector2(0f, 1f);
        experienceBackground.pivot = new Vector2(0f, 1f);
        experienceBackground.anchoredPosition = screenPosition + new Vector2(0f, -48f);
        experienceBackground.sizeDelta = new Vector2(barSize.x, 12f);

        experienceFillRect = CreateImage("ExperienceFill", experienceBackground, experienceColor);
        experienceFillRect.anchorMin = Vector2.zero;
        experienceFillRect.anchorMax = Vector2.one;
        experienceFillRect.pivot = new Vector2(0f, 0.5f);
        experienceFillRect.offsetMin = new Vector2(2f, 2f);
        experienceFillRect.offsetMax = new Vector2(-2f, -2f);

        GameObject levelObject = new GameObject(
            "PlayerLevelText",
            typeof(RectTransform),
            typeof(Text)
        );
        RectTransform levelRect = levelObject.GetComponent<RectTransform>();
        levelRect.SetParent(healthCanvas.transform, false);
        levelRect.anchorMin = new Vector2(0f, 1f);
        levelRect.anchorMax = new Vector2(0f, 1f);
        levelRect.pivot = new Vector2(0f, 1f);
        levelRect.anchoredPosition = screenPosition + new Vector2(0f, -64f);
        levelRect.sizeDelta = new Vector2(Mathf.Max(barSize.x, 420f), 30f);

        levelText = levelObject.GetComponent<Text>();
        levelText.font = GameUIFont.Default;
        levelText.fontSize = 20;
        levelText.fontStyle = FontStyle.Bold;
        levelText.alignment = TextAnchor.UpperLeft;
        levelText.color = Color.white;
        levelText.raycastTarget = false;
    }

    RectTransform CreateImage(string objectName, Transform parent, Color imageColor)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image)
        );
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = imageObject.GetComponent<Image>();
        image.color = imageColor;
        image.raycastTarget = false;
        return rect;
    }

    void OnDestroy()
    {
        if (healthCanvas != null)
            Destroy(healthCanvas.gameObject);
    }
}
