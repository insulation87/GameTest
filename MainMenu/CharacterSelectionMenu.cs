using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public sealed class CharacterSelectionMenu : MonoBehaviour
{
    Button[] cards;
    Text detailTitle, detail, hint;
    Action<string> confirm;
    Action cancel;
    int selected;
    public string SelectedId => CharacterRoster.All[selected].Id;
    public static CharacterSelectionMenu Create(Transform parent, Action<string> confirm, Action cancel)
    {
        var root = AccountLoginPanel.Box(parent,"Character Selection",Vector2.zero,Vector2.zero,GameUITheme.Ink);
        GameUITheme.Stretch(root);
        var menu = root.gameObject.AddComponent<CharacterSelectionMenu>();
        menu.confirm = confirm; menu.cancel = cancel; menu.Build(); root.gameObject.SetActive(false); return menu;
    }
    void Build()
    {
        var panel = AccountLoginPanel.Box(transform,"Roster",Vector2.zero,new Vector2(1720,940),GameUITheme.Panel);
        GameUITheme.Frame(panel);
        AccountLoginPanel.Label(panel,"選擇你的冒險者",36,new Vector2(0,425),new Vector2(1600,76)).color = GameUITheme.Gold;
        AccountLoginPanel.Label(panel,"10 位角色 · 專屬武器與技能 · 征服地城",22,new Vector2(0,369),new Vector2(1600,40));
        cards = new Button[CharacterRoster.All.Length];
        for (int i = 0; i < cards.Length; i++)
        {
            int index = i; var character = CharacterRoster.All[i];
            var card = AccountLoginPanel.Box(panel,character.Id,new Vector2((i % 5 - 2) * 328,215 - i / 5 * 280),new Vector2(304,250),GameUITheme.Raised);
            var button = card.gameObject.AddComponent<Button>(); button.targetGraphic = card.GetComponent<Image>();
            AccountLoginPanel.Label(card,character.Name,25,new Vector2(0,95),new Vector2(284,40));
            var portrait = new GameObject("Portrait",typeof(RectTransform),typeof(CanvasRenderer),typeof(CharacterPortrait));
            portrait.transform.SetParent(card,false); var rect = portrait.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0,23); rect.sizeDelta = new Vector2(98,112);
            var drawing = portrait.GetComponent<CharacterPortrait>(); drawing.Index = index; drawing.raycastTarget = false;
            AccountLoginPanel.Label(card,CharacterWeapons.Name(CharacterWeapons.ForCharacter(character.Id)),21,new Vector2(0,-62),new Vector2(284,34));
            AccountLoginPanel.Label(card,character.Role + " · " + character.Cooldown + " 秒",17,new Vector2(0,-100),new Vector2(284,28));
            GameUITheme.StyleButton(button); button.onClick.AddListener(() => Select(index));
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            card.gameObject.AddComponent<GameUIAudio>(); cards[i] = button;
        }
        var description = AccountLoginPanel.Box(panel,"Skill Description",new Vector2(0,-288),new Vector2(1616,150),GameUITheme.Recess);
        GameUITheme.Frame(description);
        detailTitle = GameUITheme.Label(description,"",27,new Vector2(-465,20),new Vector2(590,42));
        hint = GameUITheme.Label(description,"",18,new Vector2(-465,-30),new Vector2(590,40)); hint.color = GameUITheme.Muted;
        detail = GameUITheme.Label(description,"",23,new Vector2(297,0),new Vector2(875,118));
        AddButton(panel,"返回主選單",new Vector2(-225,-413),false,() => cancel?.Invoke());
        AddButton(panel,"以此角色出發",new Vector2(225,-413),true,() => confirm?.Invoke(SelectedId));
        Select(0);
    }
    static void AddButton(Transform parent,string label,Vector2 position,bool primary,UnityEngine.Events.UnityAction action)
    {
        var rect = AccountLoginPanel.Box(parent,label,position,new Vector2(400,66),GameUITheme.Raised);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        AccountLoginPanel.Label(rect,label,26,Vector2.zero,new Vector2(380,60));
        GameUITheme.StyleButton(button,primary); button.onClick.AddListener(action); rect.gameObject.AddComponent<GameUIAudio>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
    }
    public void Open(bool replacingSave)
    {
        gameObject.SetActive(true);
        hint.text = replacingSave ? "重設本輪裝備 · 保留金幣與商店解鎖" : "按 R 施放 · 方向鍵選擇 · Enter 出發";
        Select(selected);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cards[selected].gameObject);
    }
    public void OpenCamp()
    {
        Open(false);
        hint.text = "確認角色後，選擇一項免費技能升級";
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            var label = button.GetComponentInChildren<Text>();
            if (button.name == "以此角色出發") label.text = "確認角色並選擇升級";
            if (button.name == "返回主選單") label.text = "返回營地";
        }
    }
    public void Select(int index)
    {
        selected = Mathf.Clamp(index,0,cards.Length-1);
        for (int i = 0; i < cards.Length; i++)
            cards[i].GetComponent<Image>().color = i == selected ? new Color(.21f,.31f,.35f) : GameUITheme.Raised;
        var item = CharacterRoster.All[selected];
        detailTitle.text = item.Name + " · " + item.Skill; detailTitle.color = item.Color;
        detail.text = item.Description + "\n冷卻 " + item.Cooldown + " 秒 · 傷害隨攻擊力成長";
    }
    void Update()
    {
        if (GameSceneFlow.IsLoading) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { cancel?.Invoke(); return; }
        int next = selected;
        if (Input.GetKeyDown(KeyCode.LeftArrow)) next = (selected + 9) % 10;
        if (Input.GetKeyDown(KeyCode.RightArrow)) next = (selected + 1) % 10;
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)) next = (selected + 5) % 10;
        if (next != selected)
        { Select(next); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(cards[selected].gameObject); }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) confirm?.Invoke(SelectedId);
    }
}

