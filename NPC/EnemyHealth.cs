using System.Collections.Generic;
using UnityEngine;

public partial class EnemyHealth : MonoBehaviour
{
    static readonly HashSet<EnemyHealth> activeEnemies = new HashSet<EnemyHealth>();
    public static IReadOnlyCollection<EnemyHealth> ActiveEnemies => activeEnemies;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => activeEnemies.Clear();

    [Min(1f)] public float maxHealth = 100f;

    [Header("Damage Numbers")]
    public bool showDamageNumbers = true;
    public Color damageNumberColor = new Color(1f, 0.82f, 0.15f, 1f);
    [Min(0f)] public float damageNumberHeight = 0.5f;

    [Header("Experience Drop")]
    public GameObject experiencePickupPrefab;
    [Min(1f)] public float experiencePickupLifetime = 30f;
    [Min(0f)] public float experienceDropForce = 3f;

    [Header("Health Drop")]
    [Range(0f, 1f)] public float healthDropChance = 0.1f;
    [Min(0f)] public float healthRestoreAmount = 25f;
    public GameObject healthPickupPrefab;
    [Min(1f)] public float healthPickupLifetime = 30f;

    [Header("Weapon Drop")]
    [Tooltip("Drop chance for the weakest NPC.")]
    [Range(0f, 1f)] public float weakNpcWeaponDropChance = 0.175f;
    [Tooltip("Drop chance for the strongest NPC.")]
    [Range(0f, 1f)] public float strongNpcWeaponDropChance = 0.04f;
    [Min(1f)] public float weaponPickupLifetime = 45f;
    public GameObject weaponPickupPrefab;
    [SerializeField] private GameObject rifleModel;
    [SerializeField] private GameObject submachineGunModel;
    [SerializeField] private GameObject shotgunModel;
    [SerializeField] private GameObject rpgModel;
    [SerializeField] private GameObject pistolModel;
    [SerializeField] private GameObject sniperRifleModel;
    [Min(0f)] public float minWeaponDamage = 15f;
    [Min(0f)] public float maxWeaponDamage = 60f;
    [Min(0.01f)] public float slowestFireInterval = 0.35f;
    [Min(0.01f)] public float fastestFireInterval = 0.08f;
    [Min(0.1f)] public float minProjectileSpeed = 45f;
    [Min(0.1f)] public float maxProjectileSpeed = 90f;
    [Range(0f, 0.5f)] public float weaponStatVariation = 0.1f;

    [Header("Armor Equipment Drop")]
    [Range(0f, 1f)] public float armorEquipmentDropChance = .25f;

    public const float DefaultProgressionDropChance = 0.01f;
    [Header("Floor Progression Drop")]
    [Range(0f, 1f)] public float progressionDropChance = DefaultProgressionDropChance;
    [Min(1f)] public float progressionPickupLifetime = 120f;

    private float currentHealth;
    private int enemyLevel = 1;
    private int experienceReward = 10;
    private float enemyStrength;
    [SerializeField, Range(1, 3)] private int dungeonFloor = 1;

    public float CurrentHealth => currentHealth;
    public float HealthNormalized => currentHealth / maxHealth;
    public bool IsDead { get; private set; }
    public int EnemyLevel => enemyLevel;
    public int ExperienceReward => experienceReward;
    public int DungeonFloor => dungeonFloor;

    void OnEnable()
    {
        activeEnemies.Add(this);
        currentHealth = maxHealth;
        IsDead = false;
    }

    void OnDisable() => activeEnemies.Remove(this);

    public void SetMaxHealth(float newMaxHealth, bool refillHealth = true)
    {
        maxHealth = Mathf.Max(1f, newMaxHealth);

        if (refillHealth)
        {
            currentHealth = maxHealth;
            IsDead = false;
        }
        else
        {
            currentHealth = Mathf.Min(currentHealth, maxHealth);
        }
    }

    public void RestoreSavedHealth(float value)
    {
        currentHealth = Mathf.Clamp(value, 1f, maxHealth);
    }

