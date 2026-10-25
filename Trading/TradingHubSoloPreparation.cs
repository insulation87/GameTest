using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed partial class TradingHubController
{
    bool soloPreparationOpened;
    int soloChoiceClosedFrame = -1;
    Button soloCharacterButton, soloUpgradeButton;
    Text soloPreparationStatus;

    void BuildSoloPreparationUI()
    {
        var card = AccountLoginPanel.Box(canvasRoot, "Solo Camp Preparation", Vector2.zero, new Vector2(620, 190), GameUITheme.Panel);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(1, 1); card.anchoredPosition = new Vector2(-24, -110);
        GameUITheme.Frame(card);
        soloPreparationStatus = GameUITheme.Label(card, "", 21, new Vector2(0, 45), new Vector2(580, 76), TextAnchor.MiddleCenter);
        soloCharacterButton = MultiplayerMenu.Button(card, "選擇／更換角色", new Vector2(-148, -40), new Vector2(275, 60), OpenSoloCharacters);
        soloUpgradeButton = MultiplayerMenu.Button(card, "選擇技能升級", new Vector2(148, -40), new Vector2(275, 60), OpenSoloUpgrade);
        BuildCampChoices(); RefreshSoloPreparation();
    }
    void RefreshSoloPreparation()
    {
        if (soloPreparationStatus == null) return;
        soloCharacterButton.interactable = stock.IsPreparing;
        soloUpgradeButton.interactable = stock.IsPreparing && stock.HasCampCharacter;
        soloPreparationStatus.text = !stock.IsPreparing ? "接續本輪冒險 · 原角色、技能與進度已保留" :
            !stock.HasCampCharacter ? "先選擇角色，再免費選一項專屬技能升級" :
            !stock.HasCampUpgrade ? "已選擇「" + CharacterRoster.Find(stock.CharacterId).Name + "」\n請完成技能升級，再選擇地圖" :
            "角色與技能升級已完成\n可購物，並前往北側傳送門選擇地圖";
    }
    bool HandleSoloPreparationInput()
    {
        if (!soloPreparationOpened)
        {
            soloPreparationOpened = true;
            if (stock.RequiresCampPreparation) OpenSoloPreparation();
        }
        if (campUpgradePanel != null && campUpgradePanel.gameObject.activeSelf)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            { campUpgradePanel.gameObject.SetActive(false); soloChoiceClosedFrame = Time.frameCount; }
            return true;
        }
        return campSelection != null && campSelection.gameObject.activeSelf || Time.frameCount <= soloChoiceClosedFrame;
    }
    public void OpenSoloPreparation()
    {
        if (!CanOperate || !stock.IsPreparing) return;
        if (!stock.HasCampCharacter) OpenSoloCharacters(); else OpenSoloUpgrade();
    }
    public void OpenSoloCharacters()
    {
        if (!CanOperate || !stock.IsPreparing) return;
        CloseDialog(); campUpgradePanel.gameObject.SetActive(false); campSelection.OpenCamp();
    }
    public void OpenSoloUpgrade()
    {
        if (!CanOperate || !stock.IsPreparing) return;
        if (!stock.HasCampCharacter) { OpenSoloCharacters(); return; }
        CloseDialog(); campSelection.gameObject.SetActive(false);
        RefreshCampUpgrade(stock.CharacterId); campUpgradePanel.gameObject.SetActive(true);
    }
    bool SelectSoloCampCharacter(string character)
    {
        if (!CanOperate || !stock.SelectCampCharacter(character, out _)) return false;
        PixelCharacterFactory.ApplyCharacterStyle(walkingAnimation.gameObject, CharacterRoster.Find(character));
        RefreshWallet(); RefreshSoloPreparation(); return true;
    }
    bool SelectSoloCampUpgrade(int index)
    {
        if (!CanOperate || !stock.SelectCampUpgrade(index, out _)) return false;
        RefreshSoloPreparation(); GameAudio.UI(GameSound.Upgrade); return true;
    }
}
