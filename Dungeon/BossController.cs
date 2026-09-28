using System.Collections;
using UnityEngine;

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
    private bool enraged;
    private Coroutine attackRoutine;
    public event System.Action BurstFired;

    public void Initialize(Player targetPlayer, EnemyHealth bossHealth, EnemyAI bossAI)
    {
        player = targetPlayer;
        health = bossHealth;
        enemyAI = bossAI;
    }

    void Awake()
    {
        health = GetComponent<EnemyHealth>();
        enemyAI = GetComponent<EnemyAI>();
    }

    void Update()
    {
        if (enraged || health == null || health.IsDead || health.HealthNormalized > 0.5f)
            return;

        enraged = true;
        // Enrage changes the attack pattern, not the level-derived damage.
        attackRoutine = StartCoroutine(RadialAttackLoop());
    }

    IEnumerator RadialAttackLoop()
    {
        while (health != null && !health.IsDead)
        {
            yield return new WaitForSeconds(Random.Range(minAttackInterval, maxAttackInterval));
            if (health != null && !health.IsDead)
                FireRadialBurst();
        }
    }

    void FireRadialBurst()
    {
        BurstFired?.Invoke();
        Vector3 origin = enemyAI != null ? enemyAI.AttackOrigin : transform.position + Vector3.up * 0.4f;
        for (int bulletIndex = 0; bulletIndex < bulletsPerBurst; bulletIndex++)
        {
            float angle = bulletIndex * (360f / bulletsPerBurst);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            float levelDamage = EnemyCombatRules.DamageAtLevel(health != null ? health.EnemyLevel : 100);
            EnemyProjectile.Launch(origin, direction, bulletSpeed, levelDamage, bulletLifetime, gameObject);
        }
    }

    void OnDisable()
    {
        if (attackRoutine != null)
            StopCoroutine(attackRoutine);
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
