using System.Collections.Generic;
using UnityEngine;

public enum TacticalAffix { None, SlideMark, Watchful, EmberStep, LongEcho }
public enum TacticalShotStyle { None, Pierce, Ricochet, Split }

public sealed class TacticalEquipment : MonoBehaviour
{
    readonly HashSet<EnemyHealth> slideTargets = new HashSet<EnemyHealth>();
    bool wasInFire;
    float watchUntil, nextWatch, sprintUntil, nextSprint;
    public bool Has(TacticalAffix affix)
    {
        var inventory = GetComponent<WeaponInventory>();
        if (inventory != null) foreach (var item in inventory.EquippedArmorSlots)
            if (item != null && item.affix == affix) return true;
        return false;
    }
    public float EchoRange => Has(TacticalAffix.LongEcho) ? 22f : 36f;
    public float EchoDuration => Has(TacticalAffix.LongEcho) ? 28f : 18f;
    public float MoveMultiplier => Time.time < sprintUntil && Has(TacticalAffix.EmberStep) ? 1.35f : 1f;
    public void OnBlockedDamage()
    {
        if (!Has(TacticalAffix.Watchful) || Time.time < nextWatch) return;
        watchUntil = Time.time + 5; nextWatch = Time.time + 8;
        GetComponent<RogueTactics>()?.ShowNotice("守望蓄勢 · 5 秒內下次命中附帶震擊");
    }
    public float ConsumeWatchfulStrike()
    {
        if (!Has(TacticalAffix.Watchful) || Time.time >= watchUntil) return 0;
        watchUntil = 0; return 12f * (GetComponent<PlayerHealth>()?.AttackMultiplier ?? 1f);
    }
    public void ResetTransientState() { watchUntil = sprintUntil = 0; wasInFire = false; slideTargets.Clear(); }
    void Update()
    {
        if (CoopSession.IsClient || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen) return;
        var health = GetComponent<PlayerHealth>(); var player = GetComponent<Player>();
        if (health == null || health.IsDead) { ResetTransientState(); return; }
        bool sliding = player != null && player.IsSliding;
        if (!sliding) slideTargets.Clear();
        if (sliding && Has(TacticalAffix.SlideMark))
            foreach (var enemy in EnemyHealth.ActiveEnemies)
                if (enemy != null && !enemy.IsDead && (enemy.transform.position - transform.position).sqrMagnitude < 6.25f &&
                    TacticalField.Clear(transform.position + Vector3.up, enemy.transform.position + Vector3.up, enemy, health) && slideTargets.Add(enemy))
                {
                    var status = enemy.GetComponent<WeaponComboStatus>() ?? enemy.gameObject.AddComponent<WeaponComboStatus>();
                    status.Mark(health); status.Hit(health, WeaponType.RepeatingCrossbow);
                }
        bool inFire = GetComponent<CharacterSkills>()?.InsideOwnFire == true || TacticalField.InOwnFire(health);
        if (wasInFire && !inFire && Has(TacticalAffix.EmberStep) && Time.time >= nextSprint)
        { sprintUntil = Time.time + 3; nextSprint = Time.time + 8; GetComponent<RogueTactics>()?.ShowNotice("餘燼步伐 · 離開火域獲得 3 秒加速"); }
        wasInFire = inFire;
    }
    void OnDisable() => ResetTransientState();
    public static string Description(TacticalAffix affix)
    {
        switch (affix)
        {
            case TacticalAffix.SlideMark: return "滑行靴：滑過敵人留下 1 層回聲印記；護甲降低 20%";
            case TacticalAffix.Watchful: return "守望頭盔：格擋後 5 秒內下次命中附帶震擊；冷卻 8 秒，護甲降低 20%";
            case TacticalAffix.EmberStep: return "餘燼盔甲：離開自身火域後移速 +35% 持續 3 秒；冷卻 8 秒，護甲降低 20%";
            case TacticalAffix.LongEcho: return "回響長褲：回聲保留 28 秒；啟動距離縮為 22 公尺，護甲降低 20%";
            default: return "";
        }
    }
    public static ArmorItem Special(ArmorSlot slot, int level)
    {
        var item = ArmorLoot.Create(slot, level, .7f);
        return ApplySpecial(item);
    }
    public static ArmorItem ApplySpecial(ArmorItem item)
    {
        var slot = item.slot;
        item.affix = slot == ArmorSlot.Shoes ? TacticalAffix.SlideMark : slot == ArmorSlot.Helmet ? TacticalAffix.Watchful :
            slot == ArmorSlot.Chest ? TacticalAffix.EmberStep : TacticalAffix.LongEcho;
        item.armorName = slot == ArmorSlot.Shoes ? "滑行靴" : slot == ArmorSlot.Helmet ? "守望頭盔" : slot == ArmorSlot.Chest ? "餘燼盔甲" : "回響長褲";
        item.armorBonus *= .8f; return item;
    }
}
