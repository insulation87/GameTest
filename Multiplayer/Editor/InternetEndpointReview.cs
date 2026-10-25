#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

// Runs only in an isolated marked project. Public tests use invalid tokens and create no rooms.
[InitializeOnLoad]
public static class InternetEndpointReview
{
    const string Pending = "InternetEndpointReview.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static IEnumerator checks;
    static double next, deadline;
    static int assertions, editorErrors;
    static string gameError;
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../internet-play-result.txt"));

    sealed class FakeAuth : ICloudAuthTransport
    {
        public IEnumerator Send(string method, string endpoint, string json, string token, Action<CloudAuthResponse> complete)
        {
            yield return null;
            complete(new CloudAuthResponse { status = 200, body = endpoint == "user" ?
                "{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"email\":\"isolated@example.com\"}" :
                "{\"access_token\":\"invalid_public_relay_review_token\",\"refresh_token\":\"isolated-refresh\",\"expires_in\":3600}" });
        }
    }
    static InternetEndpointReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(Application.dataPath, "../internet-isolated-marker.txt")))
            throw new InvalidOperationException("Use an isolated marked batch project.");
        EditorSceneManager.playModeStartScene = null;
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Map1.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Map1.unity", true) };
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); assertions = editorErrors = 0; gameError = null;
        checks = Review(); deadline = EditorApplication.timeSinceStartup + 90;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        if (stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")) { editorErrors++; return; }
        gameError = message;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Internet endpoint Play Mode review");
            if (!checks.MoveNext()) { Finish(0, "PASS " + assertions + " Play Mode checks"); return; }
            next = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .05f);
        }
        catch (Exception ex) { Finish(1, "FAIL " + ex); }
    }
    static void Finish(int code, string result)
    {
        result += "\nUnity Editor Search startup exceptions: " + editorErrors +
            "\nPublic service: health and invalid-token rejection only; no real login or room created.";
        File.WriteAllText(Output, result); Debug.Log(result);
        CoopSession.Leave(); Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code);
    }
    static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); assertions++; Debug.Log("PASS " + label); }
    static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static IEnumerator Review()
    {
        Check(Application.isPlaying, "Checks run in Unity Play Mode");
        var config = CloudRelayConfig.Load();
        Check(config.IsValid && config.relayUrl == "wss://gametest-relay.onrender.com/coop", "Actual Resources config enables the deployed WSS endpoint");
        Check(CoopSession.InternetConfigured, "Internet room configuration is enabled");
        using (var health = UnityWebRequest.Get("https://gametest-relay.onrender.com/health"))
        {
            health.timeout = 55; health.redirectLimit = 0;
            var operation = health.SendWebRequest(); while (!operation.isDone) yield return .05f;
            Check(health.result == UnityWebRequest.Result.Success && health.responseCode == 200 && health.downloadHandler.text.Contains("\"protocol\":1"), "Unity reaches the real HTTPS health endpoint");
        }
        using (var transport = new CloudRelayTransport(new Uri(config.relayUrl), "invalid_public_relay_review_token", true, null))
        {
            CoopTransport.Event result = null;
            double until = EditorApplication.timeSinceStartup + 25;
            while (result == null)
            {
                while (transport.TryPoll(out var item)) { Check(string.IsNullOrEmpty(item.roomCode), "Invalid token cannot create a public room"); if (item.fatal) result = item; }
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Public WSS auth response");
                yield return .05f;
            }
            Check(result.error == "登入已失效，請重新登入。", "Unity WSS reaches public relay and translates its auth rejection");
        }
        var account = CloudAccountService.EnsureInstance();
        Call(account, "Configure", new CloudAuthConfig { projectUrl = "https://isolated-review.supabase.co", publishableKey = "sb_publishable_isolated_review" }, new FakeAuth());
        Check(account.Login("isolated@example.com", "review-password"), "Local fake login begins without using a real account");
        while (account.IsBusy) yield return .05f;
        var canvas = new GameObject("Internet Review Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var menu = MultiplayerMenu.Create(canvas.transform, "Map1", () => true, () => { }); menu.Open();
        Check((bool)Get(menu, "internet") && ((Button)Get(menu, "internetTab")).interactable, "Signed-in menu defaults to configured internet mode");
        Check(!((InputField)Get(menu, "port")).gameObject.activeSelf && ((InputField)Get(menu, "address")).text == "", "Internet mode requests a room code and hides LAN port");
        Call(menu, "StartMode", true);
        Check((bool)Get(menu, "joining") && CoopSession.IsInternet, "Host UI starts the configured public relay connection");
        double hostUntil = EditorApplication.timeSinceStartup + 25;
        while ((bool)Get(menu, "joining"))
        { if (EditorApplication.timeSinceStartup > hostUntil) throw new TimeoutException("Host UI error handling"); yield return .05f; }
        Check(((Text)Get(menu, "status")).text == "登入已失效，請重新登入。", "Host UI displays public auth rejection");
        Check(!CoopSession.IsMultiplayer && !CoopSession.IsInternet && CoopSession.RoomCode == null && !GameSceneFlow.IsLoading, "Failed auth leaves no room or scene transition");
        foreach (Button button in (Button[])Get(menu, "launchButtons")) Check(button.interactable, "Launch action becomes available after failure");
        ((InputField)Get(menu, "address")).text = "BAD"; Call(menu, "Join");
        Check(!(bool)Get(menu, "joining") && ((Text)Get(menu, "status")).text.Contains("10 碼"), "Invalid room code is rejected before connecting");
        account.Logout(); while (account.IsBusy) yield return .05f; menu.Open();
        Check(!(bool)Get(menu, "internet") && !((Button)Get(menu, "internetTab")).interactable, "Unsigned menu disables internet and selects LAN");
        Check(gameError == null, "No game runtime errors: " + gameError);
        UnityEngine.Object.Destroy(canvas);
    }
}
#endif
