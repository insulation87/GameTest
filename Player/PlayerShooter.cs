using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
    [SerializeField, HideInInspector] private bool useNewWeaponAppearance = true;
    [SerializeField] private Transform weaponModelMount;
    [SerializeField] private GameObject rifleModel;
    [SerializeField] private GameObject submachineGunModel;
    [SerializeField] private GameObject shotgunModel;
    [SerializeField] private GameObject rpgModel;
    [SerializeField] private GameObject pistolModel;
    [SerializeField] private GameObject sniperRifleModel;
    [SerializeField] private Vector3 weaponModelPosition = new Vector3(0.28f, -0.25f, 0.55f);
    [Tooltip("Extra rotation applied to the pixel weapon model.")]
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
    private WeaponBuildSystem weaponBuild;
    private RogueTactics rogueTactics;
    private CombatMomentum combatMomentum;
    private float weaponKick;
    private GameObject activeWeaponModel;
    private WeaponType equippedWeaponType = WeaponType.WarHammer;
    public float WeaponEfficiency => CharacterWeapons.Efficiency(gameObject, equippedWeaponType);

    void Awake()
    {
#if UNITY_EDITOR
        ResolveEditorWeaponModels();
#endif

        weaponInventory = GetComponent<WeaponInventory>();
        if (weaponInventory == null)
            weaponInventory = gameObject.AddComponent<WeaponInventory>();

        if (GetComponent<CharacterSkills>() == null) gameObject.AddComponent<CharacterSkills>();
        weaponInventory.Initialize(this);
        if (GetComponent<PickupMagnet>() == null)
            gameObject.AddComponent<PickupMagnet>();

        weaponBuild = GetComponent<WeaponBuildSystem>();
        if (weaponBuild == null)
            weaponBuild = gameObject.AddComponent<WeaponBuildSystem>();
        rogueTactics = GetComponent<RogueTactics>();
        if (rogueTactics == null)
            rogueTactics = gameObject.AddComponent<RogueTactics>();
        combatMomentum = GetComponent<CombatMomentum>();
        if (combatMomentum == null) combatMomentum = gameObject.AddComponent<CombatMomentum>();
        if (GetComponent<TacticalEquipment>() == null) gameObject.AddComponent<TacticalEquipment>();
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

    public void ClearWeapon()
    {
        if (activeWeaponModel != null) { activeWeaponModel.SetActive(false); Destroy(activeWeaponModel); }
        activeWeaponModel = null; weaponKick = 0;
        GetComponent<CrosshairUI>()?.ResetFeedback();
    }

    void ShowWeaponModel(WeaponType weaponType)
    {
        if (activeWeaponModel != null)
            Destroy(activeWeaponModel);

        useNewWeaponAppearance = true; // Also migrate scenes serialized with the legacy FBX option.
        Transform mount = GetWeaponModelMount();
        if (mount == null)
        {
            Debug.LogWarning($"No model is assigned for {weaponType}.", this);
            return;
        }

        activeWeaponModel = new GameObject($"{weaponType} First Person Rig");
        activeWeaponModel.transform.SetParent(mount, false);
        weaponKick = 0f;

        GameObject modelInstance = ItemAppearance.Create((int)weaponType, activeWeaponModel.transform);
        modelInstance.name = $"{weaponType} Model";
        modelInstance.transform.localPosition = weaponModelPosition;
        modelInstance.transform.localRotation = Quaternion.Euler(weaponModelRotation);
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
        PixelCharacterFactory.CreateArms(parent);
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
        if (weaponInventory != null && weaponInventory.ActiveWeapon == null) return;
        if (!CoopSession.Simulates(this))
        {
            var actor = CoopInput.Actor(this);
            var health = GetComponent<PlayerHealth>();
            if (actor != null && actor.Local && !GameSceneFlow.IsLoading && Time.timeScale > 0 &&
                health != null && !health.IsDead && !health.IsChoosingUpgrade &&
                weaponInventory != null && !weaponInventory.IsOpen && !weaponInventory.IsPaused &&
                CanShootInCurrentMode() && CoopInput.FireHeld(this) && Time.time >= nextFireTime)
            {
                var skills = GetComponent<CharacterSkills>();
                nextFireTime = Time.time + Mathf.Max(.02f, fireInterval * (skills != null ? skills.FireIntervalMultiplier : 1));
                weaponKick = .7f; GetComponent<CrosshairUI>()?.NotifyShot(); GameAudio.Shot(equippedWeaponType);
            }
            return;
        }
        if (weaponInventory != null &&
            (weaponInventory.IsOpen || weaponInventory.IsPaused))
            return;

        if (CanShootInCurrentMode() &&
            CoopInput.FireHeld(this) &&
            Time.time >= nextFireTime)
        {
            Fire();
        }
    }

    void LateUpdate()
    {
        if (activeWeaponModel == null || Time.timeScale <= 0f) return;
        weaponKick = Mathf.MoveTowards(weaponKick, 0f, Time.deltaTime * 10f);
        bool melee = CharacterWeapons.IsMelee(equippedWeaponType);
        activeWeaponModel.transform.localPosition = new Vector3(melee ? .08f * weaponKick : 0, 0, -.045f * weaponKick);
        activeWeaponModel.transform.localRotation = melee
            ? Quaternion.Euler(-20f * weaponKick, -35f * weaponKick, -18f * weaponKick)
            : Quaternion.Euler(-3.5f * weaponKick, 0f, 0f);
    }

    public void Fire()
    {
        if (weaponInventory != null && weaponInventory.ActiveWeapon == null) return;
        if (!CoopSession.Simulates(this)) return;
        PlayerHealth firingHealth = GetComponent<PlayerHealth>();
        if (!CanShootInCurrentMode() || Time.timeScale <= 0f ||
            (firingHealth != null && (firingHealth.IsDead || firingHealth.IsChoosingUpgrade)) ||
            (weaponInventory != null && (weaponInventory.IsOpen || weaponInventory.IsPaused)))
            return;

        Camera activeCamera = GetActiveCamera();
        if (activeCamera == null || (!CharacterWeapons.IsMelee(equippedWeaponType) && projectilePrefab == null))
        {
            Debug.LogWarning("PlayerShooter requires a camera and projectile prefab.", this);
            return;
        }

        float effectiveInterval = weaponBuild != null
            ? weaponBuild.ModifyFireInterval(fireInterval, equippedWeaponType)
            : fireInterval;
        if (combatMomentum != null) effectiveInterval = Mathf.Max(.02f, effectiveInterval * combatMomentum.FireIntervalMultiplier);
        var skills = GetComponent<CharacterSkills>();
        if (skills != null) effectiveInterval = Mathf.Max(.02f, effectiveInterval * skills.FireIntervalMultiplier);
        nextFireTime = Time.time + effectiveInterval;
        weaponKick = equippedWeaponType == WeaponType.Shotgun || equippedWeaponType == WeaponType.RPG ||
            equippedWeaponType == WeaponType.SniperRifle ? 1f : .55f;
        GetComponent<CrosshairUI>()?.NotifyShot();
        GameAudio.Shot(equippedWeaponType);
        if (weaponBuild != null)
            weaponBuild.OnShot(equippedWeaponType);

        Ray aimRay = activeCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (CharacterWeapons.IsMelee(equippedWeaponType))
        { MeleeAttack(aimRay); return; }
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
        if (firingHealth != null && firingHealth.ShotStyle == TacticalShotStyle.Split) projectileCount = Mathf.Max(2, projectileCount);
        float effectiveExplosionRadius = weaponBuild != null
            ? weaponBuild.ModifyExplosionRadius(explosionRadius, equippedWeaponType) : explosionRadius;
        effectiveExplosionRadius *= WeaponEfficiency;
        int penetration = GetProjectilePenetration();
        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            float shotDamage = GetProjectileDamage();
            Vector3 shotDirection = GetSpreadDirection(direction, spreadAngle);
            if (firingHealth != null && firingHealth.ShotStyle == TacticalShotStyle.Split)
                shotDirection = Quaternion.AngleAxis(projectileIndex % 2 == 0 ? -5f : 5f, Vector3.up) * shotDirection;
            // Capture the exact direction and per-projectile damage, including split attacks.
            rogueTactics?.RecordShot(projectilePrefab, spawnPosition, shotDirection, projectileSpeed,
                shotDamage, 1, 0, effectiveExplosionRadius, penetration, equippedWeaponType);
            Projectile projectile = Instantiate(
                projectilePrefab,
                spawnPosition,
                Quaternion.LookRotation(shotDirection)
            );

            Renderer placeholder = projectile.GetComponent<Renderer>();
            if (placeholder != null) placeholder.enabled = false;
            GameObject bolt = PixelCharacterFactory.CreateBolt(projectile.transform,
                equippedWeaponType == WeaponType.RPG ? .45f : .18f);
            // A projectile prefab may scale its root to size its physics collider.
            Vector3 parentScale = projectile.transform.lossyScale;
            bolt.transform.localScale = new Vector3(1f / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                1f / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), 1f / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
            projectile.Launch(
                shotDirection,
                projectileSpeed,
                shotDamage,
                gameObject,
                effectiveExplosionRadius,
                penetration,
                equippedWeaponType,
                weaponBuild
            );
            if (firingHealth != null && firingHealth.ShotStyle == TacticalShotStyle.Ricochet) projectile.SetRicochet();
        }
    }

    void MeleeAttack(Ray aim)
    {
        float reach = CharacterWeapons.Reach(equippedWeaponType);
        var hitEnemies = new HashSet<EnemyHealth>();
        var hitChests = new HashSet<TreasureChest>();
        foreach (var collider in Physics.OverlapSphere(aim.origin, reach, aimLayers, QueryTriggerInteraction.Ignore))
        {
            if (collider.transform.IsChildOf(transform) || PickupCollisionUtility.IsPickup(collider)) continue;
            Vector3 target = collider.ClosestPoint(aim.origin + aim.direction * reach);
            Vector3 delta = target - aim.origin;
            if (delta.magnitude > reach || Vector3.Angle(aim.direction, delta) > 65) continue;
            // Sweeps stop at walls and hit each target only once.
            bool blocked = false;
            foreach (var obstruction in Physics.RaycastAll(aim.origin, delta.normalized, delta.magnitude,
                aimLayers, QueryTriggerInteraction.Ignore))
                if (!obstruction.collider.transform.IsChildOf(transform) &&
                    obstruction.collider != collider &&
                    obstruction.collider.GetComponentInParent<EnemyHealth>() == null &&
                    obstruction.collider.GetComponentInParent<TreasureChest>() == null &&
                    !PickupCollisionUtility.IsPickup(obstruction.collider)) { blocked = true; break; }
            if (blocked) continue;
            var enemy = collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null && !enemy.IsDead && hitEnemies.Add(enemy))
            {
                float damage = GetProjectileDamage();
                if (weaponBuild != null) damage = weaponBuild.ModifyDamage(damage, enemy, equippedWeaponType, delta.magnitude, false, false);
                enemy.TakeDamage(damage, GetComponent<PlayerHealth>(), aim.origin);
                weaponBuild?.OnHit(enemy, equippedWeaponType, delta.magnitude, false, false, enemy.IsDead);
            }
            var chest = collider.GetComponentInParent<TreasureChest>();
            if (chest != null && hitChests.Add(chest)) chest.TakeDamage(GetProjectileDamage(), GetComponent<PlayerHealth>());
        }
    }

    float GetProjectileDamage()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        float damage = health != null
            ? health.CalculateOutgoingDamage(projectileDamage)
            : projectileDamage;
        float style = health != null && !CharacterWeapons.IsMelee(equippedWeaponType) ?
            health.ShotStyle == TacticalShotStyle.Split ? .65f : health.ShotStyle == TacticalShotStyle.Ricochet ? .8f : 1f : 1f;
        return damage * style * (combatMomentum != null ? combatMomentum.DamageMultiplier : 1f) *
            (GetComponent<CharacterSkills>()?.DamageMultiplier ?? 1f) * WeaponEfficiency;
    }

    int GetProjectilePenetration()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        return health != null ? health.BulletPenetration + (health.ShotStyle == TacticalShotStyle.Pierce ? 1 : 0) : 0;
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
        if (DungeonVictoryUI.IsOpen) return false;
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
    LightMachineGun,
    WarHammer = 10, SwordShield, RepeatingCrossbow, LightStaff, DualDaggers,
    FireStaff, ArcaneFocus, FrostStaff, Scythe, Longbow
}

