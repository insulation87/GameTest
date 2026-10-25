using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit entry point for an isolated batchmode project. Creates and replaces fixture scenes.
[InitializeOnLoad]
public static class CoopModeReview
{
    const string Pending = "CoopModeReview.Pending";
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static readonly List<string> errors = new List<string>();
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../"));
    static CoopModeReview() { EditorApplication.playModeStateChanged += StateChanged; }
    public static void Run()
    {
        LoginSceneSetup.PlayFromCurrentScene();
        BuildFixtures(); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    public static void BuildFixtures()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Fixture Ground";
        floor.transform.position = new Vector3(0, -.5f, 0); floor.transform.localScale = new Vector3(100, 1, 100);
        var bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere); bullet.transform.localScale = Vector3.one * .1f;
        bullet.AddComponent<Rigidbody>().useGravity = false; bullet.AddComponent<Projectile>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(bullet, "Assets/CoopReviewBullet.prefab"); Object.DestroyImmediate(bullet);
        var actor = new GameObject("Fixture Player"); actor.SetActive(false); actor.transform.position = new Vector3(0, 1, 0);
        var rb = actor.AddComponent<Rigidbody>(); rb.constraints = RigidbodyConstraints.FreezeRotation;
        actor.AddComponent<BoxCollider>().size = new Vector3(.6f, 1.8f, .6f);
        var player = actor.AddComponent<Player>(); var health = actor.AddComponent<PlayerHealth>();
        health.respawnDelay = .2f;
        var shooter = actor.AddComponent<PlayerShooter>(); var views = actor.AddComponent<CameraSwitch>();
        var fps = new GameObject("FPS", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>(); fps.transform.SetParent(actor.transform);
        var tps = new GameObject("TPS", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>(); tps.transform.SetParent(actor.transform);
        views.fpsCamera = player.fpsCamera = shooter.fpsCamera = fps;
        views.tpsCamera = player.tpsCamera = shooter.tpsCamera = tps;
        views.fpsController = fps.gameObject.AddComponent<FPSCameraLook>(); views.fpsController.player = actor.transform; views.fpsController.playerRb = rb;
        views.tpsController = tps.gameObject.AddComponent<CameraFollowTPS>(); views.tpsController.target = actor.transform;
        shooter.projectilePrefab = prefab.GetComponent<Projectile>();
        actor.AddComponent<PlayerHealthBarUI>(); actor.AddComponent<CrosshairUI>();
        actor.AddComponent<GameProgressSave>().StoragePath = Path.Combine(Output, "coop-fixture-save.json");
        actor.SetActive(true);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Map1.unity");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/MainMenu.unity");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Login.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/MainMenu.unity", true),
            new EditorBuildSettingsScene("Assets/Map1.unity", true), new EditorBuildSettingsScene("Assets/Login.unity", true) };
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); assertions = 0; errors.Clear(); checks = Checks(); deadline = EditorApplication.timeSinceStartup + .2;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    { if ((type == LogType.Error || type == LogType.Exception) && !message.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(message); }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext()) { File.WriteAllText(Path.Combine(Output, "coop-result.txt"), "PASS " + assertions + " cooperative integration checks"); Finish(0); return; }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .05f);
        }
        catch (Exception ex) { Debug.LogException(ex); File.WriteAllText(Path.Combine(Output, "coop-result.txt"), "FAIL " + ex); Finish(1); }
    }
    static void Finish(int code)
    { CoopSession.Leave(); Time.timeScale = 1; Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code); }
    static void Check(bool result, string message)
    { if (!result) throw new InvalidOperationException(message); assertions++; Debug.Log("PASS " + message); }
    static IEnumerator Ready()
    {
        double until = EditorApplication.timeSinceStartup + 30;
        while (CoopSession.Instance == null || !CoopSession.Instance.SceneReady || GameSceneFlow.IsLoading)
        { if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Coop scene readiness"); yield return .1f; }
    }
    static CoopActor Actor(int id) => Array.Find(Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None), a => a.Id == id);
    static void Invoke(object item, string method, params object[] args) => item.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(item, args);
    static void Send(CoopTransport client, CoopPacket packet) => Check(client.Send(0, JsonUtility.ToJson(packet)), "Packet queued: " + packet.kind);
    static IEnumerator Checks()
    {
        Check(CoopSession.Mode == CoopMode.SinglePlayer && GameProgressSave.SavePath == LocalAccountProgress.ActivePath, "Default single-player save routing is preserved");
        Check(CoopSession.StartSplit("Map1", "breaker", "sentinel"), "Split-screen starts through the shared loading flow");
        var ready = Ready(); while (ready.MoveNext()) yield return ready.Current;
        Check(CoopSession.Instance.PlayerCount == 2, "Exactly two actors are created for local cooperation");
        var first = Actor(0); var second = Actor(1);
        Check(first.Local && second.Local && first.LocalIndex == 0 && second.LocalIndex == 1, "Separate local input slots are assigned");
        var one = first.GetComponent<Player>(); var two = second.GetComponent<Player>();
        Check(one.fpsCamera != two.fpsCamera && two.fpsCamera.GetComponent<FPSCameraLook>().player == second.transform, "Cloned FPS rigs reference their own actor");
        Check(two.tpsCamera.GetComponent<CameraFollowTPS>().target == second.transform, "Cloned TPS rigs reference their own actor");
        Check(first.GetComponent<CharacterSkills>().Character.Id == "breaker" && second.GetComponent<CharacterSkills>().Character.Id == "sentinel", "Both character choices survive initialization");
        yield return .8f;
        var pixels1 = one.fpsCamera.GetComponent<PixelArtCamera>(); var pixels2 = two.fpsCamera.GetComponent<PixelArtCamera>();
        Check(pixels1 != null && pixels2 != null && pixels1.outputRect.x == 0 && pixels2.outputRect.x == .5f, "Both split views are presented simultaneously");
        Check(one.fpsCamera.targetTexture.width == Mathf.CeilToInt(Screen.width * .5f) && two.fpsCamera.targetTexture.height == Screen.height, "Each render target uses its viewport resolution");
        var image1 = one.fpsCamera.transform.Find("Pixel World Display/World Pixels").GetComponent<RawImage>();
        var image2 = two.fpsCamera.transform.Find("Pixel World Display/World Pixels").GetComponent<RawImage>();
        Check(image1.rectTransform.anchorMax.x == .5f && image2.rectTransform.anchorMin.x == .5f, "World outputs occupy different screen halves");
        second.GetComponent<CameraSwitch>().SetCoopView(false); yield return .15f;
        Check(two.tpsCamera.GetComponent<PixelArtCamera>().outputRect.x == .5f && two.tpsCamera.targetTexture.width == Mathf.CeilToInt(Screen.width * .5f),
            "Second-player TPS switching retains its half-screen output");
        second.GetComponent<CameraSwitch>().SetCoopView(true); yield return .15f;
        foreach (Canvas canvas in second.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.GetComponentInParent<PixelArtCamera>() != null) continue;
            Check(canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.worldCamera == null &&
                canvas.GetComponent<GameUIViewport>() != null,
                "Second-player HUD renders above the world in its own viewport");
        }
        Check(GameProgressSave.Instance == first.GetComponent<GameProgressSave>() && GameProgressSave.SavePath != LocalAccountProgress.ActivePath, "Only the primary actor owns the separate cooperative save slot");
        var pad = InputSystem.AddDevice<Gamepad>();
        Vector3 before1 = first.transform.position, before2 = second.transform.position;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(1, 0) }); InputSystem.Update(); yield return .25f;
        Check(Vector3.Distance(second.transform.position, before2) > .2f && Vector3.Distance(first.transform.position, before1) < .1f, "Gamepad movement does not move the keyboard player");
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.RightShoulder)); InputSystem.Update(); yield return .15f;
        Check(second.GetComponent<CharacterSkills>().CooldownRemaining > 0 && first.GetComponent<CharacterSkills>().CooldownRemaining == 0, "Second-player ability input does not activate player one");
        InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update(); InputSystem.RemoveDevice(pad);
        Invoke(second.GetComponent<WeaponInventory>(), "SetPaused", true); yield return .1f;
        Check(Time.timeScale == 0, "Local pause stops the shared world"); Invoke(second.GetComponent<WeaponInventory>(), "SetPaused", false);
        Check(Time.timeScale == 1, "Local pause resumes the shared world");
        GameSceneFlow.Load(GameSceneFlow.MenuScene); yield return 1.5f;
        Check(CoopSession.Mode == CoopMode.SinglePlayer && Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).Length == 0, "Leaving local cooperation cleans up actors and session state");
        Check(CoopSession.StartHost("Map1", "ranger", 0, "review", out var error), "Host opens a real TCP room: " + error);
        ready = Ready(); while (ready.MoveNext()) yield return ready.Current;
        using (var client = new CoopTransport())
        {
            client.Connect("127.0.0.1", CoopSession.Instance.ListeningPort);
            yield return .2f;
            Send(client, new CoopPacket { kind = "hello", scene = "Map1", character = "medic", password = "review" });
            int peer = 0; bool stateReceived = false, meshReceived = false;
            double until = EditorApplication.timeSinceStartup + 8;
            while (peer == 0 || !stateReceived)
            {
                while (client.TryPoll(out var item)) if (item.text != null)
                {
                    var packet = JsonUtility.FromJson<CoopPacket>(item.text);
                    if (packet.kind == "welcome") peer = packet.id;
                    if (packet.kind == "state") stateReceived = true;
                }
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Host handshake"); yield return .1f;
            }
            Check(peer > 0 && Actor(peer) != null && !Actor(peer).Local, "Handshake creates a separately controlled remote actor");
            var remote = Actor(peer); Vector3 start = remote.transform.position;
            Send(client, new CoopPacket { kind = "input", input = new CoopInputFrame { move = Vector2.right, yaw = 0, pitch = 0 } }); yield return .2f;
            Check(remote.transform.position.x > start.x + .3f, "The host simulates remote movement from input frames");
            Send(client, new CoopPacket { kind = "input", input = new CoopInputFrame { firstPerson = false, yaw = 70, pitch = 25 } }); yield return .1f;
            Check(!remote.GetComponent<CameraSwitch>().IsFirstPerson && remote.GetComponent<CameraSwitch>().tpsController.GetYaw() == 70,
                "Remote third-person view and aiming angles reach the server");
            Send(client, new CoopPacket { kind = "input", input = new CoopInputFrame { firstPerson = true } }); yield return .1f;
            var health = remote.GetComponent<PlayerHealth>(); health.AddExperience(1000); yield return .1f;
            Check(health.IsChoosingUpgrade && Time.timeScale == 1 && health.CoopUpgradeLabels().Length == 3, "Online upgrades offer choices without pausing teammates");
            Send(client, new CoopPacket { kind = "upgrade", value = 0 }); yield return .1f;
            Check(!health.IsChoosingUpgrade, "Remote upgrade selection is applied by the host");
            int weaponCount = remote.GetComponent<WeaponInventory>().Weapons.Count;
            Send(client, new CoopPacket { kind = "command", action = "buy", value = (int)WeaponType.RPG }); yield return .1f;
            Check(remote.GetComponent<WeaponInventory>().Weapons.Count == weaponCount, "The server rejects purchases without enough gold");
            Send(client, new CoopPacket { kind = "command", action = "equip", value = 999, extra = 9 }); yield return .1f;
            Check(remote.GetComponent<WeaponInventory>().Weapons.Count == weaponCount, "Invalid equipment indices cannot change inventory");
            Check(CoopSession.ClosestLiving(remote.transform.position) == remote.transform, "Enemies can acquire a remote teammate");
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule); enemy.name = "Shared Enemy"; enemy.transform.position = new Vector3(10, 1, 10); enemy.AddComponent<EnemyHealth>();
            yield return .2f;
            until = EditorApplication.timeSinceStartup + 5;
            while (!meshReceived)
            {
                while (client.TryPoll(out var item)) if (item.text != null && JsonUtility.FromJson<CoopPacket>(item.text).kind == "mesh") meshReceived = true;
                if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("World geometry transfer"); yield return .1f;
            }
            Check(meshReceived, "Generated enemy geometry reaches a real connected peer");
            var hostMirror = CoopSession.Instance.GetComponent<CoopWorldMirror>();
            var receiver = new GameObject("World Mirror Fixture").AddComponent<CoopWorldMirror>();
            foreach (var mesh in hostMirror.Meshes) receiver.ReceiveMesh(mesh); receiver.Apply(hostMirror.Capture()); yield return .1f;
            Check(GameObject.Find("Host World Visuals") != null, "Received geometry builds shared world visuals");
            Object.Destroy(receiver.gameObject); Object.Destroy(enemy);
            Check(!client.Send(0, new string('x', CoopTransport.MaximumFrameBytes + 1)), "Oversized outgoing packets are rejected");
            client.Dispose(); yield return .3f;
            Check(Actor(peer) == null && CoopSession.Mode == CoopMode.Host, "Disconnect removes only the remote actor and keeps the host running");
        }
        using (var wrong = new CoopTransport())
        {
            wrong.Connect("127.0.0.1", CoopSession.Instance.ListeningPort); yield return .2f;
            Send(wrong, new CoopPacket { kind = "hello", scene = "Map1", character = "medic", password = "wrong" }); yield return .2f;
            bool rejected = false;
            while (wrong.TryPoll(out var item)) if (item.text != null && JsonUtility.FromJson<CoopPacket>(item.text).kind == "reject") rejected = true;
            Check(rejected && CoopSession.Instance.PlayerCount == 1, "Incorrect room passwords receive a rejection without spawning actors");
        }
        var fullRoom = new List<CoopTransport>();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var guest = new CoopTransport(); fullRoom.Add(guest); guest.Connect("127.0.0.1", CoopSession.Instance.ListeningPort);
                yield return .2f; Send(guest, new CoopPacket { kind = "hello", scene = "Map1", character = "medic", password = "review" }); yield return .15f;
            }
            Check(CoopSession.Instance.PlayerCount == 4, "A host and three guests fill the four-player room");
            using (var extra = new CoopTransport())
            {
                extra.Connect("127.0.0.1", CoopSession.Instance.ListeningPort); yield return .2f;
                Send(extra, new CoopPacket { kind = "hello", scene = "Map1", character = "medic", password = "review" }); yield return .2f;
                bool rejected = false;
                while (extra.TryPoll(out var item)) if (item.text != null && JsonUtility.FromJson<CoopPacket>(item.text).kind == "reject") rejected = true;
                Check(rejected && CoopSession.Instance.PlayerCount == 4, "A fifth player receives a full-room response without spawning");
            }
        }
        finally { foreach (var guest in fullRoom) guest.Dispose(); }
        yield return .3f;
        GameSceneFlow.Load(GameSceneFlow.MenuScene); yield return 1.5f;
        Check(CoopSession.Mode == CoopMode.SinglePlayer, "Returning from hosting closes the room");
        Check(errors.Count == 0, "No unexpected Unity errors: " + string.Join(" | ", errors));
    }
}
