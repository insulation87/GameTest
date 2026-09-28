using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PlayerShooter : MonoBehaviour
{
    [Header("References")]
    public Camera tpsCamera;
    public Camera fpsCamera;
    public Transform firePoint;
    public Projectile projectilePrefab;

    [Header("Weapon")]
    public bool allowTpsShooting = false;
    [Min(0.01f)] public float fireInterval = 0.2f;
    [Min(0.1f)] public float projectileSpeed = 60f;
    [Min(0f)] public float projectileDamage = 20f;
    [Min(1)] public int projectilesPerShot = 1;
    [Min(0f)] public float spreadAngle = 0f;
    [Min(0f)] public float explosionRadius = 0f;
    [Min(1f)] public float aimDistance = 200f;
    public LayerMask aimLayers = ~0;

    [Header("Weapon Models")]
    [Tooltip("Use the matching stylized equipment models in both Editor and builds.")]
    [SerializeField] private bool useNewWeaponAppearance = true;
    [SerializeField] private Transform weaponModelMount;
    [SerializeField] private GameObject rifleModel;
    [SerializeField] private GameObject submachineGunModel;
    [SerializeField] private GameObject shotgunModel;
    [SerializeField] private GameObject rpgModel;
    [SerializeField] private GameObject pistolModel;
    [SerializeField] private GameObject sniperRifleModel;
    [SerializeField] private Vector3 weaponModelPosition = new Vector3(0.28f, -0.25f, 0.55f);
    [Tooltip("Extra rotation applied after correcting the imported FBX axes.")]
    [SerializeField] private Vector3 weaponModelRotation = Vector3.zero;
    [SerializeField] private float weaponModelScale = 0.35f;

    [Header("Projectile Models")]
    [SerializeField] private GameObject standardBulletModel;
    [SerializeField] private GameObject pistolBulletModel;
    [SerializeField] private GameObject shotgunBulletModel;
    [SerializeField] private GameObject sniperBulletModel;
    [SerializeField] private GameObject rocketModel;

    [Header("Aim Mode")]
    [Tooltip("FPS only. TPS always spawns projectiles from Fire Point.")]
    public bool shootExactlyFromCrosshair = true;
    [Min(0.05f)] public float crosshairSpawnDistance = 0.5f;

    private float nextFireTime;
    private WeaponInventory weaponInventory;
    private GrenadeThrower grenadeThrower;
    private WeaponBuildSystem weaponBuild;
    private GameObject activeWeaponModel;
    private WeaponType equippedWeaponType = WeaponType.Rifle;

    void Awake()
    {
#if UNITY_EDITOR
        ResolveEditorWeaponModels();
#endif

        weaponInventory = GetComponent<WeaponInventory>();
        if (weaponInventory == null)
            weaponInventory = gameObject.AddComponent<WeaponInventory>();

        weaponInventory.Initialize(this);
        if (GetComponent<PickupMagnet>() == null)
            gameObject.AddComponent<PickupMagnet>();

        grenadeThrower = GetComponent<GrenadeThrower>();
        if (grenadeThrower == null)
            grenadeThrower = gameObject.AddComponent<GrenadeThrower>();

        weaponBuild = GetComponent<WeaponBuildSystem>();
        if (weaponBuild == null)
            weaponBuild = gameObject.AddComponent<WeaponBuildSystem>();
    }

    public void EquipWeapon(float damage, float interval, float speed)
    {
        projectileDamage = Mathf.Max(0f, damage);
        fireInterval = Mathf.Max(0.01f, interval);
        projectileSpeed = Mathf.Max(0.1f, speed);
        nextFireTime = Mathf.Min(nextFireTime, Time.time + fireInterval);
    }

    public void EquipWeapon(WeaponItem weapon)
    {
        if (weapon == null)
            return;

        EquipWeapon(weapon.damage, weapon.fireInterval, weapon.projectileSpeed);
        projectilesPerShot = Mathf.Max(1, weapon.projectilesPerShot);
        spreadAngle = Mathf.Max(0f, weapon.spreadAngle);
        explosionRadius = Mathf.Max(0f, weapon.explosionRadius);
        equippedWeaponType = weapon.weaponType;
        ShowWeaponModel(weapon.weaponType);
    }

    void ShowWeaponModel(WeaponType weaponType)
    {
        if (activeWeaponModel != null)
            Destroy(activeWeaponModel);

        GameObject modelPrefab = useNewWeaponAppearance ? null : GetWeaponModel(weaponType);
        Transform mount = GetWeaponModelMount();
        if ((!useNewWeaponAppearance && modelPrefab == null) || mount == null)
        {
            Debug.LogWarning($"No model is assigned for {weaponType}.", this);
            return;
        }

        activeWeaponModel = new GameObject($"{weaponType} First Person Rig");
        activeWeaponModel.transform.SetParent(mount, false);

        GameObject modelInstance = useNewWeaponAppearance
            ? ItemAppearance.Create((int)weaponType, activeWeaponModel.transform)
            : Instantiate(modelPrefab, activeWeaponModel.transform);
        modelInstance.name = $"{weaponType} Model";
        modelInstance.transform.localPosition = weaponModelPosition;
        // These FBX files use +X as the muzzle direction and -Z as magazine-down.
        // Convert them to Unity view space: muzzle +Z, magazine -Y.
        Quaternion importedAxisCorrection =
            Quaternion.LookRotation(Vector3.up, Vector3.right);
        modelInstance.transform.localRotation =
            Quaternion.Euler(weaponModelRotation) *
            (useNewWeaponAppearance ? Quaternion.identity : importedAxisCorrection);
        modelInstance.transform.localScale = Vector3.one;

        foreach (Collider modelCollider in modelInstance.GetComponentsInChildren<Collider>(true))
            modelCollider.enabled = false;
        foreach (Rigidbody modelBody in modelInstance.GetComponentsInChildren<Rigidbody>(true))
        {
            modelBody.isKinematic = true;
            modelBody.detectCollisions = false;
        }

        FitWeaponModelToView(modelInstance, mount);
        CreateFirstPersonArms(activeWeaponModel.transform);
    }

    void FitWeaponModelToView(GameObject modelInstance, Transform mount)
    {
        if (useNewWeaponAppearance)
        {
            Bounds localBounds = modelInstance.GetComponent<MeshFilter>().sharedMesh.bounds;
            float lengthMultiplier = equippedWeaponType == WeaponType.Pistol ? 1f :
                equippedWeaponType == WeaponType.SubmachineGun ? 1.25f : 1.6f;
            float scale = weaponModelScale * lengthMultiplier /
                Mathf.Max(localBounds.size.x, localBounds.size.y, localBounds.size.z);
            modelInstance.transform.localScale = Vector3.one * scale;
            modelInstance.transform.localPosition = weaponModelPosition -
                modelInstance.transform.localRotation * localBounds.center * scale;
            return;
        }

        Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            bounds.Encapsulate(renderers[rendererIndex].bounds);

        float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension <= Mathf.Epsilon)
            return;

        modelInstance.transform.localScale = Vector3.one * (weaponModelScale / largestDimension);

        bounds = renderers[0].bounds;
        for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            bounds.Encapsulate(renderers[rendererIndex].bounds);

        modelInstance.transform.position +=
            mount.TransformPoint(weaponModelPosition) - bounds.center;
    }

    void CreateFirstPersonArms(Transform parent)
    {
        GameObject armsPrefab = Resources.Load<GameObject>(
            "Characters/AnimeGirl/AnimeGirlArms");
        if (armsPrefab == null)
        {
            Debug.LogError("Missing AnimeGirlArms character model.", this);
            return;
        }
        Instantiate(armsPrefab, parent, false);
    }

    Transform GetWeaponModelMount()
    {
        if (weaponModelMount != null)
            return weaponModelMount;

        if (fpsCamera == null)
            return null;

        GameObject mountObject = new GameObject("Weapon Model Mount");
        weaponModelMount = mountObject.transform;
        weaponModelMount.SetParent(fpsCamera.transform, false);
        return weaponModelMount;
    }

    GameObject GetWeaponModel(WeaponType weaponType)
    {
#if UNITY_EDITOR
        ResolveEditorWeaponModels();
#endif

        switch (weaponType)
        {
            case WeaponType.Rifle: return rifleModel;
            case WeaponType.SubmachineGun: return submachineGunModel;
            case WeaponType.Shotgun: return shotgunModel;
            case WeaponType.RPG: return rpgModel;
            case WeaponType.Pistol: return pistolModel;
            case WeaponType.SniperRifle: return sniperRifleModel;
            case WeaponType.LightMachineGun: return rifleModel;
            default: return null;
        }
    }

    void Update()
    {
        if (weaponInventory != null &&
            (weaponInventory.IsOpen || weaponInventory.IsPaused))
            return;

        if (grenadeThrower != null && grenadeThrower.IsEquipped)
            return;

        if (CanShootInCurrentMode() &&
            Input.GetMouseButton(0) &&
            Time.time >= nextFireTime)
        {
            Fire();
        }
    }

    public void SetGrenadeMode(bool equipped)
    {
        if (activeWeaponModel != null)
            activeWeaponModel.SetActive(!equipped);
    }

    public void Fire()
    {
        if (!CanShootInCurrentMode())
            return;

        Camera activeCamera = GetActiveCamera();
        if (activeCamera == null || projectilePrefab == null)
        {
            Debug.LogWarning("PlayerShooter requires a camera and projectile prefab.", this);
            return;
        }

        float effectiveInterval = weaponBuild != null
            ? weaponBuild.ModifyFireInterval(fireInterval, equippedWeaponType)
            : fireInterval;
        nextFireTime = Time.time + effectiveInterval;
        GameAudio.Shot(equippedWeaponType);
        if (weaponBuild != null)
            weaponBuild.OnShot(equippedWeaponType);

        Ray aimRay = activeCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        Vector3 aimPoint = aimRay.GetPoint(aimDistance);
        TryGetAimPoint(aimRay, ref aimPoint);

        bool isFpsMode = fpsCamera != null &&
                         fpsCamera.gameObject.activeInHierarchy;
        bool shootFromCrosshair = isFpsMode && shootExactlyFromCrosshair;

        float safeCameraDistance = Mathf.Max(
            crosshairSpawnDistance,
            activeCamera.nearClipPlane + 0.05f
        );

        Vector3 spawnPosition = shootFromCrosshair
            ? aimRay.GetPoint(safeCameraDistance)
            : firePoint != null
                ? firePoint.position
                : transform.position + Vector3.up * 1.2f;

        Vector3 direction = shootFromCrosshair
            ? aimRay.direction.normalized
            : (aimPoint - spawnPosition).normalized;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
            direction = activeCamera.transform.forward;

        int projectileCount = Mathf.Max(1, projectilesPerShot);
        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            Vector3 shotDirection = GetSpreadDirection(direction, spreadAngle);
            Projectile projectile = Instantiate(
                projectilePrefab,
                spawnPosition,
                Quaternion.LookRotation(shotDirection)
            );

            projectile.SetVisualModel(
                GetProjectileModel(equippedWeaponType),
                equippedWeaponType == WeaponType.RPG ? 0.45f : 0.18f,
                Quaternion.identity
            );
            float effectiveExplosionRadius = weaponBuild != null
                ? weaponBuild.ModifyExplosionRadius(explosionRadius, equippedWeaponType)
                : explosionRadius;
            projectile.Launch(
                shotDirection,
                projectileSpeed,
                GetProjectileDamage(),
                gameObject,
                effectiveExplosionRadius,
                GetProjectilePenetration(),
                equippedWeaponType,
                weaponBuild
            );
        }
    }

    float GetProjectileDamage()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        return health != null
            ? health.CalculateOutgoingDamage(projectileDamage)
            : projectileDamage;
    }

    int GetProjectilePenetration()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        return health != null ? health.BulletPenetration : 0;
    }

    GameObject GetProjectileModel(WeaponType weaponType)
    {
#if UNITY_EDITOR
        ResolveEditorWeaponModels();
#endif
        switch (weaponType)
        {
            case WeaponType.Pistol: return pistolBulletModel;
            case WeaponType.Shotgun: return shotgunBulletModel;
            case WeaponType.SniperRifle: return sniperBulletModel;
            case WeaponType.RPG: return pistolBulletModel;
            default: return standardBulletModel;
        }
    }

    static Vector3 GetSpreadDirection(Vector3 forward, float maxAngle)
    {
        if (maxAngle <= 0f)
            return forward;

        Quaternion spread = Quaternion.Euler(
            UnityEngine.Random.Range(-maxAngle, maxAngle),
            UnityEngine.Random.Range(-maxAngle, maxAngle),
            0f
        );
        return (Quaternion.LookRotation(forward) * spread * Vector3.forward).normalized;
    }

    void TryGetAimPoint(Ray aimRay, ref Vector3 aimPoint)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            aimRay,
            aimDistance,
            aimLayers,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = float.PositiveInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform))
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                aimPoint = hit.point;
            }
        }
    }

    Camera GetActiveCamera()
    {
        if (fpsCamera != null && fpsCamera.gameObject.activeInHierarchy)
            return fpsCamera;

        if (tpsCamera != null && tpsCamera.gameObject.activeInHierarchy)
            return tpsCamera;

        return Camera.main;
    }

    bool CanShootInCurrentMode()
    {
        bool isFpsMode = fpsCamera != null &&
                         fpsCamera.gameObject.activeInHierarchy;
        return isFpsMode || allowTpsShooting;
    }

