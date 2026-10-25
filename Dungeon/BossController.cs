using UnityEngine;
using UnityEngine.AI;

public class BossController : MonoBehaviour
{
    [Header("Enraged Mode")]
    [HideInInspector] public float enragedContactDamage = 100f; // Legacy only; never overrides level damage.
    [HideInInspector] public float bulletDamage = 10f;
    [Min(1)] public int bulletsPerBurst = 20;
    [Min(0.1f)] public float bulletSpeed = 12f;
    [Min(0.1f)] public float bulletLifetime = 8f;
    [Min(0.1f)] public float minAttackInterval = 10f;
    [Min(0.1f)] public float maxAttackInterval = 20f;

    private Player player;
    private EnemyHealth health;
    private EnemyAI enemyAI;
    public const int PhaseCount = 3;
    [Min(0.1f)] public float transformationDuration = 1.8f;
    public int Phase { get; private set; } = 1;
    public bool IsEnraged => Phase >= 2;
    public bool IsTransforming => Time.time < transformationEndsAt;
    public string FormName => Phase == 1 ? "黑鐵暴君" : Phase == 2 ? "赤焰狂戰" : "深淵魔王";
    public Color FormColor => Phase == 1 ? new Color(.95f, .65f, .2f) :
        Phase == 2 ? new Color(1f, .2f, .06f) : new Color(.65f, .3f, 1f);
    private float transformationEndsAt;
    private float nextBurstTime;
    private float burstRotation;
    private NavMeshAgent agent;
    private float baseSpeed, baseMeleeInterval, baseRangedInterval;
    private bool initialized;
    public event System.Action BurstFired;
    public event System.Action PhaseChanged;

    public void Initialize(Player targetPlayer, EnemyHealth bossHealth, EnemyAI bossAI)
    {
        player = targetPlayer;
        health = bossHealth;
        enemyAI = bossAI;
        if (!initialized)
        {
            agent = GetComponent<NavMeshAgent>();
            baseSpeed = agent != null ? agent.speed : 0f;
            baseMeleeInterval = enemyAI != null ? enemyAI.damageInterval : 1f;
            baseRangedInterval = enemyAI != null ? enemyAI.rangedInterval : 1.8f;
            initialized = true;
        }
        ApplyCombatForm();
        if (GetComponent<TacticalBossMechanics>() == null) gameObject.AddComponent<TacticalBossMechanics>();
    }

    void Awake()
    {
        health = GetComponent<EnemyHealth>();
        enemyAI = GetComponent<EnemyAI>();
    }

    void Update()
    {
        if (CoopSession.IsClient) return;
        if (CoopSession.IsMultiplayer)
        {
            Transform closest = CoopSession.ClosestLiving(transform.position, null, enemyAI != null ? enemyAI.HomeRoom : null);
            if (closest != null) player = closest.GetComponent<Player>();
        }
        if (!initialized || health == null || health.IsDead) return;
        var tactics = GetComponent<TacticalBossMechanics>();
        if (enemyAI != null) enemyAI.SetCombatPaused(IsTransforming || tactics != null && tactics.ControlsAI);
        if (IsTransforming || tactics != null && tactics.ControlsAI || (enemyAI != null && enemyAI.IsFrozen) || Time.timeScale <= 0f || Phase == 1 || Time.time < nextBurstTime) return;
        if (player == null) player = FindFirstObjectByType<Player>();
        PlayerHealth victim = player != null ? player.GetComponent<PlayerHealth>() : null;
        if (victim == null || victim.IsDead) return;
        FireRadialBurst();
        float intervalScale = Phase == 2 ? .5f : .25f;
        nextBurstTime = Time.time + Random.Range(Mathf.Max(.1f, minAttackInterval),
            Mathf.Max(minAttackInterval, maxAttackInterval)) * intervalScale;
    }

    // Called synchronously by EnemyHealth before death, so even a lethal hit cannot skip a form.
    public bool TryAdvancePhase()
    {
        if (!initialized || health == null || health.IsDead || health.CurrentHealth > 0f ||
            IsTransforming || Phase >= PhaseCount) return false;
        Phase++;
        GameAudio.At(GameSound.BossTransform, transform.position);
        health.SetMaxHealth(health.maxHealth);
        transformationEndsAt = Time.time + Mathf.Max(.1f, transformationDuration);
        nextBurstTime = transformationEndsAt + 1f;
        ApplyCombatForm();
        if (enemyAI != null) enemyAI.SetCombatPaused(true);
        PhaseChanged?.Invoke();
        return true;
    }

    public void RestoreProgress(int phase)
    {
        Phase = Mathf.Clamp(phase, 1, PhaseCount);
        transformationEndsAt = Time.time + transformationDuration;
        nextBurstTime = transformationEndsAt + 1f;
        ApplyCombatForm();
        PhaseChanged?.Invoke();
    }

    void ApplyCombatForm()
    {
        if (agent != null) agent.speed = baseSpeed * (Phase == 1 ? 1f : Phase == 2 ? 1.1f : 1.2f);
        float intervalScale = Phase == 1 ? 1f : Phase == 2 ? .8f : .6f;
        if (enemyAI != null)
        {
            enemyAI.damageInterval = baseMeleeInterval * intervalScale;
            enemyAI.rangedInterval = baseRangedInterval * intervalScale;
        }
    }

    void FireRadialBurst()
    {
        GameAudio.At(GameSound.BossBurst, transform.position);
        BurstFired?.Invoke();
        Vector3 origin = enemyAI != null ? enemyAI.AttackOrigin : transform.position + Vector3.up * 0.4f;
        int count = Mathf.Max(1, bulletsPerBurst) + (Phase == 3 ? 12 : 0);
        float levelDamage = EnemyCombatRules.DamageAtLevel(health != null ? health.EnemyLevel : 100) * ExpeditionMaps.Current.Damage;
        for (int bulletIndex = 0; bulletIndex < count; bulletIndex++)
        {
            float angle = burstRotation + bulletIndex * (360f / count);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            EnemyProjectile.Launch(origin, direction, bulletSpeed, levelDamage, bulletLifetime, gameObject);
        }
        if (Phase == 3)
        {
            burstRotation += 360f / count * .5f;
            if (player != null)
            {
                Vector3 aim = player.transform.position - origin;
                for (int i = -1; i <= 1; i++)
                    EnemyProjectile.Launch(origin, Quaternion.Euler(0f, i * 12f, 0f) * aim,
                        bulletSpeed * 1.2f, levelDamage, bulletLifetime, gameObject);
            }
        }
    }

    void OnDisable()
    {
        if (enemyAI != null) enemyAI.SetCombatPaused(false);
    }
}

public class BossBullet : MonoBehaviour
{
    private float damage;
    private GameObject owner;

    public static void Create(Vector3 position, Vector3 direction, float speed, float damage, float lifetime, GameObject owner)
    {
        GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bullet.name = "Boss Bullet";
        bullet.transform.position = position;
        bullet.transform.localScale = Vector3.one * 0.3f;

        Renderer renderer = bullet.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material.color = new Color(1f, 0.1f, 0.05f, 1f);

        Collider collider = bullet.GetComponent<Collider>();
        collider.isTrigger = true;

        Rigidbody body = bullet.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = direction.normalized * speed;

        BossBullet projectile = bullet.AddComponent<BossBullet>();
        projectile.damage = damage;
        projectile.owner = owner;
        Destroy(bullet, lifetime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (owner != null && other.transform.IsChildOf(owner.transform))
            return;

        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        if (!other.isTrigger)
            Destroy(gameObject);
    }
}
