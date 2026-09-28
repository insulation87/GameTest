using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum WeaponBuild
{
    None, Gunslinger, Redline, Fortress, OneShotTheory, Bonebreaker,
    Doomsday, Vanguard, TacticalExpert, Breacher, SuppressiveFire,
    Scavenger, Hunter, RangeMaster, AllRounder, ThreeStage,
    WarMachine, Siege, DeathZone, BlitzDemolition, ShockAssault,
    FirstStrike, BountyHunter, RoomCleaner, LastStand, BulletStorm
}

public class WeaponBuildSystem : MonoBehaviour
{
    public WeaponBuild ActiveBuild { get; private set; }
    public string ActiveBuildName => GetBuildName(ActiveBuild);

    private WeaponInventory inventory;
    private PlayerHealth playerHealth;
    private Text buildText;
    private float lastShotTime = -99f;
    private float firingStartedAt;
    private float switchBuffUntil;
    private float gunslingerUntil;
    private float speedBuffUntil;
    private float nextShotBonus = 1f;
    private WeaponType lastWeapon;
    private readonly HashSet<WeaponType> recentTypes = new HashSet<WeaponType>();
    private readonly HashSet<WeaponType> firstShotsUsed = new HashSet<WeaponType>();
    private EnemyHealth repeatedTarget;
    private int repeatedHits;
    private int scavengerStacks;
    private int barrageHits;
    private int barrageStacks;
    private int firepowerShots;
    private int firepowerPoints;
    private int headshotChain;
    private readonly Dictionary<EnemyHealth, float> tacticalMarks = new Dictionary<EnemyHealth, float>();
    private readonly Dictionary<EnemyHealth, float> hunterMarks = new Dictionary<EnemyHealth, float>();
    private readonly Dictionary<EnemyHealth, float> fireTargets = new Dictionary<EnemyHealth, float>();
    private readonly Dictionary<EnemyHealth, float> blastMarks = new Dictionary<EnemyHealth, float>();
    private readonly Dictionary<EnemyHealth, float> concussionMarks = new Dictionary<EnemyHealth, float>();
    private readonly Dictionary<EnemyHealth, int> armorBreak = new Dictionary<EnemyHealth, int>();

    public float MoveSpeedMultiplier
    {
        get
        {
            float speedMultiplier = 1f;
            if (ActiveBuild == WeaponBuild.Redline) speedMultiplier *= 1.15f;
            if (ActiveBuild == WeaponBuild.Siege) speedMultiplier *= 0.9f;
            if (Time.time < speedBuffUntil) speedMultiplier *= ActiveBuild == WeaponBuild.BlitzDemolition ? 1.25f : 1.2f;
            if (ActiveBuild == WeaponBuild.Scavenger) speedMultiplier *= 1f + scavengerStacks * 0.05f;
            return speedMultiplier;
        }
    }

    public float IncomingDamageMultiplier =>
        ActiveBuild == WeaponBuild.Fortress && Time.time - firingStartedAt >= 1f && Time.time - lastShotTime < 0.35f
            ? 0.8f : 1f;

    void Awake()
    {
        inventory = GetComponent<WeaponInventory>();
        playerHealth = GetComponent<PlayerHealth>();
        BuildStatusUI();
    }

    public void RefreshBuild()
    {
        if (inventory == null)
            inventory = GetComponent<WeaponInventory>();
        IReadOnlyList<WeaponItem> slots = inventory != null ? inventory.EquippedSlots : null;
        ActiveBuild = slots == null || slots.Count < 3 || slots[0] == null || slots[1] == null || slots[2] == null
            ? WeaponBuild.None
            : ResolveBuild(slots[0].weaponType, slots[1].weaponType, slots[2].weaponType);
        ResetCombatState();
        if (buildText != null)
            buildText.text = ActiveBuild == WeaponBuild.None ? "" : $"武器組合：{ActiveBuildName}";
    }

    public void OnWeaponSwitched(WeaponType type)
    {
        if (ActiveBuild == WeaponBuild.Gunslinger && type == WeaponType.Pistol)
            gunslingerUntil = Time.time + 3f;
        if (type != lastWeapon)
        {
            if (ActiveBuild == WeaponBuild.Vanguard)
            {
                switchBuffUntil = Time.time + 4f;
                recentTypes.Add(type);
            }
            lastWeapon = type;
        }
    }

    public void OnShot(WeaponType type)
    {
        if (Time.time - lastShotTime > 0.35f)
        {
            firingStartedAt = Time.time;
            if (ActiveBuild == WeaponBuild.Redline) firingStartedAt = Time.time;
        }
        lastShotTime = Time.time;
        if (ActiveBuild == WeaponBuild.WarMachine && ++firepowerShots >= 30)
        {
            firepowerShots = 0;
            firepowerPoints = Mathf.Min(3, firepowerPoints + 1);
        }
    }

