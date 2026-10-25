using System;
using UnityEngine;

public sealed class CharacterDefinition
{
    public readonly string Id, Name, Role, Skill, Description;
    public readonly float Cooldown;
    public readonly Color Color;
    public CharacterDefinition(string id, string name, string role, string skill, string description, float cooldown, Color color)
    { Id = id; Name = name; Role = role; Skill = skill; Description = description; Cooldown = cooldown; Color = color; }
}

/// <summary>Stable IDs belong in saves; array order is only presentation.</summary>
public static class CharacterRoster
{
    public static readonly CharacterDefinition[] All = {
        new CharacterDefinition("breaker", "震擊先鋒", "近身清群", "裂地震擊", "對 7 公尺內可見的敵人造成 80 傷害。\n貼近敵群，一擊掃開包圍。", 10, new Color(.95f,.64f,.27f)),
        new CharacterDefinition("sentinel", "鐵壁守衛", "承傷防禦", "堡壘護盾", "恢復 20% 護甲，5 秒內減傷 75%；\n腳下 4 公尺盾牆掩護隊友並累積狂潮。", 16, new Color(.48f,.66f,.95f)),
        new CharacterDefinition("gunner", "疾射獵手", "火力爆發", "極速狂射", "5 秒內射擊間隔縮短 55%。\n可與武器流派及連殺狂潮疊加。", 16, new Color(.96f,.82f,.32f)),
        new CharacterDefinition("medic", "晨光醫師", "續戰恢復", "生命脈衝", "治療自身與 8 公尺內可見隊友 35% 生命；\n接著 5 秒持續治療，有效治療累積狂潮。", 22, new Color(.46f,.89f,.65f)),
        new CharacterDefinition("assassin", "夜行影刃", "機動突襲", "幽影突襲", "1 秒內免疫一般傷害；4 秒內移速 +75%、\n武器傷害 +50%。墜落仍會致命。", 16, new Color(.75f,.54f,.95f)),
        new CharacterDefinition("pyromancer", "燼火術師", "區域壓制", "灼熱火域", "在腳下留下半徑 5 公尺、持續 8 秒的火域，\n每 0.5 秒對可見敵人造成 18 傷害。", 18, new Color(.98f,.39f,.24f)),
        new CharacterDefinition("engineer", "銅芯技師", "自動支援", "哨戒砲台", "部署 10 秒砲台，射程 16 公尺；\n每 0.4 秒射擊最近的可見敵人，造成 35 傷害。", 24, new Color(.77f,.64f,.45f)),
        new CharacterDefinition("frost", "霜華使者", "控場解圍", "冰結新星", "對 9 公尺內可見敵人造成 35 傷害並凍結 3 秒。\n首領僅凍結 0.5 秒，變身期間不受影響。", 18, new Color(.43f,.85f,.98f)),
        new CharacterDefinition("reaper", "暮影收割者", "斬殺回血", "靈魂收割", "對 6 公尺內可見敵人造成 50 傷害，斬殺生命\n低於 30% 的普通敵人；每殺回血 10%，最多 30%。", 18, new Color(.91f,.39f,.61f)),
        new CharacterDefinition("ranger", "星矢遊俠", "直線貫穿", "星芒穿刺", "沿準星射出長 25 公尺、寬 2.4 公尺的穿刺光束，\n對路徑上的敵人造成 180 傷害，牆壁會阻擋。", 14, new Color(.40f,.90f,.83f))
    };
    static string pending;
    public static CharacterDefinition Find(string id)
    {
        foreach (var item in All) if (item.Id == id) return item;
        return All[0];
    }
    public static bool IsKnown(string id)
    { foreach (var item in All) if (item.Id == id) return true; return false; }
    public static void SelectForNewRun(string id) => pending = Find(id).Id;
    public static void ClearPending() => pending = null;
    public static CharacterDefinition ConsumeSelection()
    { var item = Find(pending); pending = null; return item; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => ClearPending();
}

[Serializable]
public sealed class CharacterProgress
{
    // Zero means absent in a legacy save, including JsonUtility's inline-object defaults.
    public int version;
    public string id;
    public float cooldown;
    public int skillPower, skillHaste, skillMastery;
}
