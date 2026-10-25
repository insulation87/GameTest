#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit Play Mode regression in a separate project; no production login or saves.
[InitializeOnLoad]
public static class FriendsServiceReview
{
    const string Pending = "FriendsServiceReview.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static IEnumerator checks;
    static double next, timeout;
    static int assertions;
    static int editorSearchErrors;
    static readonly List<string> errors = new List<string>();
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../friends-play-result.txt"));

    sealed class FakeAuth : ICloudAuthTransport
    {
        public IEnumerator Send(string method, string endpoint, string json, string token, Action<CloudAuthResponse> complete)
        {
            yield return null;
            complete(new CloudAuthResponse { status = 200, body = endpoint == "user" ?
                "{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"email\":\"friends-review@example.com\"}" :
                "{\"access_token\":\"isolated-review\",\"refresh_token\":\"isolated-refresh\",\"expires_in\":3600}" });
        }
    }
    sealed class FakeFriends : ICloudFriendsTransport
    {
        public readonly Queue<CloudAuthResponse> replies = new Queue<CloudAuthResponse>();
        public readonly List<string> commands = new List<string>();
        public void Reply(long status, string body, bool networkError = false) => replies.Enqueue(new CloudAuthResponse { status = status, body = body, networkError = networkError });
        public IEnumerator Send(CloudAuthConfig config, string token, string json, Action<CloudAuthResponse> complete)
        {
            commands.Add(json); yield return null;
            complete(replies.Dequeue());
        }
    }
    sealed class EmptyRoomInvites : ICloudFriendsTransport
    {
        public IEnumerator Send(CloudAuthConfig config, string token, string json, Action<CloudAuthResponse> complete)
        { yield return null; complete(new CloudAuthResponse { status = 200, body = "{\"ok\":true,\"invites\":[]}" }); }
    }
    static FriendsServiceReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        if (!File.Exists(Path.Combine(Application.dataPath, "../friends-isolated-marker.txt")))
            throw new InvalidOperationException("Use a separate marked review project.");
        EditorSceneManager.playModeStartScene = null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); assertions = editorSearchErrors = 0; errors.Clear(); checks = Review();
        timeout = EditorApplication.timeSinceStartup + 60;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        // A Unity Search startup defect is outside the game; report it separately.
        if (stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")) { editorSearchErrors++; return; }
        errors.Add(message);
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("Friends Play Mode review");
            if (!checks.MoveNext()) { Finish(0, "PASS " + assertions + " Play Mode checks"); return; }
            next = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .05f);
        }
        catch (Exception ex) { Finish(1, "FAIL " + ex); }
    }
    static void Finish(int code, string result)
    {
        result += "\nUnity Editor Search startup exceptions (outside gameplay): " + editorSearchErrors;
        File.WriteAllText(Output, result); Debug.Log(result);
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code);
    }
    static void Check(bool pass, string message)
    { if (!pass) throw new InvalidOperationException(message); assertions++; Debug.Log("PASS " + message); }
    static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static Button Button(FriendsMenu menu, string field) => (Button)Get(menu, field);
    static string[] Labels(FriendsMenu menu) => menu.GetComponentsInChildren<Text>().Select(t => t.text).ToArray();
    static string State(string arrays = "\"friends\":[],\"incoming\":[],\"outgoing\":[]") =>
        "{\"code\":\"ABCDEF123456\",\"name\":\"測試冒險者\"," + arrays + "}";
    static IEnumerator Review()
    {
        Check(Application.isPlaying, "Checks execute in Unity Play Mode");
        var account = CloudAccountService.EnsureInstance();
        Call(account, "Configure", new CloudAuthConfig { projectUrl = "https://friends-review.supabase.co", publishableKey = "sb_publishable_isolated_review" }, new FakeAuth());
        Check(account.Login("friends-review@example.com", "review-password"), "Isolated account login starts");
        while (account.IsBusy) yield return .05f;
        Check(account.IsSignedIn, "Account login completes");
        var service = CloudFriendsService.Ensure(); var transport = new FakeFriends(); Call(service, "Configure", transport);
        Call(CloudRoomInviteService.Ensure(), "Configure", new EmptyRoomInvites());
        var canvas = new GameObject("Friends Review Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        GameUITheme.FitCanvas(canvas.GetComponent<CanvasScaler>());
        var menu = FriendsMenu.Create(canvas.transform, () => menuBack());
        void menuBack() { }
        transport.Reply(404, "{\"code\":\"PGRST202\",\"message\":\"private raw server details\"}");
        menu.Open();
        Check(service.IsBusy && !Button(menu, "requestButton").interactable && !Button(menu, "refreshButton").interactable, "Pending list disables repeated actions");
        while (service.IsBusy) yield return .05f;
        yield return .05f;
        Check(service.ServiceUnavailable && !service.HasLoaded && service.Message.Contains("PGRST202"), "Missing RPC is identified explicitly");
        Check(!service.Message.Contains("private raw"), "Raw server response is not displayed");
        Check(!Labels(menu).Any(t => t.StartsWith("尚無好友")) && Labels(menu).Any(t => t.Contains("尚未載入")), "Failed list is not presented as an empty list");
        Check(!Button(menu, "requestButton").interactable && !Button(menu, "copyButton").interactable && Button(menu, "refreshButton").interactable, "Unavailable service offers retry and disables unusable actions");
        int requests = transport.commands.Count;
        menu.GetType().GetField("nextRefresh", Flags).SetValue(menu, -1f); yield return .1f;
        Check(transport.commands.Count == requests, "Missing RPC is not polled automatically");
        transport.Reply(200, State()); Button(menu, "refreshButton").onClick.Invoke();
        while (service.IsBusy) yield return .05f; yield return .05f;
        Check(service.HasLoaded && !service.ServiceUnavailable && service.State.code == "ABCDEF123456", "Retry recovers after backend becomes available");
        Check(Button(menu, "requestButton").interactable && Button(menu, "copyButton").interactable && Labels(menu).Any(t => t.StartsWith("尚無好友")), "Successful empty list enables actions");
        Check(!service.Request("bad") && service.Message.Contains("12 碼"), "Invalid friend codes are rejected locally");
        ((InputField)Get(menu, "code")).text = "fedcba654321";
        transport.Reply(200, State("\"outgoing\":[{\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"name\":\"好友乙\",\"code\":\"FEDCBA654321\"}]"));
        Button(menu, "requestButton").onClick.Invoke(); while (service.IsBusy) yield return .05f; yield return .05f;
        Check(transport.commands.Last().Contains("FEDCBA654321") && service.State.outgoing.Length == 1, "Invite UI sends normalized code and shows pending request");
        Check(Labels(menu).Any(t => t.Contains("等待確認") && t.Contains("好友乙")), "Outgoing invite renders in the list");
        transport.Reply(200, State("\"incoming\":[{\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"name\":\"好友乙\",\"code\":\"FEDCBA654321\"}]"));
        Button(menu, "refreshButton").onClick.Invoke(); while (service.IsBusy) yield return .05f; yield return .05f;
        transport.Reply(200, State("\"friends\":[{\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"name\":\"好友乙\",\"code\":\"FEDCBA654321\"}]"));
        menu.GetComponentsInChildren<Button>().Single(b => b.name == "接受").onClick.Invoke();
        while (service.IsBusy) yield return .05f; yield return .05f;
        Check(service.State.friends.Length == 1 && service.State.incoming.Length == 0 && transport.commands.Last().Contains("accept"), "Accept UI moves incoming invitation to friends");
        transport.Reply(200, State()); menu.GetComponentsInChildren<Button>().Single(b => b.name == "移除").onClick.Invoke();
        while (service.IsBusy) yield return .05f; yield return .05f;
        Check(service.State.friends.Length == 0 && transport.commands.Last().Contains("remove"), "Remove UI refreshes the list");
        transport.Reply(0, null, true); Button(menu, "refreshButton").onClick.Invoke();
        while (service.IsBusy) yield return .05f;
        Check(!service.IsBusy && service.Message.Contains("無法連線") && Button(menu, "refreshButton").interactable, "Network failure leaves retry available");
        transport.Reply(401, "{}"); Button(menu, "refreshButton").onClick.Invoke(); while (service.IsBusy) yield return .05f;
        Check(service.Message.Contains("重新登入"), "Expired login has a distinct error");
        var multiplayer = MultiplayerMenu.Create(canvas.transform, "Map1", () => true, () => { }); multiplayer.Open();
        transport.Reply(200, State());
        var invitationEntry = multiplayer.GetComponentsInChildren<Button>().Single(b => b.name == "好友與房間邀請");
        Check(invitationEntry.interactable, "Signed-in multiplayer menu exposes the friend invitation entry");
        invitationEntry.onClick.Invoke(); while (service.IsBusy) yield return .05f; yield return .05f;
        Check(multiplayer.GetComponentInChildren<FriendsMenu>() != null, "Multiplayer invitation entry opens the shared friends menu");
        Object.Destroy(multiplayer.gameObject); yield return .05f;
        account.Logout(); while (account.IsBusy) yield return .05f; yield return .05f;
        Check(!service.HasLoaded && !service.ServiceUnavailable && service.State.code == null && service.State.friends.Length == 0, "Logout clears friend state and loading flags");
        Check(!menu.gameObject.activeSelf, "Logout closes the friends menu");
        Check(transport.replies.Count == 0 && errors.Count == 0, "All mock responses consumed without runtime errors");
        Object.Destroy(canvas);
    }
}
#endif