#if UNITY_EDITOR
    void ResolveEditorWeaponModels()
    {
        rifleModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/ak47.fbx");
        submachineGunModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/mac10.fbx");
        shotgunModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/shotgun.fbx");
        rpgModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/rocketlaucher.fbx");
        pistolModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/pew.fbx");
        sniperRifleModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/awp.fbx");
        standardBulletModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/bullet1.fbx");
        pistolBulletModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/bulletPEW.fbx");
        shotgunBulletModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/bulletshotgun.fbx");
        sniperBulletModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/bulletsniper.fbx");
        rocketModel ??= UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Weapon/Model/FBX/quadrocket.fbx");
    }
#endif

    void OnValidate()
    {
        fireInterval = Mathf.Max(0.01f, fireInterval);
        projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
        projectileDamage = Mathf.Max(0f, projectileDamage);
        projectilesPerShot = Mathf.Max(1, projectilesPerShot);
        spreadAngle = Mathf.Max(0f, spreadAngle);
        explosionRadius = Mathf.Max(0f, explosionRadius);
        aimDistance = Mathf.Max(1f, aimDistance);
        crosshairSpawnDistance = Mathf.Max(0.05f, crosshairSpawnDistance);
        weaponModelScale = Mathf.Max(0.001f, weaponModelScale);

#if UNITY_EDITOR
        ResolveEditorWeaponModels();
#endif
    }
}

public enum WeaponType
{
    Rifle,
    SubmachineGun,
    Shotgun,
    RPG,
    Pistol,
    SniperRifle,
    LightMachineGun
}

[Serializable]
public class WeaponItem
{
    public string weaponName;
    public WeaponType weaponType;
    public int level = 1;
    public float quality;
    public float damage;
    public float fireInterval;
    public float projectileSpeed;
    public int projectilesPerShot = 1;
    public float spreadAngle;
    public float explosionRadius;

    public float ShotsPerSecond => 1f / Mathf.Max(0.01f, fireInterval);
}

[Serializable]
public class ArmorItem
{
    public string armorName;
    public int tier;
    public float armorBonus;
}

public class WeaponInventory : MonoBehaviour
{
    [Header("Inventory")]
    public KeyCode toggleKey = KeyCode.E;
    [Min(1)] public int capacity = 20;

    [Header("Weapon Selling")]
    [Min(0)] public int startingGold = 0;
    [Min(0)] public int lowestWeaponSellPrice = 10;
    [Min(0)] public int highestWeaponSellPrice = 200;

    [Header("Pause Shop")]
    [Min(0)] public int riflePrice = 120;
    [Min(0)] public int submachineGunPrice = 150;
    [Min(0)] public int shotgunPrice = 180;
    [Min(0)] public int rpgPrice = 300;
    [Min(0)] public int pistolPrice = 80;
    [Min(0)] public int sniperRiflePrice = 250;
    [Min(0)] public int lightArmorPrice = 100;
    [Min(0)] public int mediumArmorPrice = 180;
    [Min(0)] public int heavyArmorPrice = 280;