    public float ModifyFireInterval(float interval, WeaponType type)
    {
        float multiplier = 1f;
        if (ActiveBuild == WeaponBuild.Gunslinger && type == WeaponType.Pistol && Time.time < gunslingerUntil)
            multiplier *= 0.75f;
        if (ActiveBuild == WeaponBuild.Redline && Time.time - lastShotTime < 0.35f)
            multiplier *= 1f - Mathf.Min(0.24f, (Time.time - firingStartedAt) * 0.04f);
        if (ActiveBuild == WeaponBuild.Vanguard && recentTypes.Count >= 3 && Time.time < switchBuffUntil)
            multiplier *= 0.85f;
        if (ActiveBuild == WeaponBuild.AllRounder && GetNearestEnemyDistance() < 10f)
            multiplier *= 0.85f;
        if (ActiveBuild == WeaponBuild.BulletStorm)
            multiplier *= 1f - barrageStacks * 0.05f;
        return Mathf.Max(0.02f, interval * multiplier);
    }

    public float ModifyExplosionRadius(float radius, WeaponType type)
    {
        if (radius <= 0f) return 0f;
        if (ActiveBuild == WeaponBuild.Doomsday) radius *= 1.25f;
        if (ActiveBuild == WeaponBuild.LastStand && playerHealth != null && playerHealth.HealthNormalized < 0.4f)
            radius *= 1.2f;
        return radius;
    }

    public float ModifyDamage(float damage, EnemyHealth enemy, WeaponType type,
        float distance, bool explosive, bool headshot)
    {
        float multiplier = nextShotBonus;
        nextShotBonus = 1f;
        bool boss = enemy != null && enemy.GetComponent<BossController>() != null;

        if (ActiveBuild == WeaponBuild.Vanguard && Time.time < switchBuffUntil) multiplier *= 1.15f;
        if (ActiveBuild == WeaponBuild.OneShotTheory && firstShotsUsed.Add(type)) multiplier *= 1.35f;
        if (ActiveBuild == WeaponBuild.Bonebreaker && distance <= 5f) multiplier *= 1.3f;
        if (ActiveBuild == WeaponBuild.Siege && (boss || enemy.EnemyLevel >= 80)) multiplier *= 1.3f;
        if (ActiveBuild == WeaponBuild.RangeMaster)
        {
            if (type == WeaponType.SniperRifle && distance >= 20f) multiplier *= 1.2f;
            else if (type == WeaponType.Shotgun && distance <= 7f) multiplier *= 1.2f;
            else if (type == WeaponType.Pistol && distance > 7f && distance < 20f) multiplier *= 1.15f;
        }
        if (ActiveBuild == WeaponBuild.AllRounder)
        {
            if (distance < 10f) { }
            else if (distance < 20f) multiplier *= 1.15f;
            else if (Random.value < 0.15f) multiplier *= 2f;
        }
        if (ActiveBuild == WeaponBuild.FirstStrike && enemy.CurrentHealth >= enemy.maxHealth - 0.01f)
            multiplier *= boss ? 1.15f : 1.4f;
        if (ActiveBuild == WeaponBuild.LastStand && playerHealth != null && playerHealth.HealthNormalized < 0.4f)
        {
            if (type == WeaponType.LightMachineGun) multiplier *= 1.2f;
            if (type == WeaponType.Pistol && Random.value < 0.2f) multiplier *= 2f;
        }
        if (ActiveBuild == WeaponBuild.SuppressiveFire)
        {
            if (enemy == repeatedTarget) repeatedHits++; else { repeatedTarget = enemy; repeatedHits = 1; }
            multiplier *= 1f + Mathf.Min(20, repeatedHits - 1) * 0.01f;
        }
        if (armorBreak.TryGetValue(enemy, out int broken)) multiplier *= 1f + broken * 0.03f;
        if (tacticalMarks.TryGetValue(enemy, out float tacticalUntil) && Time.time < tacticalUntil && type != WeaponType.SniperRifle)
            multiplier *= 1.2f;
        if (fireTargets.TryGetValue(enemy, out float fireUntil) && Time.time < fireUntil &&
            (type == WeaponType.LightMachineGun || explosive)) multiplier *= 1.2f;
        if (blastMarks.TryGetValue(enemy, out float blastUntil) && Time.time < blastUntil && distance <= 10f)
            multiplier *= 1.2f;
        if (concussionMarks.TryGetValue(enemy, out float concussUntil) && Time.time < concussUntil &&
            (type == WeaponType.Rifle || type == WeaponType.SubmachineGun)) multiplier *= 1.15f;
        if (hunterMarks.TryGetValue(enemy, out float hunterUntil) && Time.time < hunterUntil &&
            (type == WeaponType.SubmachineGun || type == WeaponType.Pistol) && Random.value < 0.25f) multiplier *= 2f;
        if (ActiveBuild == WeaponBuild.WarMachine && explosive && firepowerPoints >= 3)
        {
            multiplier *= 1.5f;
            firepowerPoints = 0;
        }
        float critChance = ActiveBuild == WeaponBuild.Gunslinger ? Mathf.Min(0.2f, headshotChain * 0.04f) : 0f;
        if (headshot || Random.value < critChance) multiplier *= 1.5f;
        return damage * multiplier;
    }

