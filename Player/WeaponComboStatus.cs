using System.Collections.Generic;
using UnityEngine;

// Per-attacker windows prevent a teammate's unrelated hit from consuming a sequence.
public sealed class WeaponComboStatus : MonoBehaviour
{
    sealed class Sequence { public float frost, exposed, mark, cooldown; public int stacks; }
    readonly Dictionary<PlayerHealth, Sequence> sequences = new Dictionary<PlayerHealth, Sequence>();
    public bool IsBurning => Time.time < burningUntil;
    float burningUntil;
    EnemyHealth enemy;
    TacticalMarker marker;
    float nextLabel;
    string shownText;
    void Awake() { enemy = GetComponent<EnemyHealth>(); }
    Sequence For(PlayerHealth owner)
    {
        if (!sequences.TryGetValue(owner, out var state)) sequences[owner] = state = new Sequence();
        return state;
    }
    public void Mark(PlayerHealth owner)
    { if (owner == null) return; var state = For(owner); state.mark = Time.time + 8; state.stacks = 0; Show("已標記 · 連弩疊層 → 回聲", GameUITheme.Gold,8); }
    public void Hit(PlayerHealth owner, WeaponType type)
    {
        if (owner == null || enemy == null || enemy.IsDead) return;
        var state = For(owner);
        if (type == WeaponType.FrostStaff && Time.time >= state.cooldown)
        {
            state.frost = Time.time + 3f; state.cooldown = Time.time + 4f;
            enemy.GetComponent<EnemyAI>()?.FreezeFor(enemy.GetComponent<BossController>() != null ? .25f : 1.2f);
            Show("冰結 · 戰錘可碎冰", Color.cyan);
            owner.GetComponent<CombatMomentum>()?.OnContribution();
        }
        if (type == WeaponType.Longbow && state.mark <= Time.time) Mark(owner);
        if (type == WeaponType.RepeatingCrossbow && state.mark > Time.time)
        { state.stacks = Mathf.Min(3, state.stacks + 1); Show($"回聲印記 {state.stacks}/3 · 回聲可引爆", GameUITheme.Gold,state.mark-Time.time); }
        if (type == WeaponType.FireStaff) burningUntil = Time.time + 4;
    }
    public float ModifyDamage(PlayerHealth owner, WeaponType type, float damage)
    {
        if (owner == null) return damage;
        var state = For(owner);
        if (type == WeaponType.WarHammer && state.frost > Time.time)
        {
            state.frost = 0; state.exposed = Time.time + 5;
            Show("碎冰破甲 · 長弓收尾", Color.cyan,5);
            owner.GetComponent<RogueTactics>()?.ShowNotice("碎冰連擊 · 戰錘 150% 傷害，長弓可收尾");
            return damage * 1.5f;
        }
        if (type == WeaponType.Longbow && state.exposed > Time.time)
        { state.exposed = 0; owner.GetComponent<RogueTactics>()?.ShowNotice("碎冰收尾 · 長弓 175% 傷害"); return damage * 1.75f; }
        return damage;
    }
    public float EchoDamage(PlayerHealth owner, float damage)
    {
        if (owner == null) return damage;
        var state = For(owner);
        if (state.mark <= Time.time || state.stacks <= 0) return damage;
        float multiplier = 1 + state.stacks * .5f;
        state.mark = 0; state.stacks = 0;
        Show("回聲引爆", GameUITheme.Gold);
        owner.GetComponent<RogueTactics>()?.ShowNotice("回聲引爆 · 印記已消耗");
        return damage * multiplier;
    }
    void Show(string text, Color color,float seconds=3)
    {
        if (marker != null) Destroy(marker.gameObject);
        marker = TacticalMarker.Create("連招狀態", transform.position + Vector3.up * .1f, transform.forward, color, .55f);
        marker.Replicate = true;
        marker.transform.SetParent(transform, true); marker.SetText(text); nextLabel = Time.time + seconds; shownText=text;
    }
    void Update()
    {
        if(marker==null) return;
        if(Time.time>=nextLabel) { Destroy(marker.gameObject); marker=null; }
        else marker.SetText(shownText+$"\n{nextLabel-Time.time:F1}s");
    }
    void OnDisable() { if (marker != null) Destroy(marker.gameObject); sequences.Clear(); burningUntil = 0; }
}
