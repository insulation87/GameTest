using System;
using System.Collections.Generic;
using UnityEngine;

public interface IMagneticPickup
{
    bool CanCollect(WeaponInventory inventory);
    bool TryCollect(Collider collector);
}

// Attached automatically by PlayerShooter. Uses the same collection path as physical contact.
[DisallowMultipleComponent]
public class PickupMagnet : MonoBehaviour
{
    [Min(.5f)] public float attractionRadius = 3f;
    [Min(.1f)] public float initialSpeed = 3f;
    [Min(.1f)] public float maximumSpeed = 14f;
    [Min(.1f)] public float acceleration = 24f;
    [Min(.05f)] public float collectDistance = .35f;
    [Tooltip("Solid geometry in these layers blocks attraction.")]
    public LayerMask obstructionLayers = ~0;

    PlayerHealth health;
    WeaponInventory inventory;
    Collider collector;
    Collider[] nearby = new Collider[32];
    readonly RaycastHit[] obstacles = new RaycastHit[32];
    readonly List<Attraction> attracted = new List<Attraction>();
    readonly HashSet<IMagneticPickup> tracked = new HashSet<IMagneticPickup>();
    float nextScan;

    sealed class Attraction
    {
        public IMagneticPickup pickup;
        public MonoBehaviour component;
        public Rigidbody body;
        public WeaponPickup weapon;
        public bool wasKinematic;
        public bool usedGravity;
        public float speed;
    }

    void FixedUpdate()
    {
        if (health == null) health = GetComponent<PlayerHealth>();
        if (inventory == null) inventory = GetComponent<WeaponInventory>();
        if (collector == null) collector = GetComponent<Collider>();
        if (health == null || health.IsDead || inventory == null ||
            collector == null || !collector.enabled)
        {
            ReleaseAll();
            return;
        }

        Vector3 destination = collector.bounds.center;
        if (Time.fixedTime >= nextScan)
        {
            nextScan = Time.fixedTime + .1f;
            Scan(destination);
        }

        for (int i = attracted.Count - 1; i >= 0; i--)
        {
            Attraction entry = attracted[i];
            if (entry.component == null || !entry.component.isActiveAndEnabled ||
                !entry.pickup.CanCollect(inventory))
            {
                Release(i);
                continue;
            }

            Vector3 position = entry.body != null ? entry.body.position : entry.component.transform.position;
            float distance = Vector3.Distance(position, destination);
            if (distance > attractionRadius + .5f || !HasClearPath(position, destination))
            {
                Release(i);
                continue;
            }

            if (distance <= collectDistance)
            {
                entry.pickup.TryCollect(collector);
                Release(i);
                continue;
            }

            entry.speed = Mathf.MoveTowards(entry.speed, maximumSpeed, acceleration * Time.fixedDeltaTime);
            Vector3 next = Vector3.MoveTowards(position, destination, entry.speed * Time.fixedDeltaTime);
            if (entry.body != null) entry.body.MovePosition(next);
            else entry.component.transform.position = next;
        }
    }

    void Scan(Vector3 destination)
    {
        int count;
        // Grow only when crowded, so a pile of colliders cannot hide other pickups.
        while ((count = Physics.OverlapSphereNonAlloc(destination, attractionRadius, nearby,
            ~0, QueryTriggerInteraction.Collide)) == nearby.Length)
            Array.Resize(ref nearby, nearby.Length * 2);

        for (int i = 0; i < count; i++)
        {
            IMagneticPickup pickup = nearby[i].GetComponentInParent<IMagneticPickup>();
            MonoBehaviour component = pickup as MonoBehaviour;
            if (component == null || !component.isActiveAndEnabled || tracked.Contains(pickup) ||
                !pickup.CanCollect(inventory) || !HasClearPath(component.transform.position, destination))
                continue;

            var entry = new Attraction
            {
                pickup = pickup,
                component = component,
                body = component.GetComponent<Rigidbody>(),
                weapon = component as WeaponPickup,
                speed = initialSpeed
            };
            if (entry.body != null)
            {
                entry.wasKinematic = entry.body.isKinematic;
                entry.usedGravity = entry.body.useGravity;
                if (!entry.wasKinematic)
                {
                    entry.body.linearVelocity = Vector3.zero;
                    entry.body.angularVelocity = Vector3.zero;
                }
                entry.body.useGravity = false;
                entry.body.isKinematic = true;
            }
            if (entry.weapon != null) entry.weapon.SetMagnetized(true);
            tracked.Add(pickup);
            attracted.Add(entry);
        }
        Array.Clear(nearby, 0, count);
    }

    bool HasClearPath(Vector3 position, Vector3 destination)
    {
        Vector3 direction = destination - position;
        float distance = direction.magnitude;
        if (distance <= .001f) return true;
        int count = Physics.RaycastNonAlloc(position, direction / distance, obstacles, distance,
            obstructionLayers, QueryTriggerInteraction.Ignore);
        if (count == obstacles.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = obstacles[i].collider;
            if (obstacle.transform.IsChildOf(transform) ||
                obstacle.GetComponentInParent<IMagneticPickup>() != null)
                continue;
            return false;
        }
        return true;
    }

    void Release(int index)
    {
        Attraction entry = attracted[index];
        if (entry.body != null)
        {
            entry.body.isKinematic = entry.wasKinematic;
            entry.body.useGravity = entry.usedGravity;
        }
        if (entry.weapon != null) entry.weapon.SetMagnetized(false);
        tracked.Remove(entry.pickup);
        attracted.RemoveAt(index);
    }

    void ReleaseAll()
    {
        for (int i = attracted.Count - 1; i >= 0; i--) Release(i);
    }

    void OnDisable() => ReleaseAll();

    void OnValidate()
    {
        attractionRadius = Mathf.Max(.5f, attractionRadius);
        initialSpeed = Mathf.Max(.1f, initialSpeed);
        maximumSpeed = Mathf.Max(initialSpeed, maximumSpeed);
        acceleration = Mathf.Max(.1f, acceleration);
        collectDistance = Mathf.Clamp(collectDistance, .05f, attractionRadius);
    }
}
