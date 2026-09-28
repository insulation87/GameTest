using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
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
    [Range(0f, 1f)] public float weakNpcWeaponDropChance = 0.35f;
    [Tooltip("Drop chance for the strongest NPC.")]
    [Range(0f, 1f)] public float strongNpcWeaponDropChance = 0.08f;
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

    [Header("Floor Progression Drop")]
    [Range(0f, 1f)] public float progressionDropChance = 0.01f;
    [Min(1f)] public float progressionPickupLifetime = 120f;

    [Header("Grenade Drop")]
    [Range(0f, 1f)] public float grenadeDropChance = 0.1f;
    [Min(1f)] public float grenadePickupLifetime = 60f;

    private float currentHealth;
    private int enemyLevel = 1;
    private int experienceReward = 10;
    private float enemyStrength;
    private int dungeonFloor = 1;

    public float CurrentHealth => currentHealth;
    public float HealthNormalized => currentHealth / maxHealth;
    public bool IsDead { get; private set; }
    public int EnemyLevel => enemyLevel;
    public int ExperienceReward => experienceReward;
    public int DungeonFloor => dungeonFloor;

    void OnEnable()
    {
        currentHealth = maxHealth;
        IsDead = false;
    }

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

    public void TakeDamage(float damage, PlayerHealth damageSource)
    {
        if (damage <= 0f || IsDead)
            return;

        BossController boss = GetComponent<BossController>();
        if (boss != null && boss.IsTransforming) return;

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
            if (boss != null && boss.TryAdvancePhase()) return;
            Die(damageSource);
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
        DropExperience();
        TryDropHealthPickup();
        TryDropWeapon();
        TryDropProgressionItem();
        TryDropGrenade();
        Destroy(gameObject);
    }

    void TryDropGrenade()
    {
        if (Random.value < grenadeDropChance)
            GrenadePickup.Create(transform.position + Vector3.up * 0.75f, grenadePickupLifetime);
    }

    void TryDropProgressionItem()
    {
        if (Random.value >= progressionDropChance)
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
        WeaponType weaponType = (WeaponType)Random.Range(0, 7);
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

    public WeaponItem CreateWeaponItem(WeaponType weaponType, int level, float quality)
    {
        quality = Mathf.Clamp01(quality);
        float damage = Mathf.Lerp(minWeaponDamage, maxWeaponDamage, quality);
        float fireInterval = Mathf.Lerp(slowestFireInterval, fastestFireInterval, quality);
        float projectileSpeed = Mathf.Lerp(minProjectileSpeed, maxProjectileSpeed, quality);
        int projectilesPerShot = 1;
        float spreadAngle = 0f;
        float explosionRadius = 0f;
        ApplyWeaponTypeStats(
            weaponType,
            ref damage,
            ref fireInterval,
            ref projectileSpeed,
            ref projectilesPerShot,
            ref spreadAngle,
            ref explosionRadius
        );
        return new WeaponItem
        {
            weaponName = $"{WeaponInventory.GetWeaponTypeName(weaponType)} 等級 {Mathf.Clamp(level, 1, 100)}",
            weaponType = weaponType,
            level = Mathf.Clamp(level, 1, 100),
            quality = quality,
            damage = damage,
            fireInterval = fireInterval,
            projectileSpeed = projectileSpeed,
            projectilesPerShot = projectilesPerShot,
            spreadAngle = spreadAngle,
            explosionRadius = explosionRadius
        };
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

    static void ApplyWeaponTypeStats(
        WeaponType type,
        ref float damage,
        ref float fireInterval,
        ref float projectileSpeed,
        ref int projectilesPerShot,
        ref float spreadAngle,
        ref float explosionRadius)
    {
        switch (type)
        {
            case WeaponType.SubmachineGun:
                damage *= 0.65f;
                fireInterval *= 0.55f;
                spreadAngle = 1.5f;
                break;
            case WeaponType.Shotgun:
                damage *= 0.45f;
                fireInterval *= 2.5f;
                projectileSpeed *= 0.8f;
                projectilesPerShot = 8;
                spreadAngle = 8f;
                break;
            case WeaponType.RPG:
                damage *= 2.5f;
                fireInterval *= 4f;
                projectileSpeed *= 0.45f;
                explosionRadius = 5f;
                break;
            case WeaponType.Pistol:
                damage *= 0.8f;
                fireInterval *= 1.4f;
                break;
            case WeaponType.SniperRifle:
                damage *= 2.2f;
                fireInterval *= 3f;
                projectileSpeed *= 1.6f;
                spreadAngle = 0f;
                break;
            case WeaponType.LightMachineGun:
                damage *= 0.75f;
                fireInterval *= 0.7f;
                projectileSpeed *= 0.95f;
                spreadAngle = 2.5f;
                break;
        }

        fireInterval = Mathf.Max(0.01f, fireInterval);
        projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
    }

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
        grenadeDropChance = Mathf.Clamp01(grenadeDropChance);
        grenadePickupLifetime = Mathf.Max(1f, grenadePickupLifetime);
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
        !collected && inventory != null && inventory.Weapons.Count < inventory.capacity;

    public bool TryCollect(Collider collector)
    {
        if (collected || collector == null)
            return false;

        WeaponInventory inventory = collector.GetComponentInParent<WeaponInventory>();
        PlayerHealth player = collector.GetComponentInParent<PlayerHealth>();
        if (!CanCollect(inventory) || player == null || player.IsDead)
            return false;

        WeaponItem weapon = new WeaponItem
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
               collider.GetComponentInParent<WeaponPickup>() != null;
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
