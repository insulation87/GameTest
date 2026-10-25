#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[InitializeOnLoad]
public static class FriendRoomInviteReview
{
    const string RoleKey = "FriendRoomInviteReview.Role";
    static string role;
    static readonly List<string> results = new List<string>(), errors = new List<string>();
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static double next, timeout;
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static FriendRoomInviteReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void RunHost() => Run("host");
    public static void RunClient() => Run("client");
    public static void RunInput() => Run("input");
    static void Run(string value)
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(Application.dataPath, "../friend-invite-isolated-marker.txt"))) throw new Exception("Isolated batch project required");
        PlayerSettings.companyName = "CodexIsolatedReview"; PlayerSettings.productName = "FriendInviteReview" + value;
        LoginSceneSetup.PlayFromCurrentScene(); CoopModeReview.BuildFixtures();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Trading Hub", typeof(TradingHubController));
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/TradingHub.unity");
        EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene("Assets/TradingHub.unity", true) }).ToArray();
        EditorSceneManager.OpenScene("Assets/MainMenu.unity");
        SessionState.SetString(RoleKey, value); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        role = SessionState.GetString(RoleKey, ""); if (role == "") return; SessionState.SetString(RoleKey, "");
        results.Clear(); errors.Clear(); steps.Clear(); steps.Push(Review()); timeout = EditorApplication.timeSinceStartup + 240;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType kind)
    {
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && !stack.Contains("UnityEditor.Search") && !message.Contains("burst JIT")) errors.Add(message + "\n" + stack);
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException(role + " integration review");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                next = EditorApplication.timeSinceStartup + .05; return;
            }
            Check(errors.Count == 0, "No unexpected runtime errors: " + string.Join(" | ", errors)); Finish(0);
        }
        catch (Exception ex) { results.Add("FAIL " + ex + "\n" + string.Join("\n", errors)); Finish(1); }
    }
    static void Finish(int code)
    {
        File.WriteAllLines(Path.Combine(Output, role + "-result.txt"), results);
        EditorApplication.update -= Tick; Application.logMessageReceived -= Log; CoopSession.Leave(); EditorApplication.Exit(code);
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); results.Add("PASS " + message); File.WriteAllLines(Path.Combine(Output, role + "-live.txt"), results); }
    static IEnumerator Wait(Func<bool> predicate, string label)
    {
        double until = EditorApplication.timeSinceStartup + 80;
        while (!predicate()) { if (EditorApplication.timeSinceStartup > until) throw new TimeoutException(label); yield return null; }
    }
    static string Marker(string name) => Path.Combine(Output, name + ".txt");
    sealed class Auth : ICloudAuthTransport
    {
        public IEnumerator Send(string method, string endpoint, string json, string token, Action<CloudAuthResponse> complete)
        {
            yield return null;
            string id = role == "host" ? "00000000-0000-0000-0000-000000000001" : "00000000-0000-0000-0000-000000000002";
            string claims = "{\"sub\":\"" + id + "\",\"exp\":" + DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() + "}";
            string jwt = "eyJhbGciOiJSRVZJRVcifQ." + Convert.ToBase64String(Encoding.UTF8.GetBytes(claims)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".isolated_review";
            complete(new CloudAuthResponse { status = 200, body = endpoint == "user" ? "{\"id\":\"" + id + "\",\"email\":\"isolated@example.com\"}" :
                "{\"access_token\":\"" + jwt + "\",\"refresh_token\":\"isolated\",\"expires_in\":3600}" });
        }
    }
    static Button Button(FriendsMenu menu, string name) => menu.GetComponentsInChildren<Button>().Single(b => b.name == name);
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static CloudFriendsTransport Transport(bool rooms) => (CloudFriendsTransport)Activator.CreateInstance(typeof(CloudFriendsTransport), Flags, null, new object[] { "http://127.0.0.1:18474", rooms }, null);
    sealed class MissingBackend : ICloudFriendsTransport
    {
        public IEnumerator Send(CloudAuthConfig config, string token, string json, Action<CloudAuthResponse> complete)
        { yield return null; complete(new CloudAuthResponse { status = 404, body = "{\"code\":\"PGRST202\"}" }); }
    }
    static void Capture(FriendsMenu menu, string name)
    {
        var canvas = menu.GetComponentInParent<Canvas>();
        var mode = canvas.renderMode; var previousCamera = canvas.worldCamera;
        var camera = new GameObject("UI Capture Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = GameUITheme.Ink;
        var target = new RenderTexture(1600, 900, 24); target.Create(); camera.targetTexture = target;
        var active = RenderTexture.active; var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = active; canvas.renderMode = mode; canvas.worldCamera = previousCamera;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(camera.gameObject); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }
    static IEnumerator Review()
    {
        Check(Application.isPlaying, "Executed in Unity Play Mode");
        var account = CloudAccountService.EnsureInstance();
        account.GetType().GetMethod("Configure", Flags).Invoke(account, new object[] { new CloudAuthConfig { projectUrl = "https://review.supabase.co", publishableKey = "sb_publishable_isolated" }, new Auth() });
        Check(account.Login("isolated@example.com", "isolated-password"), "Isolated login starts");
        yield return Wait(() => !account.IsBusy, "login"); Check(account.IsSignedIn, "Isolated account signed in");
        var friends = CloudFriendsService.Ensure(); Call(friends, "Configure", Transport(false));
        var rooms = CloudRoomInviteService.Ensure(); Call(rooms, "Configure", new MissingBackend());
        rooms.Refresh(); yield return Wait(() => !rooms.IsBusy, "missing RPC response");
        Check(rooms.ServiceUnavailable && rooms.Message.Contains("尚未啟用"), "Missing invitation backend displays a deployment message");
        Call(rooms, "Configure", Transport(true)); rooms.Refresh(); yield return Wait(() => !rooms.IsBusy, "backend retry");
        Check(!rooms.ServiceUnavailable, "Manual refresh recovers after backend becomes available");
        Call(rooms, "ConfigureReviewRelay", new Uri("ws://127.0.0.1:18475/coop"));
        Check(!rooms.Invite("00000000-0000-0000-0000-000000000002"), "Invitation requires an internet host in camp");
        if (role == "input") yield return CampInput(rooms, friends);
        else if (role == "host") yield return Host(rooms, friends);
        else yield return Client(rooms, friends);
    }
    static IEnumerator Frames(int count)
    {
        int frame = Time.frameCount + count;
        yield return Wait(() => Time.frameCount >= frame, "input frames");
    }
    static IEnumerator CampInput(CloudRoomInviteService rooms, CloudFriendsService friends)
    {
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        yield return StartHost(); yield return Frames(3);
        var hub = UnityEngine.Object.FindFirstObjectByType<TradingHubController>();
        ((Component)hub.GetType().GetField("campSelection", Flags).GetValue(hub)).gameObject.SetActive(false);
        ((Component)hub.GetType().GetField("campUpgradePanel", Flags).GetValue(hub)).gameObject.SetActive(false);
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            var hud = UnityEngine.Object.FindFirstObjectByType<CoopRoomHud>();
            hud.GetComponentsInChildren<Button>().Single(b => b.name == "邀請好友").onClick.Invoke();
            var menu = UnityEngine.Object.FindFirstObjectByType<FriendsMenu>();
            yield return Wait(() => !rooms.IsBusy && !friends.IsBusy, "input friend list");
            Vector3 before = CoopSession.Instance.LocalCampPlayer.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return Frames(8);
            Check(FriendsMenu.IsOpen && Vector3.Distance(before, CoopSession.Instance.LocalCampPlayer.position) < .01f,
                "Typing W while friends menu is open does not move camp character");
            menu.gameObject.SetActive(false); yield return Frames(8);
            Check(!FriendsMenu.IsOpen && Vector3.Distance(before, CoopSession.Instance.LocalCampPlayer.position) > .05f,
                "Closing friends menu restores camp movement with the same held key");
        }
        finally { InputSystem.RemoveDevice(keyboard); }
        CoopSession.Leave();
    }
    static IEnumerator Host(CloudRoomInviteService rooms, CloudFriendsService friends)
    {
        yield return StartHost();
        Check(CloudRoomInviteService.CanInvite, "Internet host can invite from ready camp");
        var session = CoopSession.Instance;
        foreach (int id in new[] { 100, 101, 102 }) Call(session, "AddCampPlayer", id, CharacterRoster.All[0].Id);
        Check(!CloudRoomInviteService.CanInvite && !rooms.Invite("00000000-0000-0000-0000-000000000002"), "Full camp disables new invitations");
        var players = (System.Collections.IDictionary)session.GetType().GetField("campPlayers", Flags).GetValue(session);
        foreach (int id in new[] { 100, 101, 102 }) players.Remove(id);
        Check(!rooms.Accept("00000000-0000-0000-0000-000000000002"), "Active session cannot be replaced by an incoming invitation");
        var hud = UnityEngine.Object.FindFirstObjectByType<CoopRoomHud>();
        hud.GetComponentsInChildren<Button>().Single(b => b.name == "邀請好友").onClick.Invoke();
        var menu = UnityEngine.Object.FindFirstObjectByType<FriendsMenu>();
        yield return Wait(() => !friends.IsBusy && !rooms.IsBusy, "friend list");
        Check(friends.State.friends.Length == 1 && Button(menu, "邀請進房").interactable, "Camp button opens accepted friends with enabled invite action");
        Capture(menu, "host-invite-ui");
        Button(menu, "邀請進房").onClick.Invoke(); yield return Wait(() => !rooms.IsBusy, "invite");
        Check(rooms.Message.Contains("已送出"), "Invite button sends through real SQL API");
        yield return Wait(() => File.Exists(Marker("declined")), "recipient decline");
        Button(menu, "邀請進房").onClick.Invoke(); yield return Wait(() => !rooms.IsBusy, "second invitation");
        yield return Wait(() => File.Exists(Marker("stale-observed")), "recipient saw second invitation");
        CoopSession.Leave();
        yield return Wait(() => rooms.GetType().GetField("publishedCode", Flags).GetValue(rooms) == null && !rooms.IsBusy, "close published room");
        File.WriteAllText(Marker("closed"), "closed");
        yield return Wait(() => File.Exists(Marker("stale-rejected")), "stale acceptance rejected");
        GameSceneFlow.Load(GameSceneFlow.MenuScene);
        yield return Wait(() => !GameSceneFlow.IsLoading && SceneManager.GetActiveScene().name == GameSceneFlow.MenuScene, "return to menu");
        yield return StartHost();
        hud = UnityEngine.Object.FindFirstObjectByType<CoopRoomHud>(); hud.GetComponentsInChildren<Button>().Single(b => b.name == "邀請好友").onClick.Invoke();
        menu = UnityEngine.Object.FindFirstObjectByType<FriendsMenu>();
        yield return Wait(() => !friends.IsBusy && !rooms.IsBusy, "friend list reopened");
        Button(menu, "邀請進房").onClick.Invoke(); yield return Wait(() => !rooms.IsBusy, "third invitation");
        yield return Wait(() => CoopSession.Instance.PlayerCount == 2 && File.Exists(Marker("joined")), "accepted friend in same camp");
        Check(CoopSession.InCamp && CoopSession.Instance.SceneReady && CoopSession.Instance.PlayerCount == 2, "Accepting friend joins the password-protected internet camp");
        CoopSession.Leave();
        yield return Wait(() => rooms.GetType().GetField("publishedCode", Flags).GetValue(rooms) == null && !rooms.IsBusy, "final room closure");
        Check(!CloudRoomInviteService.CanInvite, "Leaving room disables invitations and closes backend publication");
    }
    static IEnumerator StartHost()
    {
        string failure = null;
        typeof(CoopSession).GetMethod("ReviewInternet", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
            new object[] { new Uri("ws://127.0.0.1:18475/coop"), "Map1", CharacterRoster.All[0].Id, null, "friend-room-secret", (Action)(() => { }), (Action<string>)(error => failure = error) });
        yield return Wait(() => failure != null || CloudRoomInviteService.CanInvite, "host room creation");
        Check(failure == null, "Real WebSocket relay creates camp");
    }
    static IEnumerator Client(CloudRoomInviteService rooms, CloudFriendsService friends)
    {
        var canvas = new GameObject("Invite Review Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; GameUITheme.FitCanvas(canvas.GetComponent<CanvasScaler>());
        var menu = FriendsMenu.Create(canvas.transform, () => { }); menu.Open();
        yield return Wait(() => rooms.Invites.Length == 1 && !rooms.IsBusy, "incoming invite");
        Check(Button(menu, "接受進房").interactable && !CoopSession.IsMultiplayer, "Invitation appears with explicit accept and decline; no automatic join");
        Capture(menu, "client-invite-ui");
        Button(menu, "拒絕").onClick.Invoke(); yield return Wait(() => !rooms.IsBusy, "decline");
        Check(rooms.Invites.Length == 0 && !CoopSession.IsMultiplayer, "Decline removes only invitation without joining"); File.WriteAllText(Marker("declined"), "declined");
        yield return Wait(() => rooms.Invites.Length == 1 && !rooms.IsBusy, "second incoming invite");
        string staleId = rooms.Invites[0].id;
        File.WriteAllText(Marker("stale-observed"), "observed");
        yield return Wait(() => File.Exists(Marker("closed")), "host closed");
        rooms.GetType().GetField("nextPoll", Flags).SetValue(rooms, float.MaxValue);
        yield return Wait(() => !rooms.IsBusy, "finish pending refresh");
        Check(rooms.Accept(staleId), "Stale invitation acceptance request starts"); yield return Wait(() => !rooms.IsBusy, "stale acceptance");
        Check(!CoopSession.IsMultiplayer && rooms.Message.Contains("已過期"), "Closed room acceptance fails with a visible message");
        File.WriteAllText(Marker("stale-rejected"), "rejected"); rooms.Refresh();
        rooms.GetType().GetField("nextPoll", Flags).SetValue(rooms, 0f);
        yield return Wait(() => rooms.Invites.Length == 1 && !rooms.IsBusy, "third incoming invite");
        Button(menu, "接受進房").onClick.Invoke();
        yield return Wait(() => !rooms.IsBusy && !rooms.IsJoining && CoopSession.InCamp && CoopSession.Instance.SceneReady && CoopSession.Instance.PlayerCount == 2, "accept and connect");
        Check(CoopSession.IsClient && CoopSession.IsInternet && rooms.Invites.Length == 0, "Accept UI fetches room credentials and joins without typing a code or password");
        File.WriteAllText(Marker("joined"), "joined");
        yield return Wait(() => !CoopSession.IsMultiplayer && !GameSceneFlow.IsLoading, "host disconnected");
        Check(CoopSession.LastError != null, "Host departure returns friend from closed room with an error message");
        CloudAccountService.EnsureInstance().Logout();
        yield return Wait(() => !CloudAccountService.EnsureInstance().IsBusy, "logout");
        Check(rooms.Invites.Length == 0 && !rooms.IsJoining, "Logout clears invitation state");
    }
}
#endif
