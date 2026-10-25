using UnityEngine;

// Serialized legacy weapon IDs remain readable, but all gameplay sources use these ten weapons.
public static class CharacterWeapons
{
    public static readonly WeaponType[] Types = {
        WeaponType.WarHammer, WeaponType.SwordShield, WeaponType.RepeatingCrossbow,
        WeaponType.LightStaff, WeaponType.DualDaggers, WeaponType.FireStaff,
        WeaponType.ArcaneFocus, WeaponType.FrostStaff, WeaponType.Scythe, WeaponType.Longbow
    };
    static readonly string[] Names = { "震地戰錘", "守衛劍盾", "疾射連弩", "晨光法杖", "幽影雙匕",
        "燼火法杖", "銅芯魔導器", "霜華法杖", "暮影鐮刀", "星矢長弓" };
    public static bool IsCharacterWeapon(WeaponType type) => (int)type >= 10 && (int)type <= 19;
    public const float MismatchedEfficiency = .1f;
    public static float Efficiency(GameObject wielder, WeaponType type)
    {
        var skills = wielder != null ? wielder.GetComponent<CharacterSkills>() : null;
        return skills == null || Migrate(type) == ForCharacter(skills.Character.Id) ? 1f : MismatchedEfficiency;
    }
    public static WeaponType ForCharacter(string id)
    {
        for (int i = 0; i < CharacterRoster.All.Length; i++)
            if (CharacterRoster.All[i].Id == id) return Types[i];
        return WeaponType.WarHammer;
    }
    public static WeaponType Migrate(WeaponType type)
    {
        if (IsCharacterWeapon(type)) return type;
        switch (type)
        {
            case WeaponType.SubmachineGun: return WeaponType.RepeatingCrossbow;
            case WeaponType.Shotgun: return WeaponType.WarHammer;
            case WeaponType.RPG: return WeaponType.FireStaff;
            case WeaponType.Pistol: return WeaponType.LightStaff;
            case WeaponType.SniperRifle: return WeaponType.Longbow;
            case WeaponType.LightMachineGun: return WeaponType.ArcaneFocus;
            default: return WeaponType.Longbow;
        }
    }
    public static string Name(WeaponType type) => Names[(int)Migrate(type) - 10];
    public static bool IsMelee(WeaponType type) => type == WeaponType.WarHammer ||
        type == WeaponType.SwordShield || type == WeaponType.DualDaggers || type == WeaponType.Scythe;
    public static float Reach(WeaponType type) => type == WeaponType.DualDaggers ? 2.5f :
        type == WeaponType.SwordShield ? 3f : 4f;
    public static int ShopPrice(WeaponType type) => IsCharacterWeapon(type) ? 100 + ((int)type - 10) * 20 : 0;
    public static WeaponItem Create(WeaponType type, int level, float quality)
    {
        type = Migrate(type); level = Mathf.Clamp(level, 1, 100); quality = Mathf.Clamp01(quality);
        float damage = 24, interval = .5f, speed = 45, radius = 0;
        switch (type)
        {
            case WeaponType.WarHammer: damage = 55; interval = .9f; break;
            case WeaponType.SwordShield: damage = 30; interval = .55f; break;
            case WeaponType.RepeatingCrossbow: damage = 12; interval = .15f; speed = 65; break;
            case WeaponType.LightStaff: damage = 24; interval = .45f; break;
            case WeaponType.DualDaggers: damage = 16; interval = .22f; break;
            case WeaponType.FireStaff: damage = 32; interval = .75f; speed = 32; radius = 2.5f; break;
            case WeaponType.ArcaneFocus: damage = 18; interval = .25f; speed = 55; break;
            case WeaponType.FrostStaff: damage = 28; interval = .6f; speed = 38; break;
            case WeaponType.Scythe: damage = 42; interval = .7f; break;
            case WeaponType.Longbow: damage = 48; interval = .8f; speed = 90; break;
        }
        return new WeaponItem { weaponType = type, weaponName = $"{Name(type)} 等級 {level}",
            level = level, quality = quality, damage = damage * (1f + (level - 1) * .04f) * (1f + quality * .35f),
            fireInterval = interval / (1f + quality * .2f), projectileSpeed = speed,
            projectilesPerShot = 1, explosionRadius = radius };
    }
}