    public void SetLevelAndExperience(int level, int experiencePerLevel)
    {
        enemyLevel = Mathf.Max(1, level);
        experienceReward = enemyLevel * Mathf.Max(0, experiencePerLevel);
    }

    public void SetStrength(float normalizedStrength)
    {
        enemyStrength = Mathf.Clamp01(normalizedStrength);
    }

    public void SetDungeonFloor(int floor)
    {
        dungeonFloor = Mathf.Clamp(floor, 1, 3);
    }

    public void TakeDamage(float damage)
    {
        TakeDamage(damage, null);
    }

    public void TakeDamage(float damage, PlayerHealth damageSource, Vector3? attackOrigin = null)
    {
        if (damage <= 0f || IsDead)
            return;

        BossController boss = GetComponent<BossController>();
        if (boss != null && boss.IsTransforming) return;
        var mechanics = GetComponent<TacticalBossMechanics>();
        if (mechanics != null) damage = mechanics.ModifyIncoming(damage, attackOrigin ?? (damageSource != null ? damageSource.transform.position : transform.position - transform.forward));
        if (damageSource != null) TrackContributor(damageSource);
        EnemyHitFlash flash = GetComponent<EnemyHitFlash>();
        if (flash != null) flash.StopFlash();

        float actualDamage = Mathf.Min(currentHealth, damage);
        GameAudio.At(GameSound.EnemyHurt, transform.position);
        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (showDamageNumbers && damageSource != null)
        {
            DamageNumberPopup.Create(
                GetDamageNumberPosition(),
                actualDamage,
                damageNumberColor,
                damageSource.transform
            );
        }

        if (currentHealth <= 0f)
        {
            if (boss != null && boss.TryAdvancePhase())
            {
                damageSource?.GetComponent<CombatMomentum>()?.OnHit(false);
                return;
            }
            damageSource?.GetComponent<CombatMomentum>()?.OnHit(true);
            Die(damageSource);
        }
        else
        {
            if (flash == null) flash = gameObject.AddComponent<EnemyHitFlash>();
            flash.Pulse();
            damageSource?.GetComponent<CombatMomentum>()?.OnHit(false);
        }
    }

