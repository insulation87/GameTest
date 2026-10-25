using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable] public sealed class CloudRoomInvite
{
    public string id, name, room_code;
}
[Serializable] public sealed class CloudRoomInviteState
{
    public bool ok;
    public CloudRoomInvite[] invites = new CloudRoomInvite[0];
    public string room_code, room_password;
}

// Room credentials are fetched only after the invited friend explicitly accepts.
public sealed class CloudRoomInviteService : MonoBehaviour
{
    public static CloudRoomInviteService Instance { get; private set; }
    public static CloudRoomInviteService Ensure()
    {
        if (Instance == null) new GameObject("Friend Room Invitations").AddComponent<CloudRoomInviteService>();
        return Instance;
    }
    public CloudRoomInvite[] Invites { get; private set; } = new CloudRoomInvite[0];
    public string Message { get; private set; } = "房主可在網際網路營地邀請好友；收到邀請後按接受進房。";
    public bool IsBusy { get; private set; }
    public bool IsJoining { get; private set; }
    public bool ServiceUnavailable { get; private set; }
    public event Action Changed;
    public static bool CanInvite => CoopSession.IsInternet && CoopSession.Mode == CoopMode.Host &&
        CoopSession.InCamp && CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading &&
        CoopSession.Instance.PlayerCount < 4 && ValidCode(CoopSession.RoomCode);
    public static bool CanAccept => !CoopSession.IsMultiplayer && !GameSceneFlow.IsLoading &&
        SceneManager.GetActiveScene().name == GameSceneFlow.MenuScene;
    CloudAccountService account;
    ICloudFriendsTransport transport = new CloudFriendsTransport(true);
    string owner, publishedCode;
    float nextPoll, nextHeartbeat;
    [Serializable] sealed class Command { public string operation, target, room_code, room_password; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Instance = null; }
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        account = CloudAccountService.EnsureInstance(); owner = account.UserId; account.Changed += AccountChanged;
    }
    internal void Configure(ICloudFriendsTransport replacement) { transport = replacement ?? new CloudFriendsTransport(true); }
    void AccountChanged()
    {
        if (owner == account.UserId && account.IsSignedIn) return;
        StopAllCoroutines(); owner = account.UserId; publishedCode = null;
        IsBusy = IsJoining = ServiceUnavailable = false; Invites = new CloudRoomInvite[0]; nextPoll = 0;
        Message = "登入帳號後可接收好友房間邀請。"; Changed?.Invoke();
    }
    public bool Refresh() => Run(new Command { operation = "list" });
    public bool Invite(string friendId)
    {
        if (!CanInvite) return Inform("請先建立網際網路房間，並在未滿員的旅人營地邀請好友。");
        if (!Guid.TryParse(friendId, out _)) return false;
        return Run(new Command { operation = "invite", target = friendId, room_code = CoopSession.RoomCode,
            room_password = CoopSession.Instance.RoomInvitePassword });
    }
    public bool Accept(string id)
    {
        if (!CanAccept) return Inform("請先返回主選單，再接受房間邀請。");
        if (!Guid.TryParse(id, out _)) return false;
        return Run(new Command { operation = "accept", target = id });
    }
    public bool Decline(string id) => Guid.TryParse(id, out _) && Run(new Command { operation = "decline", target = id });
    bool Inform(string message) { Message = message; Changed?.Invoke(); return false; }
    bool Run(Command command)
    {
        if (!account.IsSignedIn || account.IsBusy || IsBusy || IsJoining) return false;
        IsBusy = true;
        if (command.operation == "invite" || command.operation == "accept" || command.operation == "decline")
            Message = "正在處理房間邀請……";
        Changed?.Invoke(); StartCoroutine(Send(command, owner)); return true;
    }
    IEnumerator Send(Command command, string identity)
    {
        CloudAuthResponse response = null;
        yield return transport.Send(account.Configuration, account.AccessToken, JsonUtility.ToJson(command), r => response = r);
        if (!account.IsSignedIn || owner != identity) yield break;
        IsBusy = false; nextPoll = Time.unscaledTime + 8;
        CloudRoomInviteState state = null;
        if (response != null && response.Success)
            try { state = JsonUtility.FromJson<CloudRoomInviteState>(response.body); } catch (ArgumentException) { }
        if (state == null || !state.ok)
        {
            if (response != null && response.status == 404)
            { ServiceUnavailable = true; Message = "房間邀請服務尚未啟用，請聯絡管理員更新好友後台，再按重新整理。"; }
            else if (response == null || response.networkError) Message = "房間邀請服務連線失敗，請按重新整理重試。";
            else if (response.status == 401 || response.status == 403) Message = "請重新登入後再使用房間邀請。";
            else Message = "邀請已過期、房間已關閉，或對方已不是好友。請重新整理或請房主再次邀請。";
            // A failed heartbeat may mean its room has been removed; keep retrying until local room closes.
            nextHeartbeat = Time.unscaledTime + 15;
            Changed?.Invoke(); yield break;
        }
        ServiceUnavailable = false; Invites = state.invites ?? new CloudRoomInvite[0];
        if (command.operation == "invite")
        { publishedCode = command.room_code; nextHeartbeat = Time.unscaledTime + 15; Message = "房間邀請已送出，好友接受後即可直接進入營地（5 分鐘內有效）。"; }
        else if (command.operation == "close") publishedCode = null;
        else if (command.operation == "decline") Message = "已拒絕房間邀請。";
        if (command.operation == "accept")
        {
            if (!CanAccept || !ValidCode(state.room_code)) { Inform("目前無法接受此邀請，請返回主選單重新整理。 "); yield break; }
            IsJoining = true; Message = "正在加入好友的旅人營地……"; Changed?.Invoke();
            JoinRoom(state.room_code, state.room_password ?? "", () =>
            { IsJoining = false; Message = "已加入好友房間。"; Changed?.Invoke(); }, error =>
            { IsJoining = false; Message = error + " 請重新整理，或請房主再次邀請。"; Changed?.Invoke(); });
        }
        Changed?.Invoke();
    }
    void JoinRoom(string code, string password, Action success, Action<string> failure)
    {
#if UNITY_EDITOR
        if (reviewRelay != null)
        { CoopSession.ReviewInternet(reviewRelay, "Map1", CharacterRoster.All[0].Id, code, password, success, failure); return; }
#endif
        CoopSession.JoinInternet("Map1", CharacterRoster.All[0].Id, code, password, success, failure);
    }
