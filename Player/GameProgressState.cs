using System;
using UnityEngine;

[Serializable]
public sealed class GameProgressState
{
    public int version = 1;
    public int worldLayoutVersion;
    public string scene, savedUtc;
    public Vector3 ground;
    public float yaw;
    public PlayerProgress health;
    public InventoryProgress inventory;
    public int[] floorKeys;
    public bool bossSpawned, bossDefeated;
    public int bossPhase = 1;
    public float bossHealth, bossMaxHealth;
    public bool[] explored;
    // Optional v1 extension: older saves initialize this to null and retain their existing data.
    public RogueTacticsProgress tactics;
    public CharacterProgress character;
    public ExpeditionProgress expedition;
    // Account-level shop progress retained when the run's equipment is reset.
    public int weaponPurchases;
    public bool preparingRun, runEnded;
    public bool campCharacterSelected, campUpgradeSelected;
}

[Serializable]
public sealed class PlayerProgress
{
    public float health, maxHealth, armor, maxArmor, armorRegeneration;
    public int level, experience, pendingUpgrades, penetration;
    public float attack, speed, criticalChance, criticalDamage;
    public bool doubleJump;
    public TacticalShotStyle shotStyle;
    public bool expeditionKit;
}

[Serializable]
public sealed class InventoryProgress
{
    public WeaponItem[] weapons;
    public int[] slots, keys;
    public ArmorItem armor; // Legacy single armor, migrated to the chest slot.
    public ArmorItem[] armorItems;
    public int[] armorSlots;
    public int activeSlot, capacity, gold, healthPacks;
    public int weaponPurchases; // Also travels with cooperative inventory snapshots.
    public float healAmount;
}

public partial class PlayerHealth
{
    public PlayerProgress CaptureProgress() => new PlayerProgress {
        health = currentHealth, maxHealth = maxHealth, armor = currentArmor, maxArmor = maxArmor,
        armorRegeneration = armorRegeneration, level = currentLevel, experience = currentExperience,
        pendingUpgrades = pendingUpgradeChoices, penetration = bulletPenetration, attack = attackMultiplier,
        speed = moveSpeedMultiplier, criticalChance = criticalChance, criticalDamage = criticalDamageMultiplier,
        doubleJump = hasDoubleJump, shotStyle = ShotStyle, expeditionKit = ExpeditionKitReceived
    };

    public void RestoreProgress(PlayerProgress data)
    {
        maxHealth = Mathf.Max(1, data.maxHealth);
        currentHealth = Mathf.Clamp(data.health, 1, maxHealth);
        maxArmor = Mathf.Max(0, data.maxArmor);
        currentArmor = Mathf.Clamp(data.armor, 0, maxArmor);
        armorRegeneration = Mathf.Max(0, data.armorRegeneration);
        currentLevel = Mathf.Clamp(data.level, 1, maxPlayerLevel);
        currentExperience = Mathf.Max(0, data.experience);
        pendingUpgradeChoices = Mathf.Clamp(data.pendingUpgrades, 0, maxPlayerLevel / 5 + 1);
        ShotStyle = Enum.IsDefined(typeof(TacticalShotStyle), data.shotStyle) ? data.shotStyle : TacticalShotStyle.None;
        ExpeditionKitReceived = data.expeditionKit;
        bulletPenetration = Mathf.Max(0, data.penetration);
        attackMultiplier = Mathf.Max(1, data.attack);
        moveSpeedMultiplier = Mathf.Max(1, data.speed);
        criticalChance = Mathf.Clamp01(data.criticalChance);
        criticalDamageMultiplier = Mathf.Max(1, data.criticalDamage);
        hasDoubleJump = data.doubleJump;
        lastDamageTime = Time.time;
        invincibleUntil = Time.time + respawnInvincibilityDuration;
        if (pendingUpgradeChoices > 0) ShowNextUpgradeChoice();
    }

    public void SetSavedRespawn(Vector3 position, Quaternion rotation)
    {
        initialSpawnPosition = position;
        initialSpawnRotation = rotation;
        if (respawnPoint != null) respawnPoint.SetPositionAndRotation(position, rotation);
    }
}

public partial class WeaponInventory
{
    public InventoryProgress CaptureProgress() => new InventoryProgress {
        weapons = weapons.ToArray(), slots = Array.ConvertAll(equippedSlots, w => weapons.IndexOf(w)),
        keys = (int[])floorKeyCounts.Clone(), armor = null, activeSlot = activeWeaponSlot,
        armorItems = armors.ToArray(), armorSlots = Array.ConvertAll(armorSlots, item => armors.IndexOf(item)),
        capacity = capacity, gold = gold, healthPacks = healthPackCount, weaponPurchases = WeaponPurchaseCount,
        healAmount = healthPackRestoreAmount
    };

