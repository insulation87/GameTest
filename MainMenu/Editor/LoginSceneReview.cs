using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit scene-flow checks in a temporary project; uses fake Auth, never real email.</summary>
[InitializeOnLoad]
public static class LoginSceneReview
{
    const string Pending = "LoginSceneReview.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static string output, profilePath;
    static CloudAccountService service;
    static FakeAuth transport;
    static readonly List<string> errors = new List<string>();
    static readonly string userId = "5b673b4c-9a65-42cb-b59a-804b9b2a0b71";

    sealed class FakeAuth : ICloudAuthTransport
    {
        public readonly Queue<CloudAuthResponse> responses = new Queue<CloudAuthResponse>();
        public readonly List<string> endpoints = new List<string>();
        public void Reply(long status, string body) => responses.Enqueue(new CloudAuthResponse { status = status, body = body });
        public IEnumerator Send(string method, string endpoint, string json, string token, Action<CloudAuthResponse> complete)
        {
            endpoints.Add(endpoint);
            yield return null;
            if (responses.Count == 0) throw new Exception("Unexpected mock Auth request: " + endpoint);
            complete(responses.Dequeue());
        }
        public void Session()
        {
            Reply(200, "{\"access_token\":\"review-only-access\",\"refresh_token\":\"review-only-refresh\",\"expires_in\":3600}");
            Reply(200, "{\"id\":\"" + userId + "\",\"email\":\"review@example.com\"}");
        }
    }