    Vector3 GetDamageNumberPosition()
    {
        // Skinned-mesh bounds include animation margins, not just the visible head.
        if (GetComponent<EnemyCharacterVisual>() != null && TryGetComponent(out Collider body))
        {
            Bounds bodyBounds = body.bounds;
            return new Vector3(bodyBounds.center.x, bodyBounds.max.y + damageNumberHeight, bodyBounds.center.z);
        }
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return transform.position + Vector3.up * damageNumberHeight;

        Bounds bounds = renderers[0].bounds;
        for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            bounds.Encapsulate(renderers[rendererIndex].bounds);

        return new Vector3(
            bounds.center.x,
            bounds.max.y + damageNumberHeight,
            bounds.center.z
        );
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || IsDead)
            return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }

    void Die(PlayerHealth killer)
    {
        if (GetComponent<BossController>() != null) GameAudio.UI(GameSound.BossDeath);
        else GameAudio.At(GameSound.EnemyDeath, transform.position);
        IsDead = true;
        activeEnemies.Remove(this);
        if (killer != null)
        {
            killer.GetComponent<WeaponBuildSystem>()?.ForgetEnemy(this);
            killer.GetComponent<RogueTactics>()?.OnEnemyKilled(this);
            killer.GetComponent<CombatMomentum>()?.OnEnemyKilled();
        }
        DropExperience();
        TryDropHealthPickup();
        TryDropWeapon();
        TryDropArmorEquipment();
        TryDropProgressionItem();
        RewardContributors(killer);
        // Phase transitions return before Die; only the final form opens the result.
        if (GetComponent<BossController>() != null && GetComponent<TacticalTrialBoss>() == null)
            DungeonVictoryUI.Show(killer);
        Destroy(gameObject);
    }

    void TryDropProgressionItem()
    {
        // Floor one progresses through its guaranteed trials, rather than random monster keys.
        if (dungeonFloor == 1 || Random.value >= progressionDropChance)
            return;

        FloorKeyPickup.Create(
            transform.position + Vector3.up * 0.75f,
            dungeonFloor,
            progressionPickupLifetime
        );
    }

    void TryDropWeapon()
    {
        float dropChance = Mathf.Lerp(
            weakNpcWeaponDropChance,
            strongNpcWeaponDropChance,
            enemyStrength
        );

        if (Random.value >= dropChance)
            return;

        float variation = Random.Range(-weaponStatVariation, weaponStatVariation);
        float quality = Mathf.Clamp01(enemyStrength + variation);
        int weaponLevel = GetRandomWeaponLevel();
        WeaponType weaponType = WeaponLoot.RollType();
        WeaponItem weapon = CreateWeaponItem(weaponType, weaponLevel, quality);
        // The body collider's midpoint is approximately waist height.
        Vector3 dropPosition = transform.position;
        Collider bodyCollider = GetComponent<Collider>();
        if (bodyCollider != null)
            dropPosition.y = bodyCollider.bounds.center.y;

        GameObject pickupObject;
        if (weaponPickupPrefab != null)
        {
            pickupObject = Instantiate(weaponPickupPrefab, dropPosition, Quaternion.identity);
        }
        else
        {
            pickupObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            pickupObject.transform.position = dropPosition;
            pickupObject.transform.localScale = new Vector3(0.18f, 0.45f, 0.18f);
        }

        WeaponPickup pickup = pickupObject.GetComponent<WeaponPickup>();
        if (pickup == null)
            pickup = pickupObject.AddComponent<WeaponPickup>();

        pickup.Initialize(
            weapon.weaponType, weapon.level, weapon.quality, weapon.damage,
            weapon.fireInterval, weapon.projectileSpeed, weapon.projectilesPerShot,
            weapon.spreadAngle, weapon.explosionRadius, weaponPickupLifetime);
        pickup.SetModel();

        Rigidbody pickupBody = pickupObject.GetComponent<Rigidbody>();
        if (pickupBody == null)
            pickupBody = pickupObject.AddComponent<Rigidbody>();

        pickupBody.isKinematic = true;
        pickupBody.useGravity = false;
        pickupBody.detectCollisions = true;

        foreach (Collider pickupCollider in pickupObject.GetComponentsInChildren<Collider>(true))
        {
            if (pickupCollider.GetComponentInParent<WeaponPickup>() == pickup)
                pickupCollider.isTrigger = true;
        }
    }

    void TryDropArmorEquipment()
    {
        if (Random.value >= armorEquipmentDropChance) return;
        var item = ArmorLoot.Create((ArmorSlot)Random.Range(0, 4), GetRandomWeaponLevel(),
            Mathf.Clamp01(enemyStrength + Random.Range(-weaponStatVariation, weaponStatVariation)));
        Vector3 center = GetComponent<Collider>() != null ? GetComponent<Collider>().bounds.center : transform.position;
        ArmorLoot.Drop(item, center + Vector3.right * .65f, weaponPickupLifetime);
    }

    public WeaponItem CreateWeaponItem(WeaponType weaponType, int level, float quality)
    {
        return WeaponLoot.Create(weaponType, level, quality, minWeaponDamage, maxWeaponDamage,
            slowestFireInterval, fastestFireInterval, minProjectileSpeed, maxProjectileSpeed);
    }

    int GetRandomWeaponLevel()
    {
        switch (dungeonFloor)
        {
            case 2: return Random.Range(51, 81);
            case 3: return Random.Range(81, 101);
            default: return Random.Range(1, 51);
        }
    }

    GameObject GetWeaponModel(WeaponType type)
    {
#if UNITY_EDITOR
        ResolveEditorWeaponModels();
#endif
        switch (type)
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
    }