    public void RestoreProgress(InventoryProgress data)
    {
        weapons.Clear();
        foreach (var weapon in data.weapons)
            weapons.Add(CharacterWeapons.IsCharacterWeapon(weapon.weaponType) ? weapon :
                CharacterWeapons.Create(CharacterWeapons.Migrate(weapon.weaponType), weapon.level, weapon.quality));
        capacity = Mathf.Max(1, data.capacity);
        gold = Mathf.Max(0, data.gold);
        RestoreShopProgress(data.weaponPurchases);
        healthPackCount = Mathf.Max(0, data.healthPacks);
        healthPackRestoreAmount = Mathf.Max(1, data.healAmount);
        // The health snapshot already includes this armor's bonus.
        equippedArmor = null; armors.Clear(); Array.Clear(armorSlots, 0, armorSlots.Length);
        if (data.armorItems != null) armors.AddRange(data.armorItems);
        if (data.armorSlots != null && data.armorSlots.Length == 4)
            for (int i = 0; i < 4; i++)
                if (data.armorSlots[i] >= 0 && data.armorSlots[i] < armors.Count &&
                    armors[data.armorSlots[i]].slot == (ArmorSlot)i) armorSlots[i] = armors[data.armorSlots[i]];
        if (data.armorItems == null && data.armor != null)
        {
            data.armor.slot = ArmorSlot.Chest; data.armor.level = Mathf.Clamp(Mathf.Max(1, data.armor.tier * 10), 1, 100);
            armors.Add(data.armor); armorSlots[1] = data.armor;
        }
        selectedArmor = null;
        foreach (var item in weapons) EquipmentIdentity.Ensure(item);
        foreach (var item in armors) EquipmentIdentity.Ensure(item);
        for (int i = 0; i < floorKeyCounts.Length; i++) floorKeyCounts[i] = Mathf.Max(0, data.keys[i]);
        floorKeyCounts[1] = 0; // Remove the retired inventory item from old saves and network snapshots.
        for (int i = 0; i < equippedSlots.Length; i++)
            equippedSlots[i] = data.slots[i] < 0 ? null : weapons[data.slots[i]];
        capacity = Mathf.Max(capacity, UsedItemSlots);
        activeWeaponSlot = Mathf.Clamp(data.activeSlot, 0, 2);
        if (equippedSlots[activeWeaponSlot] == null)
            for (int i = 0; i < 3; i++) if (equippedSlots[i] != null) { activeWeaponSlot = i; break; }
        selectedWeapon = equippedSlots[activeWeaponSlot];
        equippedWeapon = null;
        if (selectedWeapon == null) shooter?.ClearWeapon();
        SwitchToSlot(activeWeaponSlot);
        GetComponent<WeaponBuildSystem>()?.RefreshBuild();
        RefreshWeaponList(); RefreshDetails(); RefreshHotbar(); RefreshArmorSlots();
    }
}

public partial class FloorProgressionManager
{
    public int[] CaptureProgress() => (int[])collected.Clone();
    public void RestoreProgress(int[] keys, bool bossUnlocked = false)
    {
        for (int i = 0; i < collected.Length; i++) collected[i] = Mathf.Max(0, keys[i]);
        SetWallLocked(floorTwoWall, collected[1] < floorOneRequired);
        SetWallLocked(floorThreeWall, collected[2] < floorTwoRequired);
        SetWallLocked(bossWall, !bossUnlocked && collected[3] < floorThreeRequired);
    }
}

public partial class ExplorationMinimap
{
    public bool[] CaptureProgress()
    {
        var result = new bool[3 * fogGridSize * fogGridSize];
        for (int f = 0; f < 3; f++)
            if (fogTiles[f] != null) for (int i = 0; i < fogTiles[f].Length; i++)
                result[f * fogGridSize * fogGridSize + i] = fogTiles[f][i].color.a <= .01f;
        return result;
    }
    public void RestoreProgress(bool[] data)
    {
        if (data == null || data.Length != 3 * fogGridSize * fogGridSize) return;
        for (int f = 0; f < 3; f++)
            if (fogTiles[f] != null) for (int i = 0; i < fogTiles[f].Length; i++)
                if (data[f * fogGridSize * fogGridSize + i])
                { fogTiles[f][i].color = Color.clear; fogTiles[f][i].gameObject.SetActive(false); }
    }
}