[Serializable]
public class WeaponItem
{
    public string itemId;
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
    public string itemId;
    public string armorName;
    public int tier;
    public ArmorSlot slot = ArmorSlot.Chest;
    public int level = 1;
    public float quality;
    public float armorBonus;
    public TacticalAffix affix;
}

public partial class WeaponInventory : MonoBehaviour
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
    private bool guideOpenedFromSettings;
    private GameObject pauseShopBox;
    private GameObject hotbarPanel;
    private GameSettingsMenu pauseSettingsMenu;
    private RectTransform weaponListContent;
    private Text detailText;
    private Text equippedText;
    private Text goldText;
    private Text shopGoldText;
    private Text shopStatusText;
    private readonly Dictionary<WeaponType, Button> weaponShopButtons = new Dictionary<WeaponType, Button>();
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
    private float healthPackRestoreAmount = 25f;
    private bool leaveWithoutSaveConfirmed;
    private readonly int[] floorKeyCounts = new int[4];

    public bool IsOpen => inventoryPanel != null && inventoryPanel.activeSelf;
    public bool IsPaused => pausePanel != null && pausePanel.activeSelf;
    public IReadOnlyList<WeaponItem> Weapons => weapons;
    public IReadOnlyList<WeaponItem> EquippedSlots => equippedSlots;
    public int Gold => gold;
    public int WeaponPurchaseCount { get; private set; }
    public void RestoreShopProgress(int count) { WeaponPurchaseCount = Mathf.Max(0, count); RefreshShopUI(); }
    public void RestoreWallet(int amount) { gold = Mathf.Max(0, amount); RefreshDetails(); RefreshShopUI(); }

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

        WeaponType[] types = (WeaponType[])CharacterWeapons.Types.Clone();
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
        foreach (var item in rewards) EquipmentIdentity.Ensure(item);
        weapons.InsertRange(0, rewards);
        healthPackCount += 10;
        capacity = Mathf.Max(capacity, UsedItemSlots);
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

    public void AddFloorKey(int floor)
    {
        floor = Mathf.Clamp(floor, 1, 3);
        if (floor == 1) return; // Retired "第1層道具" inventory item; shared keystone progress is separate.
        GameAudio.UI(GameSound.Key);
        floorKeyCounts[floor]++;
        RefreshWeaponList();
    }

    void UseHealthPack()
    {
        if (CoopSession.Command(this, "heal")) return;
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

        AddWeapon(CharacterWeapons.Create(CharacterWeapons.ForCharacter(
            GetComponent<CharacterSkills>()?.Character?.Id), 1, 0f), true);

        BuildUI();
        RefreshCursor();
    }

    void Update()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        if (!CoopSession.LocalUI(this)) return;
        if (GameSceneFlow.IsLoading || DungeonVictoryUI.IsOpen || (health != null && health.IsChoosingUpgrade))
            return;

        if (GameSettingsMenu.EscapeConsumedFrame == Time.frameCount) return;

        if (CoopInput.Down(this, CoopButton.Pause, KeyCode.Escape))
        {
            if (IsOpen)
                SetOpen(false);
            else
                SetPaused(!IsPaused);

            return;
        }

        if (!IsPaused && CoopInput.Down(this, CoopButton.Inventory, toggleKey))
            SetOpen(!IsOpen);

        if (!IsPaused && !IsOpen)
        {
            if (CoopInput.Down(this, CoopButton.Slot1, KeyCode.Alpha1))
                SwitchToSlot(0);
            else if (CoopInput.Down(this, CoopButton.Slot2, KeyCode.Alpha2))
                SwitchToSlot(1);
            else if (CoopInput.Down(this, CoopButton.Slot3, KeyCode.Alpha3))
                SwitchToSlot(2);
            if (CoopInput.Down(this, CoopButton.Heal, KeyCode.H)) UseHealthPack();
        }
    }

    public bool AddWeapon(WeaponItem weapon, bool equipImmediately = false)
    {
        if (weapon == null || !HasItemSpace)
            return false;

        EquipmentIdentity.Ensure(weapon);
        weapons.Add(weapon);

        if (equipImmediately || equippedWeapon == null)
            EquipToSlot(weapon, equipImmediately ? 0 : activeWeaponSlot);

        if (inventoryPanel != null)
            RefreshWeaponList();

        return true;
    }

    public void IncreaseCapacity(int amount)
    {
        capacity = Mathf.Max(1, capacity + Mathf.Max(0, amount));
        RefreshWeaponList();
    }

    public void EquipToSlot(WeaponItem weapon, int slotIndex)
    {
        if (CoopSession.Command(this, "equip", weapons.IndexOf(weapon), slotIndex)) return;
        if (weapon == null || shooter == null ||
            slotIndex < 0 || slotIndex >= equippedSlots.Length || !weapons.Contains(weapon))
            return;

        equippedSlots[slotIndex] = weapon;
        selectedWeapon = weapon; selectedArmor = null;
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
        if (CoopSession.Command(this, "slot", slotIndex)) return;
        if (slotIndex < 0 || slotIndex >= equippedSlots.Length)
            return;

        WeaponItem weapon = equippedSlots[slotIndex];
        if (weapon == null)
            return;

        activeWeaponSlot = slotIndex;
        if (inventoryPanel != null) GameAudio.UI(GameSound.Equip);
        equippedWeapon = weapon;
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
        canvasObject.transform.SetParent(transform, false);
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
            new Vector2(1000f, 800f),
            new Color(0.48f, 0.48f, 0.48f, 1f)
        );

        RectTransform panelRect = inventoryPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = inventoryPanel.GetComponent<Image>();
        panelImage.color = GameUITheme.Panel;

        CreateText("Title", inventoryPanel.transform, "裝備背包",
            28, TextAnchor.MiddleLeft, new Vector2(-5f, 360f), new Vector2(850f, 45f));
        goldText = CreateText("Gold", inventoryPanel.transform, "",
            24, TextAnchor.MiddleRight, new Vector2(-5f, 360f), new Vector2(850f, 45f));
        CreateText("Hint", inventoryPanel.transform, "按 " + GameControls.Label(this, GameAction.Inventory) + " 關閉背包｜可裝備、出售或丟棄裝備給隊友",
            17, TextAnchor.MiddleRight, new Vector2(-5f, -365f), new Vector2(850f, 35f));

        GameObject listArea = CreateUIObject(
            "Inventory Grid",
            inventoryPanel.transform,
            new Vector2(600f, 650f),
            new Color(0.25f, 0.25f, 0.25f, 1f)
        );
        listArea.GetComponent<RectTransform>().anchoredPosition = new Vector2(-170f, 0f);

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
            new Vector2(280f, 650f),
            new Color(0.29f, 0.29f, 0.29f, 1f)
        );
        details.GetComponent<RectTransform>().anchoredPosition = new Vector2(320f, 0f);

        equippedText = CreateText("Equipped", details.transform, "",
            19, TextAnchor.MiddleLeft, new Vector2(0f, 190f), new Vector2(240f, 70f));
        equippedText.resizeTextForBestFit = true; equippedText.resizeTextMinSize = 12; equippedText.resizeTextMaxSize = 19;
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
            new Vector2(-57f, -225f),
            new Vector2(106f, 40f),
            new Color(0.48f, 0.32f, 0.08f, 1f)
        );
        sellButton.onClick.AddListener(SellSelectedWeapon);
        dropWeaponButton = CreateButton(details.transform, "丟棄武器", new Vector2(57f, -225f),
            new Vector2(106f, 40f), GameUITheme.Raised);
        dropWeaponButton.name = "Drop Weapon";
        dropWeaponButton.onClick.AddListener(() => DropWeapon(selectedWeapon));

        BuildArmorUI(details.transform);
        inventoryPanel.SetActive(false);
        BuildPauseUI(canvas.transform);
        BuildHotbar(canvas.transform);
        GameUITheme.StyleTree(canvasObject);
        RefreshWeaponList();
        RefreshHotbar();
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
        // Leave room for the multiplayer room address below the weapon slots.
        hotbarRect.anchoredPosition = new Vector2(0f, 60f);

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
        GameUITheme.Stretch(pausePanel.GetComponent<RectTransform>());

        pauseMenuBox = CreateUIObject(
            "Pause Menu Box",
            pausePanel.transform,
            new Vector2(520f, 600f),
            new Color(0.45f, 0.45f, 0.45f, 1f)
        );

        CreateText(
            "Pause Title",
            pauseMenuBox.transform,
            "遊戲暫停",
            42,
            TextAnchor.MiddleCenter,
            new Vector2(0f, 235f),
            new Vector2(440f, 80f)
        );

        Button continueButton = CreateButton(
            pauseMenuBox.transform,
            "繼續遊戲",
            new Vector2(0f, 150f),
            new Vector2(320f, 70f),
            new Color(0.28f, 0.28f, 0.28f, 1f)
        );
        continueButton.onClick.AddListener(() => SetPaused(false));

        Button progressButton = CreateButton(pauseMenuBox.transform, "儲存進度",
            new Vector2(0f, 75f), new Vector2(320f, 60f), new Color(.18f, .4f, .48f, 1f));
        progressButton.onClick.AddListener(() =>
        {
            GameProgressSave save = GetComponent<GameProgressSave>();
            bool saved = save != null && save.SaveNow();
            progressButton.GetComponentInChildren<Text>().text = save != null ? save.LastMessage : "目前無法儲存";
            GameAudio.UI(saved ? GameSound.Confirm : GameSound.Denied);
        });

        Button guideButton = CreateButton(pauseMenuBox.transform, "角色武器與防具",
            new Vector2(0f, 10f), new Vector2(320f, 55f), GameUITheme.Raised);
        guideButton.onClick.AddListener(ShowWeaponBuildGuide);

        Button settingsButton = CreateButton(
            pauseMenuBox.transform,
            "設定",
            new Vector2(0f, -55f),
            new Vector2(320f, 60f),
            new Color(0.22f, 0.38f, 0.58f, 1f)
        );
        settingsButton.onClick.AddListener(ShowPauseSettings);

        Button shopButton = CreateButton(
            pauseMenuBox.transform,
            "裝備商店",
            new Vector2(0f, -55f),
            new Vector2(320f, 60f),
            new Color(0.18f, 0.48f, 0.30f, 1f)
        );
        shopButton.onClick.AddListener(ShowPauseShop);

        settingsButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -125f);

        Button leaveButton = CreateButton(
            pauseMenuBox.transform,
            "儲存並返回主選單",
            new Vector2(0f, -195f),
            new Vector2(320f, 60f),
            new Color(0.48f, 0.18f, 0.18f, 1f)
        );
        leaveButton.onClick.AddListener(LeaveGame);

        CreateText(
            "Pause Hint",
            pauseMenuBox.transform,
            "Esc 繼續遊戲｜每 30 秒自動儲存",
            18,
            TextAnchor.MiddleCenter,
            new Vector2(0f, -266f),
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

        CreateText("Shop Title", pauseShopBox.transform, "裝備商店", 40,
            TextAnchor.MiddleCenter, new Vector2(0f, 270f), new Vector2(620f, 65f));
        shopGoldText = CreateText("Shop Gold", pauseShopBox.transform, "", 20,
            TextAnchor.MiddleCenter, new Vector2(0f, 205f), new Vector2(700f, 65f));
        shopGoldText.resizeTextForBestFit = true;
        shopGoldText.resizeTextMinSize = 16;
        shopGoldText.resizeTextMaxSize = 20;

        for (int i = 0; i < CharacterWeapons.Types.Length; i++)
            CreateShopButton(CharacterWeapons.Types[i], CharacterWeapons.ShopPrice(CharacterWeapons.Types[i]),
                new Vector2(i % 2 == 0 ? -180 : 180, 145 - (i / 2) * 60));
        for (int i = 0; i < 4; i++)
        {
            ArmorSlot slot = (ArmorSlot)i;
            Button armorButton = CreateButton(pauseShopBox.transform, ArmorLoot.SlotName(slot) + "\n100 金幣",
                new Vector2(-255 + i * 170, -170), new Vector2(160, 55), GameUITheme.Raised);
            armorButton.onClick.AddListener(() => BuyArmorPiece(slot));
        }

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
            new Vector2(300f, 55f), GetQualityColor(0.55f));
        button.onClick.AddListener(() => BuyWeapon(type, price));
        weaponShopButtons[type] = button;
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
        if (CoopSession.Command(this, "armor", Mathf.RoundToInt(baseArmor))) return;
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

        BuyArmorPiece(ArmorSlot.Chest);
    }

    void BuyArmorPiece(ArmorSlot slot)
    {
        if (!Enum.IsDefined(typeof(ArmorSlot), slot)) return;
        if (CoopSession.Command(this, "buyArmor", (int)slot)) return;
        if (!HasItemSpace || gold < 100)
        { GameAudio.UI(GameSound.Denied); if (shopStatusText != null) shopStatusText.text = "金幣不足或背包已滿。"; return; }
        var item = ArmorLoot.Create(slot, Mathf.Clamp(GetShopTier() * 10, 1, 100), GetShopQuality(GetShopTier()));
        if (!AddArmor(item)) return;
        gold -= 100; selectedArmor = item; selectedWeapon = null;
        if (shopStatusText != null) shopStatusText.text = "已購買 " + item.armorName + "，請在背包裝備。";
        GameAudio.UI(GameSound.Coins); RefreshShopUI(); RefreshDetails();
    }

    void BuyWeapon(WeaponType type, int price)
    {
        if (CoopSession.Command(this, "buy", (int)type)) return;
        string character = GetComponent<CharacterSkills>()?.Character.Id;
        if (!WeaponShopProgress.IsUnlocked(type, WeaponPurchaseCount, character))
        {
            GameAudio.UI(GameSound.Denied);
            if (shopStatusText != null) shopStatusText.text = $"武器尚未解鎖：累計購買 {WeaponShopProgress.RequiredPurchases(type, character)} 把武器後開放。";
            return;
        }
        price = Mathf.Max(0, price);
        if (!HasItemSpace)
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
        WeaponPurchaseCount = WeaponShopProgress.AfterPurchase(WeaponPurchaseCount);
        selectedWeapon = weapon; selectedArmor = null;
        GameAudio.UI(GameSound.Coins);
        shopStatusText.text = $"已購買 {weapon.weaponName}，可在背包中裝備。";
        RefreshShopUI();
        RefreshDetails();
    }

    WeaponItem CreateShopWeapon(WeaponType type)
    {
        int tier = GetShopTier();
        return CharacterWeapons.Create(type, Mathf.Clamp(tier * 10, 1, 100), GetShopQuality(tier));
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
        string character = GetComponent<CharacterSkills>()?.Character.Id;
        foreach (var pair in weaponShopButtons)
        {
            bool unlocked = WeaponShopProgress.IsUnlocked(pair.Key, WeaponPurchaseCount, character);
            pair.Value.interactable = unlocked;
            pair.Value.GetComponentInChildren<Text>().text = GetWeaponTypeName(pair.Key) + "\n" +
                (unlocked ? CharacterWeapons.ShopPrice(pair.Key) + " 金幣" : $"未解鎖 · 累計 {WeaponShopProgress.RequiredPurchases(pair.Key, character)} 把");
        }
        if (shopGoldText != null)
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            int level = health != null ? health.CurrentLevel : 1;
            int tier = GetShopTier();
            shopGoldText.text =
                $"等級 {level}　商店第 {tier} 階（{GetQualityName(GetShopQuality(tier))}）\n" +
                $"持有金幣：{gold}　背包：{weapons.Count}/{capacity}　" +
                $"累計購買武器：{WeaponPurchaseCount}";
        }
    }

    void BuildPauseSettingsUI()
    {
        pauseSettingsBox = CreateUIObject(
            "Pause Settings Box",
            pausePanel.transform,
            new Vector2(1060f, 780f),
            Color.clear
        );
        pauseSettingsBox.SetActive(false);
        pauseSettingsMenu = GameSettingsMenu.Attach(pauseSettingsBox, ShowPauseMain, ShowWeaponBuildGuide);
        BuildWeaponBuildGuideUI();
    }

    void BuildWeaponBuildGuideUI()
    {
        pauseBuildGuideBox = WeaponBuildGuideUI.Create(pausePanel.transform,
            () => { if (guideOpenedFromSettings) ShowPauseSettings(); else ShowPauseMain(); }).gameObject;
    }

    static string GetWeaponBuildGuideText() => WeaponBuildGuideUI.GetText();

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
        guideOpenedFromSettings = pauseSettingsBox.activeSelf;
        GameAudio.UI(GameSound.Open);
        pauseMenuBox.SetActive(false);
        pauseShopBox.SetActive(false);
        pauseSettingsBox.SetActive(false);
        pauseBuildGuideBox.GetComponent<WeaponBuildGuideUI>().Open();
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
        pauseSettingsMenu?.ApplyAndClose();
    }

    void LeaveGame()
    {
        if (GameSceneFlow.IsLoading) return;
        GameProgressSave save = CoopSession.IsMultiplayer ? GameProgressSave.Instance : GetComponent<GameProgressSave>();
        if (!CoopSession.IsClient && save != null && !save.SaveNow() && !leaveWithoutSaveConfirmed)
        {
            leaveWithoutSaveConfirmed = true;
            GameAudio.UI(GameSound.Denied);
            // Make a failed save visible and keep the current session available.
            foreach (Text label in pauseMenuBox.GetComponentsInChildren<Text>())
                if (label.name == "Pause Hint") label.text = "未能儲存；再次點擊可不儲存返回";
            return;
        }
        GameAudio.UI(GameSound.Close);
        GameSceneFlow.Load(GameSceneFlow.MenuScene);
    }

    void RefreshWeaponList()
    {
        if (weaponListContent == null)
            return;

        for (int childIndex = weaponListContent.childCount - 1; childIndex >= 0; childIndex--)
            Destroy(weaponListContent.GetChild(childIndex).gameObject);

        int consumableSlotCount = 0;
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

        for (int floor = 2; floor <= 3; floor++)
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
            string marker = equippedSlot >= 0 ? $" [{equippedSlot + 1}]" : "";
            Color slotColor = GetQualityColor(listedWeapon.quality);
            if (listedWeapon == selectedWeapon)
                slotColor = Color.Lerp(slotColor, Color.white, 0.35f);

            Button button = CreateButton(
                weaponListContent,
                $"{GetWeaponTypeName(listedWeapon.weaponType)}\nLv.{listedWeapon.level}{marker}",
                Vector2.zero,
                new Vector2(90f, 90f),
                slotColor
            );
            button.GetComponent<Image>().color = Color.Lerp(GameUITheme.Recess, slotColor, .45f);
            button.transform.Find("Theme Frame").GetComponent<GameUIFrame>().color = slotColor;
            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = 12;
            label.alignment = TextAnchor.LowerCenter;
            label.rectTransform.sizeDelta = new Vector2(86f, 40f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -23f);
            label.raycastTarget = false;
            ItemIconGraphic.Create(button.transform, (int)listedWeapon.weaponType,
                new Vector2(0f, 16f), new Vector2(72f, 45f));
            button.onClick.AddListener(() =>
            {
                selectedWeapon = listedWeapon; selectedArmor = null;
                RefreshDetails();
                RefreshWeaponList();
            });
        }

        AddArmorButtons();
        int emptySlotCount = Mathf.Max(0, capacity - UsedItemSlots);
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
        slot.GetComponent<Image>().color = Color.Lerp(GameUITheme.Recess, color, .45f);
        GameUITheme.Frame(slot.transform);
        slot.transform.Find("Theme Frame").GetComponent<GameUIFrame>().color = color;

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
            leaveWithoutSaveConfirmed = false;
            ShowPauseMain();
            previousTimeScale = Time.timeScale;
            if (!CoopSession.IsOnline) Time.timeScale = 0f;
        }
        else
        {
            if (!CoopSession.IsOnline) Time.timeScale = previousTimeScale;
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
        if (CoopSession.Command(this, "sell", weapons.IndexOf(selectedWeapon))) return;
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
                    ? GameUITheme.Gold
                    : GameUITheme.Raised;

            if (hotbarSlotTexts[slotIndex] != null)
            {
                hotbarSlotTexts[slotIndex].color = isActive ? GameUITheme.Ink : GameUITheme.Ivory;
                string key = GameControls.Label(this, (GameAction)((int)GameAction.Slot1 + slotIndex));
                hotbarSlotTexts[slotIndex].text = weapon == null
                    ? $"{key}\n空"
                    : $"{key}  {GetWeaponTypeName(weapon.weaponType)}\n等級 {weapon.level}";
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
        GameSettings.Changed -= RefreshControlHints;
        if (IsPaused)
            if (!CoopSession.IsOnline) Time.timeScale = previousTimeScale;
    }

    void OnEnable() => GameSettings.Changed += RefreshControlHints;
    void RefreshControlHints()
    {
        RefreshHotbar();
        if (inventoryPanel == null) return;
        Transform hint = inventoryPanel.transform.Find("Hint");
        if (hint != null) hint.GetComponent<Text>().text = "按 " + GameControls.Label(this, GameAction.Inventory) + " 關閉背包｜可裝備、出售或丟棄裝備給隊友";
    }

    void RefreshDetails()
    {
        RefreshDropButtons();
        if (detailText == null || equippedText == null || goldText == null)
            return;

        goldText.text = $"金幣：{gold}";
        equippedText.text = equippedWeapon == null
            ? "目前裝備：無"
            : $"目前裝備：{equippedWeapon.weaponName}\n{WeaponBuildSystem.LoadoutHint(this)}";

        detailText.text = selectedWeapon == null
            ? "請選擇武器或防具。"
            : $"{selectedWeapon.weaponName}\n\n" +
              $"等級 {selectedWeapon.level}｜{GetQualityName(selectedWeapon.quality)}\n" +
              $"傷害：{selectedWeapon.damage * CharacterWeapons.Efficiency(gameObject, selectedWeapon.weaponType):0.0}\n" +
              (CharacterWeapons.Efficiency(gameObject, selectedWeapon.weaponType) < 1f ? "副武器：傷害／爆炸範圍 10%，控場可用\n" : "角色專武：傷害與武器效果 100%\n") +
              $"攻擊間隔：{selectedWeapon.fireInterval:0.00} 秒\n" +
              (CharacterWeapons.IsMelee(selectedWeapon.weaponType) ? "" : $"投射物速度：{selectedWeapon.projectileSpeed:0.0}\n") +
              GetSpecialStats(selectedWeapon) +
              $"出售：{GetSellPrice(selectedWeapon)} 金幣" +
              (GetEquippedSlotIndex(selectedWeapon) >= 0 ? "\n已裝備，無法出售" : "") + "\n" + WeaponBuildSystem.UtilityHint(selectedWeapon.weaponType);
        RefreshArmorDetails();
    }

    string GetSpecialStats(WeaponItem weapon)
    {
        if (CharacterWeapons.IsMelee(weapon.weaponType)) return $"近戰範圍：{CharacterWeapons.Reach(weapon.weaponType):0.0} 公尺\n";
        if (weapon.weaponType == WeaponType.Shotgun)
            return $"每次彈丸：{weapon.projectilesPerShot} 發\n擴散角度：{weapon.spreadAngle:0.0}°\n";

        if (weapon.explosionRadius > 0)
            return $"爆炸範圍：{weapon.explosionRadius * CharacterWeapons.Efficiency(gameObject, weapon.weaponType):0.00}\n";

        return weapon.spreadAngle > 0f
            ? $"擴散角度：{weapon.spreadAngle:0.0}°\n"
            : "";
    }

    public static string GetWeaponTypeName(WeaponType type)
    {
        return CharacterWeapons.Name(type);
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
        switch (objectName)
        {
            case "Weapon Inventory": case "Weapon Hotbar": case "Pause Menu Box":
            case "Pause Weapon Shop": case "Pause Settings Box": case "Weapon Build Guide":
                GameUITheme.Surface(uiObject); break;
            case "Inventory Grid": case "Weapon Details": case "Empty Slot":
                GameUITheme.Surface(uiObject, true); break;
        }
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
        text.color = objectName.Contains("Title") || objectName.Contains("Gold") ? GameUITheme.Gold :
            objectName.Contains("Hint") ? GameUITheme.Muted : GameUITheme.Ivory;
        text.raycastTarget = false;
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
        GameUITheme.StyleButton(button);
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
