using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed partial class TradingHubController
{
    readonly Dictionary<int, CharacterController> campVisitors = new Dictionary<int, CharacterController>();
    readonly Dictionary<int, string> campStyles = new Dictionary<int, string>();
    readonly Dictionary<int, Text> campNames = new Dictionary<int, Text>();
    CharacterSelectionMenu campSelection;
    RectTransform campUpgradePanel;
    Text campRoster, campNotice, campUpgradeTitle;
    Button campConfirm;
    readonly List<Button> campUpgradeButtons = new List<Button>();
    bool campAutoSelection;
    string campUpgradeCharacter;
    string campPendingCharacter;
    public bool CampInitialized => campRoster != null;

    void StartCoopCamp()
    {
        Time.timeScale = GameSceneFlow.IsLoading ? 0 : 1;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        BuildCourtyard(); BuildVisitor(); BuildCoopUI();
        walker.enabled = false;
        for (int i = 0; i < ExpeditionMaps.All.Length; i++)
        {
            var ring = SkillTrace.Ring(PortalPosition(i) + Vector3.up * .13f, CharacterRoster.All[i].Color, CoopSession.CampPortalRadius, 86400);
            ring.name = "集合圓圈 " + ExpeditionMaps.All[i].Scene; ring.transform.SetParent(transform, true);
        }
        initialized = true;
    }
    CharacterController CampVisitor(CoopCampPlayer state)
    {
        if (!campVisitors.TryGetValue(state.id, out var controller))
        {
            bool local = state.id == CoopSession.Instance.CampLocalId;
            if (local) controller = walker;
            else
            {
                var node = new GameObject("Camp Teammate " + state.id); node.transform.SetParent(transform, false);
                controller = node.AddComponent<CharacterController>(); controller.height = 1.8f; controller.radius = .35f;
                controller.center = Vector3.up * .9f; controller.stepOffset = .3f;
                var model = PixelCharacterFactory.Create(node.transform, "Player"); model.GetComponent<Animation>().Play("Idle");
            }
            controller.enabled = false; controller.transform.position = state.position;
            controller.enabled = !CoopSession.IsClient;
            campVisitors[state.id] = controller;
            var sign = Sign("玩家 " + (state.id + 1), Vector3.zero, GameUITheme.Ivory);
            sign.transform.SetParent(controller.transform, false); sign.transform.localPosition = Vector3.up * 2.3f;
            sign.transform.localScale = Vector3.one * .003f;
            campNames[state.id] = sign.GetComponentInChildren<Text>();
        }
        if (!campStyles.TryGetValue(state.id, out var old) || old != state.character)
        {
            var model = controller.GetComponentInChildren<Animation>();
            PixelCharacterFactory.ApplyCharacterStyle(model.gameObject, CharacterRoster.Find(state.character));
            campStyles[state.id] = state.character;
        }
        return controller;
    }
    public Vector3 MoveCampVisitor(CoopCampPlayer state, Vector2 input, float delta)
    {
        var controller = CampVisitor(state);
        if (!controller.enabled) return state.position;
        controller.Move((new Vector3(input.x, 0, input.y) * 5.5f + Vector3.down * 12) * delta);
        if (input.sqrMagnitude > .01f) controller.transform.rotation = Quaternion.LookRotation(new Vector3(input.x, 0, input.y));
        controller.GetComponentInChildren<Animation>().CrossFade(input.sqrMagnitude > .01f ? "Walk" : "Idle", .12f);
        return controller.transform.position;
    }
    void UpdateCoopCamp()
    {
        if (!initialized || GameSceneFlow.IsLoading || !CoopSession.Instance.SceneReady) return;
        var session = CoopSession.Instance; var local = session.LocalCampPlayer;
        if (local == null) return;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        var active = new HashSet<int>(); string roster = "";
        foreach (var player in session.CampPlayers)
        {
            active.Add(player.id); var visitor = CampVisitor(player);
            if (CoopSession.IsClient)
            {
                Vector3 before = visitor.transform.position; visitor.transform.position = player.position;
                Vector3 movement = player.position - before; movement.y = 0;
                if (movement.sqrMagnitude > .0001f) visitor.transform.rotation = Quaternion.LookRotation(movement);
                visitor.GetComponentInChildren<Animation>().CrossFade(movement.sqrMagnitude > .0001f ? "Walk" : "Idle", .12f);
            }
            string name = player.selected ? CharacterRoster.Find(player.character).Name : "尚未選角";
            string stage = !player.selected ? "選角中" : player.upgrade < 0 ? "選擇技能升級" :
                player.portal < 0 ? "等待集合" : ExpeditionMaps.All[player.portal].Name + "圈內";
            roster += (player.id == 0 ? "房主" : "隊友") + " · " + name + " · " + stage + "\n";
            campNames[player.id].text = (player.id == session.CampLocalId ? "你 · " : "") + name;
        }
        foreach (int id in new List<int>(campVisitors.Keys)) if (!active.Contains(id))
        { Destroy(campVisitors[id].gameObject); campVisitors.Remove(id); campNames.Remove(id); campStyles.Remove(id); }
        campRoster.text = roster;
        bool canConfirm = session.CampCanConfirm(out string reason); campNotice.text = reason;
        campConfirm.interactable = !CoopSession.IsClient && canConfirm;
        if (!campAutoSelection) { campAutoSelection = true; OpenCampCharacters(); }
        if (campPendingCharacter != null && local.selected && local.character == campPendingCharacter && local.upgrade < 0) campPendingCharacter = null;
        if (campPendingCharacter == null)
        {
            if (campUpgradePanel.gameObject.activeSelf && local.selected && campUpgradeCharacter != local.character) RefreshCampUpgrade(local.character);
        }
        var keys = Keyboard.current;
        bool dialog = FriendsMenu.IsOpen || campSelection.gameObject.activeSelf || campUpgradePanel.gameObject.activeSelf;
        Vector2 move = Vector2.zero;
        if (!dialog && keys != null)
        {
            move.x = (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1 : 0) - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1 : 0);
            move.y = (keys.wKey.isPressed || keys.upArrowKey.isPressed ? 1 : 0) - (keys.sKey.isPressed || keys.downArrowKey.isPressed ? 1 : 0);
            if (keys.fKey.wasPressedThisFrame && canConfirm && !CoopSession.IsClient) session.ConfirmCamp(true);
        }
        session.MoveInCamp(move);
        if (!FriendsMenu.IsOpen && keys != null && keys.escapeKey.wasPressedThisFrame && !campSelection.gameObject.activeSelf)
            campUpgradePanel.gameObject.SetActive(false);
        foreach (var rune in portalRunes) if (rune != null) rune.Rotate(Vector3.forward, Time.deltaTime * 22, Space.Self);
    }
    public void OpenCampCharacters()
    { campUpgradePanel.gameObject.SetActive(false); campSelection.OpenCamp(); }
    public void OpenCampUpgrade()
    {
        var local = CoopSession.Instance.LocalCampPlayer;
        if (local == null || !local.selected) { OpenCampCharacters(); return; }
        campSelection.gameObject.SetActive(false); RefreshCampUpgrade(local.character); campUpgradePanel.gameObject.SetActive(true);
    }
    void RefreshCampUpgrade(string character)
    {
        campUpgradeCharacter = character;
        var item = CharacterRoster.Find(character);
        campUpgradeTitle.text = item.Name + " · " + item.Skill + "\n選擇一項免費技能升級";
        for (int i = 0; i < 3; i++) campUpgradeButtons[i].GetComponentInChildren<Text>().text = CharacterSkillUpgrades.Label(character, i);
    }
    void BuildCoopUI()
    {
        if (EventSystem.current == null)
        {
            var events = new GameObject("Camp Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false); events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var node = new GameObject("Coop Camp Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        node.transform.SetParent(transform, false); node.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        GameUITheme.FitCanvas(node.GetComponent<CanvasScaler>()); canvasRoot = node.GetComponent<RectTransform>();
        var card = AccountLoginPanel.Box(canvasRoot, "Camp Party", Vector2.zero, new Vector2(630, 330), GameUITheme.Panel);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0, 1); card.anchoredPosition = new Vector2(20, -20);
        GameUITheme.Frame(card); GameUITheme.Label(card, "旅人營地 · 合作準備", 31, new Vector2(0, 125), new Vector2(590, 60)).color = GameUITheme.Gold;
        campRoster = GameUITheme.Label(card, "正在同步隊伍……", 21, new Vector2(0, 5), new Vector2(590, 180));
        var leave = MultiplayerMenu.Button(canvasRoot, "離開房間", Vector2.zero, new Vector2(210, 60), () => {
            CoopSession.Leave(); GameSceneFlow.Load(GameSceneFlow.MenuScene); });
        var leaveRect = leave.GetComponent<RectTransform>(); leaveRect.anchorMin = leaveRect.anchorMax = Vector2.one; leaveRect.anchoredPosition = new Vector2(-130, -55);
        var bar = AccountLoginPanel.Box(canvasRoot, "Camp Departure", Vector2.zero, new Vector2(1640, 190), GameUITheme.Panel);
        bar.anchorMin = bar.anchorMax = new Vector2(.5f, 0); bar.anchoredPosition = new Vector2(0, 145); GameUITheme.Frame(bar);
        campNotice = GameUITheme.Label(bar, "", 24, new Vector2(0, 55), new Vector2(1550, 56), TextAnchor.MiddleCenter);
        GameUITheme.Label(bar, "WASD／方向鍵移動 · 全員選角與升級後，同時進入北側同一個地圖圓圈", 19,
            new Vector2(0, -66), new Vector2(1550, 40), TextAnchor.MiddleCenter).color = GameUITheme.Muted;
        MultiplayerMenu.Button(bar, "選擇／更換角色", new Vector2(-510, -7), new Vector2(450, 58), OpenCampCharacters);
        MultiplayerMenu.Button(bar, "選擇技能升級", new Vector2(0, -7), new Vector2(450, 58), OpenCampUpgrade);
        campConfirm = MultiplayerMenu.Button(bar, CoopSession.IsClient ? "等待房主確認" : "確認出發 [F]", new Vector2(510, -7), new Vector2(450, 58), () => CoopSession.Instance.ConfirmCamp(true));
        BuildCampChoices();
    }
    void BuildCampChoices()
    {
        campSelection = CharacterSelectionMenu.Create(canvasRoot, character => {
            if (!CoopSession.InCamp && !SelectSoloCampCharacter(character)) return;
            campSelection.gameObject.SetActive(false);
            if (CoopSession.InCamp) { campPendingCharacter = character; CoopSession.Instance.ChooseCampCharacter(character); }
            RefreshCampUpgrade(character); campUpgradePanel.gameObject.SetActive(true);
        }, () => { campSelection.gameObject.SetActive(false); soloChoiceClosedFrame = Time.frameCount; });
        campUpgradePanel = AccountLoginPanel.Box(canvasRoot, "Camp Skill Upgrade", Vector2.zero, Vector2.zero, GameUITheme.Ink);
        GameUITheme.Stretch(campUpgradePanel);
        var choices = AccountLoginPanel.Box(campUpgradePanel, "Skill Choices", Vector2.zero, new Vector2(1650, 760), GameUITheme.Panel);
        GameUITheme.Frame(choices);
        campUpgradeTitle = GameUITheme.Label(choices, "", 34, new Vector2(0, 265), new Vector2(1550, 130), TextAnchor.MiddleCenter);
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            campUpgradeButtons.Add(MultiplayerMenu.Button(choices, "", new Vector2((i - 1) * 520, 0), new Vector2(480, 330), () => {
                if (CoopSession.InCamp) CoopSession.Instance.ChooseCampUpgrade(index);
                else if (!SelectSoloCampUpgrade(index)) return;
                campUpgradePanel.gameObject.SetActive(false);
            }));
        }
        GameUITheme.Label(choices, "更換角色會重選升級；重新選擇只會替換開局選項，不會重複疊加。", 21,
            new Vector2(0, -230), new Vector2(1500, 50), TextAnchor.MiddleCenter).color = GameUITheme.Muted;
        MultiplayerMenu.Button(choices, "返回營地", new Vector2(0, -315), new Vector2(400, 62), () => campUpgradePanel.gameObject.SetActive(false));
        campUpgradePanel.gameObject.SetActive(false);
    }
}
