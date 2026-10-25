using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class MultiplayerMenu : MonoBehaviour
{
    string scene;
    Func<bool> canStart;
    Action cancel;
    InputField address, port, password;
    Text status, firstLabel;
    Button mapButton;
    Text connectionNote, portLabel;
    Button internetTab, lanTab;
    bool internet;
    Button[] launchButtons;
    int first;
    bool joining;
    FriendsMenu friendsMenu;
    Button friendsButton;
    public static MultiplayerMenu Create(Transform parent, string scene, Func<bool> canStart, Action cancel)
    {
        var root = AccountLoginPanel.Box(parent, "Multiplayer Modes", Vector2.zero, Vector2.zero, GameUITheme.Ink);
        GameUITheme.Stretch(root);
        var menu = root.gameObject.AddComponent<MultiplayerMenu>(); menu.scene = scene; menu.canStart = canStart; menu.cancel = cancel;
        menu.Build(); root.gameObject.SetActive(false); return menu;
    }
    public void Open() { gameObject.SetActive(true); status.text = CoopSession.LastError ?? "選擇合作模式，與朋友一起冒險。"; SetJoining(false); SelectMap(ExpeditionMaps.ForScene(scene).Index); SetNetwork(CloudAccountService.EnsureInstance().IsSignedIn && CoopSession.InternetConfigured); }
    void Build()
    {
        var card = AccountLoginPanel.Box(transform, "Coop Card", Vector2.zero, new Vector2(1500, 920), GameUITheme.Panel);
        GameUITheme.Frame(card);
        AccountLoginPanel.Label(card, "一起探索異世界", 38, new Vector2(0, 395), new Vector2(1400, 74)).color = GameUITheme.Gold;
        AccountLoginPanel.Label(card, "單人進度獨立保留 · 合作房間最多 4 位玩家", 22, new Vector2(0, 337), new Vector2(1400, 44));
        var left = AccountLoginPanel.Box(card, "Your Character", new Vector2(-375, 12), new Vector2(660, 560), GameUITheme.Recess);
        var right = AccountLoginPanel.Box(card, "Direct Connect", new Vector2(365, 12), new Vector2(700, 560), GameUITheme.Recess);
        AccountLoginPanel.Label(left, "先到旅人營地集合", 30, new Vector2(0, 225), new Vector2(620, 48));
        firstLabel = AccountLoginPanel.Label(left, "在營地選擇角色與技能升級", 25, new Vector2(0, 143), new Vector2(590, 80));
        AccountLoginPanel.Label(left, "建立／加入房間後，先進入旅人營地\n每人選擇角色，再免費選一項技能升級\n全員同時走進同一個地圖圓圈\n由房主確認後，一起進入地圖\n每台電腦操作一位玩家 · 最多 4 人\n單人與合作進度分開儲存", 22,
            new Vector2(0, -110), new Vector2(620, 240));
        lanTab = Button(right, "同網路", new Vector2(-166, 225), new Vector2(306, 48), () => SetNetwork(false));
        internetTab = Button(right, "網際網路（需登入）", new Vector2(166, 225), new Vector2(306, 48), () => SetNetwork(true));
        address = Field(right, "房間代碼／房主區域 IP", "127.0.0.1", 142, false); address.characterLimit = 45;
        port = Field(right, "連接埠", "25565", 54, false); port.contentType = InputField.ContentType.IntegerNumber; port.characterLimit = 5;
        foreach (var label in right.GetComponentsInChildren<Text>()) if (label.text == "連接埠") portLabel = label;
        password = Field(right, "房間密碼（可留空）", "", -34, true); password.characterLimit = 64;
        connectionNote = AccountLoginPanel.Label(right, "", 18, new Vector2(0, -128), new Vector2(660, 96));
        var host = Button(right, "建立房間", new Vector2(-170, -216), new Vector2(308, 62), StartHost);
        var join = Button(right, "加入房間", new Vector2(170, -216), new Vector2(308, 62), Join);
        launchButtons = new[] { host, join };
        status = AccountLoginPanel.Label(card, "", 21, new Vector2(0, -321), new Vector2(1390, 76)); status.color = GameUITheme.Gold;
        Button(card, "返回單人主選單／取消連線", new Vector2(0, -403), new Vector2(620, 62), Back);
        RefreshCharacters();
        CloudRoomInviteService.Ensure();
        friendsButton = Button(card, "好友與房間邀請", new Vector2(-375, -233), new Vector2(570, 54), () =>
        {
            if (joining || !CloudAccountService.EnsureInstance().IsSignedIn) return;
            if (friendsMenu == null) friendsMenu = FriendsMenu.Create(transform, () => friendsMenu.gameObject.SetActive(false));
            friendsMenu.Open();
        });
    }
    void SetNetwork(bool value)
    {
        if (joining) return;
        if (value && !CloudAccountService.EnsureInstance().IsSignedIn) { status.text = "登入帳號後才能使用網際網路房間。"; return; }
        internet = value; address.text = value ? "" : "127.0.0.1";
        port.gameObject.SetActive(!value); if (portLabel != null) portLabel.gameObject.SetActive(!value);
        connectionNote.text = value ? "建立後可在營地直接邀請好友，也可分享房間代碼。\n收到好友邀請時，按「好友與房間邀請」接受進房。\n可使用不同網路，不需輸入 IP。" :
            "所有玩家需連到同一個區域網路。\n加入時輸入房主畫面顯示的區域 IP。\n遊客可使用此模式；跨網路請登入並選擇網際網路。";
        GameUITheme.StyleButton(lanTab, !value); GameUITheme.StyleButton(internetTab, value);
        internetTab.interactable = CloudAccountService.EnsureInstance().IsSignedIn;
    }
    void RefreshCharacters()
    {
        firstLabel.text = "在營地選擇角色與技能升級"; firstLabel.color = GameUITheme.Teal;
    }
    public void SelectMap(int index)
    {
        if (joining) return;
        var map=ExpeditionMaps.All[Mathf.Clamp(index,0,ExpeditionMaps.All.Length-1)];scene=map.Scene;
        if (mapButton != null) mapButton.GetComponentInChildren<Text>().text="合作地圖："+map.Name+" · 3 關  ›";
    }
    bool Validate(out int number)
    {
        number = 0;
        if (joining || !canStart()) return false;
        if (!Application.CanStreamedLevelBeLoaded(GameSceneFlow.TradingHubScene)) { status.text = "旅人營地尚未加入 Build Settings"; return false; }
        if (!Application.CanStreamedLevelBeLoaded(scene)) { status.text = "地圖尚未加入 Build Settings"; return false; }
        if (!internet && (!int.TryParse(port.text, out number) || number < 1 || number > 65535))
        { status.text = "連接埠必須介於 1～65535"; return false; }
        return true;
    }
    void StartHost()
    {
        if (!Validate(out int number)) return;
        if (internet)
        {
            SetJoining(true); status.text = "正在建立網際網路房間……";
            CoopSession.StartInternetHost(scene, CharacterRoster.All[first].Id, password.text,
                () => { if (this != null) status.text = "房間已建立，正在載入旅人營地……"; }, Failure);
            return;
        }
        bool success = CoopSession.StartHost(scene, CharacterRoster.All[first].Id, number, password.text, out string error);
        if (!success) status.text = error;
        if (success) { SetJoining(true); GameAudio.UI(GameSound.StartGame); }
    }
    void Join()
    {
        if (!Validate(out int number)) return;
        if (internet)
        {
            SetJoining(true); status.text = "正在加入網際網路房間……";
            CoopSession.JoinInternet(scene, CharacterRoster.All[first].Id, address.text, password.text,
                () => { if (this != null) status.text = "連線成功，正在載入旅人營地……"; }, Failure);
            return;
        }
        string host = address.text.Trim();
        if (host.Length == 0 || host.IndexOfAny(new[] { '/', '\\', ' ', '\t', '\n' }) >= 0)
        { status.text = "請輸入 IP 或主機名稱，不需要 http:// 或連接埠"; return; }
        SetJoining(true); status.text = "正在連線，等待旅人營地就緒……";
        CoopSession.Join(scene, CharacterRoster.All[first].Id, host, number, password.text,
            () => { if (this != null) status.text = "連線成功，正在載入旅人營地……"; },
            error => { if (this == null) return; SetJoining(false); status.text = error; });
    }
    void SetJoining(bool value)
    { joining = value; foreach (var button in launchButtons) button.interactable = !value;
        if (mapButton != null) mapButton.interactable = !value;
        lanTab.interactable = !value; internetTab.interactable = !value && CloudAccountService.EnsureInstance().IsSignedIn; }
    void Failure(string error) { if (this == null) return; SetJoining(false); status.text = error; }
    void Back()
    { if (GameSceneFlow.IsLoading) return; CoopSession.Leave(); SetJoining(false); cancel(); }
    void Update()
    {
        if (friendsButton != null)
        {
            friendsButton.interactable = !joining && CloudAccountService.EnsureInstance().IsSignedIn;
            int count = CloudRoomInviteService.Ensure().Invites.Length;
            friendsButton.GetComponentInChildren<Text>().text = "好友與房間邀請" + (count > 0 ? "（" + count + "）" : "");
        }
        if (friendsMenu != null && friendsMenu.gameObject.activeSelf) return;
        if (!GameSceneFlow.IsLoading && Input.GetKeyDown(KeyCode.Escape)) Back();
    }
    public static Button Button(Transform parent, string text, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
    {
        var rect = AccountLoginPanel.Box(parent, text, position, size, GameUITheme.Raised);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        AccountLoginPanel.Label(rect, text, 23, Vector2.zero, size - new Vector2(20, 6));
        GameUITheme.StyleButton(button); button.onClick.AddListener(action); rect.gameObject.AddComponent<GameUIAudio>(); return button;
    }
    public static InputField Field(Transform parent, string label, string value, float y, bool secret)
    {
        AccountLoginPanel.Label(parent, label, 18, new Vector2(0, y + 28), new Vector2(640, 30));
        var box = AccountLoginPanel.Box(parent, label + " Input", new Vector2(0, y - 8), new Vector2(620, 46), GameUITheme.Panel);
        var field = box.gameObject.AddComponent<InputField>();
        var text = AccountLoginPanel.Label(box, value, 23, Vector2.zero, new Vector2(594, 42)); text.alignment = TextAnchor.MiddleLeft;
        field.textComponent = text; field.text = value; field.contentType = secret ? InputField.ContentType.Password : InputField.ContentType.Standard;
        field.lineType = InputField.LineType.SingleLine; return field;
    }
}
