using System;
using UnityEngine;

// Swept collision prevents bolts passing through thin walls or players.
public class EnemyProjectile : MonoBehaviour
{
    private GameObject owner;
    private Vector3 velocity;
    private float damage;
    private bool consumed;
    public static void Launch(Vector3 origin, Vector3 direction, float speed, float damage, float lifetime, GameObject owner)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        GameObject bolt = PixelCharacterFactory.CreateBolt(null);
        bolt.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        EnemyProjectile projectile = bolt.AddComponent<EnemyProjectile>();
        projectile.owner = owner; projectile.velocity = direction.normalized * speed; projectile.damage = damage;
        Destroy(bolt, Mathf.Max(0.1f, lifetime));
        foreach (Collider collider in Physics.OverlapSphere(origin, 0.035f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (projectile.Hit(collider)) break;
    }
    void Update()
    {
        Advance(Time.deltaTime);
    }
    void Advance(float seconds)
    {
        if (consumed || seconds <= 0) return;
        Vector3 step = velocity * seconds;
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, 0.035f, step.normalized, step.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
        foreach (RaycastHit hit in hits)
            if (Hit(hit.collider)) { transform.position = hit.point; return; }
        transform.position += step;
    }
    bool Hit(Collider collider)
    {
        if (consumed || collider == null || (owner != null && collider.transform.IsChildOf(owner.transform))) return false;
        PlayerHealth victim = collider.GetComponentInParent<PlayerHealth>();
        if (victim == null) GameAudio.At(GameSound.Impact, collider.ClosestPoint(transform.position));
        if (victim != null && !victim.IsDead) victim.TakeDamage(damage);
        consumed = true; Destroy(gameObject); return true;
    }
}