#endif

    void DropExperience()
    {
        if (experienceReward <= 0)
            return;

        Vector3 dropPosition = transform.position + Vector3.up * 0.75f;
        GameObject pickupObject;

        if (experiencePickupPrefab != null)
        {
            pickupObject = Instantiate(
                experiencePickupPrefab,
                dropPosition,
                Quaternion.identity
            );
        }
        else
        {
            pickupObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pickupObject.name = $"Experience Pickup ({experienceReward})";
            pickupObject.transform.position = dropPosition;
            pickupObject.transform.localScale = Vector3.one * 0.35f;

            Renderer pickupRenderer = pickupObject.GetComponent<Renderer>();
            if (pickupRenderer != null)
                pickupRenderer.material.color = new Color(0.15f, 0.85f, 1f, 1f);
        }

        ExperiencePickup pickup = pickupObject.GetComponent<ExperiencePickup>();
        if (pickup == null)
            pickup = pickupObject.AddComponent<ExperiencePickup>();

        pickup.Initialize(experienceReward, experiencePickupLifetime);

        Rigidbody pickupBody = pickupObject.GetComponent<Rigidbody>();
        if (pickupBody == null)
            pickupBody = pickupObject.AddComponent<Rigidbody>();

        pickupBody.mass = 0.1f;
        pickupBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        pickupBody.linearVelocity =
            Vector3.up * experienceDropForce +
            Random.insideUnitSphere * (experienceDropForce * 0.35f);
    }

    void TryDropHealthPickup()
    {
        if (healthRestoreAmount <= 0f || Random.value >= healthDropChance)
            return;

        Vector3 dropPosition = transform.position + Vector3.up * 0.75f;
        GameObject pickupObject;

        if (healthPickupPrefab != null)
        {
            pickupObject = Instantiate(
                healthPickupPrefab,
                dropPosition,
                Quaternion.identity
            );
        }
        else
        {
            pickupObject = new GameObject("Health Pickup");
            pickupObject.name = $"Health Pickup ({healthRestoreAmount:0})";
            pickupObject.transform.position = dropPosition;
            BoxCollider box = pickupObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.56f, 0.46f, 0.28f);
            box.center = new Vector3(0f, 0.04f, 0f);
        }

        HealthPickup pickup = pickupObject.GetComponent<HealthPickup>();
        if (pickup == null)
            pickup = pickupObject.AddComponent<HealthPickup>();

        pickup.Initialize(healthRestoreAmount, healthPickupLifetime);

        Rigidbody pickupBody = pickupObject.GetComponent<Rigidbody>();
        if (pickupBody == null)
            pickupBody = pickupObject.AddComponent<Rigidbody>();

        pickupBody.mass = 0.1f;
        pickupBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
        pickupBody.linearVelocity =
            Vector3.up * experienceDropForce +
            Random.insideUnitSphere * (experienceDropForce * 0.35f);
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        ResolveEditorWeaponModels();
#endif
        maxHealth = Mathf.Max(1f, maxHealth);
        experiencePickupLifetime = Mathf.Max(1f, experiencePickupLifetime);
        experienceDropForce = Mathf.Max(0f, experienceDropForce);
        healthDropChance = Mathf.Clamp01(healthDropChance);
        healthRestoreAmount = Mathf.Max(0f, healthRestoreAmount);
        healthPickupLifetime = Mathf.Max(1f, healthPickupLifetime);
        weakNpcWeaponDropChance = Mathf.Clamp01(weakNpcWeaponDropChance);
        strongNpcWeaponDropChance = Mathf.Clamp(
            strongNpcWeaponDropChance,
            0f,
            weakNpcWeaponDropChance
        );
        weaponPickupLifetime = Mathf.Max(1f, weaponPickupLifetime);
        minWeaponDamage = Mathf.Max(0f, minWeaponDamage);
        maxWeaponDamage = Mathf.Max(minWeaponDamage, maxWeaponDamage);
        slowestFireInterval = Mathf.Max(0.01f, slowestFireInterval);
        fastestFireInterval = Mathf.Clamp(
            fastestFireInterval,
            0.01f,
            slowestFireInterval
        );
        minProjectileSpeed = Mathf.Max(0.1f, minProjectileSpeed);
        maxProjectileSpeed = Mathf.Max(minProjectileSpeed, maxProjectileSpeed);
        weaponStatVariation = Mathf.Clamp(weaponStatVariation, 0f, 0.5f);
        progressionDropChance = Mathf.Clamp01(progressionDropChance);
        progressionPickupLifetime = Mathf.Max(1f, progressionPickupLifetime);
    }
}

