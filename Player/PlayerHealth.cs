using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    enum UpgradeType
    {
        Inventory,
        Attack,
        CriticalDamage,
        BulletPiercing,
        DoubleJump,
        MoveSpeed,
        MaxArmor,
        ArmorRegeneration
    }

    class UpgradeOption
    {
        public UpgradeType type;
        public string title;
        public string description;
        public float bonusAmount;
    }

    [Header("Health")]
    [Min(1f)] public float maxHealth = 100f;

    [Header("Level and Experience")]
    [Min(1)] public int startingLevel = 1;
    [Min(1)] public int maxPlayerLevel = 100;
    [Min(1)] public int baseExperienceToLevel = 100;
    [Min(1f)] public float experienceGrowth = 1.25f;

    [Header("Armor")]
    [Min(0f)] public float startingMaxArmor = 25f;
    [Min(0f)] public float startingArmorRegeneration = 2f;
    [Min(0f)] public float armorRegenerationDelay = 3f;

    [Header("Upgrade Bonuses")]
    [Min(1f)] public float attackMultiplier = 1f;
    [Min(1f)] public float moveSpeedMultiplier = 1f;
    [Range(0f, 1f)] public float criticalChance = 0.1f;
    [Min(1f)] public float criticalDamageMultiplier = 1.5f;
    [Min(0)] public int bulletPenetration;
    public bool hasDoubleJump;

    [Header("Death")]
    public bool respawnOnDeath = true;
    public Transform respawnPoint;
    [Min(0f)] public float respawnDelay = 3f;
    [Min(0f)] public float respawnInvincibilityDuration = 2f;
    public bool pauseWorldWhileDead = true;

    [Header("Respawn UI")]
    public GameObject respawnCanvas;
    public Text respawnText;
    public string deathMessage = "你已死亡";

    private float currentHealth;
    private int currentLevel;
    private int currentExperience;
    private Rigidbody playerRigidbody;
    private Player playerController;
    private PlayerShooter playerShooter;
    private Coroutine respawnRoutine;
    private bool playerControllerWasEnabled;
    private bool playerShooterWasEnabled;
    private Vector3 initialSpawnPosition;
    private Quaternion initialSpawnRotation;
    private float invincibleUntil;
    private float currentArmor;
    private float maxArmor;
    private float armorRegeneration;
    private float lastDamageTime = float.NegativeInfinity;
    private int pendingUpgradeChoices;
    private GameObject upgradeCanvas;
    private GameObject upgradePanel;
    private Text upgradeTitle;
    private readonly List<Button> upgradeButtons = new List<Button>();
    private float timeScaleBeforeUpgrade = 1f;
    private float timeScaleBeforeDeath = 1f;
    private bool worldIsPausedByDeath;

    public float CurrentHealth => currentHealth;
    public float HealthNormalized => currentHealth / maxHealth;
    public float CurrentArmor => currentArmor;
    public float MaxArmor => maxArmor;
    public float ArmorNormalized => maxArmor > 0f ? currentArmor / maxArmor : 0f;
    public float ArmorRegeneration => armorRegeneration;
    public float AttackMultiplier => attackMultiplier;
    public float MoveSpeedMultiplier => moveSpeedMultiplier;
    public float CriticalChance => criticalChance;
    public float CriticalDamageMultiplier => criticalDamageMultiplier;
    public int BulletPenetration => bulletPenetration;
    public bool HasDoubleJump => hasDoubleJump;
    public bool IsChoosingUpgrade => upgradePanel != null && upgradePanel.activeSelf;
    public bool IsDead { get; private set; }
    public bool IsInvincible => Time.time < invincibleUntil;
    public int CurrentLevel => currentLevel;
    public int CurrentExperience => currentExperience;
    public int ExperienceToNextLevel => GetExperienceRequired(currentLevel);
    public float ExperienceNormalized => currentLevel >= maxPlayerLevel
        ? 1f
        : (float)currentExperience / ExperienceToNextLevel;

    void Awake()
    {
        playerRigidbody = GetComponent<Rigidbody>();
        playerController = GetComponent<Player>();
        playerShooter = GetComponent<PlayerShooter>();
        initialSpawnPosition = transform.position;
        initialSpawnRotation = transform.rotation;
        currentLevel = Mathf.Clamp(startingLevel, 1, maxPlayerLevel);
        currentExperience = 0;
        maxArmor = Mathf.Max(0f, startingMaxArmor);
        currentArmor = maxArmor;
        armorRegeneration = Mathf.Max(0f, startingArmorRegeneration);
        PrepareRespawnCanvas();
        ResetHealth();
    }

    void Update()
    {
        RegenerateArmor();
    }

    public void TakeDamage(float damage)
    {
        if (damage <= 0f || IsDead || IsInvincible)
            return;

        WeaponBuildSystem build = GetComponent<WeaponBuildSystem>();
        if (build != null)
            damage *= build.IncomingDamageMultiplier;

        lastDamageTime = Time.time;
        float absorbedDamage = Mathf.Min(currentArmor, damage);
        currentArmor -= absorbedDamage;
        currentHealth = Mathf.Max(0f, currentHealth - (damage - absorbedDamage));
        GameAudio.Local(absorbedDamage > 0f ? (currentArmor <= 0f ? GameSound.ArmorBreak : GameSound.ArmorHit) : GameSound.PlayerHurt);

        if (currentHealth <= 0f)
            Die();
    }

    public void KillInstantly()
    {
        if (!IsDead)
        {
            currentArmor = 0f;
            currentHealth = 0f;
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || IsDead)
            return;

        if (currentHealth < maxHealth) GameAudio.UI(GameSound.Heal);
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }

    public void ApplyEquipmentArmorBonus(float difference)
    {
        if (Mathf.Approximately(difference, 0f))
            return;

        maxArmor = Mathf.Max(0f, maxArmor + difference);
        if (difference > 0f)
            currentArmor = Mathf.Min(maxArmor, currentArmor + difference);
        else
            currentArmor = Mathf.Min(currentArmor, maxArmor);
    }

    public float CalculateOutgoingDamage(float baseDamage)
    {
        float damage = Mathf.Max(0f, baseDamage) * attackMultiplier;
        if (Random.value < criticalChance)
            damage *= criticalDamageMultiplier;

        return damage;
    }

    public void AddExperience(int amount)
    {
        if (amount <= 0 || currentLevel >= maxPlayerLevel)
            return;

        int previousLevel = currentLevel;
        currentExperience += amount;

        while (currentLevel < maxPlayerLevel &&
               currentExperience >= ExperienceToNextLevel)
        {
            currentExperience -= ExperienceToNextLevel;
            currentLevel++;

            if (currentLevel % 5 == 0)
                pendingUpgradeChoices++;
        }

        if (currentLevel >= maxPlayerLevel)
            currentExperience = 0;

        if (currentLevel > previousLevel) GameAudio.UI(GameSound.LevelUp);
        if (pendingUpgradeChoices > 0 && !IsDead && !IsChoosingUpgrade)
            ShowNextUpgradeChoice();
    }

    void RegenerateArmor()
    {
        if (IsDead || currentArmor >= maxArmor || armorRegeneration <= 0f)
            return;

        if (Time.time < lastDamageTime + armorRegenerationDelay)
            return;

        currentArmor = Mathf.Min(
            maxArmor,
            currentArmor + armorRegeneration * Time.deltaTime
        );
        if (currentArmor >= maxArmor) GameAudio.Local(GameSound.ArmorRestored);
    }

    int GetExperienceRequired(int level)
    {
        return Mathf.Max(
            1,
            Mathf.RoundToInt(
                baseExperienceToLevel * Mathf.Pow(experienceGrowth, level - 1)
            )
        );
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        currentArmor = maxArmor;
        IsDead = false;
    }

    void Die()
    {
        GameAudio.UI(GameSound.PlayerDeath);
        IsDead = true;

        if (!respawnOnDeath)
        {
            gameObject.SetActive(false);
            return;
        }

        SetPlayerControlEnabled(false);
        SetRespawnCanvasVisible(true);
        PauseWorldForDeath();

        if (playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }

        if (respawnRoutine != null)
            StopCoroutine(respawnRoutine);

        respawnRoutine = StartCoroutine(RespawnAfterDelay());
    }

    IEnumerator RespawnAfterDelay()
    {
        float remainingTime = respawnDelay;
        UpdateRespawnText(remainingTime);

        while (remainingTime > 0f)
        {
            yield return null;
            remainingTime -= Time.unscaledDeltaTime;
            UpdateRespawnText(remainingTime);
        }

        Vector3 targetPosition = respawnPoint != null
            ? respawnPoint.position
            : initialSpawnPosition;
        Quaternion targetRotation = respawnPoint != null
            ? respawnPoint.rotation
            : initialSpawnRotation;

        if (playerRigidbody != null)
        {
            playerRigidbody.position = targetPosition;
            playerRigidbody.rotation = targetRotation;
            playerRigidbody.linearVelocity = Vector3.zero;
            playerRigidbody.angularVelocity = Vector3.zero;
        }
        else
        {
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        ResumeWorldAfterDeath();
        ResetHealth();
        GameAudio.UI(GameSound.Respawn);
        invincibleUntil = Time.time + respawnInvincibilityDuration;
        SetPlayerControlEnabled(true);
        SetRespawnCanvasVisible(false);
        respawnRoutine = null;
    }

    void PauseWorldForDeath()
    {
        if (!pauseWorldWhileDead || worldIsPausedByDeath)
            return;

        timeScaleBeforeDeath = Time.timeScale;
        Time.timeScale = 0f;
        worldIsPausedByDeath = true;
    }

    void ResumeWorldAfterDeath()
    {
        if (!worldIsPausedByDeath)
            return;

        Time.timeScale = timeScaleBeforeDeath;
        WeaponInventory weaponInventory = GetComponent<WeaponInventory>();
        if (weaponInventory != null)
            weaponInventory.HandleDeathPauseEnded(timeScaleBeforeDeath);

        worldIsPausedByDeath = false;
    }

    void OnDisable()
    {
        CloseUpgradeChoice(false);
        ResumeWorldAfterDeath();
    }

    void ShowNextUpgradeChoice()
    {
        if (pendingUpgradeChoices <= 0)
            return;

        BuildUpgradeCanvas();
        PopulateUpgradeChoices();

        timeScaleBeforeUpgrade = Time.timeScale;
        Time.timeScale = 0f;
        upgradePanel.SetActive(true);
        RefreshCursor();
    }

    void BuildUpgradeCanvas()
    {
        if (upgradeCanvas != null)
            return;

        upgradeCanvas = new GameObject(
            "Player Upgrade Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );

        Canvas canvas = upgradeCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;

        CanvasScaler scaler = upgradeCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        upgradePanel = CreateUpgradeImage(
            "Upgrade Selection",
            upgradeCanvas.transform,
            new Vector2(1180f, 580f),
            new Color(0.04f, 0.06f, 0.09f, 0.97f)
        );
        RectTransform panelRect = upgradePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);

        upgradeTitle = CreateUpgradeText(
            "Title",
            upgradePanel.transform,
            "",
            36,
            TextAnchor.MiddleCenter,
            new Vector2(0f, 220f),
            new Vector2(1080f, 70f)
        );

        CreateUpgradeText(
            "Hint",
            upgradePanel.transform,
            "選擇一項永久升級",
            22,
            TextAnchor.MiddleCenter,
            new Vector2(0f, 165f),
            new Vector2(1080f, 45f)
        );

        for (int buttonIndex = 0; buttonIndex < 3; buttonIndex++)
        {
            Button button = CreateUpgradeButton(
                upgradePanel.transform,
                new Vector2(-380f + buttonIndex * 380f, -35f),
                new Vector2(330f, 330f)
            );
            upgradeButtons.Add(button);
        }

        upgradePanel.SetActive(false);
    }

    void PopulateUpgradeChoices()
    {
        int tier = Mathf.Clamp(1 + currentLevel / 20, 1, 6);
        List<UpgradeType> types = new List<UpgradeType>
        {
            UpgradeType.Inventory,
            UpgradeType.Attack,
            UpgradeType.CriticalDamage,
            UpgradeType.BulletPiercing,
            UpgradeType.MoveSpeed,
            UpgradeType.MaxArmor,
            UpgradeType.ArmorRegeneration
        };

        if (!hasDoubleJump)
            types.Add(UpgradeType.DoubleJump);

        for (int typeIndex = 0; typeIndex < types.Count; typeIndex++)
        {
            int swapIndex = Random.Range(typeIndex, types.Count);
            UpgradeType typeBeingSwapped = types[typeIndex];
            types[typeIndex] = types[swapIndex];
            types[swapIndex] = typeBeingSwapped;
        }

        upgradeTitle.text = $"等級 {currentLevel} 升級選擇  ·  品質階級 {tier}";

        for (int buttonIndex = 0; buttonIndex < upgradeButtons.Count; buttonIndex++)
        {
            UpgradeOption option = CreateUpgradeOption(types[buttonIndex], tier);
            Button button = upgradeButtons[buttonIndex];
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectUpgrade(option));

            Text label = button.GetComponentInChildren<Text>();
            label.text = $"{option.title}\n\n{option.description}";
        }
    }

    UpgradeOption CreateUpgradeOption(UpgradeType type, int tier)
    {
        switch (type)
        {
            case UpgradeType.Inventory:
            {
                int slots = 2 + tier;
                return new UpgradeOption
                {
                    type = type,
                    title = "背包擴充",
                    description = $"背包容量 +{slots}",
                    bonusAmount = slots
                };
            }
            case UpgradeType.Attack:
            {
                float percent = 6f + tier * 2f;
                return new UpgradeOption
                {
                    type = type,
                    title = "攻擊強化",
                    description = $"所有武器傷害 +{percent:0}%",
                    bonusAmount = percent / 100f
                };
            }
            case UpgradeType.MoveSpeed:
            {
                float percent = 4f + tier * 2f;
                return new UpgradeOption
                {
                    type = type,
                    title = "移動強化",
                    description = $"走路與跑步速度 +{percent:0}%",
                    bonusAmount = percent / 100f
                };
            }
            case UpgradeType.CriticalDamage:
            {
                float percent = 10f + tier * 5f;
                return new UpgradeOption
                {
                    type = type,
                    title = "暴擊強化",
                    description = $"暴擊傷害倍率 +{percent:0}%\n目前暴擊率 {criticalChance * 100f:0}%",
                    bonusAmount = percent / 100f
                };
            }
            case UpgradeType.BulletPiercing:
            {
                return new UpgradeOption
                {
                    type = type,
                    title = "子彈穿透",
                    description = "子彈可額外穿透 1 名敵人",
                    bonusAmount = 1f
                };
            }
            case UpgradeType.DoubleJump:
            {
                return new UpgradeOption
                {
                    type = type,
                    title = "二段跳",
                    description = "解鎖一次空中跳躍",
                    bonusAmount = 1f
                };
            }
            case UpgradeType.MaxArmor:
            {
                float armor = 10f + tier * 5f;
                return new UpgradeOption
                {
                    type = type,
                    title = "護甲擴充",
                    description = $"護甲上限 +{armor:0}\n並立即補充新增護甲",
                    bonusAmount = armor
                };
            }
            default:
            {
                float regeneration = 0.5f + tier * 0.25f;
                return new UpgradeOption
                {
                    type = type,
                    title = "護甲修復",
                    description = $"每秒護甲回復 +{regeneration:0.00}",
                    bonusAmount = regeneration
                };
            }
        }
    }

    void SelectUpgrade(UpgradeOption option)
    {
        GameAudio.UI(GameSound.Upgrade);
        switch (option.type)
        {
            case UpgradeType.Inventory:
                WeaponInventory inventory = GetComponent<WeaponInventory>();
                if (inventory != null)
                    inventory.IncreaseCapacity(Mathf.RoundToInt(option.bonusAmount));
                break;
            case UpgradeType.Attack:
                attackMultiplier += option.bonusAmount;
                break;
            case UpgradeType.MoveSpeed:
                moveSpeedMultiplier += option.bonusAmount;
                break;
            case UpgradeType.CriticalDamage:
                criticalDamageMultiplier += option.bonusAmount;
                break;
            case UpgradeType.BulletPiercing:
                bulletPenetration += Mathf.RoundToInt(option.bonusAmount);
                break;
            case UpgradeType.DoubleJump:
                hasDoubleJump = true;
                break;
            case UpgradeType.MaxArmor:
                maxArmor += option.bonusAmount;
                currentArmor = Mathf.Min(maxArmor, currentArmor + option.bonusAmount);
                break;
            case UpgradeType.ArmorRegeneration:
                armorRegeneration += option.bonusAmount;
                break;
        }

        pendingUpgradeChoices = Mathf.Max(0, pendingUpgradeChoices - 1);
        CloseUpgradeChoice(true);
    }

    void CloseUpgradeChoice(bool showPendingChoice)
    {
        if (!IsChoosingUpgrade)
            return;

        upgradePanel.SetActive(false);
        Time.timeScale = timeScaleBeforeUpgrade;
        RefreshCursor();

        if (showPendingChoice && pendingUpgradeChoices > 0)
            ShowNextUpgradeChoice();
    }

    void RefreshCursor()
    {
        PlayerCursorController cursorController = GetComponent<PlayerCursorController>();
        if (cursorController != null)
            cursorController.RefreshCursor();
    }

    static GameObject CreateUpgradeImage(
        string objectName,
        Transform parent,
        Vector2 size,
        Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        imageObject.GetComponent<RectTransform>().sizeDelta = size;
        imageObject.GetComponent<Image>().color = color;
        return imageObject;
    }

    static Text CreateUpgradeText(
        string objectName,
        Transform parent,
        string content,
        int fontSize,
        TextAnchor alignment,
        Vector2 position,
        Vector2 size)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.sizeDelta = size;
        textRect.anchoredPosition = position;

        Text text = textObject.GetComponent<Text>();
        text.font = GameUIFont.Default;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.text = content;
        return text;
    }

    static Button CreateUpgradeButton(Transform parent, Vector2 position, Vector2 size)
    {
        GameObject buttonObject = CreateUpgradeImage(
            "Upgrade Option",
            parent,
            size,
            new Color(0.12f, 0.22f, 0.32f, 1f)
        );
        buttonObject.GetComponent<RectTransform>().anchoredPosition = position;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        CreateUpgradeText(
            "Label",
            buttonObject.transform,
            "",
            24,
            TextAnchor.MiddleCenter,
            Vector2.zero,
            size - new Vector2(40f, 40f)
        );
        return button;
    }

    void PrepareRespawnCanvas()
    {
        if (respawnCanvas == null)
            BuildDefaultRespawnCanvas();

        if (respawnText == null && respawnCanvas != null)
            respawnText = respawnCanvas.GetComponentInChildren<Text>(true);

        if (respawnText != null)
            respawnText.font = GameUIFont.Default;

        SetRespawnCanvasVisible(false);
    }

    void BuildDefaultRespawnCanvas()
    {
        respawnCanvas = new GameObject(
            "RespawnCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        respawnCanvas.transform.SetParent(transform, false);

        Canvas canvas = respawnCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = respawnCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject backgroundObject = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(Image)
        );
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.SetParent(respawnCanvas.transform, false);
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        Image background = backgroundObject.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.75f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject(
            "RespawnText",
            typeof(RectTransform),
            typeof(Text)
        );
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(backgroundRect, false);
        textRect.anchorMin = new Vector2(0.1f, 0.35f);
        textRect.anchorMax = new Vector2(0.9f, 0.65f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        respawnText = textObject.GetComponent<Text>();
        respawnText.font = GameUIFont.Default;
        respawnText.fontSize = 54;
        respawnText.fontStyle = FontStyle.Bold;
        respawnText.alignment = TextAnchor.MiddleCenter;
        respawnText.color = Color.white;
        respawnText.raycastTarget = false;
    }

    void SetRespawnCanvasVisible(bool visible)
    {
        if (respawnCanvas != null)
            respawnCanvas.SetActive(visible);
    }

    void UpdateRespawnText(float remainingTime)
    {
        if (respawnText == null)
            return;

        int seconds = Mathf.Max(0, Mathf.CeilToInt(remainingTime));
        respawnText.text = $"{deathMessage}\n{seconds} 秒後重生";
    }

    void SetPlayerControlEnabled(bool controlEnabled)
    {
        if (!controlEnabled)
        {
            if (playerController != null)
            {
                playerControllerWasEnabled = playerController.enabled;
                playerController.enabled = false;
            }

            if (playerShooter != null)
            {
                playerShooterWasEnabled = playerShooter.enabled;
                playerShooter.enabled = false;
            }

            return;
        }

        if (playerController != null)
            playerController.enabled = playerControllerWasEnabled;

        if (playerShooter != null)
            playerShooter.enabled = playerShooterWasEnabled;
    }

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        startingLevel = Mathf.Max(1, startingLevel);
        maxPlayerLevel = Mathf.Max(startingLevel, maxPlayerLevel);
        baseExperienceToLevel = Mathf.Max(1, baseExperienceToLevel);
        experienceGrowth = Mathf.Max(1f, experienceGrowth);
        respawnDelay = Mathf.Max(0f, respawnDelay);
        respawnInvincibilityDuration = Mathf.Max(0f, respawnInvincibilityDuration);
        startingMaxArmor = Mathf.Max(0f, startingMaxArmor);
        startingArmorRegeneration = Mathf.Max(0f, startingArmorRegeneration);
        armorRegenerationDelay = Mathf.Max(0f, armorRegenerationDelay);
        attackMultiplier = Mathf.Max(1f, attackMultiplier);
        moveSpeedMultiplier = Mathf.Max(1f, moveSpeedMultiplier);
        criticalChance = Mathf.Clamp01(criticalChance);
        criticalDamageMultiplier = Mathf.Max(1f, criticalDamageMultiplier);
    }
}
