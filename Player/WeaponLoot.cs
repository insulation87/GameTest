using UnityEngine;

// Shared enemy and treasure weapon balance; no EnemyHealth component on breakable props.
public static class WeaponLoot
{
    public static WeaponType RollType()
    {
        return CharacterWeapons.Types[Random.Range(0, CharacterWeapons.Types.Length)];
    }

    public static WeaponPickup Drop(WeaponItem weapon, Vector3 position, float lifetime = 180f)
    {
        var node = new GameObject("Treasure Weapon", typeof(SphereCollider), typeof(Rigidbody));
        node.transform.position = position;
        var collider = node.GetComponent<SphereCollider>();
        collider.radius = .4f;
        collider.isTrigger = true;
        var body = node.GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        var pickup = node.AddComponent<WeaponPickup>();
        pickup.Initialize(weapon, lifetime);
        pickup.SetModel();
        return pickup;
    }

    public static WeaponItem RollChest(int floor, int equippedLevel)
    {
        // Above ordinary floor drops where possible; level 100 remains the cap.
        int floorMinimum = floor >= 3 ? 95 : floor == 2 ? 81 : 51;
        int floorMaximum = floor >= 2 ? 100 : 70;
        int minimum = Mathf.Clamp(Mathf.Max(floorMinimum, equippedLevel + 5), 1, 100);
        int maximum = Mathf.Clamp(Mathf.Max(floorMaximum, minimum + 10), minimum, 100);
        return Create(RollType(), Random.Range(minimum, maximum + 1), Random.Range(.75f, 1f));
    }

    public static WeaponItem Create(WeaponType weaponType, int level, float quality,
        float minWeaponDamage = 15f, float maxWeaponDamage = 60f,
        float slowestFireInterval = .35f, float fastestFireInterval = .08f,
        float minProjectileSpeed = 45f, float maxProjectileSpeed = 90f)
    {
        return CharacterWeapons.Create(weaponType, level, quality);
    }
}