    public void OnHit(EnemyHealth enemy, WeaponType type, float distance,
        bool explosive, bool headshot, bool killed)
    {
        if (headshot) headshotChain = Mathf.Min(5, headshotChain + 1); else headshotChain = 0;
        if (ActiveBuild == WeaponBuild.Bonebreaker && !killed)
            armorBreak[enemy] = Mathf.Min(10, armorBreak.TryGetValue(enemy, out int armorBreakStacks) ? armorBreakStacks + 1 : 1);
        if (ActiveBuild == WeaponBuild.TacticalExpert && headshot) tacticalMarks[enemy] = Time.time + 5f;
        if (ActiveBuild == WeaponBuild.Hunter && type == WeaponType.SniperRifle) hunterMarks[enemy] = Time.time + 8f;
        if (ActiveBuild == WeaponBuild.DeathZone && type == WeaponType.SniperRifle) fireTargets[enemy] = Time.time + 8f;
        if (ActiveBuild == WeaponBuild.BlitzDemolition && explosive) { blastMarks[enemy] = Time.time + 5f; speedBuffUntil = Time.time + 5f; }
        if (ActiveBuild == WeaponBuild.ShockAssault && explosive) concussionMarks[enemy] = Time.time + 5f;
        if (ActiveBuild == WeaponBuild.BulletStorm)
        {
            if (++barrageHits >= 15) { barrageHits = 0; barrageStacks = Mathf.Min(4, barrageStacks + 1); }
        }
        if (!killed) return;

        if (ActiveBuild == WeaponBuild.Breacher && distance <= 10f) { speedBuffUntil = Time.time + 4f; nextShotBonus = 1.25f; }
        if (ActiveBuild == WeaponBuild.Scavenger && distance <= 10f) scavengerStacks = Mathf.Min(3, scavengerStacks + 1);
        if (ActiveBuild == WeaponBuild.ThreeStage)
        {
            if (type == WeaponType.SniperRifle) nextShotBonus = 1.15f;
            else if (type == WeaponType.Rifle) nextShotBonus = 1.2f;
            else if (type == WeaponType.Shotgun) nextShotBonus = 1.25f;
        }
        if (ActiveBuild == WeaponBuild.Doomsday && explosive && Random.value < 0.2f)
            DamageArea(enemy.transform.position, 3f, 50f);
        if (ActiveBuild == WeaponBuild.RoomCleaner)
            DamageArea(enemy.transform.position, 3f, 35f);
        if (ActiveBuild == WeaponBuild.BountyHunter &&
            (enemy.EnemyLevel >= 80 || enemy.GetComponent<BossController>() != null))
            inventory.AddGold(25);
    }

