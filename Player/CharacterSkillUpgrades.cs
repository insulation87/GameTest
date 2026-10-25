using UnityEngine;

public static class CharacterSkillUpgrades
{
    static readonly string[] PowerTitles = { "震擊重壓", "堅壁加固", "狂射增幅", "脈衝強化", "幽影銳刃", "烈焰升溫", "砲台增壓", "新星增幅", "靈魂汲取", "星芒聚焦" };
    static readonly string[] PowerEffects = { "裂地震擊傷害 +20%", "堡壘護盾減傷效果與護甲回復增強", "極速狂射期間，射擊間隔進一步縮短", "生命脈衝的立即與持續治療 +20%", "幽影突襲的傷害與移速加成增強", "灼熱火域每次灼燒傷害 +20%", "哨戒砲台每發傷害 +20%", "冰結新星傷害 +20%", "靈魂收割傷害與斬殺回血 +20%", "星芒穿刺傷害 +20%" };
    public static string Label(string character, int choice)
    {
        int index = System.Array.FindIndex(CharacterRoster.All, item => item.Id == character);
        if (index < 0) index = 0;
        string skill = CharacterRoster.All[index].Skill;
        if (choice == 0) return PowerTitles[index] + "\n\n" + PowerEffects[index] + "\n只強化此角色的專屬技能";
        if (choice == 1) return skill + " · 再臨\n\n技能冷卻時間 ×90%\n最低為原始冷卻的 25%";
        bool range = UsesRange(character);
        return skill + (range ? " · 擴張\n\n技能作用範圍 +15%" : " · 延續\n\n技能持續時間 +20%") + "\n可與同角色的其他技能升級搭配";
    }
    public static bool UsesRange(string id) => id == "breaker" || id == "frost" || id == "reaper" || id == "ranger";
}

public sealed partial class CharacterSkills
{
    int skillPower, skillHaste, skillMastery;
    bool campUpgradeApplied;
    public int SkillPowerLevel => skillPower;
    public int SkillHasteLevel => skillHaste;
    public int SkillMasteryLevel => skillMastery;
    public float SkillPower => 1 + .2f * skillPower;
    public float SkillRange => CharacterSkillUpgrades.UsesRange(Character.Id) ? 1 + .15f * skillMastery : 1;
    public float SkillDuration => CharacterSkillUpgrades.UsesRange(Character.Id) ? 1 : 1 + .2f * skillMastery;
    public float SkillCooldown => Character.Cooldown * Mathf.Max(.25f, Mathf.Pow(.9f, skillHaste));
    public void ApplySkillUpgrade(int index)
    {
        if (!CoopSession.Simulates(this)) return;
        if (index == 0) skillPower = Mathf.Min(100, skillPower + 1);
        if (index == 1) skillHaste = Mathf.Min(100, skillHaste + 1);
        if (index == 2) skillMastery = Mathf.Min(100, skillMastery + 1);
        CooldownRemaining = Mathf.Min(CooldownRemaining, SkillCooldown);
        RefreshHud();
    }
    public void ApplyStartingCampUpgrade(int index)
    {
        if (campUpgradeApplied || index < 0 || index > 2) return;
        campUpgradeApplied = true; ApplySkillUpgrade(index);
    }
    void RestoreSkillUpgrades(CharacterProgress progress)
    {
        skillPower = Mathf.Clamp(progress != null ? progress.skillPower : 0, 0, 100);
        skillHaste = Mathf.Clamp(progress != null ? progress.skillHaste : 0, 0, 100);
        skillMastery = Mathf.Clamp(progress != null ? progress.skillMastery : 0, 0, 100);
    }
}