    private readonly List<WeaponItem> weapons = new List<WeaponItem>();
    private PlayerShooter shooter;
    private GameObject inventoryPanel;
    private GameObject pausePanel;
    private GameObject pauseMenuBox;
    private GameObject pauseSettingsBox;
    private GameObject pauseBuildGuideBox;
    private GameObject pauseShopBox;
    private GameObject hotbarPanel;
    private Slider pauseVolumeSlider;
    private Toggle pauseFullscreenToggle;
    private RectTransform weaponListContent;
    private Text detailText;
    private Text equippedText;
    private Text goldText;
    private Text shopGoldText;
    private Text shopStatusText;
    private WeaponItem selectedWeapon;
    private WeaponItem equippedWeapon;
    private ArmorItem equippedArmor;
    private readonly WeaponItem[] equippedSlots = new WeaponItem[3];
    private readonly Image[] hotbarSlotImages = new Image[3];
    private readonly Text[] hotbarSlotTexts = new Text[3];
    private readonly ItemIconGraphic[] hotbarSlotIcons = new ItemIconGraphic[3];
    private int activeWeaponSlot;
    private bool initialized;
    private float previousTimeScale = 1f;
    private int gold;
    private int healthPackCount;
    private int grenadeCount;
    private float healthPackRestoreAmount = 25f;
    private readonly int[] floorKeyCounts = new int[4];

    public bool IsOpen => inventoryPanel != null && inventoryPanel.activeSelf;
    public bool IsPaused => pausePanel != null && pausePanel.activeSelf;
    public IReadOnlyList<WeaponItem> Weapons => weapons;
    public IReadOnlyList<WeaponItem> EquippedSlots => equippedSlots;
    public int Gold => gold;

    public void AddGold(int amount)
    {
        if (amount > 0) GameAudio.UI(GameSound.Coins);
        gold += Mathf.Max(0, amount);
        RefreshDetails();
    }

    public void GrantBossTestSupplies(EnemyHealth weaponStats)
    {
        if (weaponStats == null)
            return;

        WeaponType[] types = (WeaponType[])Enum.GetValues(typeof(WeaponType));
        var rewards = new List<WeaponItem>();
        for (int i = 0; i < Mathf.Min(5, types.Length); i++)
        {
            int chosen = UnityEngine.Random.Range(i, types.Length);
            WeaponType swap = types[i];
            types[i] = types[chosen];
            types[chosen] = swap;
            rewards.Add(weaponStats.CreateWeaponItem(types[i], 100, 1f));
        }

        // Put the new weapons first so they remain accessible even in a full backpack.
        weapons.InsertRange(0, rewards);
        healthPackCount += 10;
        int itemStacks = 1 + (grenadeCount > 0 ? 1 : 0);
        for (int floor = 1; floor <= 3; floor++)
            if (floorKeyCounts[floor] > 0) itemStacks++;
        capacity = Mathf.Max(capacity, weapons.Count + itemStacks);
        selectedWeapon = rewards[0];
        GameAudio.UI(GameSound.Pickup);
        RefreshWeaponList();
        RefreshDetails();
    }

    public void CloseMenusForGameplay()
    {
        if (IsPaused) SetPaused(false);
        if (IsOpen) SetOpen(false);
    }

    public void AddHealthPack(float restoreAmount)
    {
        GameAudio.UI(GameSound.Pickup);
        healthPackCount++;
        healthPackRestoreAmount = Mathf.Max(1f, restoreAmount);
        RefreshWeaponList();
    }

    public void AddGrenade()
    {
        GameAudio.UI(GameSound.Pickup);
        grenadeCount++;
        RefreshWeaponList();
    }

    public bool ConsumeGrenade()
    {
        if (grenadeCount <= 0)
            return false;

        grenadeCount--;
        RefreshWeaponList();
        return true;
    }

    void EquipGrenade()
    {
        if (grenadeCount <= 0)
        {
            GameAudio.UI(GameSound.Denied);
            return;
        }

        GrenadeThrower thrower = GetComponent<GrenadeThrower>();
        if (thrower != null)
        {
            GameAudio.UI(GameSound.Equip);
            thrower.SetEquipped(true);
        }
        RefreshWeaponList();
    }

    public void AddFloorKey(int floor)
    {
        GameAudio.UI(GameSound.Key);
        floor = Mathf.Clamp(floor, 1, 3);
        floorKeyCounts[floor]++;
        RefreshWeaponList();
    }

    void UseHealthPack()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        if (healthPackCount <= 0 || health == null || health.IsDead ||
            health.CurrentHealth >= health.maxHealth)
        {
            GameAudio.UI(GameSound.Denied);
            return;
        }

