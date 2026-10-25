using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// RunHost and RunClient belong in two separate isolated Unity projects with a common parent folder.
[InitializeOnLoad]
public static class CoopNetworkPairReview
{
    const string Role = "CoopNetworkPairReview.Role";
    static string role;
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static readonly List<string> errors = new List<string>();
    static string Shared => Path.GetFullPath(Path.Combine(Application.dataPath, "../../"));
    static CoopNetworkPairReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void RunHost() => Run("host");
    public static void RunClient() => Run("client");
    static void Run(string value)
    {
        LoginSceneSetup.PlayFromCurrentScene(); CoopModeReview.BuildFixtures(); SessionState.SetString(Role, value); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        role = SessionState.GetString(Role, ""); if (role.Length == 0) return;
        SessionState.SetString(Role, ""); assertions = 0; errors.Clear(); checks = role == "host" ? Host() : Client();
        deadline = EditorApplication.timeSinceStartup + .2; Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    { if ((type == LogType.Error || type == LogType.Exception) && !message.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(message); }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext()) { Finish(0, "PASS " + assertions + " independent " + role + " checks"); return; }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .1f);
        }
        catch (Exception ex) { Debug.LogException(ex); Finish(1, "FAIL " + ex); }
    }
    static void Finish(int code, string result)
    {
        File.WriteAllText(Path.Combine(Shared, "coop-pair-" + role + "-result.txt"), result);
        CoopSession.Leave(); Time.timeScale = 1; Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code);
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); assertions++; Debug.Log("PASS " + message); }
    static IEnumerator Wait(Func<bool> condition, float seconds = 60)
    {
        double until = EditorApplication.timeSinceStartup + seconds;
        while (!condition()) { if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Independent " + role + " review"); yield return .1f; }
    }
    static CoopActor Local => Array.Find(Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None), actor => actor.Local);
    static IEnumerator Host()
    {
        Check(CoopSession.StartHost("Map1", "ranger", 0, "pair", out var error), "A separate process hosts a room: " + error);
        var wait = Wait(() => CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading);
        while (wait.MoveNext()) yield return wait.Current;
        var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule); enemy.name = "Pair Shared Enemy";
        enemy.transform.position = new Vector3(15, 1, 15); enemy.AddComponent<EnemyHealth>();
        File.WriteAllText(Path.Combine(Shared, "coop-pair-port.txt"), CoopSession.Instance.ListeningPort.ToString());
        wait = Wait(() => CoopSession.Instance.PlayerCount == 2); while (wait.MoveNext()) yield return wait.Current;
        var remote = Array.Find(Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None), actor => !actor.Local);
        Check(remote.GetComponent<CharacterSkills>().Character.Id == "medic", "The remote process chooses its own character");
        remote.GetComponent<WeaponInventory>().AddGold(2000);
        Vector3 start = remote.transform.position;
        wait = Wait(() => remote != null && Vector3.Distance(remote.transform.position, start) > .4f); while (wait.MoveNext()) yield return wait.Current;
        Check(true, "Keyboard input from another process moves the server-side actor");
        wait = Wait(() => remote.GetComponent<WeaponInventory>().Weapons.Count == 2); while (wait.MoveNext()) yield return wait.Current;
        Check(remote.GetComponent<WeaponInventory>().Gold == 1700 && remote.GetComponent<WeaponInventory>().Weapons[1].weaponType == WeaponType.RPG,
            "The host validates an RPC purchase and charges its own price");
        remote.GetComponent<PlayerHealth>().AddExperience(1000);
        wait = Wait(() => !remote.GetComponent<PlayerHealth>().IsChoosingUpgrade); while (wait.MoveNext()) yield return wait.Current;
        Check(Time.timeScale == 1, "Client upgrade selection does not freeze the host");
        wait = Wait(() => File.Exists(Path.Combine(Shared, "coop-pair-client-ready.txt"))); while (wait.MoveNext()) yield return wait.Current;
        Check(errors.Count == 0, "The host has no unexpected Unity errors: " + string.Join(" | ", errors));
        // Disconnect deliberately; the client must close the room and return to MainMenu.
        CoopSession.Leave(); yield return .3f;
    }
    static IEnumerator Client()
    {
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        GameSettings.Load("CoopNetworkPairReview.Client.", false);
        var controls = GameSettings.Current.Clone();
        GameControls.TryBind(controls.bindings, 0, GameAction.Right, (int)KeyCode.L, out _);
        GameControls.TryBind(controls.bindings, 0, GameAction.Skill, (int)KeyCode.F, out _);
        GameSettings.Apply(controls, false);
        var wait = Wait(() => File.Exists(Path.Combine(Shared, "coop-pair-port.txt"))); while (wait.MoveNext()) yield return wait.Current;
        int port = int.Parse(File.ReadAllText(Path.Combine(Shared, "coop-pair-port.txt")));
        string failure = null; bool connected = false;
        CoopSession.Join("Map1", "medic", "127.0.0.1", port, "pair", () => connected = true, error => failure = error);
        wait = Wait(() => failure != null || connected && CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading);
        while (wait.MoveNext()) yield return wait.Current;
        Check(failure == null && CoopSession.IsClient, "A separate process completes the real client loading flow: " + failure);
        wait = Wait(() => CoopSession.Instance.PlayerCount == 2); while (wait.MoveNext()) yield return wait.Current;
        Check(Local.Id > 0 && Local.GetComponent<CharacterSkills>().Character.Id == "medic", "The assigned remote ID belongs to the local client actor");
        Check(Local.GetComponent<Rigidbody>().isKinematic && !Local.GetComponent<PickupMagnet>().enabled, "The client cannot simulate authoritative movement or collect duplicate loot");
        Check(GameProgressSave.Instance == null && Local.GetComponent<GameProgressSave>().Ready, "Client loading completes without taking ownership of a local save");
        wait = Wait(() => GameObject.Find("Host World Visuals") != null); while (wait.MoveNext()) yield return wait.Current;
        Check(GameObject.Find("Host World Visuals").transform.childCount > 0, "Host world geometry survives client scene loading");
        var keyboard = InputSystem.AddDevice<Keyboard>(); Vector3 start = Local.transform.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.L)); InputSystem.Update(); yield return .6f;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        Check(Vector3.Distance(Local.transform.position, start) > .4f, "Rebound client input returns as an authoritative, interpolated pose");
        Check(GameControls.Label(0, GameAction.Right) == "L" && GameControls.Label(0, GameAction.Skill) == "F",
            "Host snapshots preserve the client's independent control bindings");
        var inventory = Local.GetComponent<WeaponInventory>();
        wait = Wait(() => inventory.Gold == 2000); while (wait.MoveNext()) yield return wait.Current;
        inventory.ApplyCoopCommand("buy", (int)WeaponType.RPG, 0);
        wait = Wait(() => inventory.Weapons.Count == 2); while (wait.MoveNext()) yield return wait.Current;
        Check(inventory.Gold == 1700 && inventory.Weapons[1].weaponType == WeaponType.RPG, "Client inventory reflects the server purchase");
        var health = Local.GetComponent<PlayerHealth>();
        wait = Wait(() => health.IsChoosingUpgrade); while (wait.MoveNext()) yield return wait.Current;
        Check(Time.timeScale == 1, "Client upgrade modal keeps the online world running");
        CoopSession.Upgrade(health, 0);
        wait = Wait(() => !health.IsChoosingUpgrade); while (wait.MoveNext()) yield return wait.Current;
        InputSystem.RemoveDevice(keyboard);
        Check(errors.Count == 0, "The client has no unexpected Unity errors before disconnect: " + string.Join(" | ", errors));
        File.WriteAllText(Path.Combine(Shared, "coop-pair-client-ready.txt"), "ready");
        wait = Wait(() => CoopSession.Mode == CoopMode.SinglePlayer && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu" && !GameSceneFlow.IsLoading);
        while (wait.MoveNext()) yield return wait.Current;
        Check(!string.IsNullOrEmpty(CoopSession.LastError) && Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).Length == 0,
            "Host disconnect returns the client to MainMenu and removes old actors");
        Check(errors.Count == 0, "The client has no unexpected Unity errors after disconnect: " + string.Join(" | ", errors));
    }
}