#if UNITY_EDITOR
    Uri reviewRelay;
    internal void ConfigureReviewRelay(Uri endpoint)
    {
        if (endpoint.Scheme != "ws" || !System.Net.IPAddress.TryParse(endpoint.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip))
            throw new ArgumentException("Loopback review only");
        reviewRelay = endpoint;
    }
#endif
    static bool ValidCode(string code)
    {
        if (code == null || code.Length != 10) return false;
        foreach (char c in code) if ("23456789ABCDEFGHJKLMNPQRSTUVWXYZ".IndexOf(c) < 0) return false;
        return true;
    }
    void Update()
    {
        if (!account.IsSignedIn || account.IsBusy || IsBusy || IsJoining) return;
        if (publishedCode != null && (!CanInvite || publishedCode != CoopSession.RoomCode))
        { if (Time.unscaledTime >= nextHeartbeat) { nextHeartbeat = Time.unscaledTime + 15; Run(new Command { operation = "close", room_code = publishedCode }); } return; }
        if (publishedCode != null && Time.unscaledTime >= nextHeartbeat && !ServiceUnavailable)
        { nextHeartbeat = Time.unscaledTime + 15; Run(new Command { operation = "heartbeat", room_code = publishedCode }); return; }
        if (!ServiceUnavailable && CanAccept && Time.unscaledTime >= nextPoll) Refresh();
    }
    void OnDestroy() { if (account != null) account.Changed -= AccountChanged; if (Instance == this) Instance = null; }
}
