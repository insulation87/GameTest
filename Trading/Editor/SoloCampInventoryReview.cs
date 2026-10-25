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

// Explicit Play Mode integration entry point, restricted to isolated projects.
[InitializeOnLoad]
public static class SoloCampInventoryReview
{
    const string Pending = "SoloCampInventoryReview.Pending";
    static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../SoloCampInventoryReview-20261025"));
    static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<string> results = new List<string>(), errors = new List<string>();
    static Keyboard keyboard;
    static double next, timeout;
    static int frame;
    static SoloCampInventoryReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').Contains("/Builds/")) throw new Exception("Isolated project required");
        Directory.CreateDirectory(Output); PlayerSettings.companyName = "GameTestReview";
        PlayerSettings.productName = "SoloCampInventoryReview20261024"; AssetDatabase.SaveAssets();
        LoginSceneSetup.PlayFromCurrentScene(); EditorSceneManager.OpenScene("Assets/Scenes/Login.unity");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); results.Clear(); errors.Clear(); routines.Clear();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        GameSettings.Load("SoloCampInventoryReview.", false);
        var settings = GameSettings.Current.Clone(); settings.bindings = GameControls.Defaults(); GameSettings.Apply(settings, false);
        keyboard = InputSystem.AddDevice<Keyboard>(); routines.Push(Checks()); timeout = EditorApplication.timeSinceStartup + 240;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string text, string stack, LogType kind)
    {
        if ((kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) && !text.StartsWith("Failed to confine the cursor") &&
            !text.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(text + "\n" + stack);
    }
    static void Tick()
    {
        if (Time.frameCount < frame || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("Solo camp inventory review");
            if (routines.Count == 0) { Check(errors.Count == 0, "No unexpected runtime errors: " + string.Join(" | ", errors)); Finish(0, "PASS solo camp and inventory Play Mode review"); return; }
            var current = routines.Peek(); if (!current.MoveNext()) { routines.Pop(); return; }
            if (current.Current is IEnumerator child) routines.Push(child);
            frame = Time.frameCount + 2; next = EditorApplication.timeSinceStartup + (current.Current is float delay ? delay : .04f);
        }
        catch (Exception ex) { Finish(1, "FAIL " + ex + "\n" + string.Join("\n", errors)); }
    }
    static void Finish(int code, string message)
    {
        results.Add(message); File.WriteAllLines(Path.Combine(Output, "solo-result.txt"), results);
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard); Time.timeScale = 1; EditorApplication.Exit(code);
    }
    static void Check(bool pass, string description)
    { if (!pass) throw new Exception(description); results.Add("PASS " + description); Debug.Log("PASS " + description); }
    static IEnumerator Wait(Func<bool> ready)
    { double end = EditorApplication.timeSinceStartup + 45; while (!ready()) { if (EditorApplication.timeSinceStartup > end) throw new TimeoutException("Waiting in " + SceneManager.GetActiveScene().name); yield return .05f; } yield return .2f; }
    static IEnumerator Scene(string name) => Wait(() => SceneManager.GetActiveScene().name == name && !GameSceneFlow.IsLoading);
    static TradingHubController Hub => Object.FindFirstObjectByType<TradingHubController>();
    static void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
    static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static void VisitorAt(Vector3 position)
    { var walker = Hub.Visitor.GetComponent<CharacterController>(); walker.enabled = false; walker.transform.position = position; walker.enabled = true; Physics.SyncTransforms(); }
    static IEnumerator Choose(string role, int choice)
    {
        Hub.OpenSoloCharacters(); var menu = Hub.GetComponentInChildren<CharacterSelectionMenu>(true);
        menu.Select(Array.FindIndex(CharacterRoster.All, c => c.Id == role));
        menu.GetComponentsInChildren<Button>(true).Single(b => b.name == "以此角色出發").onClick.Invoke(); yield return .15f;
        var buttons = (List<Button>)typeof(TradingHubController).GetField("campUpgradeButtons", Flags).GetValue(Hub);
        Check(buttons.Select((b, i) => b.GetComponentInChildren<Text>().text == CharacterSkillUpgrades.Label(role, i)).All(v => v), role + " solo camp choices match its skills");
        buttons[choice].onClick.Invoke(); yield return .2f;
    }
    static IEnumerator Portal(int index)
    { VisitorAt(TradingHubController.PortalPosition(index) + new Vector3(0, .2f, -2)); Hub.Interact(); yield return .2f; Hub.EnterPortal(); yield return Scene(ExpeditionMaps.All[index].Scene); yield return Wait(() => GameProgressSave.Instance != null && GameProgressSave.Instance.Ready); yield return .7f; }
    static IEnumerator Checks()
    {
        Check(CloudAccountService.EnsureInstance().EnterGuest(), "Use isolated guest save directory"); yield return Scene("MainMenu");
        Object.FindFirstObjectByType<MainMenuController>().BeginNewGame(); yield return Scene("TradingHub");
        int retainedGold = Hub.Stock.Inventory.gold;
        Check(Hub.Stock.RequiresCampPreparation && Object.FindFirstObjectByType<Player>() == null, "New solo game enters camp before role selection or combat map");
        var selection = Hub.GetComponentInChildren<CharacterSelectionMenu>(true);
        Check(selection.gameObject.activeSelf, "Role selection appears inside the solo camp");
        selection.GetComponentsInChildren<Button>(true).Single(b => b.name == "返回主選單").onClick.Invoke();
        VisitorAt(TradingHubController.PortalPosition(0) + new Vector3(0, .2f, -2)); Hub.Interact(); yield return .2f;
        Check(SceneManager.GetActiveScene().name == "TradingHub" && !GameSceneFlow.IsLoading && selection.gameObject.activeSelf, "Portal requires role and opening skill selection");
        yield return Choose("gunner", 0);
        Check(!Hub.Stock.RequiresCampPreparation, "Solo opening preparation is complete");
        Hub.OpenSoloUpgrade(); yield return .2f;
        var choices = (List<Button>)typeof(TradingHubController).GetField("campUpgradeButtons", Flags).GetValue(Hub); choices[1].onClick.Invoke();
        yield return Portal(0);
        var player = Object.FindFirstObjectByType<Player>(); var skills = player.GetComponent<CharacterSkills>(); var health = player.GetComponent<PlayerHealth>(); var inventory = player.GetComponent<WeaponInventory>();
        Check(skills.Character.Id == "gunner" && skills.SkillHasteLevel == 1 && skills.SkillPowerLevel == 0, "Selected role and replaced opening upgrade enter Map1 exactly once");
        Check(!health.IsChoosingUpgrade && Time.timeScale == 1, "Map does not repeat the opening upgrade modal");
        var platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.name = "Inventory Movement Fixture";
        platform.transform.position = new Vector3(1000, 4999, 1000); platform.transform.localScale = new Vector3(40, 1, 40);
        Physics.SyncTransforms(); player.Teleport(new Vector3(1000, 5001, 1000), Quaternion.identity); yield return 1f;
        Vector3 start = player.transform.position;
        Keys(Key.E, Key.D); yield return .2f; Keys(Key.D); yield return .7f;
        Check(inventory.IsOpen && Vector3.Distance(start, player.transform.position) > .6f && Time.timeScale == 1,
            "E opens the bag while held movement continues in solo gameplay (open=" + inventory.IsOpen + ", distance=" + Vector3.Distance(start, player.transform.position) +
            ", timeScale=" + Time.timeScale + ", player=" + player.enabled + ", choosing=" + health.IsChoosingUpgrade + ", input=" + GameControls.Movement(0) + ")");
        Check(Cursor.lockState == CursorLockMode.None, "Bag frees cursor for item interaction");
        Check(!skills.TryUseSkill(), "Bag prevents accidentally casting a skill while using its UI");
        Keys(Key.E, Key.D); yield return .2f; Keys(Key.D); start = player.transform.position; yield return .5f;
        Check(!inventory.IsOpen && Vector3.Distance(start, player.transform.position) > .6f, "E closes the bag and movement continues");
        Keys(); yield return .15f; Keys(Key.Escape); yield return .2f; Keys(Key.D); yield return .4f;
        Check(inventory.IsPaused && Time.timeScale == 0, "Solo pause menu retains its pause behavior");
        Keys(Key.Escape); yield return .2f; Keys(); yield return .2f;
        health.AddExperience(600); yield return .2f;
        Check(health.IsChoosingUpgrade && health.CoopUpgradeLabels().All(label => Enumerable.Range(0, 3).Any(i => label.Contains(CharacterSkillUpgrades.Label("gunner", i)))), "Solo level upgrades use the chosen character skill pool");
        health.SelectCoopUpgrade(0); yield return .2f;
        inventory.AddGold(500); Check(GameProgressSave.Instance.SaveNow(), "Save actual solo map progress");
        string path = GameProgressSave.SavePath; GameProgressSave.TryRead(path, out var saved, out _);
        string roleProgress = JsonUtility.ToJson(saved.character), healthProgress = JsonUtility.ToJson(saved.health);
        GameSceneFlow.Load(GameSceneFlow.MenuScene); yield return Scene("MainMenu"); Object.FindFirstObjectByType<MainMenuController>().JoinGame(); yield return Scene("TradingHub");
        Check(!Hub.Stock.IsPreparing && Hub.Stock.CharacterId == "gunner" && Hub.Stock.Inventory.gold == retainedGold + 500, "Continue game returns to camp with the existing character and wallet");
        string checkpoint = File.ReadAllText(path);
        Check(!Hub.Stock.SelectCampUpgrade(2, out _) && File.ReadAllText(path) == checkpoint, "Continuing cannot claim another free opening upgrade");
        GameProgressSave.TryRead(path, out var retained, out _);
        Check(JsonUtility.ToJson(retained.character) == roleProgress && JsonUtility.ToJson(retained.health) == healthProgress, "Camp return preserves exact character skills and level progress");
        Check(Hub.Stock.Buy(HubMerchant.Supplies, 0, out _), "Camp supplies remain purchasable after continuing");
        yield return Portal(0); player = Object.FindFirstObjectByType<Player>(); skills = player.GetComponent<CharacterSkills>(); inventory = player.GetComponent<WeaponInventory>();
        Check(skills.SkillHasteLevel == 1 && skills.SkillPowerLevel == 1 && inventory.Gold == retainedGold + 470 && inventory.CaptureProgress().healthPacks > 0, "Portal resumes upgrades and purchased supplies without resetting the run");
        GameSceneFlow.Load(GameSceneFlow.MenuScene); yield return Scene("MainMenu"); Object.FindFirstObjectByType<MainMenuController>().BeginNewGame(); yield return Scene("TradingHub");
        Check(Hub.Stock.RequiresCampPreparation && Hub.Stock.Inventory.gold == retainedGold + 470, "New run requires new preparation and keeps earned gold");
        yield return Choose("medic", 2); yield return Portal(1);
        skills = Object.FindFirstObjectByType<Player>().GetComponent<CharacterSkills>();
        Check(SceneManager.GetActiveScene().name == "Map2" && skills.Character.Id == "medic" && skills.SkillMasteryLevel == 1, "Choose another map only after role and skill preparation");
    }
}