class DamageNumberPopup : MonoBehaviour
{
    private const float Lifetime = 0.9f;
    private TextMesh numberText;
    private Color baseColor;
    private float elapsed;
    private Vector3 velocity;
    private Transform viewer;

    public static void Create(
        Vector3 position,
        float damage,
        Color color,
        Transform viewer)
    {
        GameObject popupObject = new GameObject("Damage Number");
        popupObject.transform.position = position;

        DamageNumberPopup popup = popupObject.AddComponent<DamageNumberPopup>();
        popup.numberText = popupObject.AddComponent<TextMesh>();
        popup.numberText.text = damage.ToString("0.#");
        popup.numberText.anchor = TextAnchor.MiddleCenter;
        popup.numberText.alignment = TextAlignment.Center;
        popup.numberText.fontSize = 64;
        popup.numberText.characterSize = 0.075f;
        popup.numberText.fontStyle = FontStyle.Bold;
        popup.numberText.color = color;
        popup.baseColor = color;
        popup.viewer = viewer;
        popup.velocity = new Vector3(
            Random.Range(-0.18f, 0.18f),
            1.15f,
            0f
        );

        MeshRenderer meshRenderer = popup.numberText.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.sortingOrder = 500;
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
        velocity *= Mathf.Pow(0.2f, Time.deltaTime);

        float progress = Mathf.Clamp01(elapsed / Lifetime);
        Color fadedColor = baseColor;
        fadedColor.a = 1f - progress;
        numberText.color = fadedColor;

        if (elapsed >= Lifetime)
            Destroy(gameObject);
    }

    void LateUpdate()
    {
        if (viewer == null)
            return;

        Vector3 direction = transform.position - viewer.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }
}

public class HealthPickup : MonoBehaviour, IMagneticPickup
{
    private float restoreAmount;
    private bool collected;

    public void Initialize(float amount, float lifetime)
    {
        restoreAmount = Mathf.Max(0f, amount);
        foreach (Renderer previous in GetComponentsInChildren<Renderer>(true))
            previous.enabled = false;
        GameObject visual = ItemAppearance.Create(ItemAppearance.HealthPack, transform);
        Vector3 scale = transform.lossyScale;
        visual.transform.localScale = new Vector3(
            1f / Mathf.Max(.001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(.001f, Mathf.Abs(scale.y)),
            1f / Mathf.Max(.001f, Mathf.Abs(scale.z)));
        PickupCollisionUtility.IgnoreCollisionsWithProjectiles(gameObject);
        Destroy(gameObject, Mathf.Max(1f, lifetime));
    }

    void OnCollisionEnter(Collision collision)
    {
        TryCollect(collision.collider);
    }

    void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    public bool CanCollect(WeaponInventory inventory) => !collected && inventory != null;

    public bool TryCollect(Collider collector)
    {
        if (collected || collector == null)
            return false;

        PlayerHealth player = collector.GetComponentInParent<PlayerHealth>();
        WeaponInventory inventory = collector.GetComponentInParent<WeaponInventory>();
        if (player == null || player.IsDead || inventory == null)
            return false;

        collected = true;
        inventory.AddHealthPack(restoreAmount);
        Destroy(gameObject);
        return true;
    }
}

public class ExperiencePickup : MonoBehaviour, IMagneticPickup
{
    private int experienceAmount;
    private bool collected;

    public void Initialize(int amount, float lifetime)
    {
        experienceAmount = Mathf.Max(0, amount);
        PickupCollisionUtility.IgnoreCollisionsWithProjectiles(gameObject);
        Destroy(gameObject, Mathf.Max(1f, lifetime));
    }