    void DamageArea(Vector3 center, float radius, float damage)
    {
        HashSet<EnemyHealth> hit = new HashSet<EnemyHealth>();
        foreach (Collider collider in Physics.OverlapSphere(center, radius))
        {
            EnemyHealth enemy = collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null && !enemy.IsDead && hit.Add(enemy))
                enemy.TakeDamage(damage, playerHealth);
        }
    }

    float GetNearestEnemyDistance()
    {
        float nearest = float.PositiveInfinity;
        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
            nearest = Mathf.Min(nearest, Vector3.Distance(transform.position, enemy.transform.position));
        return nearest;
    }

    void ResetCombatState()
    {
        recentTypes.Clear(); firstShotsUsed.Clear(); repeatedTarget = null; repeatedHits = 0;
        scavengerStacks = barrageHits = barrageStacks = firepowerShots = firepowerPoints = headshotChain = 0;
    }

    void BuildStatusUI()
    {
        GameObject canvasObject = new GameObject("Weapon Build Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 65;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        GameObject textObject = new GameObject("Active Build", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(canvasObject.transform, false);
        RectTransform buildTextRect = textObject.GetComponent<RectTransform>();
        buildTextRect.anchorMin = buildTextRect.anchorMax = new Vector2(0.5f, 1f);
        buildTextRect.pivot = new Vector2(0.5f, 1f);
        buildTextRect.anchoredPosition = new Vector2(0f, -25f);
        buildTextRect.sizeDelta = new Vector2(650f, 42f);
        buildText = textObject.GetComponent<Text>();
        buildText.font = GameUIFont.Default;
        buildText.fontSize = 24;
        buildText.fontStyle = FontStyle.Bold;
        buildText.alignment = TextAnchor.MiddleCenter;
        buildText.color = new Color(1f, 0.82f, 0.2f);
        buildText.raycastTarget = false;
    }

    static WeaponBuild ResolveBuild(WeaponType firstWeapon, WeaponType secondWeapon, WeaponType thirdWeapon)
    {
        if (All(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Pistol)) return WeaponBuild.Gunslinger;
        if (All(firstWeapon, secondWeapon, thirdWeapon, WeaponType.SubmachineGun)) return WeaponBuild.Redline;
        if (All(firstWeapon, secondWeapon, thirdWeapon, WeaponType.LightMachineGun)) return WeaponBuild.Fortress;
        if (All(firstWeapon, secondWeapon, thirdWeapon, WeaponType.SniperRifle)) return WeaponBuild.OneShotTheory;
        if (All(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Shotgun)) return WeaponBuild.Bonebreaker;
        if (All(firstWeapon, secondWeapon, thirdWeapon, WeaponType.RPG)) return WeaponBuild.Doomsday;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.SubmachineGun, WeaponType.Shotgun)) return WeaponBuild.Vanguard;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.SniperRifle, WeaponType.Pistol)) return WeaponBuild.TacticalExpert;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.Shotgun, WeaponType.Pistol)) return WeaponBuild.Breacher;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.LightMachineGun, WeaponType.Pistol)) return WeaponBuild.SuppressiveFire;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.SubmachineGun, WeaponType.Shotgun, WeaponType.Pistol)) return WeaponBuild.Scavenger;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.SubmachineGun, WeaponType.SniperRifle, WeaponType.Pistol)) return WeaponBuild.Hunter;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Shotgun, WeaponType.SniperRifle, WeaponType.Pistol)) return WeaponBuild.RangeMaster;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.SubmachineGun, WeaponType.SniperRifle)) return WeaponBuild.AllRounder;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.Shotgun, WeaponType.SniperRifle)) return WeaponBuild.ThreeStage;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.LightMachineGun, WeaponType.RPG)) return WeaponBuild.WarMachine;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Shotgun, WeaponType.LightMachineGun, WeaponType.RPG)) return WeaponBuild.Siege;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.SniperRifle, WeaponType.LightMachineGun, WeaponType.RPG)) return WeaponBuild.DeathZone;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.SubmachineGun, WeaponType.Shotgun, WeaponType.RPG)) return WeaponBuild.BlitzDemolition;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.SubmachineGun, WeaponType.RPG)) return WeaponBuild.ShockAssault;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.SniperRifle, WeaponType.RPG)) return WeaponBuild.FirstStrike;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Pistol, WeaponType.SniperRifle, WeaponType.RPG)) return WeaponBuild.BountyHunter;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Pistol, WeaponType.Shotgun, WeaponType.RPG)) return WeaponBuild.RoomCleaner;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Pistol, WeaponType.LightMachineGun, WeaponType.RPG)) return WeaponBuild.LastStand;
        if (Mix(firstWeapon, secondWeapon, thirdWeapon, WeaponType.Rifle, WeaponType.SubmachineGun, WeaponType.LightMachineGun)) return WeaponBuild.BulletStorm;
        return WeaponBuild.None;
    }

    static bool All(WeaponType firstWeapon, WeaponType secondWeapon, WeaponType thirdWeapon, WeaponType expectedType) =>
        firstWeapon == expectedType && secondWeapon == expectedType && thirdWeapon == expectedType;

    static bool Mix(WeaponType firstWeapon, WeaponType secondWeapon, WeaponType thirdWeapon,
        WeaponType requiredTypeA, WeaponType requiredTypeB, WeaponType requiredTypeC)
    {
        HashSet<WeaponType> equippedTypes = new HashSet<WeaponType> { firstWeapon, secondWeapon, thirdWeapon };
        return equippedTypes.Count == 3 &&
               equippedTypes.Contains(requiredTypeA) &&
               equippedTypes.Contains(requiredTypeB) &&
               equippedTypes.Contains(requiredTypeC);
    }

    static string GetBuildName(WeaponBuild build)
    {
        string[] names = { "", "槍神", "紅線", "堡壘", "一擊理論", "碎骨者",
            "末日火力", "突擊兵", "戰術專家", "突破手", "壓制火力", "清道夫", "獵人",
            "距離支配者", "全能戰士", "三段戰法", "戰爭機器", "攻城者", "死亡區域", "閃擊爆破",
            "震撼突擊", "第一擊", "賞金獵人", "房間清理者", "最後防線", "彈幕風暴" };
        return names[Mathf.Clamp((int)build, 0, names.Length - 1)];
    }
}
