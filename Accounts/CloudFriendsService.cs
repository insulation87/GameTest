using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public sealed class CloudFriend
{
    public string id, name, code;
}
[Serializable] public sealed class CloudSocialState
{
    public string code, name;
    public CloudFriend[] friends = new CloudFriend[0], incoming = new CloudFriend[0], outgoing = new CloudFriend[0];
}
public interface ICloudFriendsTransport
{
    IEnumerator Send(CloudAuthConfig config, string token, string json, Action<CloudAuthResponse> complete);
}
public sealed class CloudFriendsTransport : ICloudFriendsTransport
{
    string reviewUrl;
    readonly string rpc = "game_social";
    internal CloudFriendsTransport(bool roomInvites) { rpc = roomInvites ? "game_room_invites" : "game_social"; }
#if UNITY_EDITOR || OVERWORLD_VALIDATION
    internal CloudFriendsTransport(string endpoint, bool roomInvites = false)
    {
        var uri = new Uri(endpoint);
        if (uri.Scheme != "http" || !System.Net.IPAddress.TryParse(uri.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip)) throw new ArgumentException("Loopback review only");
        reviewUrl = endpoint.TrimEnd('/');
        rpc = roomInvites ? "game_room_invites" : "game_social";
    }
#endif
    public CloudFriendsTransport() { }
    public IEnumerator Send(CloudAuthConfig config, string token, string json, Action<CloudAuthResponse> complete)
    {
        if (config == null || !config.IsValid || string.IsNullOrEmpty(token))
        { complete(new CloudAuthResponse { status = 401 }); yield break; }
        using (var request = new UnityWebRequest((reviewUrl ?? config.projectUrl.TrimEnd('/')) + "/rest/v1/rpc/" + rpc, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 15; request.redirectLimit = 0;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", config.publishableKey); request.SetRequestHeader("Authorization", "Bearer " + token);
            yield return request.SendWebRequest();
            complete(new CloudAuthResponse { status = request.responseCode, body = request.downloadHandler.text,
                networkError = request.result == UnityWebRequest.Result.ConnectionError });
        }
    }
}
public sealed class CloudFriendsService : MonoBehaviour
{
    public static CloudFriendsService Instance { get; private set; }
    public static CloudFriendsService Ensure()
    {
        if (Instance == null) new GameObject("Cloud Friends").AddComponent<CloudFriendsService>();
        return Instance;
    }
    public CloudSocialState State { get; private set; } = new CloudSocialState();
    public string Message { get; private set; } = "輸入好友代碼，送出邀請。";
    public bool IsBusy { get; private set; }
    public bool HasLoaded { get; private set; }
    public bool ServiceUnavailable { get; private set; }
    public event Action Changed;
    ICloudFriendsTransport transport = new CloudFriendsTransport();
    CloudAccountService account;
    string owner;
    [Serializable] sealed class Command { public string operation, target, nickname; }
    [Serializable] sealed class ErrorBody { public string code; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Instance = null; }
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        account = CloudAccountService.EnsureInstance(); account.Changed += AccountChanged; owner = account.UserId;
    }
    internal void Configure(ICloudFriendsTransport replacement) { transport = replacement ?? new CloudFriendsTransport(); }
    void AccountChanged()
    {
        if (owner == account.UserId && account.IsSignedIn) return;
        StopAllCoroutines(); IsBusy = HasLoaded = ServiceUnavailable = false; owner = account.UserId; State = new CloudSocialState();
        Message = account.IsSignedIn ? "輸入好友代碼，送出邀請。" : "登入帳號後才能使用好友功能。"; Changed?.Invoke();
    }
    public bool Refresh() => Run("list", null, null);
    public bool Rename(string name) => Run("profile", null, name);
    public bool Request(string code) => Run("request", (code ?? "").Trim().ToUpperInvariant(), null);
    public bool Accept(string id) => Run("accept", id, null);
    public bool Remove(string id) => Run("remove", id, null);
    bool Run(string operation, string target, string name)
    {
        if (!account.IsSignedIn) { Message = "登入帳號後才能使用好友功能。"; Changed?.Invoke(); return false; }
        if (account.IsBusy) { Message = "正在更新登入狀態，請稍後重試。"; Changed?.Invoke(); return false; }
        if (IsBusy) return false;
        if (operation == "request" && (target.Length != 12 || !Hex(target)))
        { Message = "請輸入 12 碼好友代碼。"; Changed?.Invoke(); return false; }
        if ((operation == "accept" || operation == "remove") && !Guid.TryParse(target, out _)) return false;
        if (operation == "profile" && (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 24))
        { Message = "暱稱需為 1～24 個字元。"; Changed?.Invoke(); return false; }
        IsBusy = true; Message = "正在更新好友資料……"; Changed?.Invoke();
        StartCoroutine(Send(new Command { operation = operation, target = target, nickname = name?.Trim() }, account.UserId)); return true;
    }
    static bool Hex(string value) { foreach (char c in value) if (!(c >= '0' && c <= '9') && !(c >= 'A' && c <= 'F')) return false; return true; }
    IEnumerator Send(Command command, string identity)
    {
        CloudAuthResponse response = null;
        yield return transport.Send(account.Configuration, account.AccessToken, JsonUtility.ToJson(command), r => response = r);
        if (!account.IsSignedIn || identity != account.UserId) yield break;
        IsBusy = false;
        CloudSocialState state = null;
        if (response != null && response.Success)
        {
            try { state = JsonUtility.FromJson<CloudSocialState>(response.body); } catch (ArgumentException) { }
        }
        if (state != null && state.code != null && state.code.Length == 12)
        {
            state.friends = state.friends ?? new CloudFriend[0]; state.incoming = state.incoming ?? new CloudFriend[0]; state.outgoing = state.outgoing ?? new CloudFriend[0];
            State = state; HasLoaded = true; ServiceUnavailable = false;
            Message = command.operation == "request" ? "好友邀請已送出，等待對方確認。" : "好友資料已更新。";
        }
        else if (response == null || response.networkError) Message = "無法連線好友服務，請稍後重試。";
        else if (response.status == 404)
        {
            ErrorBody error = null;
            try { error = JsonUtility.FromJson<ErrorBody>(response.body); } catch (ArgumentException) { }
            ServiceUnavailable = true;
            Message = error?.code == "PGRST202" ? "好友服務尚未啟用或正在更新（PGRST202）。請聯絡遊戲管理員，稍後按重新整理。" :
                "好友服務目前無法使用（HTTP 404）。請聯絡遊戲管理員，稍後按重新整理。";
        }
        else if (response.status == 401 || response.status == 403) Message = "請重新登入帳號後再試。";
        else if (response.status == 429) Message = "操作太頻繁，請稍後再試。";
        else Message = "無法完成操作，請確認代碼、邀請狀態或好友數量。";
        Changed?.Invoke();
    }
    void OnDestroy() { if (account != null) account.Changed -= AccountChanged; if (Instance == this) Instance = null; }
}
