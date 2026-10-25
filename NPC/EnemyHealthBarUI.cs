using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(EnemyHealth))]
public class EnemyHealthBarUI : MonoBehaviour
{
    public Vector3 worldOffset = new Vector3(0f, 1.5f, 0f);
    [Min(0.1f)] public float worldWidth = 1.2f;
    [Min(0.01f)] public float worldHeight = 0.12f;
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.75f);
    public Color healthColor = new Color(0.9f, 0.1f, 0.1f, 1f);
    public Color levelTextColor = Color.white;
    [Min(1)] public int levelFontSize = 24;
    public bool hideWhenFull = false;
    [Tooltip("Target that the health bar and level text face. The Player is found automatically when left empty.")]
    public Transform facingTarget;

    private EnemyHealth health;
    private Canvas healthCanvas;
    private RectTransform fillRect;
    private Text levelText;
    private BossController boss;
    private RectTransform[] phaseFills;
    private Image[] phaseImages;

    void Awake()
    {
        backgroundColor = GameUITheme.Recess;
        healthColor = GameUITheme.Health;
        levelTextColor = GameUITheme.Ivory;
        health = GetComponent<EnemyHealth>();
        FindFacingTarget();
        BuildHealthBar();
        if (boss == null) boss = GetComponent<BossController>();
        if (boss != null) SetBossPhases(boss);
    }

    void LateUpdate()
    {
        if (health == null || fillRect == null)
            return;

        float normalizedHealth = Mathf.Clamp01(health.HealthNormalized);
        fillRect.localScale = new Vector3(normalizedHealth, 1f, 1f);
        if (boss != null && phaseFills != null)
        {
            for (int i = 0; i < phaseFills.Length; i++)
            {
                float remaining = i + 1 < boss.Phase ? 0f : i + 1 == boss.Phase ? normalizedHealth : 1f;
                phaseFills[i].localScale = new Vector3(remaining, 1f, 1f);
                phaseImages[i].color = i + 1 == boss.Phase ? boss.FormColor :
                    new Color(.35f, .3f, .4f, .8f);
            }
            levelText.text = $"{boss.FormName}　{boss.Phase}/{BossController.PhaseCount}\n" +
                (boss.IsTransforming ? "型態轉換中" : $"{Mathf.CeilToInt(health.CurrentHealth):N0} / {Mathf.CeilToInt(health.maxHealth):N0}");
            levelText.color = boss.FormColor;
        }

        if (healthCanvas != null)
            healthCanvas.enabled = boss != null || !hideWhenFull || normalizedHealth < 0.999f;

        if (facingTarget == null)
            FindFacingTarget();

        if (facingTarget != null && healthCanvas != null)
        {
            Vector3 direction = healthCanvas.transform.position - facingTarget.position;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.0001f)
                healthCanvas.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }

    void FindFacingTarget()
    {
        Player player = FindFirstObjectByType<Player>();
        if (player != null)
            facingTarget = player.transform;
    }

    void BuildHealthBar()
    {
        GameObject canvasObject = new GameObject(
            "EnemyHealthCanvas",
            typeof(RectTransform),
            typeof(Canvas)
        );
        canvasObject.transform.SetParent(transform, false);
        canvasObject.transform.localPosition = worldOffset;

        healthCanvas = canvasObject.GetComponent<Canvas>();
        healthCanvas.renderMode = RenderMode.WorldSpace;
        healthCanvas.sortingOrder = 10;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(100f, 10f);
        canvasRect.localScale = new Vector3(
            worldWidth / 100f,
            worldHeight / 10f,
            1f
        );

        RectTransform background = CreateImage(
            "Background",
            canvasRect,
            backgroundColor
        );
        background.anchorMin = Vector2.zero;
        background.anchorMax = Vector2.one;
        background.offsetMin = Vector2.zero;
        background.offsetMax = Vector2.zero;

        fillRect = CreateImage("Fill", background, healthColor);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.offsetMin = new Vector2(3f, 2f);
        fillRect.offsetMax = new Vector2(-3f, -2f);

        GameObject textObject = new GameObject(
            "LevelText",
            typeof(RectTransform),
            typeof(Text)
        );
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(canvasRect, false);
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 0f);
        textRect.anchoredPosition = new Vector2(0f, 4f);
        textRect.sizeDelta = new Vector2(140f, 36f);

        levelText = textObject.GetComponent<Text>();
        levelText.font = GameUIFont.Default;
        levelText.fontSize = levelFontSize;
        levelText.fontStyle = FontStyle.Bold;
        levelText.alignment = TextAnchor.MiddleCenter;
        levelText.color = levelTextColor;
        levelText.raycastTarget = false;
        SetLevel(1);
    }

    public void SetLevel(int level)
    {
        if (levelText != null)
            levelText.text = $"等級 {Mathf.Max(1, level)}";
    }

    public void SetBossLevel(int level)
    {
        if (levelText != null)
            levelText.text = $"首領　等級 {Mathf.Max(1, level)}";
    }

    public void SetBossPresentation(float height)
    {
        worldOffset = Vector3.up * height;
        worldWidth = 2.6f;
        worldHeight = 0.16f;
        levelTextColor = new Color(1f, 0.72f, 0.3f);
        if (healthCanvas != null)
        {
            healthCanvas.transform.localPosition = worldOffset +
                (phaseFills != null ? Vector3.up * (worldHeight * 2.6f) : Vector3.zero);
            healthCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 20f);
            healthCanvas.transform.localScale = new Vector3(worldWidth / 200f, worldHeight / 20f, 1f);
        }
        if (levelText != null) levelText.color = levelTextColor;
    }

    public void SetBossPhases(BossController controller)
    {
        boss = controller;
        if (boss == null || healthCanvas == null || phaseFills != null) return;
        healthCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 20f);
        healthCanvas.transform.localScale = new Vector3(worldWidth / 200f, worldHeight / 20f, 1f);
        phaseFills = new RectTransform[BossController.PhaseCount];
        phaseImages = new Image[BossController.PhaseCount];
        phaseFills[0] = fillRect;
        fillRect.offsetMin = new Vector2(6f, 4f);
        fillRect.offsetMax = new Vector2(-6f, -4f);
        phaseImages[0] = fillRect.GetComponent<Image>();
        for (int i = 1; i < phaseFills.Length; i++)
        {
            RectTransform background = CreateImage("Phase " + (i + 1), healthCanvas.transform, backgroundColor);
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = new Vector2(0f, -i * 26f);
            background.offsetMax = new Vector2(0f, -i * 26f);
            RectTransform fill = CreateImage("Fill", background, healthColor);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, .5f);
            fill.offsetMin = new Vector2(6f, 4f);
            fill.offsetMax = new Vector2(-6f, -4f);
            phaseFills[i] = fill;
            phaseImages[i] = fill.GetComponent<Image>();
        }
        // Keep the lowest of the three bars above the horns.
        healthCanvas.transform.localPosition = worldOffset + Vector3.up * (worldHeight * 2.6f);
        levelText.fontSize = 24;
        levelText.rectTransform.anchoredPosition = new Vector2(0f, 8f);
        levelText.rectTransform.sizeDelta = new Vector2(100f, 72f);
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

    void OnValidate()
    {
        worldWidth = Mathf.Max(0.1f, worldWidth);
        worldHeight = Mathf.Max(0.01f, worldHeight);
        levelFontSize = Mathf.Max(1, levelFontSize);
    }
}
