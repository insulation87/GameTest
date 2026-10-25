using UnityEngine;

public enum WeaponBuild
{
    None, Gunslinger, Redline, Fortress, OneShotTheory, Bonebreaker,
    Doomsday, Vanguard, TacticalExpert, Breacher, SuppressiveFire,
    Scavenger, Hunter, RangeMaster, AllRounder, ThreeStage,
    WarMachine, Siege, DeathZone, BlitzDemolition, ShockAssault,
    FirstStrike, BountyHunter, RoomCleaner, LastStand, BulletStorm
}

// Stateful weapon sequences replace the retired gun recipes. Utility works off-role;
// direct damage still uses CharacterWeapons.Efficiency.
public class WeaponBuildSystem : MonoBehaviour
{
    public WeaponBuild ActiveBuild => WeaponBuild.None;
    public string ActiveBuildName => LoadoutHint(GetComponent<WeaponInventory>());
    public float MoveSpeedMultiplier => 1f;
    float guardUntil, nextVortex, nextFire;
    public float IncomingDamageMultiplier => Time.time < guardUntil ? .7f : 1f;
    public bool IsGuarding => Time.time < guardUntil;
    public void RefreshBuild() { }
    public void OnWeaponSwitched(WeaponType type) { }
    public void OnShot(WeaponType type)
    { if (type == WeaponType.SwordShield) guardUntil = Time.time + 2f; }
    public float ModifyFireInterval(float interval, WeaponType type) => Mathf.Max(.02f, interval);
    public float ModifyExplosionRadius(float radius, WeaponType type) => Mathf.Max(0, radius);
    public float ModifyDamage(float damage, EnemyHealth enemy, WeaponType type, float distance, bool explosive, bool headshot)
    {
        if (enemy == null) return damage;
        float shock = GetComponent<TacticalEquipment>()?.ConsumeWatchfulStrike() ?? 0f;
        if (shock > 0) enemy.GetComponent<EnemyAI>()?.FreezeFor(enemy.GetComponent<BossController>() != null ? .1f : .4f);
        damage += shock;
        var status = enemy.GetComponent<WeaponComboStatus>();
        return (status != null ? status.ModifyDamage(GetComponent<PlayerHealth>(), type, damage) : damage) * (headshot ? 1.5f : 1f);
    }
    public void OnHit(EnemyHealth enemy, WeaponType type, float distance, bool explosive, bool headshot, bool killed)
    {
        if (enemy == null) return;
        var boss = enemy.GetComponent<BossController>();
        if (boss != null && boss.IsTransforming) return;
        GetComponent<RogueTactics>()?.OnWeaponHit(enemy, type);
        if (enemy.IsDead || CoopSession.IsClient) return;
        var status = enemy.GetComponent<WeaponComboStatus>();
        if (status == null) status = enemy.gameObject.AddComponent<WeaponComboStatus>();
        status.Hit(GetComponent<PlayerHealth>(), type);
        if (type == WeaponType.ArcaneFocus && Time.time >= nextVortex)
        {
            nextVortex = Time.time + 7f;
            TacticalField.Create(TacticalField.Kind.Vortex, enemy.transform.position, GetComponent<PlayerHealth>(), 3.5f, 3f, 0);
        }
        if (type == WeaponType.FireStaff && Time.time >= nextFire)
        {
            nextFire = Time.time + 6f;
            var shooter = GetComponent<PlayerShooter>();
            float power = (shooter != null ? shooter.projectileDamage : 20f) * CharacterWeapons.Efficiency(gameObject, type);
            TacticalField.Create(TacticalField.Kind.Fire, enemy.transform.position, GetComponent<PlayerHealth>(), 3f, 4f, power * .2f);
        }
        if (type == WeaponType.SwordShield && status.IsBurning)
        {
            guardUntil = Time.time + 4f;
            TacticalField.Create(TacticalField.Kind.Ward, transform.position, GetComponent<PlayerHealth>(), 4f, 3f, 0);
            GetComponent<RogueTactics>()?.ShowNotice("焚陣守線 · 燃燒敵人觸發隊伍盾牆");
        }
    }
    public void ForgetEnemy(EnemyHealth enemy) { }
    public static string UtilityHint(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.FrostStaff: return "冰結起手 → 戰錘碎冰 → 長弓收尾";
            case WeaponType.WarHammer: return "擊碎冰結，留下 5 秒破甲 → 長弓收尾";
            case WeaponType.Longbow: return "標記 8 秒 → 連弩疊層 → 回聲引爆；可收尾破甲敵人";
            case WeaponType.RepeatingCrossbow: return "命中標記疊 3 層 → 回聲引爆";
            case WeaponType.ArcaneFocus: return "每 7 秒命中部署聚怪場 → 火杖燃燒 → 劍盾守線";
            case WeaponType.FireStaff: return "每 6 秒命中部署火域 → 劍盾觸發隊伍盾牆";
            case WeaponType.SwordShield: return "攻擊後短暫格擋；命中燃燒目標部署隊伍盾牆";
            default: return "專武輸出；搭配冰結、標記與火域副武器控場";
        }
    }
    public static string LoadoutHint(WeaponInventory inventory)
    {
        if (inventory == null) return "";
        bool frost = false, hammer = false, bow = false, rapid = false, focus = false, fire = false, shield = false;
        foreach (var item in inventory.EquippedSlots)
        {
            if (item == null) continue;
            frost |= item.weaponType == WeaponType.FrostStaff; hammer |= item.weaponType == WeaponType.WarHammer;
            bow |= item.weaponType == WeaponType.Longbow; rapid |= item.weaponType == WeaponType.RepeatingCrossbow;
            focus |= item.weaponType == WeaponType.ArcaneFocus; fire |= item.weaponType == WeaponType.FireStaff;
            shield |= item.weaponType == WeaponType.SwordShield;
        }
        if (frost && hammer && bow) return "可形成碎冰連擊：霜杖 → 戰錘 → 長弓";
        if (bow && rapid) return "可形成回聲引爆：長弓 → 連弩 ×3 → 回聲";
        if (focus && fire && shield) return "可形成焚陣守線：魔導器 → 火杖 → 劍盾";
        return "副武器的控場可完整使用；傷害依角色適性計算";
    }
}
