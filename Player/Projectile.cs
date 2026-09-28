using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class Projectile : MonoBehaviour
{
    [Min(0.1f)] public float lifetime = 5f;

    private float damage;
    private GameObject owner;
    private bool hasHit;
    private float explosionRadius;
    private int remainingPenetrations;
    private Vector3 launchVelocity;
    private Vector3 launchPosition;
    private WeaponType weaponType;
    private WeaponBuildSystem weaponBuild;
    private readonly HashSet<EnemyHealth> damagedEnemies = new HashSet<EnemyHealth>();

    public void SetVisualModel(
        GameObject modelPrefab,
        float visualLength,
        Quaternion localRotation = default)
    {
        if (modelPrefab == null)
            return;

        Renderer rootRenderer = GetComponent<Renderer>();
        if (rootRenderer != null)
            rootRenderer.enabled = false;

        GameObject visual = Instantiate(modelPrefab, transform);
        visual.name = "Projectile Model";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = localRotation == default
            ? Quaternion.identity
            : localRotation;
        visual.transform.localScale = Vector3.one;

        foreach (Collider visualCollider in visual.GetComponentsInChildren<Collider>(true))
            visualCollider.enabled = false;
        foreach (Rigidbody visualBody in visual.GetComponentsInChildren<Rigidbody>(true))
        {
            visualBody.isKinematic = true;
            visualBody.detectCollisions = false;
        }

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            bounds.Encapsulate(renderers[rendererIndex].bounds);

        float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension > Mathf.Epsilon)
            visual.transform.localScale = Vector3.one * (visualLength / largestDimension);

        bounds = renderers[0].bounds;
        for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            bounds.Encapsulate(renderers[rendererIndex].bounds);
        visual.transform.position += transform.position - bounds.center;
    }

    public void Launch(
        Vector3 direction,
        float speed,
        float damageAmount,
        GameObject projectileOwner,
        float blastRadius = 0f,
        int penetrationCount = 0,
        WeaponType shotWeaponType = WeaponType.Rifle,
        WeaponBuildSystem buildSystem = null)
    {
        damage = damageAmount;
        owner = projectileOwner;
        explosionRadius = Mathf.Max(0f, blastRadius);
        remainingPenetrations = Mathf.Max(0, penetrationCount);
        weaponType = shotWeaponType;
        weaponBuild = buildSystem;
        launchPosition = transform.position;

        Rigidbody body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        launchVelocity = direction.normalized * speed;
        body.linearVelocity = launchVelocity;

        IgnoreOwnerCollisions();
        IgnoreOtherProjectileCollisions();
        PickupCollisionUtility.IgnoreCollisionsWithPickups(gameObject);
        Destroy(gameObject, lifetime);
    }

    void OnCollisionEnter(Collision collision)
    {
        ProcessHit(collision.collider);
    }

    void OnTriggerEnter(Collider other)
    {
        ProcessHit(other);
    }

    void ProcessHit(Collider hitCollider)
    {
        if (hasHit || hitCollider == null)
            return;

        if (owner != null && hitCollider.transform.IsChildOf(owner.transform))
            return;

        if (PickupCollisionUtility.IsPickup(hitCollider))
            return;

        EnemyHealth enemyHealth = hitCollider.GetComponentInParent<EnemyHealth>();
        if (enemyHealth != null)
        {
            PlayerHealth player = owner != null
                ? owner.GetComponentInParent<PlayerHealth>()
                : null;

            if (explosionRadius > 0f)
                Explode(hitCollider.transform.position, player);
            else if (damagedEnemies.Add(enemyHealth))
            {
                bool headshot =
                    hitCollider.name.IndexOf("head", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    hitCollider.bounds.center.y >= enemyHealth.transform.position.y + 1.2f;
                float distance = Vector3.Distance(launchPosition, hitCollider.transform.position);
                float dealtDamage = weaponBuild != null
                    ? weaponBuild.ModifyDamage(damage, enemyHealth, weaponType, distance, false, headshot)
                    : damage;
                enemyHealth.TakeDamage(dealtDamage, player);
                if (weaponBuild != null)
                    weaponBuild.OnHit(enemyHealth, weaponType, distance, false, headshot, enemyHealth.IsDead);

                if (remainingPenetrations > 0)
                {
                    remainingPenetrations--;
                    IgnoreEnemyCollisions(enemyHealth);

                    Rigidbody body = GetComponent<Rigidbody>();
                    body.linearVelocity = launchVelocity;
                    return;
                }
            }
        }
        else if (explosionRadius > 0f)
        {
            Explode(transform.position, owner != null
                ? owner.GetComponentInParent<PlayerHealth>()
                : null);
        }
        else GameAudio.At(GameSound.Impact, transform.position);

        hasHit = true;
        Destroy(gameObject);
    }

    void IgnoreEnemyCollisions(EnemyHealth enemy)
    {
        Collider[] projectileColliders = GetComponentsInChildren<Collider>();
        Collider[] enemyColliders = enemy.GetComponentsInChildren<Collider>();

        foreach (Collider projectileCollider in projectileColliders)
        {
            foreach (Collider enemyCollider in enemyColliders)
            {
                if (projectileCollider != null && enemyCollider != null)
                    Physics.IgnoreCollision(projectileCollider, enemyCollider, true);
            }
        }
    }

    void Explode(Vector3 center, PlayerHealth damageSource)
    {
        GameAudio.At(GameSound.Explosion, center);
        Collider[] hits = Physics.OverlapSphere(
            center,
            explosionRadius,
            ~0,
            QueryTriggerInteraction.Ignore
        );
        HashSet<EnemyHealth> damagedEnemies = new HashSet<EnemyHealth>();

        foreach (Collider hit in hits)
        {
            EnemyHealth enemy = hit.GetComponentInParent<EnemyHealth>();
            if (enemy != null && damagedEnemies.Add(enemy))
            {
                float distance = Vector3.Distance(launchPosition, enemy.transform.position);
                float dealtDamage = weaponBuild != null
                    ? weaponBuild.ModifyDamage(damage, enemy, weaponType, distance, true, false)
                    : damage;
                enemy.TakeDamage(dealtDamage, damageSource);
                if (weaponBuild != null)
                    weaponBuild.OnHit(enemy, weaponType, distance, true, false, enemy.IsDead);
            }
        }
    }

    void IgnoreOwnerCollisions()
    {
        if (owner == null)
            return;

        Collider projectileCollider = GetComponent<Collider>();
        Collider[] ownerColliders = owner.GetComponentsInChildren<Collider>();

        foreach (Collider ownerCollider in ownerColliders)
            Physics.IgnoreCollision(projectileCollider, ownerCollider);
    }

    void IgnoreOtherProjectileCollisions()
    {
        Collider[] ownColliders = GetComponentsInChildren<Collider>();
        Projectile[] otherProjectiles = FindObjectsByType<Projectile>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (Projectile otherProjectile in otherProjectiles)
        {
            if (otherProjectile == null || otherProjectile == this)
                continue;

            Collider[] otherColliders =
                otherProjectile.GetComponentsInChildren<Collider>();

            foreach (Collider ownCollider in ownColliders)
            {
                if (ownCollider == null)
                    continue;

                foreach (Collider otherCollider in otherColliders)
                {
                    if (otherCollider != null)
                        Physics.IgnoreCollision(ownCollider, otherCollider, true);
                }
            }
        }
    }

    void OnValidate()
    {
        lifetime = Mathf.Max(0.1f, lifetime);
    }
}