    void OnCollisionEnter(Collision collision)
    {
        TryCollect(collision.collider);
    }

    void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    public bool CanCollect(WeaponInventory inventory) => !collected;

    public bool TryCollect(Collider collector)
    {
        if (collected || collector == null)
            return false;

        PlayerHealth player = collector.GetComponentInParent<PlayerHealth>();
        if (player == null || player.IsDead)
            return false;

        collected = true;
        player.AddExperience(experienceAmount);
        GameAudio.UI(GameSound.Experience);
        Destroy(gameObject);
        return true;
    }
}

public class WeaponPickup : MonoBehaviour, IMagneticPickup
{
    private WeaponItem storedWeapon;
    private WeaponType weaponType;
    private int weaponLevel = 1;
    private float quality;
    private float damage;
    private float fireInterval;
    private float projectileSpeed;
    private int projectilesPerShot;
    private float spreadAngle;
    private float explosionRadius;
    private bool collected;
    private Transform modelVisual;
    private Vector3 hoverOrigin;
    private float hoverPhase;
    private const float HoverAmplitude = .05f;
    private bool magnetized;

    public void Initialize(WeaponItem item, float lifetime)
    {
        Initialize(item.weaponType, item.level, item.quality, item.damage, item.fireInterval,
            item.projectileSpeed, item.projectilesPerShot, item.spreadAngle, item.explosionRadius, lifetime);
        storedWeapon = JsonUtility.FromJson<WeaponItem>(JsonUtility.ToJson(item));
        gameObject.name = item.weaponName;
    }

    public void Initialize(
        WeaponType type,
        int level,
        float quality,
        float weaponDamage,
        float weaponFireInterval,
        float weaponProjectileSpeed,
        int weaponProjectilesPerShot,
        float weaponSpreadAngle,
        float weaponExplosionRadius,
        float lifetime)
    {
        weaponType = type;
        weaponLevel = Mathf.Clamp(level, 1, 100);
        this.quality = Mathf.Clamp01(quality);
        damage = Mathf.Max(0f, weaponDamage);
        fireInterval = Mathf.Max(0.01f, weaponFireInterval);
        projectileSpeed = Mathf.Max(0.1f, weaponProjectileSpeed);
        projectilesPerShot = Mathf.Max(1, weaponProjectilesPerShot);
        spreadAngle = Mathf.Max(0f, weaponSpreadAngle);
        explosionRadius = Mathf.Max(0f, weaponExplosionRadius);
        gameObject.name =
            $"{WeaponInventory.GetWeaponTypeName(type)} 等級 {weaponLevel}";
        hoverOrigin = transform.position;
        hoverPhase = Random.Range(0f, Mathf.PI * 2f);

        PickupCollisionUtility.IgnoreCollisionsWithProjectiles(gameObject);
        Destroy(gameObject, Mathf.Max(1f, lifetime));
    }

