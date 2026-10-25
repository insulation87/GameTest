using System;
using UnityEngine;
using UnityEngine.UI;

public partial class Player
{
    float networkVerticalSpeed;
    public void ApplyCoopAnimationState(CoopPlayerState state)
    {
        isGrounded = state.grounded; isRunning = state.running; isCrouching = state.crouching; isSliding = state.sliding;
        moveDirection = Vector3.forward * state.movement; networkVerticalSpeed = state.verticalSpeed; cameraHeightOffset = state.cameraHeight;
    }
}

public partial class PlayerHealth
{
    readonly UpgradeOption[] coopOptions = new UpgradeOption[3];
    public string[] CoopUpgradeLabels()
    {
        if (!IsChoosingUpgrade) return null;
        return upgradeButtons.ConvertAll(button => button.GetComponentInChildren<Text>().text).ToArray();
    }
    public void SelectCoopUpgrade(int index)
    {
        if (!IsChoosingUpgrade || index < 0 || index >= coopOptions.Length || coopOptions[index] == null) return;
        SelectUpgrade(coopOptions[index]);
    }
    public void ApplyCoopState(PlayerProgress data, string[] labels)
    {
        maxHealth = data.maxHealth; currentHealth = data.health; currentArmor = data.armor; maxArmor = data.maxArmor;
        armorRegeneration = data.armorRegeneration; currentLevel = data.level; currentExperience = data.experience;
        pendingUpgradeChoices = data.pendingUpgrades; bulletPenetration = data.penetration; attackMultiplier = data.attack;
        moveSpeedMultiplier = data.speed; criticalChance = data.criticalChance; criticalDamageMultiplier = data.criticalDamage;
        hasDoubleJump = data.doubleJump; IsDead = data.health <= 0;
        ShotStyle = data.shotStyle;
        ExpeditionKitReceived = data.expeditionKit;
        if (!CoopSession.LocalUI(this)) return;
        if (labels == null || labels.Length != 3)
        { if (upgradePanel != null) upgradePanel.SetActive(false); return; }
        BuildUpgradeCanvas(); upgradePanel.SetActive(true);
        upgradeTitle.text = "選擇升級 · 戰鬥持續進行";
        for (int i = 0; i < upgradeButtons.Count; i++)
        {
            int index = i;
            upgradeButtons[i].GetComponentInChildren<Text>().text = labels[i];
            upgradeButtons[i].onClick.RemoveAllListeners();
            upgradeButtons[i].onClick.AddListener(() => CoopSession.Upgrade(this, index));
        }
    }
}
public sealed partial class CharacterSkills
{
    public void ApplyCoopState(CharacterProgress progress, float effect)
    {
        if (Character.Id != progress.id) RestoreProgress(progress);
        RestoreSkillUpgrades(progress);
        CooldownRemaining = Mathf.Max(0, progress.cooldown); EffectRemaining = Mathf.Max(0, effect);
        RefreshHud();
    }
}
public partial class WeaponInventory
{
    // Only fixed actions reach gameplay. The server chooses prices, damage and item stats.
    public void ApplyCoopCommand(string action, int value, int extra, string itemId = null)
    {
        var health = GetComponent<PlayerHealth>();
        if (health == null || health.IsDead || DungeonVictoryUI.IsOpen || GameSceneFlow.IsLoading) return;
        switch (action)
        {
            case "dropWeapon":
                if (!string.IsNullOrEmpty(itemId)) DropWeapon(weapons.Find(item => item.itemId == itemId));
                break;
            case "dropArmor":
                if (!string.IsNullOrEmpty(itemId)) DropArmor(armors.Find(item => item.itemId == itemId));
                break;
            case "slot": SwitchToSlot(value); break;
            case "equip": if (value >= 0 && value < weapons.Count && extra >= 0 && extra < 3) EquipToSlot(weapons[value], extra); break;
            case "heal": UseHealthPack(); break;
            case "sell": if (value >= 0 && value < weapons.Count) { selectedWeapon = weapons[value]; SellSelectedWeapon(); } break;
            case "buy":
                if (CharacterWeapons.IsCharacterWeapon((WeaponType)value))
                    BuyWeapon((WeaponType)value, CharacterWeapons.ShopPrice((WeaponType)value));
                break;
            case "equipArmor": if (value >= 0 && value < armors.Count) EquipArmor(armors[value]); break;
            case "unequipArmor": if (value >= 0 && value < 4) UnequipArmor((ArmorSlot)value); break;
            case "sellArmor": if (value >= 0 && value < armors.Count) { selectedArmor = armors[value]; SellSelectedArmor(); } break;
            case "buyArmor": if (value >= 0 && value < 4) BuyArmorPiece((ArmorSlot)value); break;
            case "armor":
                if (value == 15) BuyArmor("輕型防具", 15, lightArmorPrice);
                else if (value == 30) BuyArmor("中型防具", 30, mediumArmorPrice);
                else if (value == 50) BuyArmor("重型防具", 50, heavyArmorPrice);
                break;
        }
    }
}
public sealed partial class RogueTactics
{
    public CoopTacticsState CaptureCoopState() => new CoopTacticsState { resonance = Resonance,
        armed = EchoArmed, position = EchoPosition, direction = replayCount > 0 ? replay[0].direction : transform.forward,
        remaining = EchoRemaining, range = ActivationRange,
        echo = echoText != null ? echoText.text.Replace("[" + GameControls.Label(this, GameAction.Echo) + "]", "{echo}") : "",
        pact = pactText != null ? pactText.text.Replace("[" + GameControls.Label(this, GameAction.Accept) + "]", "{accept}")
            .Replace("[" + GameControls.Label(this, GameAction.Decline) + "]", "{decline}") : "", notice = noticeText != null ? noticeText.text : "" };
    public void ApplyCoopState(CoopTacticsState state)
    {
        if (state == null) return;
        Resonance = state.resonance;
        if (state.armed)
        {
            if (echoMarker != null && (echoMarker.transform.position-state.position).sqrMagnitude>.01f)
            { Destroy(echoMarker.gameObject); echoMarker=null; }
            if (echoMarker == null) echoMarker = TacticalMarker.Create("隊伍預設回聲",state.position,state.direction,GameUITheme.Teal);
            echoMarker.SetText($"預設回聲 {state.remaining:F1}s\n啟動距離 {state.range:0}m");
        }
        else if (echoMarker != null) { Destroy(echoMarker.gameObject); echoMarker = null; }
        if (echoText != null) echoText.text = (state.echo ?? "").Replace("{echo}", "[" + GameControls.Label(this, GameAction.Echo) + "]");
        if (pactText != null) pactText.text = (state.pact ?? "").Replace("{accept}", "[" + GameControls.Label(this, GameAction.Accept) + "]")
            .Replace("{decline}", "[" + GameControls.Label(this, GameAction.Decline) + "]");
        if (noticeText != null) noticeText.text = state.notice;
        if (chargeFill != null) chargeFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(Resonance / 100), 1);
    }
}
