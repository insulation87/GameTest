using System;
using UnityEngine;
using UnityEngine.UI;

// Stable identity lets the host reject replayed clicks even after inventory indices shift.
public static class EquipmentIdentity
{
    public static void Ensure(WeaponItem item) { if (item != null && string.IsNullOrEmpty(item.itemId)) item.itemId = Guid.NewGuid().ToString("N"); }
    public static void Ensure(ArmorItem item) { if (item != null && string.IsNullOrEmpty(item.itemId)) item.itemId = Guid.NewGuid().ToString("N"); }
}

/// <summary>Teammates can collect immediately; the owner must leave pickup range before reclaiming.</summary>
public sealed class DroppedEquipmentPickup : MonoBehaviour
{
    WeaponInventory owner;
    float releaseDistance;
    bool released;

    public void Initialize(WeaponInventory dropper)
    {
        owner = dropper;
        releaseDistance = Mathf.Max(3f, dropper.GetComponent<PickupMagnet>()?.attractionRadius ?? 3f) + .75f;
    }

    void Update()
    {
        if (released || owner == null) return;
        Vector3 position = owner.TryGetComponent(out Collider body) ? body.bounds.center : owner.transform.position;
        if (Vector3.Distance(position, transform.position) > releaseDistance) released = true;
    }

    public bool Allows(WeaponInventory inventory) => released || owner == null || inventory != owner;
}

public partial class WeaponInventory
{
    Button dropWeaponButton, dropArmorButton;
    public WeaponItem ActiveWeapon => equippedWeapon;

    bool CanDropEquipment
    {
        get
        {
            var health = GetComponent<PlayerHealth>();
            return health != null && !health.IsDead && !health.IsChoosingUpgrade && !IsPaused &&
                Time.timeScale > 0 && !GameSceneFlow.IsLoading && !DungeonVictoryUI.IsOpen;
        }
    }

    public bool DropWeapon(WeaponItem item)
    {
        if (item == null || !weapons.Contains(item) || !CanDropEquipment) return DenyDrop();
        EquipmentIdentity.Ensure(item);
        if (CoopSession.Command(this, "dropWeapon", 0, 0, item.itemId)) return true;
        if (CoopSession.IsClient || !TryEquipmentDropPosition(out Vector3 position)) return DenyDrop();
        var pickup = WeaponLoot.Drop(item, position);
        pickup.gameObject.AddComponent<DroppedEquipmentPickup>().Initialize(this);
        weapons.Remove(item);
        for (int i = 0; i < equippedSlots.Length; i++) if (equippedSlots[i] == item) equippedSlots[i] = null;
        if (equippedWeapon == item)
        {
            equippedWeapon = null;
            for (int i = 0; i < equippedSlots.Length; i++)
                if (equippedSlots[i] != null) { SwitchToSlot(i); break; }
            if (equippedWeapon == null && weapons.Count > 0) EquipToSlot(weapons[0], activeWeaponSlot);
            if (equippedWeapon == null) shooter?.ClearWeapon();
        }
        selectedWeapon = null;
        RefreshAfterDrop();
        return true;
    }

    public bool DropArmor(ArmorItem item)
    {
        if (item == null || !armors.Contains(item) || !CanDropEquipment) return DenyDrop();
        EquipmentIdentity.Ensure(item);
        if (CoopSession.Command(this, "dropArmor", 0, 0, item.itemId)) return true;
        if (CoopSession.IsClient || !TryEquipmentDropPosition(out Vector3 position)) return DenyDrop();
        var pickup = ArmorLoot.Drop(item, position);
        pickup.gameObject.AddComponent<DroppedEquipmentPickup>().Initialize(this);
        for (int i = 0; i < armorSlots.Length; i++)
            if (armorSlots[i] == item)
            {
                armorSlots[i] = null;
                GetComponent<PlayerHealth>().ApplyEquipmentArmorBonus(-item.armorBonus, false);
            }
        armors.Remove(item);
        selectedArmor = null;
        RefreshAfterDrop();
        return true;
    }

    bool DenyDrop() { GameAudio.UI(GameSound.Denied); return false; }
    void RefreshAfterDrop()
    {
        GameAudio.UI(GameSound.Equip);
        RefreshWeaponList(); RefreshDetails(); RefreshHotbar(); RefreshArmorSlots();
        GetComponent<WeaponBuildSystem>()?.RefreshBuild();
    }

    void RefreshDropButtons()
    {
        if (dropWeaponButton != null) dropWeaponButton.interactable = selectedWeapon != null && CanDropEquipment;
        if (dropArmorButton != null) dropArmorButton.interactable = selectedArmor != null && CanDropEquipment;
    }

    bool IgnoreDropObstacle(Collider collider) => collider.transform.IsChildOf(transform) ||
        collider.GetComponentInParent<Player>() != null || PickupCollisionUtility.IsPickup(collider);

    bool TryEquipmentDropPosition(out Vector3 position)
    {
        Vector3 origin = TryGetComponent(out Collider body) ? body.bounds.center : transform.position + Vector3.up;
        var player = GetComponent<Player>();
        var views = GetComponent<CameraSwitch>();
        Camera view = views != null ? views.ActiveCamera : player != null ? player.fpsCamera : null;
        Vector3 direction = Vector3.ProjectOnPlane(view != null ? view.transform.forward : transform.forward, Vector3.up).normalized;
        if (direction.sqrMagnitude < .01f) direction = transform.forward;
        float distance = 1.8f;
        foreach (var hit in Physics.SphereCastAll(origin, .35f, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            if (!IgnoreDropObstacle(hit.collider)) distance = Mathf.Min(distance, hit.distance - .05f);
        position = default;
        if (distance < .45f) return false;
        Vector3 candidate = origin + direction * distance;
        var groundHits = Physics.RaycastAll(candidate + Vector3.up * .5f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(groundHits, (a, b) => a.distance.CompareTo(b.distance));
        bool grounded = false;
        foreach (var hit in groundHits)
        {
            if (IgnoreDropObstacle(hit.collider)) continue;
            if (hit.normal.y < .5f) return false;
            position = hit.point + Vector3.up * .65f; grounded = true; break;
        }
        if (!grounded) return false;
        foreach (var obstacle in Physics.OverlapSphere(position, .35f, ~0, QueryTriggerInteraction.Ignore))
            if (!IgnoreDropObstacle(obstacle)) return false;
        Vector3 path = position - origin;
        foreach (var hit in Physics.RaycastAll(origin, path.normalized, path.magnitude, ~0, QueryTriggerInteraction.Ignore))
            if (!IgnoreDropObstacle(hit.collider)) return false;
        return true;
    }
}