    public void SetModel()
    {
        foreach (Renderer previous in GetComponentsInChildren<Renderer>(true))
            previous.enabled = false;
        if (modelVisual != null) Destroy(modelVisual.parent.gameObject);

        // Cancel the old capsule/prefab scale before rotating the mesh to avoid stretching.
        Transform mount = new GameObject("Loot Visual Mount").transform;
        mount.SetParent(transform, false);
        Vector3 scale = transform.lossyScale;
        mount.localScale = new Vector3(1f / Mathf.Max(.001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(.001f, Mathf.Abs(scale.z)));
        modelVisual = new GameObject("Loot Rotation").transform;
        modelVisual.SetParent(mount, false);
        GameObject model = ItemAppearance.Create((int)weaponType, modelVisual);
        Bounds bounds = model.GetComponent<MeshFilter>().sharedMesh.bounds;
        float size = .9f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        model.transform.localScale = Vector3.one * size;
        model.transform.localPosition = -bounds.center * size;
        // Keep every weapon centered at the waist-height drop position.
        transform.position = hoverOrigin;
    }

    void Update()
    {
        if (!magnetized)
            transform.position = hoverOrigin +
                Vector3.up * (Mathf.Sin(Time.time * 2f + hoverPhase) * HoverAmplitude);

        if (modelVisual != null)
            modelVisual.Rotate(Vector3.up, 55f * Time.deltaTime, Space.World);
    }

    public void SetMagnetized(bool value)
    {
        if (magnetized && !value)
            hoverOrigin = transform.position -
                Vector3.up * (Mathf.Sin(Time.time * 2f + hoverPhase) * HoverAmplitude);
        magnetized = value;
    }

    void OnCollisionEnter(Collision collision)
    {
        TryCollect(collision.collider);
    }

    void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    public bool CanCollect(WeaponInventory inventory) =>
        !CoopSession.IsClient && !collected && inventory != null && inventory.HasItemSpace &&
        (!TryGetComponent(out DroppedEquipmentPickup dropped) || dropped.Allows(inventory));

    public bool TryCollect(Collider collector)
    {
        if (collected || collector == null)
            return false;

        WeaponInventory inventory = collector.GetComponentInParent<WeaponInventory>();
        PlayerHealth player = collector.GetComponentInParent<PlayerHealth>();
        if (!CanCollect(inventory) || player == null || player.IsDead)
            return false;

        WeaponItem weapon = storedWeapon ?? new WeaponItem
        {
            weaponName =
            $"{WeaponInventory.GetWeaponTypeName(weaponType)} 等級 {weaponLevel}",
            weaponType = weaponType,
            level = weaponLevel,
            quality = quality,
            damage = damage,
            fireInterval = fireInterval,
            projectileSpeed = projectileSpeed,
            projectilesPerShot = projectilesPerShot,
            spreadAngle = spreadAngle,
            explosionRadius = explosionRadius
        };

        if (inventory.AddWeapon(weapon))
        {
            GameAudio.UI(GameSound.Pickup);
            collected = true;
            Destroy(gameObject);
            return true;
        }
        return false;
    }
}

public static class PickupCollisionUtility
{
    public static bool IsPickup(Collider collider)
    {
        if (collider == null)
            return false;

        return collider.GetComponentInParent<HealthPickup>() != null ||
               collider.GetComponentInParent<ExperiencePickup>() != null ||
               collider.GetComponentInParent<WeaponPickup>() != null ||
               collider.GetComponentInParent<ArmorPickup>() != null;
    }

    public static void IgnoreCollisionsWithProjectiles(GameObject pickupObject)
    {
        if (pickupObject == null)
            return;

        Collider[] pickupColliders = pickupObject.GetComponentsInChildren<Collider>();
        Projectile[] projectiles = Object.FindObjectsByType<Projectile>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (Projectile projectile in projectiles)
        {
            if (projectile == null)
                continue;

            IgnoreColliderGroups(
                pickupColliders,
                projectile.GetComponentsInChildren<Collider>()
            );
        }
    }

    public static void IgnoreCollisionsWithPickups(GameObject projectileObject)
    {
        if (projectileObject == null)
            return;

        Collider[] projectileColliders = projectileObject.GetComponentsInChildren<Collider>();
        IgnorePickupType<HealthPickup>(projectileColliders);
        IgnorePickupType<ExperiencePickup>(projectileColliders);
        IgnorePickupType<WeaponPickup>(projectileColliders);
        IgnorePickupType<ArmorPickup>(projectileColliders);
    }

    static void IgnorePickupType<T>(Collider[] projectileColliders) where T : Component
    {
        T[] pickups = Object.FindObjectsByType<T>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (T pickup in pickups)
        {
            IgnoreColliderGroups(
                projectileColliders,
                pickup.GetComponentsInChildren<Collider>()
            );
        }
    }

    static void IgnoreColliderGroups(Collider[] first, Collider[] second)
    {
        foreach (Collider firstCollider in first)
        {
            if (firstCollider == null)
                continue;

            foreach (Collider secondCollider in second)
            {
                if (secondCollider != null)
                    Physics.IgnoreCollision(firstCollider, secondCollider, true);
            }
        }
    }
}
