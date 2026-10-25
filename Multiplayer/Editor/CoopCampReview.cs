using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit integration test entry points. Use separate projects under Builds only.
[InitializeOnLoad]
public static class CoopCampReview
{
    const string RoleKey = "CoopCampReview.Role";
    static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../SoloCampInventoryReview-20261025"));
    static readonly List<string> results = new List<string>(), errors = new List<string>();
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static string role;
    static double next, timeout;
    static int frame;
    static Keyboard keyboard;
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static CoopCampReview() { EditorApplication.playModeStateChanged += Changed; }
    static string Marker(string name) => Path.Combine(Output, name + ".txt");
    public static void RunHost() => Run("host");
    public static void RunClient() => Run("client");
    public static void RunSkills() => Run("skills");
    static void Run(string value)
    {
        if (!Application.dataPath.Replace('\\', '/').Contains("/Builds/")) throw new Exception("Isolated project required");
        Directory.CreateDirectory(Output);
        PlayerSettings.companyName = "GameTestReview"; PlayerSettings.productName = "CoopCampReview20261024" + value;
        AssetDatabase.SaveAssets();
        LoginSceneSetup.PlayFromCurrentScene();
        if (value == "skills") EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        else EditorSceneManager.OpenScene("Assets/Scenes/Login.unity");
        SessionState.SetString(RoleKey, value); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        role = SessionState.GetString(RoleKey, ""); if (role.Length == 0) return;
        SessionState.SetString(RoleKey, ""); results.Clear(); errors.Clear(); routines.Clear();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>();
        routines.Push(role == "skills" ? Skills() : Pair()); timeout = EditorApplication.timeSinceStartup + 360;
        next = EditorApplication.timeSinceStartup + .3; frame = Time.frameCount + 3;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string text, string stack, LogType kind)
    {
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) &&
            !text.Contains("burst JIT compiler") && !text.StartsWith("Failed to confine the cursor") && !stack.Contains("UnityEditor.Search")) errors.Add(text + "\n" + stack);
    }
    static void Tick()
    {
        if (Time.frameCount < frame || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("Coop camp " + role);
            if (routines.Count == 0) { Check(errors.Count == 0, "No unexpected runtime errors: " + string.Join(" | ", errors)); Finish(0, "PASS " + role + " Play Mode integration"); return; }
            var routine = routines.Peek();
            if (!routine.MoveNext()) { routines.Pop(); return; }
            if (routine.Current is IEnumerator child) routines.Push(child);
            frame = Time.frameCount + 2; next = EditorApplication.timeSinceStartup + (routine.Current is float delay ? delay : .03f);
        }
        catch (Exception ex) { Finish(1, "FAIL " + ex + "\n" + string.Join("\n", errors)); }
    }
    static void Finish(int code, string result)
    {
        results.Add(result); File.WriteAllLines(Marker(role + "-result"), results);
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        CoopSession.Leave(); Time.timeScale = 1; EditorApplication.Exit(code);
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); results.Add("PASS " + message); Debug.Log("PASS " + message); }
    static IEnumerator Wait(Func<bool> predicate)
    {
        double end = EditorApplication.timeSinceStartup + 70;
        while (!predicate()) { if (EditorApplication.timeSinceStartup > end) throw new TimeoutException(role + " waiting; scene=" + SceneManager.GetActiveScene().name); yield return .05f; }
        yield return .1f;
    }
    static void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
    static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static IEnumerator MoveTo(float x, float z)
    {
        double end = EditorApplication.timeSinceStartup + 25;
        while (true)
        {
            Vector3 position = CoopSession.Instance.LocalCampPlayer.position;
            if (Mathf.Abs(position.x - x) < .3f && Mathf.Abs(position.z - z) < .3f) break;
            if (EditorApplication.timeSinceStartup > end) throw new TimeoutException("Movement at " + position + " toward " + x + "," + z);
            var keys = new List<Key>();
            if (position.x < x - .2f) keys.Add(Key.D); if (position.x > x + .2f) keys.Add(Key.A);
            if (position.z < z - .2f) keys.Add(Key.W); if (position.z > z + .2f) keys.Add(Key.S);
            Keys(keys.ToArray()); yield return .04f;
        }
        Keys(); yield return .25f;
    }
    static IEnumerator Choose(string id, int upgrade)
    {
        var hub = Object.FindFirstObjectByType<TradingHubController>(); hub.OpenCampCharacters();
        var menu = hub.GetComponentInChildren<CharacterSelectionMenu>(true);
        menu.Select(Array.FindIndex(CharacterRoster.All, c => c.Id == id));
        menu.GetComponentsInChildren<Button>(true).Single(b => b.name == "以此角色出發").onClick.Invoke();
        yield return Wait(() => CoopSession.Instance.LocalCampPlayer.selected && CoopSession.Instance.LocalCampPlayer.character == id);
        var choices = (List<Button>)typeof(TradingHubController).GetField("campUpgradeButtons", Flags).GetValue(hub);
        Check(choices.Count == 3 && choices.All(b => b.GetComponentInChildren<Text>().text == CharacterSkillUpgrades.Label(id, choices.IndexOf(b))), id + " receives three character skill choices");
        choices[upgrade].onClick.Invoke();
        yield return Wait(() => CoopSession.Instance.LocalCampPlayer.upgrade == upgrade);
    }
    static IEnumerator Pair()
    {
        Check(CloudAccountService.EnsureInstance().EnterGuest(), "Isolated guest account");
        yield return Wait(() => !GameSceneFlow.IsLoading && SceneManager.GetActiveScene().name == "MainMenu");
        if (role == "host")
        {
            Check(CoopSession.StartHost("Map1", "breaker", 0, "camp", out var error), "Host starts: " + error);
            yield return Wait(() => CoopSession.InCamp && CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading);
            Check(SceneManager.GetActiveScene().name == "TradingHub" && Object.FindFirstObjectByType<Player>() == null, "Host enters peaceful camp before any combat player spawns");
            CoopSession.Instance.ConfirmCamp(true); yield return .3f;
            Check(CoopSession.InCamp, "A lone host cannot depart");
            yield return Choose("sentinel", 0);
            var hub = Object.FindFirstObjectByType<TradingHubController>(); hub.OpenCampUpgrade(); yield return .3f;
            var panel = (RectTransform)typeof(TradingHubController).GetField("campUpgradePanel", Flags).GetValue(hub);
            Check(panel.gameObject.activeInHierarchy, "Opening upgrade choices again remains interactive");
            var buttons = (List<Button>)typeof(TradingHubController).GetField("campUpgradeButtons", Flags).GetValue(hub);
            buttons[1].onClick.Invoke(); yield return Wait(() => CoopSession.Instance.LocalCampPlayer.upgrade == 1);
            hub.OpenCampUpgrade(); yield return .2f; buttons[0].onClick.Invoke();
            yield return Wait(() => CoopSession.Instance.LocalCampPlayer.upgrade == 0);
            File.WriteAllText(Marker("port"), CoopSession.Instance.ListeningPort.ToString());
        }
        else
        {
            yield return Wait(() => File.Exists(Marker("port")));
            bool connected = false; string error = null;
            CoopSession.Join("Map4", "ranger", "127.0.0.1", int.Parse(File.ReadAllText(Marker("port"))), "camp", () => connected = true, e => error = e);
            yield return Wait(() => error != null || connected && CoopSession.InCamp && CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading);
            Check(error == null && SceneManager.GetActiveScene().name == "TradingHub", "Independent client joins camp without requiring a matching preselected map: " + error);
            yield return Choose("ranger", 0);
            CoopSession.Instance.ChooseCampCharacter("medic");
            yield return Wait(() => CoopSession.Instance.LocalCampPlayer.character == "medic" && CoopSession.Instance.LocalCampPlayer.upgrade == -1);
            Check(!CoopSession.Instance.CampCanConfirm(out _), "Changing character resets the opening upgrade");
            yield return Choose("medic", 2);
        }
        yield return Wait(() => CoopSession.Instance.PlayerCount == 2 && CoopSession.Instance.CampPlayers.All(p => p.selected && p.upgrade >= 0));
        Check(Object.FindFirstObjectByType<TradingHubController>().GetComponentsInChildren<CharacterController>().Length == 2, "Both camp visitors appear");
        var session = CoopSession.Instance;
        session.ConfirmCamp(true); yield return .3f;
        Check(CoopSession.InCamp && !session.CampCanConfirm(out _), "Confirmation outside a map circle cannot start gameplay");
        yield return MoveTo(session.LocalCampPlayer.position.x, 8);
        yield return MoveTo(role == "host" ? -9.7f : -8.3f, 8);
        yield return MoveTo(role == "host" ? -9.7f : -8.3f, 11);
        File.WriteAllText(Marker(role + "-in-circle"), "ready");
        yield return Wait(() => File.Exists(Marker("host-in-circle")) && File.Exists(Marker("client-in-circle")) && session.CampCanConfirm(out _));
        Check(CoopSession.InCamp, "All players in the same circle still wait for host confirmation");
        if (role == "client")
        {
            session.ConfirmCamp(true); yield return .4f;
            Check(CoopSession.InCamp, "Client confirmation cannot launch the room");
            File.WriteAllText(Marker("client-cannot-launch"), "pass");
        }
        else
        {
            yield return Wait(() => File.Exists(Marker("client-cannot-launch")));
            yield return MoveTo(-9.7f, 7);
            Check(!session.CampCanConfirm(out _), "Leaving the circle cancels departure eligibility");
            session.ConfirmCamp(true); yield return .2f;
            Check(CoopSession.InCamp, "Host cannot launch while one player is outside");
            yield return MoveTo(-9.7f, 11);
            yield return Wait(() => session.CampCanConfirm(out _));
            session.ConfirmCamp(true);
        }
        yield return Wait(() => !CoopSession.InCamp && session.SceneReady && !GameSceneFlow.IsLoading && SceneManager.GetActiveScene().name == "Map1");
        var local = Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).Single(a => a.Local);
        var skills = local.GetComponent<CharacterSkills>();
        yield return Wait(() => skills.Character.Id == (role == "host" ? "sentinel" : "medic") && (role == "host" ? skills.SkillPowerLevel == 1 : skills.SkillMasteryLevel == 1));
        Check(Time.timeScale == 1, "Map loads without pausing the online world");
        var health = local.GetComponent<PlayerHealth>();
        Check(!health.IsChoosingUpgrade, "Opening camp upgrade is applied once without a duplicate level-two modal");
        if (role == "host")
        {
            foreach (var actor in Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None)) actor.GetComponent<PlayerHealth>().AddExperience(600);
        }
        yield return Wait(() => health.IsChoosingUpgrade);
        Check(health.CoopUpgradeLabels().All(label => Enumerable.Range(0, 3).Any(i => label.Contains(CharacterSkillUpgrades.Label(skills.Character.Id, i)))), "Later level upgrades contain only this character's skill effects");
        if (role == "host") health.SelectCoopUpgrade(1); else CoopSession.Upgrade(health, 1);
        yield return Wait(() => !health.IsChoosingUpgrade && skills.SkillHasteLevel == 1);
        Check(Mathf.Approximately(skills.SkillCooldown, skills.Character.Cooldown * .9f), "Chosen later upgrade changes actual skill cooldown and replicates");
        if (role == "host")
        {
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.name = "Bag Movement Platform";
            platform.transform.position = new Vector3(1000, 4999, 1000); platform.transform.localScale = new Vector3(40, 1, 40);
            Physics.SyncTransforms();
            foreach (var actor in Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None))
                actor.GetComponent<Player>().Teleport(new Vector3(1000 + (actor.Local ? -3 : 3), 5001, 1000), Quaternion.identity);
            File.WriteAllText(Marker("bag-platform"), "ready");
        }
        yield return Wait(() => File.Exists(Marker("bag-platform")) && local.transform.position.x > 900);
        yield return .8f;
        var inventory = local.GetComponent<WeaponInventory>(); var movementStart = local.transform.position;
        Keys(Key.E, Key.D); yield return .2f; Keys(Key.D); yield return .8f;
        Check(inventory.IsOpen && Vector3.Distance(movementStart, local.transform.position) > .6f && Time.timeScale == 1,
            role + " moves authoritatively while E inventory remains open");
        Keys(Key.E, Key.D); yield return .2f; Keys(); yield return .2f;
        Check(!inventory.IsOpen, role + " can close inventory with E after moving");
        File.WriteAllText(Marker(role + "-map-done"), "done");
        yield return Wait(() => File.Exists(Marker("host-map-done")) && File.Exists(Marker("client-map-done")));
        yield return .4f;
    }
    static IEnumerator Skills()
    {
        var node = new GameObject("Skill Upgrade Fixture"); node.transform.position = new Vector3(0, 10000, 0);
        node.AddComponent<BoxCollider>().center = Vector3.up;
        var health = node.AddComponent<PlayerHealth>(); health.enabled = false;
        var skills = node.AddComponent<CharacterSkills>();
        var camera = new GameObject("Skill Fixture Camera", typeof(Camera)).GetComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = node.transform.position + Vector3.up;
        yield return .2f;
        foreach (var definition in CharacterRoster.All)
        {
            skills.RestoreProgress(new CharacterProgress { version = 1, id = definition.Id });
            skills.ApplySkillUpgrade(0); skills.ApplySkillUpgrade(1); skills.ApplySkillUpgrade(2);
            float distance = definition.Id == "breaker" ? 7.5f : definition.Id == "frost" ? 9.5f : definition.Id == "reaper" ? 6.5f : definition.Id == "ranger" ? 27 : 3;
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule); target.transform.position = node.transform.position + Vector3.up + Vector3.forward * distance;
            var enemy = target.AddComponent<EnemyHealth>(); enemy.SetMaxHealth(1000); enemy.SetLevelAndExperience(1, 0); enemy.showDamageNumbers = false;
            typeof(PlayerHealth).GetField("currentHealth", Flags).SetValue(health, 20f);
            typeof(PlayerHealth).GetField("currentArmor", Flags).SetValue(health, 0f);
            Physics.SyncTransforms();
            Check(skills.TryUseSkill(), definition.Id + " upgraded skill casts in Play Mode");
            Check(Mathf.Approximately(skills.CooldownRemaining, definition.Cooldown * .9f), definition.Id + " cooldown upgrade affects the cast");
            if (definition.Id == "pyromancer" || definition.Id == "engineer") Invoke(skills, "Advance", .01f);
            float damage = definition.Id == "breaker" ? 96 : definition.Id == "frost" ? 42 : definition.Id == "reaper" ? 60 : definition.Id == "ranger" ? 216 : definition.Id == "pyromancer" ? 21.6f : definition.Id == "engineer" ? 42 : 0;
            if (damage > 0) Check(Mathf.Abs(enemy.CurrentHealth - (1000 - damage)) < .1f, definition.Id + " actual damage and upgraded reach match its skill");
            if (definition.Id == "sentinel") Check(Mathf.Approximately(health.CurrentArmor, 6) && skills.IncomingDamageMultiplier < .25f, "Sentinel upgrade restores more armor and improves protection");
            if (definition.Id == "gunner") Check(Mathf.Approximately(skills.FireIntervalMultiplier, .375f), "Gunner upgrade changes active fire interval");
            if (definition.Id == "medic") Check(Mathf.Approximately(health.CurrentHealth, 62), "Medic upgrade increases actual healing");
            if (definition.Id == "assassin") Check(Mathf.Approximately(skills.DamageMultiplier, 1.6f) && Mathf.Approximately(skills.MoveSpeedMultiplier, 1.9f), "Assassin upgrade improves its active damage and movement buffs");
            if (!CharacterSkillUpgrades.UsesRange(definition.Id)) Check(skills.EffectRemaining > (definition.Id == "engineer" ? 11.9f : definition.Id == "pyromancer" ? 9.5f : definition.Id == "assassin" ? 4.7f : 5.9f), definition.Id + " skill duration extends");
            var progress = skills.CaptureProgress(); skills.RestoreProgress(progress);
            Check(skills.SkillPowerLevel == 1 && skills.SkillHasteLevel == 1 && skills.SkillMasteryLevel == 1, definition.Id + " upgrade progress round-trips");
            Object.DestroyImmediate(target); yield return .03f;
        }
    }
}
