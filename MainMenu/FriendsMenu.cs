using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class FriendsMenu : MonoBehaviour
{
    static FriendsMenu activeMenu;
    public static bool IsOpen => activeMenu != null && activeMenu.isActiveAndEnabled;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { activeMenu = null; }
    CloudFriendsService friends;
    CloudRoomInviteService rooms;
    Action back;
    Text identity, status;
    InputField nickname, code;
    Button copyButton, renameButton, requestButton, refreshButton;
    RectTransform content;
    float nextRefresh;
    bool lastCanInvite, lastCanAccept;
    public static FriendsMenu Create(Transform parent, Action back)
    {
        var root = AccountLoginPanel.Box(parent, "Friends Menu", Vector2.zero, Vector2.zero, GameUITheme.Ink);
        GameUITheme.Stretch(root);
        var menu = root.gameObject.AddComponent<FriendsMenu>(); menu.back = back; menu.Build(); root.gameObject.SetActive(false); return menu;
    }
    public void Open()
    {
        if (!CloudAccountService.EnsureInstance().IsSignedIn) return;
        activeMenu = this; gameObject.SetActive(true); Refresh(); friends.Refresh(); rooms.Refresh(); nextRefresh = Time.unscaledTime + 10;
    }
    void Build()
    {
        friends = CloudFriendsService.Ensure(); friends.Changed += Refresh;
        rooms = CloudRoomInviteService.Ensure(); rooms.Changed += Refresh;
        var card = AccountLoginPanel.Box(transform, "Friends Card", Vector2.zero, new Vector2(1280, 920), GameUITheme.Panel);
        GameUITheme.Frame(card);
        AccountLoginPanel.Label(card, "冒險好友", 36, new Vector2(0, 398), new Vector2(1180, 64)).color = GameUITheme.Gold;
        identity = AccountLoginPanel.Label(card, "", 23, new Vector2(0, 332), new Vector2(1150, 42));
        identity.supportRichText = false;
        copyButton = MultiplayerMenu.Button(card, "複製我的好友代碼", new Vector2(330, 286), new Vector2(390, 40), () => GUIUtility.systemCopyBuffer = friends.State.code ?? "");
        nickname = Field(card, "暱稱", 230); nickname.characterLimit = 24;
        renameButton = MultiplayerMenu.Button(card, "儲存暱稱", new Vector2(330, 230), new Vector2(390, 46), () => friends.Rename(nickname.text));
        code = Field(card, "好友代碼（12 碼）", 140); code.characterLimit = 12;
        requestButton = MultiplayerMenu.Button(card, "送出好友邀請", new Vector2(330, 140), new Vector2(390, 46), () => friends.Request(code.text));
        var viewport = AccountLoginPanel.Box(card, "Friends Scroll", new Vector2(0, -96), new Vector2(1140, 376), GameUITheme.Recess);
        viewport.gameObject.AddComponent<RectMask2D>();
        var node = new GameObject("Rows", typeof(RectTransform)); content = node.GetComponent<RectTransform>(); content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
        status = AccountLoginPanel.Label(card, "", 21, new Vector2(0, -333), new Vector2(1160, 72)); status.color = GameUITheme.Gold;
        refreshButton = MultiplayerMenu.Button(card, "重新整理", new Vector2(-240, -405), new Vector2(390, 52), () => { friends.Refresh(); rooms.Refresh(); });
        MultiplayerMenu.Button(card, "返回", new Vector2(240, -405), new Vector2(390, 52), Close);
    }
    void Refresh()
    {
        if (identity == null) return;
        var data = friends.State;
        identity.text = string.IsNullOrEmpty(data.code) ? "尚未取得好友代碼" : data.name + "　｜我的好友代碼：" + data.code;
        if (!nickname.isFocused && !string.IsNullOrEmpty(data.name)) nickname.text = data.name;
        status.text = friends.Message + "\n" + rooms.Message;
        lastCanInvite = CloudRoomInviteService.CanInvite; lastCanAccept = CloudRoomInviteService.CanAccept;
        bool ready = friends.HasLoaded && !friends.ServiceUnavailable && !friends.IsBusy;
        copyButton.interactable = renameButton.interactable = requestButton.interactable = ready;
        nickname.interactable = code.interactable = ready;
        refreshButton.interactable = !friends.IsBusy;
        foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        int row = 0;
        foreach (var invite in rooms.Invites)
        {
            var person = new CloudFriend { name = invite.name, code = invite.room_code };
            Row(person, "房間邀請", ref row, "接受進房", () => rooms.Accept(invite.id), "拒絕", () => rooms.Decline(invite.id),
                !rooms.IsBusy && !rooms.IsJoining && lastCanAccept, !rooms.IsBusy && !rooms.IsJoining);
        }
        foreach (var person in data.incoming) Row(person, "邀請", ref row, "接受", () => friends.Accept(person.id), "拒絕", () => friends.Remove(person.id));
        foreach (var person in data.friends)
            if (CoopSession.IsMultiplayer)
                Row(person, "好友", ref row, "邀請進房", () => rooms.Invite(person.id), "移除", () => friends.Remove(person.id),
                    lastCanInvite && !rooms.IsBusy && !rooms.IsJoining && !friends.IsBusy && !friends.ServiceUnavailable);
            else Row(person, "好友", ref row, "複製代碼", () => GUIUtility.systemCopyBuffer = person.code, "移除", () => friends.Remove(person.id));
        foreach (var person in data.outgoing) Row(person, "等待確認", ref row, "", null, "取消", () => friends.Remove(person.id));
        if (row == 0)
        {
            string message = friends.IsBusy ? "正在讀取好友資料……" : friends.ServiceUnavailable || !friends.HasLoaded ?
                "尚未載入好友列表。請查看下方訊息，並按重新整理重試。" : "尚無好友或邀請。分享你的好友代碼，開始一起冒險。";
            var empty = AccountLoginPanel.Label(content, message, 21, new Vector2(0, -55), new Vector2(1090, 70));
            empty.rectTransform.anchorMin = empty.rectTransform.anchorMax = new Vector2(.5f, 1);
        }
        content.sizeDelta = new Vector2(0, Mathf.Max(376, row * 76));
    }
    void Row(CloudFriend person, string type, ref int row, string primary, UnityEngine.Events.UnityAction action, string secondary, UnityEngine.Events.UnityAction other, bool? primaryReady = null, bool? secondaryReady = null)
    {
        var rect = AccountLoginPanel.Box(content, "Friend Row", new Vector2(0, -38 - row++ * 76), new Vector2(1100, 68), GameUITheme.Raised);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        var label = AccountLoginPanel.Label(rect, type + "　" + person.name + "　" + person.code, 21, new Vector2(-185, 0), new Vector2(680, 64)); label.supportRichText = false;
        bool ready = !friends.IsBusy && !friends.ServiceUnavailable;
        if (action != null) MultiplayerMenu.Button(rect, primary, new Vector2(295, 0), new Vector2(170, 48), action).interactable = primaryReady ?? ready;
        MultiplayerMenu.Button(rect, secondary, new Vector2(475, 0), new Vector2(150, 48), other).interactable = secondaryReady ?? ready;
    }
    static InputField Field(Transform parent, string label, float y)
    {
        AccountLoginPanel.Label(parent, label, 18, new Vector2(-240, y + 34), new Vector2(540, 30));
        var rect = AccountLoginPanel.Box(parent, label, new Vector2(-240, y), new Vector2(540, 46), GameUITheme.Recess);
        var field = rect.gameObject.AddComponent<InputField>();
        var text = AccountLoginPanel.Label(rect, "", 23, Vector2.zero, new Vector2(508, 42)); text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false;
        field.textComponent = text; field.lineType = InputField.LineType.SingleLine; return field;
    }
    void Update()
    {
        if (!CloudAccountService.EnsureInstance().IsSignedIn) { gameObject.SetActive(false); back(); return; }
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 10; if (!friends.ServiceUnavailable) friends.Refresh(); }
        if (lastCanInvite != CloudRoomInviteService.CanInvite || lastCanAccept != CloudRoomInviteService.CanAccept) Refresh();
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }
    void Close() { if (!rooms.IsJoining) back(); }
    void OnDestroy() { if (friends != null) friends.Changed -= Refresh; if (rooms != null) rooms.Changed -= Refresh; }
}
