using System;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public sealed class WeaponBuildGuideUI : MonoBehaviour
{
    Action back;
    ScrollRect scroll;
    public bool IsOpen => gameObject.activeInHierarchy;
    public static WeaponBuildGuideUI Create(Transform parent, Action onBack)
    {
        var root = AccountLoginPanel.Box(parent, "Weapon Build Guide", Vector2.zero, new Vector2(1080, 850), GameUITheme.Panel);
        GameUITheme.Frame(root);
        var guide = root.gameObject.AddComponent<WeaponBuildGuideUI>(); guide.back = onBack;
        GameUITheme.Label(root, "角色武器、連招與回聲", 38, new Vector2(0, 375), new Vector2(980, 60), TextAnchor.MiddleCenter).color = GameUITheme.Gold;
        GameUITheme.Label(root, "專武輸出 · 副武器控場 · 第三格接技", 20,
            new Vector2(0, 322), new Vector2(1000, 44), TextAnchor.MiddleCenter).color = GameUITheme.Muted;
        var viewport = AccountLoginPanel.Box(root, "Build List Viewport", new Vector2(0, 5), new Vector2(980, 580), GameUITheme.Recess);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = AccountLoginPanel.Box(viewport, "Build List Content", Vector2.zero, Vector2.zero, Color.clear);
        content.GetComponent<Image>().raycastTarget = false;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        var text = GameUITheme.Label(content, GetText(), 23, new Vector2(0, -16), new Vector2(920, 100), TextAnchor.UpperLeft);
        text.name = "Build Descriptions"; text.lineSpacing = 1.15f; text.raycastTarget = false;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(.5f, 1); text.rectTransform.pivot = new Vector2(.5f, 1);
        text.rectTransform.sizeDelta = new Vector2(920, text.preferredHeight);
        content.sizeDelta = new Vector2(0, text.preferredHeight + 32);
        guide.scroll = viewport.gameObject.AddComponent<ScrollRect>();
        guide.scroll.viewport = viewport; guide.scroll.content = content; guide.scroll.horizontal = false;
        guide.scroll.movementType = ScrollRect.MovementType.Clamped; guide.scroll.scrollSensitivity = 48;
        var buttonRoot = AccountLoginPanel.Box(root, "Back", new Vector2(0, -371), new Vector2(330, 58), GameUITheme.Raised);
        var button = buttonRoot.gameObject.AddComponent<Button>(); button.targetGraphic = buttonRoot.GetComponent<Image>();
        GameUITheme.Label(buttonRoot, "返回", 24, Vector2.zero, new Vector2(310, 52), TextAnchor.MiddleCenter);
        GameUITheme.StyleButton(button); button.onClick.AddListener(guide.Back);
        GameUITheme.Label(root, "滑鼠滾輪／方向鍵／手把上下捲動 · Esc 返回", 18,
            new Vector2(0, -318), new Vector2(980, 36), TextAnchor.MiddleCenter).color = GameUITheme.Muted;
        root.gameObject.SetActive(false); return guide;
    }
    public void Open()
    {
        gameObject.SetActive(true); transform.SetAsLastSibling(); scroll.verticalNormalizedPosition = 1;
        GameAudio.UI(GameSound.Open);
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
    }
    public void Hide() => gameObject.SetActive(false);
    public void Back() { Hide(); GameAudio.UI(GameSound.Close); back?.Invoke(); }
    void Update()
    {
        if (!CoopSession.LocalUI(this) || GameSceneFlow.IsLoading || GameControls.Blocked) return;
        if (GameControls.EscapeDown)
        { GameSettingsMenu.ConsumeEscape(); Back(); return; }
        float direction = 0;
        var keys = UnityEngine.InputSystem.Keyboard.current;
        if (keys != null) direction = (keys.upArrowKey.isPressed ? 1 : 0) - (keys.downArrowKey.isPressed ? 1 : 0);
        float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
        if (overflow > 0) scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + direction * 480 * Time.unscaledDeltaTime / overflow);
    }
    public static string GetText()
    {
        string text = "預設回聲：先遠程攻擊，再按回聲鍵留下最近 3 秒射擊；\n" +
            "18 秒內移動或滑行繞背，再按回聲鍵啟動。每人限一個，傷害 45%。\n" +
            "碎冰連擊：霜華法杖冰結 → 震地戰錘碎冰 → 星矢長弓收尾。\n" +
            "回聲引爆：星矢長弓標記 → 疾射連弩命中三次 → 回聲引爆。\n" +
            "焚陣守線：銅芯魔導器聚怪 → 燼火法杖燃燒 → 守衛劍盾建立隊伍盾牆。\n" +
            "副武器保留控場功能；非專武的直接傷害仍為 10%。\n" +
            "第二級先選貫穿、彈射或分裂路線，之後每五級能力升級。\n\n" +
            "NPC 隨機掉落十種角色武器，以及頭盔、盔甲、褲子、鞋子。\n" +
            "等級 1–100，等級與品質越高，傷害或護甲加成越高。\n" +
            "背包選取武器可裝備至快捷欄 1–3；選取防具可裝備對應部位。\n" +
            "點擊防具欄可卸下；替換的防具留在背包。裝備不會恢復護甲。\n\n";
        foreach (var character in CharacterRoster.All)
        {
            var type = CharacterWeapons.ForCharacter(character.Id);
            text += character.Name + "｜" + CharacterWeapons.Name(type) + "\n" +
                (CharacterWeapons.IsMelee(type) ? $"近戰掃擊，範圍 {CharacterWeapons.Reach(type):0.0} 公尺。" :
                    type == WeaponType.FireStaff ? "發射火球，命中產生範圍爆炸。" : "沿準星發射箭矢或魔法投射物。") + "\n" + WeaponBuildSystem.UtilityHint(type) + "\n\n";
        }
        return text;
    }
}