        health.Heal(healthPackRestoreAmount);
        healthPackCount--;
        RefreshWeaponList();
    }

    public void Initialize(PlayerShooter owner)
    {
        if (initialized)
            return;

        initialized = true;
        shooter = owner;
        gold = Mathf.Max(0, startingGold);

        AddWeapon(new WeaponItem
        {
            weaponName = "初始武器",
            weaponType = WeaponType.Rifle,
            level = 1,
            quality = 0f,
            damage = shooter.projectileDamage,
            fireInterval = shooter.fireInterval,
            projectileSpeed = shooter.projectileSpeed,
            projectilesPerShot = shooter.projectilesPerShot,
            spreadAngle = shooter.spreadAngle,
            explosionRadius = shooter.explosionRadius
        }, true);

        BuildUI();
        RefreshCursor();
    }

    void Update()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        if (health != null && health.IsChoosingUpgrade)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsOpen)
                SetOpen(false);
            else
                SetPaused(!IsPaused);

            return;
        }

        if (!IsPaused && Input.GetKeyDown(toggleKey))
            SetOpen(!IsOpen);

        if (!IsPaused && !IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SwitchToSlot(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2))
                SwitchToSlot(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3))
                SwitchToSlot(2);
        }
    }

    public bool AddWeapon(WeaponItem weapon, bool equipImmediately = false)
    {
        if (weapon == null || weapons.Count >= capacity)
            return false;

        weapons.Add(weapon);

        if (equipImmediately)
            EquipToSlot(weapon, 0);

        if (inventoryPanel != null)
            RefreshWeaponList();

        return true;
    }

    public void IncreaseCapacity(int amount)
    {
        capacity = Mathf.Max(1, capacity + Mathf.Max(0, amount));
        RefreshWeaponList();
    }

    void EquipToSlot(WeaponItem weapon, int slotIndex)
    {
        if (weapon == null || shooter == null ||
            slotIndex < 0 || slotIndex >= equippedSlots.Length)
            return;

        equippedSlots[slotIndex] = weapon;
        selectedWeapon = weapon;
        SwitchToSlot(slotIndex);
        RefreshDetails();
        RefreshWeaponList();
        RefreshHotbar();
        WeaponBuildSystem build = GetComponent<WeaponBuildSystem>();
        if (build != null)
            build.RefreshBuild();
    }

    void SwitchToSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= equippedSlots.Length)
            return;

        WeaponItem weapon = equippedSlots[slotIndex];
        if (weapon == null)
            return;

        activeWeaponSlot = slotIndex;
        if (inventoryPanel != null) GameAudio.UI(GameSound.Equip);
        equippedWeapon = weapon;
        GrenadeThrower thrower = GetComponent<GrenadeThrower>();
        if (thrower != null)
            thrower.SetEquipped(false);
        shooter.EquipWeapon(weapon);
        WeaponBuildSystem build = GetComponent<WeaponBuildSystem>();
        if (build != null)
            build.OnWeaponSwitched(weapon.weaponType);
        RefreshDetails();
        RefreshWeaponList();
        RefreshHotbar();
    }

    void SetOpen(bool open)
    {
        if (inventoryPanel == null)
            return;

        if (open != IsOpen) GameAudio.UI(open ? GameSound.Open : GameSound.Close);
        inventoryPanel.SetActive(open);
        if (hotbarPanel != null)
            hotbarPanel.SetActive(!open && !IsPaused);
        if (open)
        {
            RefreshWeaponList();
            RefreshDetails();
        }
        RefreshCursor();
    }

    void RefreshCursor()
    {
        PlayerCursorController cursorController = GetComponent<PlayerCursorController>();
        if (cursorController != null)
            cursorController.RefreshCursor();
    }

    void BuildUI()
    {
        GameObject canvasObject = new GameObject(
            "Weapon Inventory Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        inventoryPanel = CreateUIObject(
            "Weapon Inventory",
            canvas.transform,
            new Vector2(920f, 650f),
            new Color(0.48f, 0.48f, 0.48f, 1f)
        );

        RectTransform panelRect = inventoryPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = inventoryPanel.GetComponent<Image>();
        panelImage.color = new Color(0.55f, 0.55f, 0.55f, 1f);

        CreateText("Title", inventoryPanel.transform, "武器背包",
            28, TextAnchor.MiddleLeft, new Vector2(-5f, 285f), new Vector2(850f, 45f));
        goldText = CreateText("Gold", inventoryPanel.transform, "",
            24, TextAnchor.MiddleRight, new Vector2(-5f, 285f), new Vector2(850f, 45f));
        CreateText("Hint", inventoryPanel.transform, "按 E 關閉背包｜選擇武器後按下裝備",
            17, TextAnchor.MiddleRight, new Vector2(-5f, -292f), new Vector2(850f, 35f));

        GameObject listArea = CreateUIObject(
            "Inventory Grid",
            inventoryPanel.transform,
            new Vector2(550f, 500f),
            new Color(0.25f, 0.25f, 0.25f, 1f)
        );
        listArea.GetComponent<RectTransform>().anchoredPosition = new Vector2(-155f, 0f);

        listArea.AddComponent<RectMask2D>();
        GameObject gridContent = new GameObject("Inventory Items", typeof(RectTransform));
        weaponListContent = gridContent.GetComponent<RectTransform>();
        weaponListContent.SetParent(listArea.transform, false);
        weaponListContent.anchorMin = new Vector2(0f, 1f);
        weaponListContent.anchorMax = Vector2.one;
        weaponListContent.pivot = new Vector2(0.5f, 1f);
        weaponListContent.sizeDelta = Vector2.zero;
        GridLayoutGroup layout = gridContent.AddComponent<GridLayoutGroup>();
        layout.padding = new RectOffset(25, 25, 25, 25);
        layout.spacing = new Vector2(10f, 10f);
        layout.cellSize = new Vector2(90f, 90f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 5;
        ContentSizeFitter fitter = gridContent.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect inventoryScroll = listArea.AddComponent<ScrollRect>();
        inventoryScroll.viewport = listArea.GetComponent<RectTransform>();
        inventoryScroll.content = weaponListContent;
        inventoryScroll.horizontal = false;
        inventoryScroll.vertical = true;
        inventoryScroll.movementType = ScrollRect.MovementType.Clamped;
        inventoryScroll.scrollSensitivity = 35f;

        GameObject details = CreateUIObject(
            "Weapon Details",
            inventoryPanel.transform,
            new Vector2(280f, 500f),
            new Color(0.29f, 0.29f, 0.29f, 1f)
        );
        details.GetComponent<RectTransform>().anchoredPosition = new Vector2(300f, 0f);

        equippedText = CreateText("Equipped", details.transform, "",
            19, TextAnchor.MiddleLeft, new Vector2(0f, 190f), new Vector2(240f, 70f));
        detailText = CreateText("Stats", details.transform, "",
            20, TextAnchor.UpperLeft, new Vector2(0f, 25f), new Vector2(240f, 240f));
        detailText.resizeTextForBestFit = true;
        detailText.resizeTextMinSize = 12;
        detailText.resizeTextMaxSize = 20;

        CreateText("Equip Hint", details.transform, "裝備至快捷欄",
            18, TextAnchor.MiddleCenter, new Vector2(0f, -137f), new Vector2(240f, 30f));

        for (int slotIndex = 0; slotIndex < 3; slotIndex++)
        {
            int capturedSlot = slotIndex;
            Button equipButton = CreateButton(
                details.transform,
                $"{slotIndex + 1}",
                new Vector2(-80f + slotIndex * 80f, -175f),
                new Vector2(65f, 50f),
                new Color(0.35f, 0.35f, 0.35f, 1f)
            );
            equipButton.onClick.AddListener(
                () => EquipToSlot(selectedWeapon, capturedSlot)
            );
        }

        Button sellButton = CreateButton(
            details.transform,
            "出售武器",
            new Vector2(0f, -225f),
            new Vector2(220f, 40f),
            new Color(0.48f, 0.32f, 0.08f, 1f)
        );
        sellButton.onClick.AddListener(SellSelectedWeapon);

        inventoryPanel.SetActive(false);
        BuildPauseUI(canvas.transform);
        BuildHotbar(canvas.transform);
        RefreshWeaponList();
    }

    void BuildHotbar(Transform canvasTransform)
    {
        hotbarPanel = CreateUIObject(
            "Weapon Hotbar",
            canvasTransform,
            new Vector2(360f, 115f),
            new Color(0.08f, 0.08f, 0.08f, 0.88f)
        );

        RectTransform hotbarRect = hotbarPanel.GetComponent<RectTransform>();
        hotbarRect.anchorMin = hotbarRect.anchorMax = new Vector2(0.5f, 0f);
        hotbarRect.pivot = new Vector2(0.5f, 0f);
        hotbarRect.anchoredPosition = new Vector2(0f, 20f);

        for (int slotIndex = 0; slotIndex < 3; slotIndex++)
        {
            int capturedSlotIndex = slotIndex;
            Button slotButton = CreateButton(
                hotbarPanel.transform,
                "",
                new Vector2(-115f + slotIndex * 115f, 0f),
                new Vector2(100f, 90f),
                new Color(0.18f, 0.18f, 0.18f, 1f)
            );
            slotButton.onClick.AddListener(() => SwitchToSlot(capturedSlotIndex));
            hotbarSlotImages[slotIndex] = slotButton.GetComponent<Image>();
            hotbarSlotTexts[slotIndex] = slotButton.GetComponentInChildren<Text>();
            hotbarSlotIcons[slotIndex] = ItemIconGraphic.Create(slotButton.transform, 0,
                new Vector2(0f, 16f), new Vector2(88f, 46f));
            hotbarSlotTexts[slotIndex].fontSize = 13;
            hotbarSlotTexts[slotIndex].alignment = TextAnchor.LowerCenter;
        }

        RefreshHotbar();
    }

    void BuildPauseUI(Transform canvasTransform)
    {
        pausePanel = CreateUIObject(
            "Pause Menu",
            canvasTransform,
            new Vector2(1920f, 1080f),
            new Color(0f, 0f, 0f, 0.72f)
        );

        pauseMenuBox = CreateUIObject(
            "Pause Menu Box",
            pausePanel.transform,
            new Vector2(520f, 500f),
            new Color(0.45f, 0.45f, 0.45f, 1f)
        );

        CreateText(
            "Pause Title",
            pauseMenuBox.transform,
            "遊戲暫停",
            42,
            TextAnchor.MiddleCenter,
            new Vector2(0f, 175f),
            new Vector2(440f, 80f)
        );

        Button continueButton = CreateButton(
            pauseMenuBox.transform,
            "繼續遊戲",
            new Vector2(0f, 65f),
            new Vector2(320f, 70f),
            new Color(0.28f, 0.28f, 0.28f, 1f)
        );
        continueButton.onClick.AddListener(() => SetPaused(false));

        Button settingsButton = CreateButton(
            pauseMenuBox.transform,
            "設定",
            new Vector2(0f, -45f),
            new Vector2(320f, 60f),
            new Color(0.22f, 0.38f, 0.58f, 1f)
        );
        settingsButton.onClick.AddListener(ShowPauseSettings);

        Button shopButton = CreateButton(
            pauseMenuBox.transform,
            "武器商店",
            new Vector2(0f, -45f),
            new Vector2(320f, 60f),
            new Color(0.18f, 0.48f, 0.30f, 1f)
        );
        shopButton.onClick.AddListener(ShowPauseShop);

        settingsButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -115f);

        Button leaveButton = CreateButton(
            pauseMenuBox.transform,
            "離開遊戲",
            new Vector2(0f, -185f),
            new Vector2(320f, 60f),
            new Color(0.48f, 0.18f, 0.18f, 1f)
        );
        leaveButton.onClick.AddListener(LeaveGame);

        CreateText(
            "Pause Hint",
            pauseMenuBox.transform,
            "也可以再次按 Esc 繼續",
            18,
            TextAnchor.MiddleCenter,
            new Vector2(0f, -225f),
            new Vector2(420f, 40f)
        );

        BuildPauseSettingsUI();
        BuildPauseShopUI();
        pausePanel.SetActive(false);
    }

    void BuildPauseShopUI()
    {
        pauseShopBox = CreateUIObject(
            "Pause Weapon Shop",
            pausePanel.transform,
            new Vector2(760f, 650f),
            new Color(0.16f, 0.18f, 0.22f, 1f)
        );

        CreateText("Shop Title", pauseShopBox.transform, "武器商店", 40,
            TextAnchor.MiddleCenter, new Vector2(0f, 270f), new Vector2(620f, 65f));
        shopGoldText = CreateText("Shop Gold", pauseShopBox.transform, "", 20,
            TextAnchor.MiddleCenter, new Vector2(0f, 205f), new Vector2(700f, 65f));
        shopGoldText.resizeTextForBestFit = true;
        shopGoldText.resizeTextMinSize = 16;
        shopGoldText.resizeTextMaxSize = 20;

        CreateShopButton(WeaponType.Rifle, riflePrice, new Vector2(-180f, 130f));
        CreateShopButton(WeaponType.SubmachineGun, submachineGunPrice, new Vector2(180f, 130f));
        CreateShopButton(WeaponType.Shotgun, shotgunPrice, new Vector2(-180f, 45f));
        CreateShopButton(WeaponType.RPG, rpgPrice, new Vector2(180f, 45f));
        CreateShopButton(WeaponType.Pistol, pistolPrice, new Vector2(-180f, -40f));
        CreateShopButton(WeaponType.SniperRifle, sniperRiflePrice, new Vector2(180f, -40f));

        CreateArmorShopButton("輕型防具", 15f, lightArmorPrice, new Vector2(-240f, -125f));
        CreateArmorShopButton("中型防具", 30f, mediumArmorPrice, new Vector2(0f, -125f));
        CreateArmorShopButton("重型防具", 50f, heavyArmorPrice, new Vector2(240f, -125f));

        shopStatusText = CreateText("Shop Status", pauseShopBox.transform,
            "選擇想購買的武器或防具", 20, TextAnchor.MiddleCenter,
            new Vector2(0f, -205f), new Vector2(650f, 55f));

        Button backButton = CreateButton(pauseShopBox.transform, "返回",
            new Vector2(0f, -275f), new Vector2(320f, 50f),
            new Color(0.28f, 0.28f, 0.28f, 1f));
        backButton.onClick.AddListener(ShowPauseMain);
        pauseShopBox.SetActive(false);
    }

    void CreateShopButton(WeaponType type, int price, Vector2 position)
    {
        Button button = CreateButton(pauseShopBox.transform,
            $"{GetWeaponTypeName(type)}\n{price} 金幣", position,
            new Vector2(300f, 68f), GetQualityColor(0.55f));
        button.onClick.AddListener(() => BuyWeapon(type, price));
    }

    void CreateArmorShopButton(string armorName, float baseArmor, int price, Vector2 position)
    {
        Button button = CreateButton(pauseShopBox.transform,
            $"{armorName}\n基礎 +{baseArmor:0} / {price} 金幣", position,
            new Vector2(210f, 68f), new Color(0.25f, 0.42f, 0.62f, 1f));
        button.onClick.AddListener(() => BuyArmor(armorName, baseArmor, price));
    }

    void BuyArmor(string armorName, float baseArmor, int price)
    {
        price = Mathf.Max(0, price);
        if (gold < price)
        {
            GameAudio.UI(GameSound.Denied);
            shopStatusText.text = $"金幣不足，還需要 {price - gold} 金幣。";
            return;
        }

        PlayerHealth health = GetComponent<PlayerHealth>();
        if (health == null)
        {
            shopStatusText.text = "找不到玩家護甲系統。";
            return;
        }

        int tier = GetShopTier();
        float bonus = baseArmor * (1f + (tier - 1) * 0.35f);
        float oldBonus = equippedArmor != null ? equippedArmor.armorBonus : 0f;
        equippedArmor = new ArmorItem
        {
            armorName = $"第 {tier} 階 {armorName}",
            tier = tier,
            armorBonus = bonus
        };

        gold -= price;
        health.ApplyEquipmentArmorBonus(bonus - oldBonus);
        GameAudio.UI(GameSound.Coins);
        shopStatusText.text = $"已裝備 {equippedArmor.armorName}，最大護甲 +{bonus:0}。";
        RefreshShopUI();
    }

    void BuyWeapon(WeaponType type, int price)
    {
        price = Mathf.Max(0, price);
        if (weapons.Count >= capacity)
        {
            GameAudio.UI(GameSound.Denied);
            shopStatusText.text = "背包已滿，請先出售武器。";
            return;
        }

        if (gold < price)
        {
            GameAudio.UI(GameSound.Denied);
            shopStatusText.text = $"金幣不足，還需要 {price - gold} 金幣。";
            return;
        }

        WeaponItem weapon = CreateShopWeapon(type);
        if (!AddWeapon(weapon))
        {
            GameAudio.UI(GameSound.Denied);
            shopStatusText.text = "購買失敗，背包沒有空位。";
            return;
        }

        gold -= price;
        selectedWeapon = weapon;
        GameAudio.UI(GameSound.Coins);
        shopStatusText.text = $"已購買 {weapon.weaponName}，可在背包中裝備。";
        RefreshShopUI();
        RefreshDetails();
    }

    WeaponItem CreateShopWeapon(WeaponType type)
    {
        int tier = GetShopTier();
        float quality = GetShopQuality(tier);
        WeaponItem weapon = new WeaponItem
        {
            weaponName = $"第 {tier} 階 商店 {GetWeaponTypeName(type)}",
            weaponType = type,
            quality = quality,
            damage = 30f,
            fireInterval = 0.2f,
            projectileSpeed = 70f,
            projectilesPerShot = 1
        };

        switch (type)
        {
            case WeaponType.SubmachineGun:
                weapon.damage = 18f; weapon.fireInterval = 0.08f; weapon.spreadAngle = 4f; break;
            case WeaponType.Shotgun:
                weapon.damage = 12f; weapon.fireInterval = 0.75f;
                weapon.projectilesPerShot = 8; weapon.spreadAngle = 14f; break;
            case WeaponType.RPG:
                weapon.damage = 90f; weapon.fireInterval = 1.2f;
                weapon.projectileSpeed = 35f; weapon.explosionRadius = 5f; break;
            case WeaponType.Pistol:
                weapon.damage = 24f; weapon.fireInterval = 0.3f; break;
            case WeaponType.SniperRifle:
                weapon.damage = 85f; weapon.fireInterval = 1f; weapon.projectileSpeed = 120f; break;
            case WeaponType.LightMachineGun:
                weapon.damage = 22f; weapon.fireInterval = 0.11f; weapon.spreadAngle = 3f; break;
        }

        // Every shop tier meaningfully improves damage, handling and projectile speed.
        float powerMultiplier = 1f + (tier - 1) * 0.28f;
        weapon.damage *= powerMultiplier;
        weapon.projectileSpeed *= 1f + (tier - 1) * 0.08f;
        weapon.fireInterval /= 1f + (tier - 1) * 0.06f;

        return weapon;
    }

    int GetShopTier()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        int level = health != null ? health.CurrentLevel : 1;
        return Mathf.Clamp(1 + level / 20, 1, 6);
    }

    static float GetShopQuality(int tier)
    {
        float[] tierQualities = { 0.12f, 0.32f, 0.52f, 0.72f, 0.88f, 1f };
        return tierQualities[Mathf.Clamp(tier, 1, tierQualities.Length) - 1];
    }

    void RefreshShopUI()
    {
        if (shopGoldText != null)
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            int level = health != null ? health.CurrentLevel : 1;
            int tier = GetShopTier();
            shopGoldText.text =
                $"等級 {level}　商店第 {tier} 階（{GetQualityName(GetShopQuality(tier))}）\n" +
                $"持有金幣：{gold}　背包：{weapons.Count}/{capacity}　" +
                $"防具：{(equippedArmor != null ? equippedArmor.armorName : "無")}";
        }
    }

    void BuildPauseSettingsUI()
    {
        pauseSettingsBox = CreateUIObject(
            "Pause Settings Box",
            pausePanel.transform,
            new Vector2(620f, 580f),
            new Color(0.45f, 0.45f, 0.45f, 1f)
        );

        CreateText("Settings Title", pauseSettingsBox.transform, "設定", 40,
            TextAnchor.MiddleCenter, new Vector2(0f, 220f), new Vector2(500f, 70f));
        CreateText("Volume Label", pauseSettingsBox.transform, "主音量", 22,
            TextAnchor.MiddleLeft, new Vector2(-190f, 75f), new Vector2(150f, 45f));
        pauseVolumeSlider = CreatePauseSlider(pauseSettingsBox.transform, new Vector2(80f, 75f));
        pauseVolumeSlider.value = PlayerPrefs.GetFloat("Settings.MasterVolume", AudioListener.volume);
        pauseVolumeSlider.onValueChanged.AddListener(value => AudioListener.volume = Mathf.Clamp01(value));

        CreateText("Fullscreen Label", pauseSettingsBox.transform, "全螢幕", 22,
            TextAnchor.MiddleLeft, new Vector2(-190f, 0f), new Vector2(150f, 45f));
        pauseFullscreenToggle = CreatePauseToggle(pauseSettingsBox.transform, new Vector2(-65f, 0f));
        pauseFullscreenToggle.isOn =
            PlayerPrefs.GetInt("Settings.Fullscreen", Screen.fullScreen ? 1 : 0) == 1;

        Button guideButton = CreateButton(pauseSettingsBox.transform, "武器組合圖鑑",
            new Vector2(0f, -90f), new Vector2(320f, 60f),
            new Color(0.48f, 0.32f, 0.08f, 1f));
        guideButton.onClick.AddListener(ShowWeaponBuildGuide);

        Button saveButton = CreateButton(pauseSettingsBox.transform, "儲存並返回",
            new Vector2(0f, -165f), new Vector2(320f, 60f),
            new Color(0.18f, 0.38f, 0.62f, 1f));
        saveButton.onClick.AddListener(SavePauseSettings);

        Button backButton = CreateButton(pauseSettingsBox.transform, "取消",
            new Vector2(0f, -235f), new Vector2(320f, 50f),
            new Color(0.28f, 0.28f, 0.28f, 1f));
        backButton.onClick.AddListener(ShowPauseMain);
        pauseSettingsBox.SetActive(false);
        BuildWeaponBuildGuideUI();
    }

    void BuildWeaponBuildGuideUI()
    {
        pauseBuildGuideBox = CreateUIObject(
            "Weapon Build Guide",
            pausePanel.transform,
            new Vector2(1080f, 850f),
            new Color(0.12f, 0.14f, 0.18f, 0.98f)
        );

        CreateText("Guide Title", pauseBuildGuideBox.transform, "武器組合圖鑑",
            40, TextAnchor.MiddleCenter, new Vector2(0f, 375f), new Vector2(900f, 60f));
        CreateText("Guide Hint", pauseBuildGuideBox.transform,
            "快捷欄中的三把武器符合條件時自動啟用｜組合順序不限",
            18, TextAnchor.MiddleCenter, new Vector2(0f, 330f), new Vector2(980f, 45f));

        GameObject viewportObject = CreateUIObject(
            "Build List Viewport",
            pauseBuildGuideBox.transform,
            new Vector2(980f, 620f),
            new Color(0.07f, 0.08f, 0.11f, 1f)
        );
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchoredPosition = new Vector2(0f, 5f);
        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        GameObject contentObject = new GameObject("Build List Content", typeof(RectTransform));
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(-35f, 0f);

        Text guideText = CreateText(
            "Build Descriptions",
            content,
            GetWeaponBuildGuideText(),
            20,
            TextAnchor.UpperLeft,
            new Vector2(0f, -10f),
            new Vector2(920f, 3290f)
        );
        guideText.horizontalOverflow = HorizontalWrapMode.Wrap;
        guideText.verticalOverflow = VerticalWrapMode.Overflow;
        guideText.lineSpacing = 1.15f;
        guideText.raycastTarget = false;

        // Size the scroll area from the translated text so every entry stays reachable.
        float guideHeight = guideText.preferredHeight;
        RectTransform guideRect = guideText.rectTransform;
        guideRect.anchorMin = guideRect.anchorMax = new Vector2(0.5f, 1f);
        guideRect.pivot = new Vector2(0.5f, 1f);
        guideRect.anchoredPosition = new Vector2(0f, -10f);
        guideRect.sizeDelta = new Vector2(920f, guideHeight);
        content.sizeDelta = new Vector2(-35f, guideHeight + 20f);

        ScrollRect scrollRect = viewportObject.AddComponent<ScrollRect>();
        scrollRect.viewport = viewport;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 45f;

        Button backButton = CreateButton(pauseBuildGuideBox.transform, "返回設定",
            new Vector2(0f, -375f), new Vector2(300f, 52f),
            new Color(0.25f, 0.32f, 0.42f, 1f));
        backButton.onClick.AddListener(ShowPauseSettings);
        pauseBuildGuideBox.SetActive(false);
    }

    static string GetWeaponBuildGuideText()
    {
        return
            "1. 槍神｜手槍＋手槍＋手槍\n切換手槍後 3 秒射速 +25%；連續爆頭提高暴擊率，最高 +20%。\n\n" +
            "2. 紅線｜衝鋒槍＋衝鋒槍＋衝鋒槍\n持續射擊每秒射速 +4%，最高 +24%；移動速度 +15%，停止射擊後重置。\n\n" +
            "3. 堡壘｜輕機槍＋輕機槍＋輕機槍\n持續射擊 1 秒後獲得 20% 減傷。\n\n" +
            "4. 一擊理論｜狙擊槍＋狙擊槍＋狙擊槍\n每種武器第一發傷害 +35%。\n\n" +
            "5. 碎骨者｜散彈槍＋散彈槍＋散彈槍\n5 公尺內傷害 +30%；連續命中產生破甲，最高 30%。\n\n" +
            "6. 末日火力｜火箭筒＋火箭筒＋火箭筒\n爆炸範圍 +25%；爆炸擊殺有 20% 機率產生二次爆炸。\n\n" +
            "7. 突擊兵｜步槍＋衝鋒槍＋散彈槍\n切換不同類型獲得 4 秒傷害 +15%；三種都使用後射速 +15%。\n\n" +
            "8. 戰術專家｜步槍＋狙擊槍＋手槍\n爆頭標記敵人 5 秒，其他武器對標記目標傷害 +20%。\n\n" +
            "9. 突破手｜步槍＋散彈槍＋手槍\n10 公尺內擊殺後移動速度 +20%，持續 4 秒；下一次攻擊傷害 +25%。\n\n" +
            "10. 壓制火力｜步槍＋輕機槍＋手槍\n連續攻擊同一敵人每發傷害 +1%，最高 +20%，更換目標後重置。\n\n" +
            "11. 清道夫｜衝鋒槍＋散彈槍＋手槍\n近距離擊殺獲得 5% 移動速度，最多堆疊 3 層。\n\n" +
            "12. 獵人｜衝鋒槍＋狙擊槍＋手槍\n狙擊命中標記弱點 8 秒，衝鋒槍與手槍攻擊有額外 25% 暴擊率。\n\n" +
            "13. 距離支配者｜散彈槍＋狙擊槍＋手槍\n狙擊槍對 20 公尺外傷害 +20%；散彈槍對 7 公尺內傷害 +20%；手槍對中距離傷害 +15%。\n\n" +
            "14. 全能戰士｜步槍＋衝鋒槍＋狙擊槍\n近距離強化射速、中距離強化傷害、遠距離增加暴擊。\n\n" +
            "15. 三段戰法｜步槍＋散彈槍＋狙擊槍\n擊殺後強化下一擊：狙擊槍 +15%；步槍 +20%；散彈槍 +25%。\n\n" +
            "16. 戰爭機器｜步槍＋輕機槍＋火箭筒\n每射擊 30 發累積火力；3 點後下一次爆炸傷害 +50%。\n\n" +
            "17. 攻城者｜散彈槍＋輕機槍＋火箭筒\n對等級 80 以上敵人與首領傷害 +30%，移動速度 -10%。\n\n" +
            "18. 死亡區域｜狙擊槍＋輕機槍＋火箭筒\n狙擊槍標記火力目標，輕機槍與爆炸武器對其傷害 +20%。\n\n" +
            "19. 閃擊爆破｜衝鋒槍＋散彈槍＋火箭筒\n爆炸命中後 5 秒移動速度 +25%，近距離攻擊該目標傷害 +20%。\n\n" +
            "20. 震撼突擊｜步槍＋衝鋒槍＋火箭筒\n爆炸造成震盪，步槍與衝鋒槍對該目標傷害 +15%。\n\n" +
            "21. 第一擊｜步槍＋狙擊槍＋火箭筒\n滿血敵人首次受擊傷害 +40%；首領改為 +15%。\n\n" +
            "22. 賞金獵人｜手槍＋狙擊槍＋火箭筒\n擊殺等級 80 以上敵人或首領獲得額外金幣。\n\n" +
            "23. 房間清理者｜手槍＋散彈槍＋火箭筒\n擊殺敵人產生 3 公尺傷害衝擊波。\n\n" +
            "24. 最後防線｜手槍＋輕機槍＋火箭筒\n生命值低於 40% 時，輕機槍傷害 +20%、手槍暴擊 +20%、爆炸範圍 +20%。\n\n" +
            "25. 彈幕風暴｜步槍＋衝鋒槍＋輕機槍\n每連續命中 15 發獲得一層彈幕，最多 4 層，每層射速 +5%。";
    }

    Slider CreatePauseSlider(Transform parent, Vector2 position)
    {
        GameObject root = CreateUIObject("Volume Slider", parent, new Vector2(340f, 32f),
            new Color(1f, 1f, 1f, 0.2f));
        root.GetComponent<RectTransform>().anchoredPosition = position;

        GameObject fill = CreateUIObject("Fill", root.transform, Vector2.zero,
            new Color(0.12f, 0.45f, 0.85f, 1f));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(5f, 5f);
        fillRect.offsetMax = new Vector2(-5f, -5f);

        GameObject handle = CreateUIObject("Handle", root.transform, new Vector2(28f, 42f), Color.white);
        Slider slider = root.AddComponent<Slider>();
        slider.fillRect = fillRect;
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        return slider;
    }

    Toggle CreatePauseToggle(Transform parent, Vector2 position)
    {
        GameObject root = CreateUIObject("Fullscreen Toggle", parent, new Vector2(42f, 42f),
            new Color(1f, 1f, 1f, 0.2f));
        root.GetComponent<RectTransform>().anchoredPosition = position;
        GameObject checkmark = CreateUIObject("Checkmark", root.transform, new Vector2(28f, 28f),
            new Color(0.12f, 0.45f, 0.85f, 1f));
        Toggle toggle = root.AddComponent<Toggle>();
        toggle.targetGraphic = root.GetComponent<Image>();
        toggle.graphic = checkmark.GetComponent<Image>();
        return toggle;
    }

    void ShowPauseSettings()
    {
        GameAudio.UI(GameSound.Open);
        pauseMenuBox.SetActive(false);
        pauseShopBox.SetActive(false);
        if (pauseBuildGuideBox != null)
            pauseBuildGuideBox.SetActive(false);
        pauseSettingsBox.SetActive(true);
    }

    void ShowWeaponBuildGuide()
    {
        GameAudio.UI(GameSound.Open);
        pauseMenuBox.SetActive(false);
        pauseShopBox.SetActive(false);
        pauseSettingsBox.SetActive(false);
        pauseBuildGuideBox.SetActive(true);
    }

    void ShowPauseShop()
    {
        GameAudio.UI(GameSound.Open);
        pauseMenuBox.SetActive(false);
        pauseSettingsBox.SetActive(false);
        if (pauseBuildGuideBox != null)
            pauseBuildGuideBox.SetActive(false);
        pauseShopBox.SetActive(true);
        shopStatusText.text = "選擇想購買的武器或防具";
        RefreshShopUI();
    }

    void ShowPauseMain()
    {
        if ((pauseSettingsBox != null && pauseSettingsBox.activeSelf) ||
            (pauseShopBox != null && pauseShopBox.activeSelf) ||
            (pauseBuildGuideBox != null && pauseBuildGuideBox.activeSelf)) GameAudio.UI(GameSound.Close);
        pauseSettingsBox.SetActive(false);
        pauseShopBox.SetActive(false);
        if (pauseBuildGuideBox != null)
            pauseBuildGuideBox.SetActive(false);
        pauseMenuBox.SetActive(true);
    }

    void SavePauseSettings()
    {
        AudioListener.volume = pauseVolumeSlider.value;
        GameAudio.UI(GameSound.Confirm);
        Screen.fullScreen = pauseFullscreenToggle.isOn;
        PlayerPrefs.SetFloat("Settings.MasterVolume", pauseVolumeSlider.value);
        PlayerPrefs.SetInt("Settings.Fullscreen", pauseFullscreenToggle.isOn ? 1 : 0);
        PlayerPrefs.Save();
        ShowPauseMain();
    }

    void LeaveGame()
    {
        GameAudio.UI(GameSound.Close);
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }

    void RefreshWeaponList()
    {
        if (weaponListContent == null)
            return;

        for (int childIndex = weaponListContent.childCount - 1; childIndex >= 0; childIndex--)
            Destroy(weaponListContent.GetChild(childIndex).gameObject);

        int consumableSlotCount = 0;
        if (grenadeCount > 0)
        {
            GrenadeThrower thrower = GetComponent<GrenadeThrower>();
            string equippedMarker = thrower != null && thrower.IsEquipped ? "\n已裝備" : "\n右鍵裝備";
            CreateStackSlot(
                $"手榴彈\n×{grenadeCount}{equippedMarker}",
                new Color(0.22f, 0.45f, 0.18f, 1f),
                EquipGrenade
            );
            consumableSlotCount++;
        }

        if (healthPackCount > 0)
        {
            CreateStackSlot(
                $"血包 ×{healthPackCount}\n右鍵使用",
                new Color(0.7f, 0.12f, 0.12f, 1f),
                UseHealthPack,
                ItemAppearance.HealthPack
            );
            consumableSlotCount++;
        }

        for (int floor = 1; floor <= 3; floor++)
        {
            if (floorKeyCounts[floor] <= 0)
                continue;

            CreateStackSlot(
                $"第{floor}層道具\n×{floorKeyCounts[floor]}",
                new Color(0.85f, 0.58f, 0.08f, 1f),
                null
            );
            consumableSlotCount++;
        }

        foreach (WeaponItem weapon in weapons)
        {
            WeaponItem listedWeapon = weapon;
            int equippedSlot = GetEquippedSlotIndex(listedWeapon);
            string marker = equippedSlot >= 0 ? $"\n快捷欄 {equippedSlot + 1}" : "";
            Color slotColor = GetQualityColor(listedWeapon.quality);
            if (listedWeapon == selectedWeapon)
                slotColor = Color.Lerp(slotColor, Color.white, 0.35f);

            Button button = CreateButton(
                weaponListContent,
                $"{GetWeaponTypeName(listedWeapon.weaponType)}\n等級 {listedWeapon.level}{marker}",
                Vector2.zero,
                new Vector2(90f, 90f),
                slotColor
            );
            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = 12;
            label.alignment = TextAnchor.LowerCenter;
            label.rectTransform.sizeDelta = new Vector2(86f, 54f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -16f);
            label.raycastTarget = false;
            ItemIconGraphic.Create(button.transform, (int)listedWeapon.weaponType,
                new Vector2(0f, 28f), new Vector2(82f, 28f));
            button.onClick.AddListener(() =>
            {
                selectedWeapon = listedWeapon;
                RefreshDetails();
                RefreshWeaponList();
            });
        }

        int emptySlotCount = Mathf.Max(0, capacity - weapons.Count - consumableSlotCount);
        for (int emptySlotIndex = 0; emptySlotIndex < emptySlotCount; emptySlotIndex++)
        {
            GameObject emptySlot = CreateUIObject(
                "Empty Slot",
                weaponListContent,
                new Vector2(90f, 90f),
                new Color(0.12f, 0.12f, 0.12f, 1f)
            );
            Image emptyImage = emptySlot.GetComponent<Image>();
            emptyImage.raycastTarget = false;
        }
    }

    void CreateStackSlot(string label, Color color, Action onRightClick, int iconItem = -1)
    {
        GameObject slot = CreateUIObject(
            "Stackable Item",
            weaponListContent,
            new Vector2(90f, 90f),
            color
        );

        Text countText = CreateText(
            "Stack Count",
            slot.transform,
            label,
            16,
            TextAnchor.MiddleCenter,
            Vector2.zero,
            new Vector2(86f, 86f)
        );
        if (iconItem >= 0)
        {
            countText.fontSize = 13;
            countText.rectTransform.sizeDelta = new Vector2(86f, 36f);
            countText.rectTransform.anchoredPosition = new Vector2(0f, -25f);
            countText.raycastTarget = false;
            ItemIconGraphic.Create(slot.transform, iconItem, new Vector2(0f, 17f), new Vector2(76f, 47f));
        }

        if (onRightClick == null)
            return;

        EventTrigger trigger = slot.AddComponent<EventTrigger>();
        EventTrigger.Entry entry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerClick
        };
        entry.callback.AddListener(eventData =>
        {
            PointerEventData pointer = eventData as PointerEventData;
            if (pointer != null && pointer.button == PointerEventData.InputButton.Right)
                onRightClick();
        });
        trigger.triggers.Add(entry);
    }

    void SetPaused(bool paused)
    {
        if (pausePanel == null || paused == IsPaused)
            return;

        GameAudio.UI(paused ? GameSound.Open : GameSound.Close);
        pausePanel.SetActive(paused);
        if (hotbarPanel != null)
            hotbarPanel.SetActive(!paused && !IsOpen);
        if (paused)
        {
            ShowPauseMain();
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = previousTimeScale;
        }
        RefreshCursor();
    }

    public void HandleDeathPauseEnded(float restoredTimeScale)
    {
        // If Escape was opened while the death pause owned timeScale = 0,
        // closing it later must restore the post-respawn scale, not that stale zero.
        if (IsPaused && previousTimeScale <= 0f)
            previousTimeScale = restoredTimeScale;
    }

    void SellSelectedWeapon()
    {
        if (selectedWeapon == null || GetEquippedSlotIndex(selectedWeapon) >= 0)
        {
            GameAudio.UI(GameSound.Denied);
            return;
        }

        GameAudio.UI(GameSound.Coins);
        gold += GetSellPrice(selectedWeapon);
        weapons.Remove(selectedWeapon);
        selectedWeapon = null;
        RefreshWeaponList();
        RefreshDetails();
    }

    int GetEquippedSlotIndex(WeaponItem weapon)
    {
        for (int slotIndex = 0; slotIndex < equippedSlots.Length; slotIndex++)
        {
            if (equippedSlots[slotIndex] == weapon)
                return slotIndex;
        }

        return -1;
    }

    void RefreshHotbar()
    {
        if (hotbarPanel == null)
            return;

        for (int slotIndex = 0; slotIndex < equippedSlots.Length; slotIndex++)
        {
            WeaponItem weapon = equippedSlots[slotIndex];
            bool isActive = slotIndex == activeWeaponSlot && weapon != null;
            if (hotbarSlotIcons[slotIndex] != null)
            {
                hotbarSlotIcons[slotIndex].gameObject.SetActive(weapon != null);
                if (weapon != null) hotbarSlotIcons[slotIndex].SetItem((int)weapon.weaponType);
            }

            if (hotbarSlotImages[slotIndex] != null)
                hotbarSlotImages[slotIndex].color = isActive
                    ? Color.white
                    : new Color(0.18f, 0.18f, 0.18f, 1f);

            if (hotbarSlotTexts[slotIndex] != null)
            {
                hotbarSlotTexts[slotIndex].color = isActive ? Color.black : Color.white;
                hotbarSlotTexts[slotIndex].text = weapon == null
                    ? $"{slotIndex + 1}\n空"
                    : $"{slotIndex + 1}  {GetWeaponTypeName(weapon.weaponType)}\n等級 {weapon.level}";
            }
        }
    }

    int GetSellPrice(WeaponItem weapon)
    {
        if (weapon == null)
            return 0;

        return Mathf.RoundToInt(Mathf.Lerp(
            lowestWeaponSellPrice,
            highestWeaponSellPrice,
            Mathf.Clamp01(weapon.quality)
        ));
    }

    void OnDisable()
    {
        if (IsPaused)
            Time.timeScale = previousTimeScale;
    }

    void RefreshDetails()
    {
        if (detailText == null || equippedText == null || goldText == null)
            return;

        goldText.text = $"金幣：{gold}";
        equippedText.text = equippedWeapon == null
            ? "目前裝備：無"
            : $"目前裝備：{equippedWeapon.weaponName}";

        detailText.text = selectedWeapon == null
            ? "請選擇一把武器\n查看詳細數值。"
            : $"{selectedWeapon.weaponName}\n\n" +
              $"類型：{GetWeaponTypeName(selectedWeapon.weaponType)}\n" +
                $"武器等級：{selectedWeapon.level}\n" +
              $"品質：{GetQualityName(selectedWeapon.quality)}\n" +
              $"品質分數：{Mathf.RoundToInt(selectedWeapon.quality * 100f)}\n" +
              $"傷害：{selectedWeapon.damage:0.0}\n" +
              $"射擊間隔：{selectedWeapon.fireInterval:0.00} 秒\n" +
              $"每秒射擊：{selectedWeapon.ShotsPerSecond:0.0} 發\n" +
              $"子彈速度：{selectedWeapon.projectileSpeed:0.0}\n" +
              GetSpecialStats(selectedWeapon) +
              $"出售價格：{GetSellPrice(selectedWeapon)} 金幣" +
              (GetEquippedSlotIndex(selectedWeapon) >= 0
                  ? "\n（快捷欄中的武器無法出售）"
                  : "");
    }

    static string GetSpecialStats(WeaponItem weapon)
    {
        if (weapon.weaponType == WeaponType.Shotgun)
            return $"每次彈丸：{weapon.projectilesPerShot} 發\n擴散角度：{weapon.spreadAngle:0.0}°\n";

        if (weapon.weaponType == WeaponType.RPG)
            return $"爆炸範圍：{weapon.explosionRadius:0.0}\n";

        return weapon.spreadAngle > 0f
            ? $"擴散角度：{weapon.spreadAngle:0.0}°\n"
            : "";
    }

    public static string GetWeaponTypeName(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Rifle: return "步槍";
            case WeaponType.SubmachineGun: return "衝鋒槍";
            case WeaponType.Shotgun: return "散彈槍";
            case WeaponType.RPG: return "火箭筒";
            case WeaponType.Pistol: return "手槍";
            case WeaponType.SniperRifle: return "狙擊槍";
            case WeaponType.LightMachineGun: return "輕機槍";
            default: return "未知";
        }
    }

    static string GetQualityName(float quality)
    {
        if (quality < 0.25f)
            return "普通";
        if (quality < 0.5f)
            return "優良";
        if (quality < 0.75f)
            return "稀有";
        if (quality < 0.9f)
            return "史詩";
        return "傳說";
    }

    static Color GetQualityColor(float quality)
    {
        quality = Mathf.Clamp01(quality);
        if (quality < 0.25f)
            return new Color(0.45f, 0.45f, 0.45f, 1f);
        if (quality < 0.5f)
            return new Color(0.2f, 0.55f, 0.25f, 1f);
        if (quality < 0.75f)
            return new Color(0.18f, 0.38f, 0.72f, 1f);
        if (quality < 0.9f)
            return new Color(0.55f, 0.22f, 0.72f, 1f);
        return new Color(0.88f, 0.48f, 0.08f, 1f);
    }

    static GameObject CreateUIObject(
        string objectName,
        Transform parent,
        Vector2 size,
        Color color)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        uiObject.transform.SetParent(parent, false);
        RectTransform rect = uiObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        uiObject.GetComponent<Image>().color = color;
        return uiObject;
    }

    static Text CreateText(
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
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        Text text = textObject.GetComponent<Text>();
        text.font = GameUIFont.Default;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.text = content;
        return text;
    }

    static Button CreateButton(
        Transform parent,
        string label,
        Vector2 position,
        Vector2 size,
        Color? backgroundColor = null)
    {
        GameObject buttonObject = CreateUIObject(
            "Weapon Button",
            parent,
            size,
            backgroundColor ?? new Color(0.18f, 0.22f, 0.28f, 1f)
        );
        buttonObject.GetComponent<RectTransform>().anchoredPosition = position;
        Button button = buttonObject.AddComponent<Button>();
        buttonObject.AddComponent<GameUIAudio>();
        button.targetGraphic = buttonObject.GetComponent<Image>();

        CreateText(
            "Label",
            buttonObject.transform,
            label,
            19,
            TextAnchor.MiddleCenter,
            Vector2.zero,
            size
        );
        return button;
    }

    void OnValidate()
    {
        capacity = Mathf.Max(1, capacity);
        startingGold = Mathf.Max(0, startingGold);
        lowestWeaponSellPrice = Mathf.Max(0, lowestWeaponSellPrice);
        highestWeaponSellPrice = Mathf.Max(lowestWeaponSellPrice, highestWeaponSellPrice);
        riflePrice = Mathf.Max(0, riflePrice);
        submachineGunPrice = Mathf.Max(0, submachineGunPrice);
        shotgunPrice = Mathf.Max(0, shotgunPrice);
        rpgPrice = Mathf.Max(0, rpgPrice);
        pistolPrice = Mathf.Max(0, pistolPrice);
        sniperRiflePrice = Mathf.Max(0, sniperRiflePrice);
        lightArmorPrice = Mathf.Max(0, lightArmorPrice);
        mediumArmorPrice = Mathf.Max(0, mediumArmorPrice);
        heavyArmorPrice = Mathf.Max(0, heavyArmorPrice);
    }
}