/// <summary>Native pixel portraits with distinct palettes, headgear and tabard emblems.</summary>
public sealed class CharacterPortrait : MaskableGraphic
{
    public int Index;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var rect = rectTransform.rect;
        var item = CharacterRoster.All[Mathf.Clamp(Index,0,9)];
        float pixel = Mathf.Min(rect.width/14f,rect.height/16f);
        Vector2 start = rect.center - new Vector2(7,8)*pixel;
        Color skin = new Color(.94f,.72f,.52f);
        Color hair = Index % 3 == 0 ? GameUITheme.Gold : Index % 3 == 1 ? new Color(.25f,.18f,.20f) : new Color(.84f,.87f,.93f);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 14; x++)
        {
            Color ink = Color.clear;
            if (y < 6 && x >= 3 && x <= 10) ink = item.Color;
            if (y < 2 && x >= 3 && x <= 10) ink = GameUITheme.Ink;
            if (y == 5 && x >= 5 && x <= 8) ink = skin;
            if (y >= 6 && y <= 12 && x >= 3 && x <= 10) ink = skin;
            if ((y == 9 && (x == 5 || x == 8)) || (y == 7 && x >= 6 && x <= 7)) ink = GameUITheme.Ink;
            if (y >= 12 && y <= 14 && x >= 2 && x <= 11) ink = hair;
            if ((Index == 1 || Index == 6 || Index == 8) && y >= 11 && y <= 14 && x >= 2 && x <= 11) ink = item.Color;
            if ((Index == 4 || Index == 5 || Index == 7) && y >= 7 && y <= 13 && (x == 2 || x == 11)) ink = item.Color;
            if (Index == 5 && y == 15 && x >= 5 && x <= 8) ink = item.Color;
            if (Index == 9 && y == 13 && x >= 10) ink = item.Color;
            if (y >= 2 && y <= 4 && x >= 5 && x <= 8 && (x + y + Index) % 3 == 0) ink = GameUITheme.Ivory;
            if (ink.a == 0) continue;
            int count = vh.currentVertCount; Vector2 p = start + new Vector2(x,y)*pixel;
            vh.AddVert(p,ink,Vector2.zero); vh.AddVert(p+Vector2.up*pixel,ink,Vector2.zero);
            vh.AddVert(p+Vector2.one*pixel,ink,Vector2.zero); vh.AddVert(p+Vector2.right*pixel,ink,Vector2.zero);
            vh.AddTriangle(count,count+1,count+2); vh.AddTriangle(count,count+2,count+3);
        }
    }
}