    static LoginSceneReview() { EditorApplication.playModeStateChanged += StateChanged; }
    public static void Run()
    {
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        assertions = 0;
        PrepareScenes();
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Extra.unity", false),
            new EditorBuildSettingsScene("Assets/Scenes/Map1.unity", true) };
        LoginSceneSetup.UpdateBuildSettings();
        Check(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).SequenceEqual(new[] {
            "Assets/Scenes/Login.unity", "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Map1.unity" }), "Enabled build scenes start at Login, followed by MainMenu and Map1");
        Check(EditorBuildSettings.scenes.Any(s => s.path.EndsWith("Extra.unity") && !s.enabled), "Rebuilding menu settings preserves other disabled scenes");
        LoginSceneSetup.PlayFromCurrentScene();
        Check(EditorSceneManager.playModeStartScene == null, "Developers can explicitly play the current scene");
        LoginSceneSetup.PlayFromLogin();
        Check(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == LoginSceneSetup.ScenePath, "Editor Play is configured to start from Login");
        SessionState.SetInt("LoginSceneReview.EditorChecks", assertions);
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void PrepareScenes()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Scene login = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var node = new GameObject("Login Script Reference");
        var controller = node.AddComponent<LoginSceneController>();
        MonoScript script = MonoScript.FromMonoBehaviour(controller);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string scriptGuid, out long scriptId);
        string original = File.ReadAllText(Path.Combine(output, "source-login.unity"));
        string sourceGuid = File.ReadAllText(Path.Combine(output, "source-login-script-guid.txt")).Trim();
        string remapped = original.Replace("{fileID: 11500000, guid: " + sourceGuid + ", type: 3}",
            "{fileID: " + scriptId + ", guid: " + scriptGuid + ", type: 3}")
            .Replace("Assembly-CSharp::LoginSceneController", "Review::LoginSceneController");
        Check(remapped != original && remapped.Contains("Review::LoginSceneController"), "The actual Login scene is exercised with its script reference remapped to the isolated runtime DLL");
        Object.DestroyImmediate(node);
        File.WriteAllText(LoginSceneSetup.ScenePath, remapped);
        AssetDatabase.ImportAsset(LoginSceneSetup.ScenePath, ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.OpenScene(LoginSceneSetup.ScenePath, OpenSceneMode.Single);
        Check(Object.FindFirstObjectByType<LoginSceneController>() != null && Object.FindFirstObjectByType<MainMenuController>() == null,
            "The actual Login asset imports as a separate scene with its own controller");

        Scene menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Menu Camera", typeof(Camera), typeof(AudioListener)).tag = "MainCamera";
        new GameObject("Menu Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var canvas = new GameObject("Menu Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var background = AccountLoginPanel.Box(canvas.transform, "Background", Vector2.zero, Vector2.zero, GameUITheme.Ink);
        GameUITheme.Stretch(background);
        var main = new GameObject("Main Panel", typeof(RectTransform)); main.transform.SetParent(background, false); GameUITheme.Stretch(main.GetComponent<RectTransform>());
        var settings = new GameObject("Settings Panel", typeof(RectTransform)); settings.transform.SetParent(background, false); GameUITheme.Stretch(settings.GetComponent<RectTransform>());
        var menuController = background.gameObject.AddComponent<MainMenuController>();
        var serialized = new SerializedObject(menuController);
        serialized.FindProperty("mainPanel").objectReferenceValue = main;
        serialized.FindProperty("settingsPanel").objectReferenceValue = settings;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AccountLoginPanel.Label(main.transform, "地城探索", 48, Vector2.zero, new Vector2(660, 90));
        UnityEventTools.AddPersistentListener(MenuButton(main.transform, "加入遊戲").onClick, menuController.JoinGame);
        UnityEventTools.AddPersistentListener(MenuButton(main.transform, "設定").onClick, menuController.ShowSettings);
        UnityEventTools.AddPersistentListener(MenuButton(main.transform, "退出遊戲").onClick, menuController.QuitGame);
        settings.SetActive(false);
        EditorSceneManager.SaveScene(menu, "Assets/Scenes/MainMenu.unity");

        Scene map = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var player = new GameObject("Protected Map Player");
        player.AddComponent<GameProgressSave>().enabled = false;
        player.AddComponent<Player>().enabled = false;
        player.AddComponent<PlayerHealth>().enabled = false;
        EditorSceneManager.SaveScene(map, "Assets/Scenes/Map1.unity");
        Scene extra = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(extra, "Assets/Scenes/Extra.unity");
    }

    static Button MenuButton(Transform parent, string label)
    {
        var rect = AccountLoginPanel.Box(parent, label, Vector2.zero, new Vector2(360, 62), GameUITheme.Raised);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        AccountLoginPanel.Label(rect, label, 26, Vector2.zero, new Vector2(344, 56));
        return button;
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        assertions = SessionState.GetInt("LoginSceneReview.EditorChecks", 0);
        errors.Clear(); Application.logMessageReceived += OnLog;
        checks = Checks(); deadline = EditorApplication.timeSinceStartup + .2;
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext())
            {
                File.WriteAllText(Path.Combine(output, "login-result.txt"), $"PASS {assertions} login-scene integration checks");
                Application.logMessageReceived -= OnLog; EditorApplication.update -= Tick; EditorApplication.Exit(0); return;
            }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .08f);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "login-failure.txt"), ex.ToString());
            Application.logMessageReceived -= OnLog;
            Debug.LogException(ex); EditorApplication.update -= Tick; EditorApplication.Exit(1);
        }
    }
    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (message.Contains("burst JIT compiler") || stack.Contains("UnityEditor.Search")) return;
        errors.Add(message + "\n" + stack);
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++; Debug.Log("PASS " + message);
    }
    static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static T Control<T>(AccountLoginPanel panel, string field) => (T)Get(panel, field);
    static string Active => SceneManager.GetActiveScene().name;
    static IEnumerator WaitScene(string scene)
    {
        double timeout = EditorApplication.timeSinceStartup + 12;
        while (Active != scene || GameSceneFlow.IsLoading)
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new Exception("Timed out loading " + scene + "; current=" + Active);
            yield return .1f;
        }
        yield return .2f;
    }
    static void PrepareMapSave(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Map1") return;
        SceneManager.sceneLoaded -= PrepareMapSave;
        var actor = Object.FindFirstObjectByType<Player>();
        var save = actor.GetComponent<GameProgressSave>();
        save.StoragePath = Path.Combine(output,"loading-isolated-save.json");
        actor.gameObject.AddComponent<PlayerShooter>().enabled = false;
        save.enabled = true;
    }
    static IEnumerator Capture(string file)
    {
        Camera camera = Camera.main;
        PixelArtCamera pixelCamera = camera.GetComponent<PixelArtCamera>();
        bool resumePixels = pixelCamera != null && pixelCamera.enabled;
        if (resumePixels) { pixelCamera.enabled = false; yield return .1f; }
        float oldAspect = camera.aspect;
        var rt = new RenderTexture(1280, 720, 24); camera.targetTexture = rt;
        camera.aspect = 1280f / 720f;
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        foreach (Canvas canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
        yield return .15f; Canvas.ForceUpdateCanvases(); camera.Render();
        RenderTexture before = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, file + ".png"), image.EncodeToPNG());
        RenderTexture.active = before; camera.targetTexture = null;
        camera.aspect = oldAspect;
        foreach (Canvas canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        Object.DestroyImmediate(image); Object.DestroyImmediate(rt);
        if (resumePixels) pixelCamera.enabled = true;
    }

    static IEnumerator Checks()
    {
        Object.FindFirstObjectByType<PixelArtDirector>().enabled = false;
        Check(Active == "Login", "Starting Editor Play from an edited MainMenu still opens Login first");
        Check(Time.timeScale == 1 && Cursor.lockState == CursorLockMode.None && Cursor.visible, "Login restores time and mouse controls");
        Check(Object.FindObjectsByType<LoginSceneController>(FindObjectsSortMode.None).Length == 1 &&
            Object.FindObjectsByType<AccountLoginPanel>(FindObjectsSortMode.None).Length == 1, "Login owns exactly one controller and one account panel");
        Check(Object.FindFirstObjectByType<MainMenuController>() == null, "No main-menu controller is embedded in Login");
        Check(EventSystem.current != null && EventSystem.current.currentInputModule is InputSystemUIInputModule, "Login has its own functioning input system");
        Check(Object.FindFirstObjectByType<PixelMenuBackdrop>().GetComponent<CanvasRenderer>() != null,
            "The login backdrop has a renderer for its generated artwork");
        Check(GameMusic.SelectTheme(null) == MusicTheme.Menu, "The standalone login scene keeps the menu music theme");
        service = CloudAccountService.EnsureInstance(); transport = new FakeAuth();
        Call(service, "Configure", new CloudAuthConfig { projectUrl = "https://login-scene-review.supabase.co", publishableKey = "sb_publishable_isolated_review" }, transport);
        Call(service, "Say", "請登入帳號；遊戲進度儲存在這台電腦");
        var panel = Object.FindFirstObjectByType<AccountLoginPanel>();
        Check(Control<Button>(panel, "primary").interactable, "Configured Auth enables the login button");
        IEnumerator capture = Capture("standalone-login"); while (capture.MoveNext()) yield return capture.Current;
        Control<InputField>(panel, "email").text = "review@example.com";
        Control<InputField>(panel, "password").text = "invalid-password";
        transport.Reply(401, "{\"error_code\":\"invalid_credentials\"}"); Control<Button>(panel, "primary").onClick.Invoke();
        Check(service.IsBusy && Active == "Login", "A submitted login stays in Login while authentication is pending");
        yield return .4f;
        Check(!service.IsSignedIn && Active == "Login" && Control<Text>(panel, "status").text.Length > 0, "Failed authentication shows an error without entering MainMenu");
        Check(Control<InputField>(panel, "password").text.Length == 0, "Submitting clears the password from the scene UI");
        Control<Button>(panel, "registerTab").onClick.Invoke();
        Check(Control<InputField>(panel, "confirm").gameObject.activeInHierarchy && Control<Text>(panel, "title").text == "建立帳號", "The standalone scene retains registration fields");
        Control<InputField>(panel, "password").text = "review-password"; Control<InputField>(panel, "confirm").text = "review-password";
        transport.Reply(200, "{}"); Control<Button>(panel, "primary").onClick.Invoke(); yield return .4f;
        Check(Active == "Login" && !service.IsSignedIn && Control<InputField>(panel, "code").gameObject.activeInHierarchy,
            "Unverified registration stays in Login and opens email verification");
        Control<Button>(panel, "resetTab").onClick.Invoke();
        Check(Control<InputField>(panel, "password").gameObject.activeInHierarchy && Control<InputField>(panel, "code").gameObject.activeInHierarchy,
            "The standalone scene retains password recovery fields");
        Control<InputField>(panel, "password").text = "new-review-password"; Control<InputField>(panel, "confirm").text = "new-review-password";
        Control<InputField>(panel, "code").text = "123456";
        transport.Reply(200, "{\"access_token\":\"review-recovery\"}"); transport.Reply(200, "{}"); transport.Reply(204, "");
        Control<Button>(panel, "primary").onClick.Invoke(); yield return .5f;
        Check(!service.IsSignedIn && Active == "Login" && service.Message.Contains("密碼已更新"), "Recovery tokens do not bypass login or navigate to MainMenu");
        Control<Button>(panel, "verifyTab").onClick.Invoke(); Control<InputField>(panel, "code").text = "123456";
        transport.Session(); Control<Button>(panel, "primary").onClick.Invoke();
        IEnumerator wait = WaitScene("MainMenu"); while (wait.MoveNext()) yield return wait.Current;
        Check(service.IsSignedIn && !service.IsBusy && CloudAccountService.Instance == service, "Verified sign-in loads MainMenu and retains the same account service");
        profilePath = LocalAccountProgress.ActivePath;
        Check(Object.FindFirstObjectByType<AccountLoginPanel>() == null && Object.FindFirstObjectByType<LoginSceneController>() == null,
            "MainMenu contains no login panel after changing scenes");
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        Check(menu != null && ((Text)Get(menu, "accountLabel")).text.Contains("review@example.com"), "MainMenu displays the signed-in identity");
        Check(Object.FindFirstObjectByType<PixelMenuBackdrop>().GetComponent<CanvasRenderer>() != null,
            "The signed-in menu retains its rendered artwork");
        menu.ShowSettings(); Check(((GameObject)Get(menu, "settingsPanel")).activeSelf && service.IsSignedIn, "MainMenu settings still work after sign-in");
        menu.ShowMainMenu();
        capture = Capture("signed-in-main-menu"); while (capture.MoveNext()) yield return capture.Current;
        menu.JoinGame();
        var selection = Object.FindFirstObjectByType<CharacterSelectionMenu>();
        Check(Active == "MainMenu" && selection != null && selection.gameObject.activeSelf,
            "Starting a new run opens character selection before the map");
        Check(selection.GetComponentsInChildren<CharacterPortrait>().Length == 10, "All ten character cards have native portraits");
        menu.ShowMainMenu();
        Check(!selection.gameObject.activeSelf && !GameProgressSave.StartFresh, "Cancelling selection preserves the current run state");
        menu.JoinGame(); selection.Select(9);
        Check(selection.SelectedId == "ranger", "The final character can be selected before departure");
        capture = Capture("character-selection"); while (capture.MoveNext()) yield return capture.Current;
        foreach (Text text in selection.GetComponentsInChildren<Text>())
            Check(text.preferredHeight <= text.rectTransform.rect.height && text.cachedTextGenerator.vertexCount > 4,
                "Character selection text fits and renders: " + text.text);
        SceneManager.sceneLoaded += PrepareMapSave;
        selection.GetComponentsInChildren<Button>().Single(b => b.name == "以此角色出發").onClick.Invoke();
        Check(GameSceneFlow.IsLoading && Active == "MainMenu" && Time.timeScale == 0,
            "Departure immediately opens loading and freezes the previous scene");
        var loading = Object.FindFirstObjectByType<GameLoadingScreen>();
        Check(loading != null && loading.Destination == "Map1" && loading.GetComponentInChildren<Canvas>().sortingOrder > 1000,
            "The loading overlay covers all gameplay and menu canvases");
        Check(loading.GetComponentInChildren<CanvasGroup>().blocksRaycasts && EventSystem.current.currentSelectedGameObject == null,
            "Loading blocks pointer clicks and clears keyboard selection");
        Check(GameSceneFlow.Load("Map1") && !GameSceneFlow.Load("MainMenu"),
            "Duplicate requests reuse the operation and competing transitions are rejected");
        var rune = loading.GetComponentInChildren<LoadingRune>(); float phase = rune.Phase;
        yield return .12f;
        Check(rune.Phase != phase && Time.timeScale == 0 && loading.VisibleProgress >= 0 && loading.VisibleProgress <= 1,
            "The rune and real progress animate while gameplay is paused");
        capture = Capture("loading-screen"); while (capture.MoveNext()) yield return capture.Current;
        Check(loading.GetComponentInChildren<LoadingRune>().canvasRenderer.GetMesh().vertexCount > 0,
            "The loading animation has renderable UI geometry");
        wait = WaitScene("Map1"); while (wait.MoveNext()) yield return wait.Current;
        Check(!loading.gameObject.activeSelf && !GameSceneFlow.IsLoading && Time.timeScale == 1 && loading.VisibleProgress == 1,
            "Completed loading hides the overlay, reaches one hundred percent and resumes gameplay");
        Check(Object.FindFirstObjectByType<CharacterSkills>().Character.Id == "ranger", "The map player receives the selected character");
        Check(Object.FindFirstObjectByType<GameProgressSave>().Ready,
            "Loading waits for the actual player save initialization before revealing the map");
        Check(service.IsSignedIn && CloudAccountService.Instance == service && Object.FindFirstObjectByType<Player>() != null,
            "MainMenu enters the map with the same authenticated session");
        DungeonVictoryUI.Show(Object.FindFirstObjectByType<PlayerHealth>());
        yield return .55f;
        DungeonVictoryUI.Instance.ReturnToMainMenu();
        Check(!GameSceneFlow.IsLoading && DungeonVictoryUI.IsOpen && Time.timeScale == 0,
            "A failed victory save requires explicit confirmation before starting loading");
        DungeonVictoryUI.Instance.ReturnToMainMenu();
        Check(GameSceneFlow.IsLoading && Object.FindFirstObjectByType<GameLoadingScreen>() == loading,
            "Returning from a paused map reuses the persistent loading screen");
        wait = WaitScene("MainMenu"); while (wait.MoveNext()) yield return wait.Current;
        Check(Time.timeScale == 1 && Object.FindFirstObjectByType<AccountLoginPanel>() == null, "Returning from a paused map restores a signed-in MainMenu");
        menu = Object.FindFirstObjectByType<MainMenuController>(); transport.Reply(204, "");
        ((Button)Get(menu, "logoutButton")).onClick.Invoke();
        wait = WaitScene("Login"); while (wait.MoveNext()) yield return wait.Current;
        Check(!service.IsSignedIn && !LocalAccountProgress.HasAccount && CloudAccountService.Instance == service, "Logout returns to Login and clears account selection without destroying the service");
        panel = Object.FindFirstObjectByType<AccountLoginPanel>();
        Check(Control<InputField>(panel, "email").text == "" && Control<InputField>(panel, "password").text == "", "A new Login scene carries no previous credentials");
        GameSceneFlow.Load("Map1"); yield return .6f;
        wait = WaitScene("Login"); while (wait.MoveNext()) yield return wait.Current;
        Check(Active == "Login" && Object.FindFirstObjectByType<Player>() == null, "The build runtime guard redirects an unauthenticated map to Login");
        GameSceneFlow.Load(GameSceneFlow.MenuScene); yield return .6f;
        wait = WaitScene("Login"); while (wait.MoveNext()) yield return wait.Current;
        Check(Object.FindFirstObjectByType<MainMenuController>() == null, "Direct access to an unsigned MainMenu also redirects to Login");
        panel = Object.FindFirstObjectByType<AccountLoginPanel>();
        Control<InputField>(panel, "email").text = "review@example.com"; Control<InputField>(panel, "password").text = "review-password";
        transport.Session(); Control<Button>(panel, "primary").onClick.Invoke();
        wait = WaitScene("MainMenu"); while (wait.MoveNext()) yield return wait.Current;
        Check(service.IsSignedIn && transport.endpoints.Contains("token?grant_type=password"), "Password login works again after logout and scene redirects");
        Check(Object.FindObjectsByType<CloudAccountService>(FindObjectsSortMode.None).Length == 1 && Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1,
            "Repeated transitions do not duplicate persistent accounts or scene input systems");
        Check(!File.Exists(profilePath), "Scene-flow checks never write player progress");
        Check(!File.Exists(Path.Combine(output,"loading-isolated-save.json")), "Loading uses an isolated save path and preserves the no-ground save protection");
        Check(Object.FindObjectsByType<GameLoadingScreen>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length == 1 &&
            !GameSceneFlow.IsLoading, "Authentication redirects finish without duplicated loading screens or stuck transitions");
        Check(transport.responses.Count == 0 && transport.endpoints.All(e => e == "token?grant_type=password" || e == "user" || e == "signup" || e == "verify" || e == "logout?scope=local"),
            "All Auth operations use the isolated mock transport");
        Check(errors.Count == 0, "No unexpected gameplay errors: " + string.Join("\n", errors));
    }
}
