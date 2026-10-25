using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicitly enters Play Mode in a separate project with its own product/save directory.</summary>
[InitializeOnLoad]
public static class TradingHubReview
{
    const string Pending = "TradingHubReview.Pending";
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static readonly List<string> errors = new List<string>();
    static readonly List<string> results = new List<string>();
    static double next, timeout;
    static int frame;
    static Keyboard keyboard;
    static TradingHubReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        if (!Application.isBatchMode || (Application.productName != "TradingHubReview20261023" && Application.productName != "MapSelectionReview20261023"))
            throw new InvalidOperationException("Run only in the separate TradingHubReview20261023 batch project.");
        string guestPath = Path.Combine(Application.persistentDataPath, "Guest", "progress-v1.json");
        foreach (string file in new[] { guestPath, guestPath + ".bak" }) if (File.Exists(file)) File.Delete(file);
        if (File.Exists(Path.Combine(Output, "trading-hub-result.txt"))) File.Delete(Path.Combine(Output, "trading-hub-result.txt"));
        LoginSceneSetup.PlayFromCurrentScene();
        EditorSceneManager.OpenScene("Assets/Scenes/Login.unity");
        var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.Show(); gameView.Focus();
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); errors.Clear(); results.Clear(); routines.Clear();
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>();
        routines.Push(Checks()); next = EditorApplication.timeSinceStartup + .5; frame = Time.frameCount + 3;
        timeout = EditorApplication.timeSinceStartup + 240;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Assert || type == LogType.Exception) &&
            !message.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(message + "\n" + stack);
    }
    static void Tick()
    {
        try {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("Camp Play Mode review exceeded 240 seconds.");
            if (EditorApplication.timeSinceStartup < next || Time.frameCount < frame) return;
            if (routines.Count == 0) { Finish(0, "PASS " + results.Count + " Play Mode checks"); return; }
            var routine = routines.Peek();
            if (!routine.MoveNext()) { routines.Pop(); return; }
            if (routine.Current is IEnumerator child) routines.Push(child);
            frame = Time.frameCount + 2;
            next = EditorApplication.timeSinceStartup + (routine.Current is float delay ? delay : .05f);
        }
        catch (Exception ex) { Finish(1, "FAIL " + ex + "\n" + string.Join("\n", errors)); }
    }
    static void Finish(int code, string summary)
    {
        File.WriteAllText(Path.Combine(Output, "trading-hub-result.txt"), summary + "\n" + string.Join("\n", results));
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        Time.timeScale = 1; EditorApplication.Exit(code);
    }
    static void Check(bool condition, string description)
    { if (!condition) throw new InvalidOperationException(description); results.Add(description); Debug.Log("PASS " + description); }
    static IEnumerator Until(Func<bool> ready, string description)
    {
        double deadline = EditorApplication.timeSinceStartup + 45;
        while (!ready()) { if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(description); yield return .1f; }
        yield return .2f;
    }
    static IEnumerator Scene(string name) => Until(() => SceneManager.GetActiveScene().name == name && !GameSceneFlow.IsLoading, "Loading " + name);
    static Button FindButton(string label) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
        .First(b => b.GetComponentInChildren<Text>() != null && b.GetComponentInChildren<Text>().text.Contains(label));
    static void Click(string text)
    {
        var button = FindButton(text); Check(button.interactable, "UI button is available: " + text);
        Canvas.ForceUpdateCanvases();
        var click = new PointerEventData(EventSystem.current) {
            position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position), button = PointerEventData.InputButton.Left
        };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(click, hits);
        Check(hits.Count > 0 && hits[0].gameObject == button.gameObject, "UI raycasts reach the intended button: " + text + " (hit " + (hits.Count > 0 ? hits[0].gameObject.name : "none") + ")");
        ExecuteEvents.Execute(button.gameObject, click, ExecuteEvents.pointerClickHandler);
    }
    static TradingHubController Hub => Object.FindFirstObjectByType<TradingHubController>();
    static void MoveTo(Vector3 position)
    {
        var controller = Hub.Visitor.GetComponent<CharacterController>(); controller.enabled = false;
        Hub.Visitor.position = position; controller.enabled = true;
    }
    static void Press(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
    static GameProgressState Read(string path)
    { if (!GameProgressSave.TryRead(path, out var data, out string error)) throw new Exception(error); return data; }
    static void Write(string path, GameProgressState data)
    { if (!GameProgressSave.TryWrite(path, data, out string error)) throw new Exception(error); }
    static GameProgressState Clone(GameProgressState data) => JsonUtility.FromJson<GameProgressState>(JsonUtility.ToJson(data));
    static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    static IEnumerator ChooseUpgrades()
    {
        var health = Object.FindFirstObjectByType<PlayerHealth>();
        while (health != null && health.IsChoosingUpgrade) {
            var buttons = (List<Button>)typeof(PlayerHealth).GetField("upgradeButtons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(health);
            Click(buttons[0].GetComponentInChildren<Text>().text); yield return .15f;
        }
    }

    static IEnumerator Checks()
    {
        Check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == "Assets/Scenes/TradingHub.unity"), "TradingHub is included in enabled build scenes");
        var account = CloudAccountService.EnsureInstance();
        Check(account.EnterGuest(), "Guest mode enters the actual login/menu flow without network requests");
        yield return Scene("MainMenu");
        // Only this product's isolated slot is cleared for the fresh-player scenario.
        string path = GameProgressSave.SavePath;
        Check(path.Contains(Application.productName), "Every gameplay save uses a separate review product directory");
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "01-menu.png")); yield return .4f;
        Click("旅人營地"); yield return .2f;
        var selection = Object.FindFirstObjectByType<CharacterSelectionMenu>();
        Check(selection != null, "A first visit opens the existing character selection UI");
        selection.Select(9); Click("以此角色出發");
        yield return Scene("TradingHub");
        Check(Hub != null && Hub.Stock.CharacterId == "ranger" && Hub.Stock.CanTrade && Hub.Stock.Inventory.gold == 0 && Read(path).preparingRun,
            "A new ranger reaches camp with zero gold and a resumable preparation checkpoint");
        Check(Object.FindFirstObjectByType<Player>() == null && Object.FindFirstObjectByType<GameProgressSave>() == null &&
            Object.FindFirstObjectByType<EnemyAI>() == null, "The camp contains no combat player, enemy AI, or map autosave writer");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "02-fresh-camp.png")); yield return .4f;
        Vector3 beforeMove = Hub.Visitor.position; Press(Key.D); yield return .35f; Press(); yield return .1f;
        Check(Hub.Visitor.position.x > beforeMove.x + .2f, $"WASD input moves the visitor through the running CharacterController ({beforeMove} -> {Hub.Visitor.position}, timeScale {Time.timeScale})");
        Hub.Interact();
        Check(!FindButton("互動  [F]").interactable, "Merchant/portal interactions are unavailable outside interaction range");
        MoveTo(new Vector3(-7, .2f, .6f)); yield return .15f; Press(Key.F); yield return .12f; Press(); yield return .15f;
        Check(FindButton("震地戰錘") != null, "F opens the nearby weapon merchant's shop");
        string freshCheckpoint = File.ReadAllText(path); Click("震地戰錘");
        Check(Hub.Stock.Inventory.gold == 0 && File.ReadAllText(path) == freshCheckpoint,
            "A new visitor cannot purchase without gold or modify the preparation checkpoint");
        Click("結束交易"); MoveTo(TradingHubController.PortalPosition(0) + new Vector3(0, .2f, -3)); Hub.Interact();
        yield return .15f;
        Check(SceneManager.GetActiveScene().name == "TradingHub", "Approaching the portal opens a confirmation and does not auto-enter gameplay");
        Click("繼續留在營地"); Hub.Interact(); yield return .15f; Click("進入遊戲"); yield return Scene("Map1");
        yield return Until(() => GameProgressSave.Instance != null && GameProgressSave.Instance.Ready, "Fresh map save initialization");
        yield return 1f;
        var player = Object.FindFirstObjectByType<Player>(); var inventory = player.GetComponent<WeaponInventory>();
        Check(player.GetComponent<CharacterSkills>().Character.Id == "ranger", "The portal preserves the selected character into actual Map1 gameplay");
        inventory.AddGold(2000);
        Check(GameProgressSave.Instance.SaveNow(), "Actual Map1 gameplay saves a funded inventory on safe ground");
        GameProgressState initial = Read(path); int gold = initial.inventory.gold;
        Check(GameSceneFlow.Load(GameSceneFlow.MenuScene), "Gameplay returns to the main menu"); yield return Scene("MainMenu");
        Click("旅人營地"); yield return Scene("TradingHub");
        Check(Hub.Stock.CanTrade && Hub.Stock.Inventory.gold == gold, "Revisiting the camp loads the existing single-player wallet");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "02-camp.png")); yield return .4f;
        MoveTo(new Vector3(-7, .2f, .6f)); Hub.Interact();
        yield return .15f;
        int oldWeapons = Hub.Stock.Inventory.weapons.Length; Click("星矢長弓");
        gold -= CharacterWeapons.ShopPrice(WeaponType.Longbow);
        Check(Hub.Stock.Inventory.gold == gold && Hub.Stock.Inventory.weapons.Length == oldWeapons + 1,
            "The weapon merchant debits the exact price and grants one weapon");
        Check(Hub.Stock.WeaponPurchases == 1 && !FindButton("晨光法杖").interactable,
            "One successful purchase advances persistent shop progress but keeps the two-purchase weapon locked");
        Check(!Hub.Stock.Buy(HubMerchant.Weapons, 3, out _) && Hub.Stock.Inventory.gold == gold && Hub.Stock.WeaponPurchases == 1,
            "Locked weapons cannot be purchased through the transaction API or advance the unlock counter");
        Click("震地戰錘"); gold -= 100;
        Check(Hub.Stock.WeaponPurchases == 2 && FindButton("晨光法杖").interactable,
            "The second purchase immediately unlocks the next weapon in the actual shop UI");
        yield return .1f; Click("晨光法杖"); gold -= CharacterWeapons.ShopPrice(WeaponType.LightStaff);
        ScreenCapture.CaptureScreenshot(Path.Combine(Output, "03-shop.png")); yield return .4f;
        Click("結束交易"); MoveTo(new Vector3(7, .2f, .6f)); Hub.Interact();
        yield return .15f;
        int oldArmor = Hub.Stock.Inventory.armorItems?.Length ?? 0; Click("頭盔"); gold -= 100;
        Check(Hub.Stock.Inventory.gold == gold && Hub.Stock.Inventory.armorItems.Length == oldArmor + 1,
            "A different NPC sells armor using the same wallet");
        Click("結束交易"); MoveTo(new Vector3(-7, .2f, -7.4f)); Hub.Interact();
        yield return .15f;
        int oldPacks = Hub.Stock.Inventory.healthPacks, oldCapacity = Hub.Stock.Inventory.capacity;
        Click("血包 ×1"); gold -= 30; Click("背包擴充"); gold -= 120;
        Check(Hub.Stock.Inventory.gold == gold && Hub.Stock.Inventory.healthPacks == oldPacks + 1 && Hub.Stock.Inventory.capacity == oldCapacity + 5,
            "The supply NPC sells health packs and backpack space");
        var afterTrade = Read(path);
        Check(afterTrade.ground == initial.ground && afterTrade.scene == initial.scene && afterTrade.worldLayoutVersion == initial.worldLayoutVersion &&
            afterTrade.floorKeys.SequenceEqual(initial.floorKeys) && JsonUtility.ToJson(afterTrade.health) == JsonUtility.ToJson(initial.health) &&
            JsonUtility.ToJson(afterTrade.expedition) == JsonUtility.ToJson(initial.expedition), "Trades preserve map position, health, keys, and expedition progress");
        Click("結束交易"); Click("返回主選單"); yield return Scene("MainMenu"); Click("旅人營地"); yield return Scene("TradingHub");
        Check(Hub.Stock.Inventory.gold == gold && Hub.Stock.Inventory.weapons.Length == oldWeapons + 3 && Hub.Stock.WeaponPurchases == 3,
            "Completed trades survive camp/menu/camp scene reloads");
        MoveTo(TradingHubController.PortalPosition(0) + new Vector3(0, .2f, -3)); Hub.Interact(); yield return .15f; Click("進入遊戲"); yield return Scene("Map1");
        yield return Until(() => GameProgressSave.Instance != null && GameProgressSave.Instance.Ready, "Restored map inventory");
        player = Object.FindFirstObjectByType<Player>(); inventory = player.GetComponent<WeaponInventory>();
        Check(inventory.Gold == gold && inventory.Weapons.Count == oldWeapons + 3 && inventory.Armors.Count == oldArmor + 1 &&
            inventory.CaptureProgress().healthPacks == oldPacks + 1 && inventory.capacity == oldCapacity + 5,
            "The portal restores all purchased equipment, packs, space, and remaining gold in actual Map1");
        Check(player.GetComponent<CharacterSkills>().Character.Id == "ranger", "Saved character identity survives the return portal");
        Check(inventory.WeaponPurchaseCount == 3, "Map1 restores the same lifetime weapon-purchase count as the camp");
        yield return ChooseUpgrades();
        Call(inventory, "SetPaused", true); Call(inventory, "ShowPauseShop"); yield return .2f;
        Check(!FindButton("幽影雙匕").interactable, "The pause shop uses the same four-purchase weapon lock");
        Call(inventory, "BuyWeapon", WeaponType.DualDaggers, CharacterWeapons.ShopPrice(WeaponType.DualDaggers));
        Check(inventory.Gold == gold && inventory.WeaponPurchaseCount == 3, "The pause-shop purchase handler also rejects locked weapons without charging");
        Click("震地戰錘"); gold -= 100;
        Check(inventory.WeaponPurchaseCount == 4 && FindButton("幽影雙匕").interactable, "Pause-shop purchases count toward the same progressive unlocks");
        var networkInventory = inventory.CaptureProgress();
        inventory.RestoreShopProgress(0); inventory.RestoreProgress(networkInventory);
        Check(inventory.WeaponPurchaseCount == 4 && FindButton("幽影雙匕").interactable,
            "Inventory snapshot capture/restore carries weapon unlock progress through the cooperative synchronization path");
        inventory.CloseMenusForGameplay();
        Check(GameProgressSave.Instance.SaveNow() && GameSceneFlow.Load(GameSceneFlow.MenuScene), "The traded inventory can be saved from gameplay");
        yield return Scene("MainMenu"); Click("繼續遊戲"); yield return Scene("Map1");
        Check(Object.FindFirstObjectByType<Player>().GetComponent<WeaponInventory>().Gold == gold,
            "The original direct continue-game entry still restores the traded inventory");
        player = Object.FindFirstObjectByType<Player>(); inventory = player.GetComponent<WeaponInventory>();
        string[] previousWeaponIds = inventory.Weapons.Select(w => w.itemId).ToArray();
        inventory.AddGold(37); gold += 37;
        DungeonVictoryUI.Show(player.GetComponent<PlayerHealth>()); yield return .7f;
        Click("返回主選單"); yield return Scene("MainMenu");
        Check(Read(path).runEnded && !GameProgressSave.HasActiveRun && Read(path).inventory.gold == gold && Read(path).weaponPurchases == 4,
            "Ending a victorious run settles gold and unlock count and marks the equipment as belonging to the finished run");
        Click("旅人營地"); yield return .2f;
        Object.FindFirstObjectByType<CharacterSelectionMenu>().Select(0); Click("以此角色出發"); yield return Scene("TradingHub");
        Check(Hub.Stock.Inventory.gold == gold && Hub.Stock.WeaponPurchases == 4 && Hub.Stock.Inventory.weapons.Length == 1 &&
            Hub.Stock.Inventory.weapons[0].weaponType == WeaponType.WarHammer && Hub.Stock.Inventory.armorItems.Length == 0 &&
            Hub.Stock.Inventory.healthPacks == 0 && Hub.Stock.Inventory.capacity == 20 && !previousWeaponIds.Contains(Hub.Stock.Inventory.weapons[0].itemId),
            "The next run retains only gold/unlocks and resets weapons, armor, packs, and backpack to the new character's starting loadout");
        MoveTo(new Vector3(-7, .2f, .6f)); Hub.Interact(); yield return .15f;
        Check(FindButton("幽影雙匕").interactable, "Previously earned weapon unlocks remain available after a run ends");
        Click("晨光法杖"); gold -= CharacterWeapons.ShopPrice(WeaponType.LightStaff);
        string preparedWeapon = Hub.Stock.Inventory.weapons.Last().itemId;
        Click("結束交易"); Click("返回主選單"); yield return Scene("MainMenu");
        Check(Read(path).preparingRun && Read(path).weaponPurchases == 5, "Pre-run purchases and unlocks persist even if the camp is left before entering the portal");
        Click("繼續遊戲"); yield return Scene("TradingHub");
        Check(Hub.Stock.Inventory.weapons.Any(w => w.itemId == preparedWeapon), "Continuing a preparation returns to camp with its purchased equipment for map selection");
        MoveTo(TradingHubController.PortalPosition(0) + new Vector3(0, .2f, -3)); Hub.Interact(); yield return .15f; Click("進入遊戲"); yield return Scene("Map1");
        inventory = Object.FindFirstObjectByType<Player>().GetComponent<WeaponInventory>();
        Check(inventory.Gold == gold && inventory.WeaponPurchaseCount == 5 && inventory.Weapons.Any(w => w.itemId == preparedWeapon) &&
            !inventory.Weapons.Any(w => previousWeaponIds.Contains(w.itemId)) && !Read(path).preparingRun,
            "A resumed preparation enters fresh Map1 with its newly bought weapon and no equipment IDs from the previous run");
        Check(GameProgressSave.Instance.SaveNow() && GameSceneFlow.Load(GameSceneFlow.MenuScene), "The second run returns to the menu");
        yield return Scene("MainMenu"); Click("開始新遊戲"); Click("重設本輪"); yield return .2f;
        Object.FindFirstObjectByType<CharacterSelectionMenu>().Select(9); Click("以此角色出發"); yield return Scene("TradingHub");
        Check(Hub.Stock.Inventory.gold == gold && Hub.Stock.WeaponPurchases == 5 && Hub.Stock.Inventory.weapons.Length == 1 &&
            Hub.Stock.Inventory.weapons[0].weaponType == WeaponType.Longbow,
            "Explicit New Game starts at camp, resets the old run's weapons, and preserves the bank and shop unlock progress");
        MoveTo(TradingHubController.PortalPosition(0) + new Vector3(0, .2f, -3)); Hub.Interact(); yield return .15f; Click("進入遊戲"); yield return Scene("Map1");
        var currentPlayer = Object.FindFirstObjectByType<Player>(); inventory = currentPlayer.GetComponent<WeaponInventory>();
        inventory.AddGold(19); gold += 19;
        var hp = currentPlayer.GetComponent<PlayerHealth>(); hp.respawnDelay = 30;
        // Bypass only the brief spawn invulnerability; use the real death handler and quit-save hook.
        typeof(PlayerHealth).GetField("invincibleUntil", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(hp, -1f);
        hp.TakeDamage(100000); yield return .1f;
        Check(hp.IsDead && !GameProgressSave.Instance.SaveNow(), "A real player death prevents overwriting the living map snapshot");
        Call(GameProgressSave.Instance, "OnApplicationQuit");
        Check(Read(path).inventory.gold == gold && Read(path).weaponPurchases == 5 && Read(path).health.health > 0,
            "The quit-save hook retains the latest gold/unlocks even when the player is dead, without saving a dead health snapshot");

        // Edge cases run during Play Mode against independent save fixtures.
        string fixture = Path.Combine(Output, "fixture-progress.json"); var data = Clone(afterTrade);
        data.inventory.gold = 0; Write(fixture, data);
        var shop = new TradingHubStock(fixture, false, "ranger"); string unchanged = File.ReadAllText(fixture);
        Check(!shop.Buy(HubMerchant.Weapons, 0, out _) && shop.Inventory.gold == 0 && File.ReadAllText(fixture) == unchanged,
            "Insufficient gold leaves both the inventory and disk save unchanged");
        data.inventory.gold = 1000; data.inventory.healthPacks = 0; data.inventory.capacity = TradingHubStock.CountSlots(data.inventory); Write(fixture, data);
        shop = new TradingHubStock(fixture, false, "ranger");
        Check(!shop.Buy(HubMerchant.Weapons, 0, out _) && !shop.Buy(HubMerchant.Armor, 0, out _) && !shop.Buy(HubMerchant.Supplies, 0, out _) && shop.Inventory.gold == 1000,
            "A full backpack rejects weapons, armor, and the first health-pack stack without charging gold");
        Check(shop.Buy(HubMerchant.Supplies, 1, out _) && shop.Inventory.capacity == data.inventory.capacity + 5 && shop.Buy(HubMerchant.Supplies, 0, out _),
            "Backpack expansion works at full capacity and makes room for a health pack");
        data = Read(fixture); data.inventory.capacity = TradingHubStock.CountSlots(data.inventory); Write(fixture, data);
        shop = new TradingHubStock(fixture, false, "ranger");
        Check(shop.Buy(HubMerchant.Supplies, 0, out _), "Existing health packs stack even when all backpack slots are occupied");
        string failPath = Path.Combine(Output, "write-failure.json"); Write(failPath, afterTrade);
        shop = new TradingHubStock(failPath, false, "ranger");
        using (var locked = new FileStream(failPath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            int before = shop.Inventory.gold;
            Check(!shop.Buy(HubMerchant.Weapons, 0, out _) && shop.Inventory.gold == before && Read(failPath).inventory.gold == before,
                "A real locked-file write failure rolls back the whole transaction");
        }
        Write(fixture, afterTrade); Write(fixture, afterTrade); File.WriteAllText(fixture, "damaged primary");
        shop = new TradingHubStock(fixture, false, "ranger");
        Check(shop.CanTrade && shop.Buy(HubMerchant.Supplies, 0, out _) && Read(fixture).inventory.healthPacks == afterTrade.inventory.healthPacks + 1,
            "A damaged primary recovers from backup and commits a valid trade");
        File.WriteAllText(fixture, "damaged primary"); File.WriteAllText(fixture + ".bak", "damaged backup");
        shop = new TradingHubStock(fixture, false, "ranger");
        Check(!shop.CanTrade && !shop.Buy(HubMerchant.Supplies, 0, out _) && File.ReadAllText(fixture) == "damaged primary",
            "Unrecoverable saves disable trading and remain untouched");
        Check(errors.Count == 0, "No runtime errors or exceptions occurred during the scene/transaction flow");
    }
}
