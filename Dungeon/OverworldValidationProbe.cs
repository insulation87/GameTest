#if UNITY_EDITOR || OVERWORLD_VALIDATION
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Test instrumentation is excluded from normal player builds. Requires an isolated-run marker.
public sealed class OverworldValidationProbe : MonoBehaviour
{
    string role, output;
    readonly List<string> results = new List<string>(), errors = new List<string>();
    readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    double nextTick, deadline;
    Keyboard keyboard;
    string singleSlotAtStart;
    byte[] originalSingleSlot;
    bool SingleSlotUnchanged => originalSingleSlot == null ? !File.Exists(singleSlotAtStart) :
        File.Exists(singleSlotAtStart) && File.ReadAllBytes(singleSlotAtStart).SequenceEqual(originalSingleSlot);
    static float GroundDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x,a.z), new Vector2(b.x,b.z));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
#if !UNITY_EDITOR
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-overworld-review");
        int path = Array.IndexOf(args, "-overworld-review-output");
        if (at >= 0 && at + 1 < args.Length && path >= 0 && path + 1 < args.Length)
            Begin(args[at + 1], args[path + 1]);
#endif
    }
    public static void Begin(string role, string output)
    {
        if (!File.Exists(Path.Combine(output, "authored-world-review.json")))
            throw new InvalidOperationException("Missing isolated verification marker.");
        var probe = new GameObject("Isolated Overworld Validation").AddComponent<OverworldValidationProbe>();
        DontDestroyOnLoad(probe.gameObject);
        probe.role = role; probe.output = output;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        GameSettings.Load("OverworldValidation." + role + ".", false);
        var settings = GameSettings.Current.Clone(); settings.bindings = GameControls.Defaults();
        GameSettings.Apply(settings, false);
        probe.deadline = Time.realtimeSinceStartupAsDouble + 180;
        Application.logMessageReceived += probe.Log;
        probe.routines.Push(probe.Run());
    }
    void Log(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search") || message.Contains("burst JIT compiler")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
    }
    void Update()
    {
        if (Time.realtimeSinceStartupAsDouble < nextTick || routines.Count == 0) return;
        try
        {
            if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Validation deadline: " + role);
            while (routines.Count > 0)
            {
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                nextTick = Time.realtimeSinceStartupAsDouble + (current.Current is float seconds ? seconds : .03f);
                return;
            }
            Check(errors.Count == 0, "No unexpected Unity gameplay errors: " + string.Join("; ", errors));
            Finish(0, "PASS");
        }
        catch (Exception ex) { Finish(1, "FAIL " + ex); }
    }
    void Finish(int code, string status)
    {
        Application.logMessageReceived -= Log;
        File.WriteAllText(Path.Combine(output, role + "-result.txt"), status + "\n" + string.Join("\n", results) + "\n" + string.Join("\n", errors));
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        CoopSession.Leave(); Time.timeScale = 1;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        results.Add(label); Debug.Log("PASS " + role + ": " + label);
    }
    IEnumerator Wait(Func<bool> condition, string description, float timeout = 60)
    {
        double until = Time.realtimeSinceStartupAsDouble + timeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException(role + ": " + description);
            yield return .05f;
        }
    }
    string FilePath(string name) => Path.Combine(output, name + ".txt");
    void Signal(string name, string data = "ready") => File.WriteAllText(FilePath(name), data);
    bool Signalled(string name) => File.Exists(FilePath(name));
    CoopActor Local => Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).First(a => a.Local);
    CoopActor Remote => Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).First(a => !a.Local);
    OverworldDirector World => Object.FindFirstObjectByType<OverworldDirector>();
    FloorProgressionManager Progress => FloorProgressionManager.Instance;
    RegionPortal Forward(int region) => Progress.Portals.First(p => p.SourceRegion == region && p.TargetRegion == region + 1);
    void Key(params Key[] keys)
    {
        if (keyboard == null) keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update();
    }
    void Place(Player player, Vector3 ground)
    {
        Check(player.TryGetTeleportPosition(ground, Quaternion.identity, out var safe), "Safe player placement at " + ground);
        player.Teleport(safe, Quaternion.identity);
    }
    void Collect(int floor, Player player)
    {
        var key = FloorKeyPickup.Create(player.transform.position + Vector3.up * .2f, floor, 15);
        Check(key.TryCollect(player.GetComponent<BoxCollider>()), "Real region " + floor + " pickup is collected by actor " + player.name);
        Check(!key.TryCollect(player.GetComponent<BoxCollider>()), "The same pickup cannot count twice");
    }
    sealed class FakeAuth : ICloudAuthTransport
    {
        readonly string id;
        public FakeAuth(string id) { this.id = id; }
        public IEnumerator Send(string method, string endpoint, string json, string token, Action<CloudAuthResponse> complete)
        {
            yield return null;
            string body;
            if (endpoint == "token?grant_type=password")
            {
                string claims = "{\"sub\":\"" + id + "\",\"exp\":" + DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() + "}";
                string jwt = "eyJhbGciOiJSRVZJRVcifQ." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(claims)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".isolated_review";
                body = "{\"access_token\":\"" + jwt + "\",\"refresh_token\":\"isolated-refresh\",\"expires_in\":3600}";
            }
            else if (endpoint == "user") body = "{\"id\":\"" + id + "\",\"email\":\"review@example.com\"}";
            else throw new InvalidOperationException("Unexpected test authentication endpoint: " + endpoint);
            complete(new CloudAuthResponse { status = 200, body = body });
        }
    }
    IEnumerator Run()
    {
        yield return .3f;
        Check(SceneManager.GetActiveScene().name == "Login" && Object.FindFirstObjectByType<LoginSceneController>() != null,
            "Actual Login scene and login UI initialize");
        var account = CloudAccountService.EnsureInstance();
        string id = Guid.NewGuid().ToString();
        account.Configure(new CloudAuthConfig { projectUrl = "https://isolated-overworld-review.invalid", publishableKey = "sb_publishable_isolated_test_only" }, new FakeAuth(id));
        if (role.StartsWith("guest"))
        {
            var guest = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.GetComponentInChildren<Text>()?.text == "以遊客身分遊玩");
            guest.onClick.Invoke();
            Check(account.IsGuest && !account.IsSignedIn && account.CanPlay, "Actual guest button enters guest mode without account authentication");
        }
        else Check(account.Login("review@example.com", "isolated-test-password"), "Test-only authentication starts without external requests");
        yield return Wait(() => account.CanPlay && SceneManager.GetActiveScene().name == "MainMenu" && !GameSceneFlow.IsLoading, "Account/guest to MainMenu");
        Check(Object.FindFirstObjectByType<MainMenuController>() != null, "Actual MainMenu survives the login transition");
        singleSlotAtStart = LocalAccountProgress.ActivePath;
        originalSingleSlot = File.Exists(singleSlotAtStart) ? File.ReadAllBytes(singleSlotAtStart) : null;
        if (role.StartsWith("guest"))
        {
            Check(LocalAccountProgress.ActivePath.Contains(Path.DirectorySeparatorChar + "Guest" + Path.DirectorySeparatorChar) && !LocalAccountProgress.HasAccount, "Guest saves are isolated from account and legacy progress");
            var friends = CloudFriendsService.Ensure();
            Check(!friends.Refresh() && !friends.Request("ABCDEF123456") && !friends.IsBusy, "Guest cannot query or invite cloud friends");
            var button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.GetComponentInChildren<Text>()?.text == "好友");
            Check(!button.interactable, "MainMenu disables friends for guests");
            string denied = null;
            CoopSession.StartInternetHost("Map1", "ranger", "", () => { }, e => denied = e);
            Check(denied != null && CoopSession.Mode == CoopMode.SinglePlayer, "Guest is refused Internet hosting at session API");
            CoopSession.JoinInternet("Map1", "ranger", "23456789AB", "", () => { }, e => denied = e);
            Check(denied != null && !CoopSession.IsInternet, "Guest is refused Internet joining at session API");
            Check(!LanAccess.IsLocal(System.Net.IPAddress.Parse("8.8.8.8")) && LanAccess.IsLocal(System.Net.IPAddress.Loopback), "LAN policy rejects public Internet IPs and accepts loopback");
            var canvas = button.GetComponentInParent<Canvas>();
            CaptureMenu(canvas, "guest-menu-" + role + ".png");
        }
        if (role == "relay-host" || role == "relay-client") yield return Social();
        if (role == "relay-load-failure") { yield return LoadingFailure(); yield break; }
        if (role == "enemy-range") { yield return EnemyDetection(); yield break; }
        if (role == "loot-balance") { yield return LootBalance(); yield break; }
        if (role == "grenade-removal") { yield return GrenadeRemoval(); yield break; }
        if (role.EndsWith("host")) yield return Host();
        else if (role.EndsWith("client")) yield return Client();
        else if (role == "split" || role == "guest-split") yield return Split();
        else yield return Solo();
    }
    void CheckWorld()
    {
        Check(World != null && World.IsAuthored && World.Built && World.TreeCount > 300, "Map1 loads its saved grassland and forest geometry");
        Check(Progress != null && Progress.Portals.Count == 4, "Both bidirectional portal links exist");
        Check(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).All(c => !c.GetType().Name.Contains("Grenade")) &&
            Object.FindObjectsByType<Text>(FindObjectsSortMode.None).All(t => !t.text.Contains("手榴彈")), "Actual solo/cooperative Map1 contains no grenade components or HUD entries");
        for (int i = 0; i < 3; i++) Check(NavMesh.SamplePosition(World.RegionBounds[i].center, out _, 5, NavMesh.AllAreas), "Region " + (i + 1) + " has baked navigation");
        foreach (var actor in Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None))
        {
            var health = actor.GetComponent<PlayerHealth>(); var state = health.CaptureProgress();
            state.health = state.maxHealth = 10000; health.RestoreProgress(state);
        }
    }
    IEnumerator GrenadeRemoval()
    {
        GameProgressSave.StartFresh = true; CharacterRoster.SelectForNewRun("ranger");
        Check(GameSceneFlow.Load("Map1"), "Actual Map1 loads after grenade removal");
        yield return Wait(() => !GameSceneFlow.IsLoading && World != null && Progress != null && Progress.Portals.Count == 4,
            "Saved outdoor Map1 initialization"); yield return .5f;
        CheckWorld();
        var player = Object.FindFirstObjectByType<Player>();
        var inventory = player.GetComponent<WeaponInventory>();
        var health = player.GetComponent<PlayerHealth>();
        var assembly = typeof(PlayerShooter).Assembly;
        player.GetComponent<PickupMagnet>().enabled = false;
        Check(new[] { "GrenadeThrower", "GrenadePickup", "ThrownGrenade", "GrenadeExplosion" }.All(n => assembly.GetType(n) == null),
            "All four grenade gameplay types are absent from the running game");
        Check(Progress.RequiredItemsFor(1) == 10 && Progress.RequiredItemsFor(2) == 20,
            "The existing 10/20 progression requirements are preserved");
        int used = inventory.UsedItemSlots;
        string prior = JsonUtility.ToJson(inventory.CaptureProgress());
        inventory.RestoreProgress(JsonUtility.FromJson<InventoryProgress>("{\"grenades\":999," + prior.Substring(1)));
        Check(inventory.UsedItemSlots == used && JsonUtility.ToJson(inventory.CaptureProgress()) == prior,
            "Legacy grenade inventory data is ignored while all remaining items and capacity are preserved");
        inventory.ApplyCoopCommand("grenade", 0, 0);
        Check(JsonUtility.ToJson(inventory.CaptureProgress()) == prior && player.GetComponent<PlayerShooter>().WeaponEfficiency == 1f,
            "A retired grenade network command cannot change the equipped weapon or inventory");
        foreach (var sp in Object.FindObjectsByType<NPCSpawner>(FindObjectsSortMode.None)) sp.enabled = false;
        foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) ai.SetCombatPaused(true);
        var spawner = Object.FindFirstObjectByType<NPCSpawner>();
        var previous = new HashSet<EnemyHealth>(EnemyHealth.ActiveEnemies);
        Check(spawner.TrySpawnNpc(), "The actual updated NPC prefab still spawns on saved navigation");
        var enemy = EnemyHealth.ActiveEnemies.First(e => !previous.Contains(e)); enemy.GetComponent<EnemyAI>().SetCombatPaused(true);
        Check(enemy.progressionDropChance == .01f && typeof(EnemyHealth).GetField("grenadeDropChance") == null,
            "Spawned enemy keeps one-percent keys and exposes no grenade loot setting");
        enemy.weakNpcWeaponDropChance = enemy.strongNpcWeaponDropChance = enemy.armorEquipmentDropChance = enemy.healthDropChance = 1;
        enemy.progressionDropChance = 1; enemy.SetLevelAndExperience(1,10); enemy.TakeDamage(enemy.maxHealth);
        yield return .15f;
        Check(Object.FindFirstObjectByType<WeaponPickup>() != null && Object.FindFirstObjectByType<ArmorPickup>() != null &&
            Object.FindFirstObjectByType<HealthPickup>() != null && Object.FindFirstObjectByType<ExperiencePickup>() != null &&
            Object.FindFirstObjectByType<FloorKeyPickup>() != null,
            "Real NPC death still produces weapon, armor, medical, experience and progression loot");
        Check(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).All(c => !c.GetType().Name.Contains("Grenade")),
            "Real NPC death creates no grenade pickup or thrown entity");
        var medical = Object.FindFirstObjectByType<HealthPickup>();
        Check(medical.TryCollect(player.GetComponent<BoxCollider>()), "The actual remaining medical pickup enters the backpack");
        var state = health.CaptureProgress(); state.health = state.maxHealth * .5f; health.RestoreProgress(state);
        float beforeHeal = health.CurrentHealth;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(WeaponInventory).GetMethod("UseHealthPack", flags).Invoke(inventory, null);
        Check(health.CurrentHealth > beforeHeal && inventory.CaptureProgress().healthPacks == 0, "The remaining medical-pack action heals and consumes its item normally");
        Key(UnityEngine.InputSystem.Key.G); yield return .15f; Key();
        Check(player.GetComponent<PlayerShooter>().WeaponEfficiency == 1f &&
            player.GetComponentsInChildren<Transform>(true).All(t => !t.name.Contains("Grenade")),
            "Pressing the former grenade key leaves the normal weapon available and creates no held grenade");
        Check(GameProgressSave.Instance.SaveNow() && GameProgressSave.TryRead(GameProgressSave.SavePath, out var saved, out _) &&
            !File.ReadAllText(GameProgressSave.SavePath).Contains("grenades") && saved.inventory.weapons.Length > 0,
            "Actual Map1 saves and reloads normal equipment without any grenade inventory field");
    }
    IEnumerator LootBalance()
    {
        GameProgressSave.StartFresh = true; CharacterRoster.SelectForNewRun("breaker");
        Check(GameSceneFlow.Load("Map1"), "Actual saved outdoor Map1 starts a fresh isolated balance review");
        yield return Wait(() => !GameSceneFlow.IsLoading && World != null && Progress != null && Progress.Portals.Count == 4,
            "Saved Map1 and progression initialization");
        yield return .5f;
        var player = Object.FindFirstObjectByType<Player>();
        var spawner = Object.FindFirstObjectByType<NPCSpawner>();
        foreach (var item in Object.FindObjectsByType<NPCSpawner>(FindObjectsSortMode.None)) item.enabled = false;
        foreach (var enemy in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) enemy.SetCombatPaused(true);
        Check(Progress.RequiredItemsFor(1) == 10 && Progress.RequiredItemsFor(2) == 20, "Map1 requires exactly 10 and 20 progression items");
        Progress.RestoreProgress(new[] { 0, 3, 6, 0 });
        Check(!Forward(1).IsUnlocked && !Forward(2).IsUnlocked && Progress.GuidanceFor(player) == null,
            "Previously saved counts of 3 and 6 do not prematurely unlock the new requirements");
        Progress.RestoreProgress(new int[4]);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var spawn = typeof(NPCSpawner).GetMethod("TrySpawnNpcInRoom", flags);
        var drop = typeof(EnemyHealth).GetMethod("TryDropProgressionItem", flags);
        var keyFloor = typeof(FloorKeyPickup).GetField("floor", flags);
        var markers = Object.FindObjectsByType<MedievalDungeonKit.DungeonMarker>(FindObjectsSortMode.None);
        for (int floor = 1; floor <= 3; floor++)
        {
            var marker = markers.First(m => m.kind == "Room" && OverworldDirector.MarkerFloor(m.transform) == floor);
            var previous = new HashSet<EnemyHealth>(EnemyHealth.ActiveEnemies);
            Check((bool)spawn.Invoke(spawner, new object[] { DungeonRoomBoundary.GetOrCreate(marker) }), "Real saved Map1 spawner produces a region " + floor + " enemy");
            var enemy = EnemyHealth.ActiveEnemies.First(e => !previous.Contains(e));
            enemy.GetComponent<EnemyAI>().SetCombatPaused(true);
            Check(enemy.DungeonFloor == floor && Mathf.Approximately(enemy.progressionDropChance, .01f),
                "Generated region " + floor + " enemy retains the exact one-percent progression drop chance");
            var savedRandom = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(7341 + floor);
                for (int i = 0; i < 3000; i++) drop.Invoke(enemy, null);
            }
            finally { UnityEngine.Random.state = savedRandom; }
            var pickups = Object.FindObjectsByType<FloorKeyPickup>(FindObjectsSortMode.None);
            Check(pickups.Length >= 12 && pickups.Length <= 55 && pickups.All(p => (int)keyFloor.GetValue(p) == floor),
                "Region " + floor + " actual loot rolls produce rare, correctly labelled keys: " + pickups.Length + "/3000");
            foreach (var pickup in pickups) Destroy(pickup.gameObject);
            yield return .1f;

            // Check the real death path on both sides of the probability boundary.
            for (int success = 0; success < 2; success++)
            {
                var node = new GameObject("Death Drop Boundary", typeof(EnemyHealth));
                var health = node.GetComponent<EnemyHealth>(); health.SetDungeonFloor(floor); health.SetLevelAndExperience(1, 0);
                health.healthRestoreAmount = 0; health.weakNpcWeaponDropChance = health.strongNpcWeaponDropChance = 0;
                health.armorEquipmentDropChance = 0; health.showDamageNumbers = false;
                int seed = 0; float roll;
                var state = UnityEngine.Random.state;
                do { UnityEngine.Random.InitState(seed++); _ = UnityEngine.Random.value; _ = UnityEngine.Random.value; roll = UnityEngine.Random.value; }
                while (success == 0 ? roll < .009f || roll >= .01f : roll < .01f || roll > .011f);
                UnityEngine.Random.InitState(seed - 1); health.TakeDamage(health.maxHealth);
                UnityEngine.Random.state = state;
                var keys = Object.FindObjectsByType<FloorKeyPickup>(FindObjectsSortMode.None);
                Check(keys.Length == (success == 0 ? 1 : 0) && keys.All(p => (int)keyFloor.GetValue(p) == floor),
                    "Region " + floor + " real enemy death " + (success == 0 ? "drops below" : "does not drop at or above") + " the one-percent boundary");
                foreach (var key in keys) Destroy(key.gameObject);
                yield return .1f;
            }
        }
        for (int floor = 1; floor <= 2; floor++)
        {
            Place(player, World.RegionBounds[floor - 1].center); yield return .2f;
            for (int i = 1; i < Progress.RequiredItemsFor(floor); i++) Collect(floor, player);
            Check(!Forward(floor).IsUnlocked && Progress.GuidanceFor(player) == null,
                "One key below region " + floor + " requirement keeps both portal and guidance locked");
            Collect(floor, player); yield return .2f;
            Check(Forward(floor).IsUnlocked && Progress.GuidanceFor(player) == Forward(floor).transform,
                "Exactly " + Progress.RequiredItemsFor(floor) + " keys unlock region " + (floor + 1) + " and portal guidance");
            Check(Progress.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains(Progress.RequiredItemsFor(floor).ToString())),
                "Actual objective UI displays the updated region " + floor + " requirement");
            Place(player, Forward(floor).transform.position - Forward(floor).transform.forward * 3f); yield return .2f;
            Check(Progress.TryUsePortal(Forward(floor), player), "Unlocked portal accepts the actual player's travel request");
            yield return Wait(() => World.RegionAt(player.transform.position) == floor + 1, "Next outdoor region arrival");
            yield return 1.3f;
            Check(player.IsGrounded && !player.GetComponent<PlayerHealth>().IsDead, "Unlocked region " + (floor + 1) + " arrival stays alive and grounded");
        }
        Check(GameProgressSave.Instance.SaveNow() && GameProgressSave.TryRead(GameProgressSave.SavePath, out var saved, out _) &&
            saved.floorKeys[1] == 10 && saved.floorKeys[2] == 20, "Actual single-player save retains the new 10/20 key totals");
    }
    IEnumerator EnemyDetection()
    {
        GameProgressSave.StartFresh = true;
        CharacterRoster.SelectForNewRun("ranger");
        Check(GameSceneFlow.Load("Map1"), "Actual saved outdoor Map1 loads for enemy detection review");
        yield return Wait(() => World != null && World.Built && !GameSceneFlow.IsLoading, "Outdoor map loading");
        yield return 1f;
        var player = Object.FindFirstObjectByType<Player>();
        var spawner = Object.FindFirstObjectByType<NPCSpawner>();
        Check(spawner != null && spawner.npcPrefab.GetComponent<EnemyAI>().chaseRange == 200f,
            "The actual NPC prefab used by Map1 has a serialized 200-unit pursuit range");
        foreach (var instance in Object.FindObjectsByType<NPCSpawner>(FindObjectsSortMode.None)) instance.enabled = false;
        var previous = Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        foreach (var instance in previous) instance.SetCombatPaused(true);
        Check(spawner.TrySpawnNpc(), "Actual Map1 NPC spawner creates an enemy from the updated prefab");
        var enemy = Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None).First(e => !previous.Contains(e));
        enemy.SetCombatPaused(true); enemy.GetComponent<EnemyHealth>().SetLevelAndExperience(9, 10);
        yield return .2f;
        var agent = enemy.GetComponent<NavMeshAgent>();
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        var room = World.EnemyBoundaryAt(World.RegionBounds[0].center);
        Check(room != null, "Actual meadow region boundary is available");
        Vector3 origin = default, near = default, far = default;
        bool found = false;
        foreach (Vector3 direction in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back,
            new Vector3(1,0,1).normalized, new Vector3(-1,0,1).normalized })
        {
            Vector3 center = World.RegionBounds[0].center;
            if (!NavMesh.SamplePosition(center - direction * 120, out var a, 5, filter) ||
                !NavMesh.SamplePosition(center + direction * 40, out var b, 5, filter) ||
                !NavMesh.SamplePosition(center + direction * 120, out var c, 5, filter)) continue;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, filter, path) || !room.ContainsPath(path, agent.radius)) continue;
            origin = a.position; near = b.position; far = c.position; found = true; break;
        }
        Check(found && Vector3.Distance(origin, near) > 140 && Vector3.Distance(origin, far) > 220,
            "Saved meadow navigation provides targets beyond the old 100-unit range and outside 200 units");
        Check(agent.Warp(origin), "Generated enemy warps to real meadow navigation");
        enemy.AssignRoom(room); enemy.target = player.transform;
        Place(player, near); yield return .2f;
        enemy.chaseRange = 100f; enemy.SetCombatPaused(false); yield return .7f;
        Check(agent.isStopped && !agent.hasPath && GroundDistance(enemy.transform.position, origin) < .5f,
            "Old 100-unit setting does not pursue the distant meadow player");
        enemy.chaseRange = 200f; enemy.SetCombatPaused(false);
        yield return Wait(() => agent.hasPath && !agent.isStopped && GroundDistance(enemy.transform.position, origin) > .5f,
            "Expanded range starts actual NavMesh pursuit", 5);
        Check(room.ContainsPath(agent.path, agent.radius), "Longer pursuit follows a complete path inside the meadow region");
        enemy.SetCombatPaused(true); agent.Warp(origin); Place(player, far); yield return .2f;
        enemy.SetCombatPaused(false); yield return .7f;
        Check(agent.isStopped && !agent.hasPath && GroundDistance(enemy.transform.position, origin) < .5f,
            "A player farther than 200 units remains outside the detection range");
        enemy.SetCombatPaused(true);
        Vector3 edge = World.RegionBounds[0].center; edge.x = World.RegionBounds[0].max.x - 20;
        Vector3 other = World.RegionBounds[1].center; other.x = World.RegionBounds[1].min.x + 20;
        Check(NavMesh.SamplePosition(edge, out var edgeHit, 5, filter) && room.Contains(edgeHit.position, agent.radius),
            "Enemy boundary test starts on valid meadow navigation");
        Check(NavMesh.SamplePosition(other, out var otherHit, 5, filter), "Neighbouring forest has a valid player destination");
        agent.Warp(edgeHit.position); Place(player, otherHit.position); yield return .2f;
        Check(Vector3.Distance(enemy.transform.position, player.transform.position) < 200 && !room.Contains(player.transform.position),
            "Forest player is in detection radius but outside this enemy's home region");
        enemy.SetCombatPaused(false); yield return .7f;
        Check(agent.isStopped && !agent.hasPath && room.Contains(enemy.transform.position, agent.radius),
            "Expanded detection does not let enemies chase across region boundaries");
        enemy.SetCombatPaused(true);
        Check(enemy.stopRange == 1.5f && enemy.rangedRange == 12f, "Melee and crossbow attack distances retain their current settings");
    }
    IEnumerator LoadingFailure()
    {
        string slot = LocalAccountProgress.ActivePath;
        Directory.CreateDirectory(Path.GetDirectoryName(slot));
        byte[] original = System.Text.Encoding.UTF8.GetBytes("isolated single-player slot must remain byte-identical");
        File.WriteAllBytes(slot, original);
        bool failed = false;
        CoopSession.ReviewInternet(new Uri(File.ReadAllText(FilePath("relay-endpoint"))), "Map1", "ranger", null, "", () =>
        {
            Check(GameSceneFlow.IsLoading, "Relay failure is injected during actual asynchronous Map1 loading");
            typeof(CoopSession).GetMethod("Fail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(CoopSession.Instance, new object[] { "Injected connection failure during loading" });
        }, e => failed = true);
        yield return Wait(() => failed && SceneManager.GetActiveScene().name == "MainMenu" && !GameSceneFlow.IsLoading, "Failure returns to MainMenu");
        Check(!CoopSession.IsMultiplayer && CoopSession.RoomCode == null && Object.FindFirstObjectByType<Player>() == null,
            "Loading failure clears multiplayer and leaves the actual menu without stray players");
        Check(File.ReadAllBytes(slot).SequenceEqual(original), "Failure during map loading preserves the original single-player slot byte-for-byte");
    }
    IEnumerator Social()
    {
        var friends = CloudFriendsService.Ensure();
        friends.Configure(new CloudFriendsTransport(File.ReadAllText(FilePath("auth-endpoint"))));
        Check(friends.Refresh(), "Signed-in player can request cloud friend profile over real HTTP");
        yield return Wait(() => !friends.IsBusy, "Cloud friend profile response");
        Check(friends.State.code?.Length == 12 && friends.State.friends.Length == 0, "Backend creates an isolated friend code with no existing friends");
        bool host = role.EndsWith("host"); string own = host ? "social-host-code" : "social-client-code";
        string other = host ? "social-client-code" : "social-host-code";
        Signal(own, friends.State.code);
        Check(friends.Rename(host ? "草原遊俠" : "森林醫者"), "Signed-in player updates their own nickname");
        yield return Wait(() => !friends.IsBusy, "Nickname update");
        Check(friends.State.name == (host ? "草原遊俠" : "森林醫者"), "Nickname persists in the backend profile");
        Check(!friends.Request("BAD") && !friends.IsBusy, "Malformed friend codes are rejected before a request");
        yield return Wait(() => Signalled(other), "Other signed-in player's friend code");
        if (host)
        {
            Check(friends.Request(File.ReadAllText(FilePath(other))), "Host sends friend invitation using peer code");
            yield return Wait(() => !friends.IsBusy, "Invitation response");
            Check(friends.State.outgoing.Length == 1 && friends.State.friends.Length == 0, "Sending an invitation does not establish friendship");
            string recipient = friends.State.outgoing[0].id;
            Check(friends.Accept(recipient), "Sender tries to accept their own sent invitation");
            yield return Wait(() => !friends.IsBusy, "Invalid acceptance response");
            Check(friends.State.friends.Length == 0, "Backend refuses acceptance by the inviter");
            Signal("social-invite-sent");
            yield return Wait(() => Signalled("social-accepted"), "Recipient accepts invitation");
            friends.Refresh(); yield return Wait(() => !friends.IsBusy, "Mutual friend list");
            Check(friends.State.friends.Length == 1 && friends.State.friends[0].name == "森林醫者", "Host observes mutually accepted friend");
            yield return FriendsPreview();
            friends.Remove(recipient); yield return Wait(() => !friends.IsBusy, "Host removes friend");
            Check(friends.State.friends.Length == 0, "Host removes an accepted friendship"); Signal("social-removed");
        }
        else
        {
            yield return Wait(() => Signalled("social-invite-sent"), "Host sends invitation");
            friends.Refresh(); yield return Wait(() => !friends.IsBusy, "Incoming invitation refresh");
            Check(friends.State.incoming.Length == 1 && friends.State.friends.Length == 0, "Recipient receives invitation before becoming friends");
            friends.Accept(friends.State.incoming[0].id); yield return Wait(() => !friends.IsBusy, "Recipient accepts");
            Check(friends.State.friends.Length == 1 && friends.State.incoming.Length == 0, "Recipient acceptance establishes friendship");
            Signal("social-accepted"); yield return Wait(() => Signalled("social-removed"), "Host removes friendship");
            friends.Refresh(); yield return Wait(() => !friends.IsBusy, "Recipient refreshes after removal");
            Check(friends.State.friends.Length == 0, "Removing friendship removes it for both players");
        }
        if (!host) yield return FriendsPreview();
    }
    IEnumerator FriendsPreview()
    {
        var friends = CloudFriendsService.Ensure();
        var friendButton = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.GetComponentInChildren<Text>()?.text == "好友");
        var canvas = friendButton.GetComponentInParent<Canvas>();
        var menu = FriendsMenu.Create(canvas.transform, () => { }); menu.Open();
        yield return Wait(() => !friends.IsBusy, "Actual friends menu refresh"); yield return .1f;
        Check(menu.GetComponentInChildren<ScrollRect>(true) != null && menu.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains(friends.State.code)), "Actual friends menu displays own code and scrollable lists");
        CaptureMenu(canvas, "friends-" + role + ".png"); menu.gameObject.SetActive(false);
    }
    void CaptureMenu(Canvas canvas, string name)
    {
        var node = new GameObject("Isolated Menu Capture", typeof(Camera)); var camera = node.GetComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 540; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.nearClipPlane = .01f; camera.farClipPlane = 100; camera.enabled = false;
        var target = new RenderTexture(1920, 1080, 24); target.Create(); camera.targetTexture = target;
        RenderMode oldMode = canvas.renderMode; Camera oldCamera = canvas.worldCamera; float oldDistance = canvas.planeDistance;
        var oldActive = RenderTexture.active;
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0,0,1920,1080),0,0); texture.Apply();
            Check(texture.GetPixels32().Count(p => p.r > 8 || p.g > 8 || p.b > 8) > 1920 * 1080 / 10, "Actual menu produces a visible offscreen UI capture");
            File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG()); Object.Destroy(texture);
        }
        finally
        {
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
            RenderTexture.active = oldActive; camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(node); Canvas.ForceUpdateCanvases();
        }
    }
    IEnumerator Host()
    {
        if (role.StartsWith("relay"))
        {
            string failure = null; bool ready = false;
            CoopSession.ReviewInternet(new Uri(File.ReadAllText(FilePath("relay-endpoint"))), "Map1", "ranger", null, "overworld-review", () => ready = true, e => failure = e);
            yield return Wait(() => failure != null || ready, "Authenticated relay room creation");
            Check(failure == null && CoopSession.IsInternet && CoopSession.RoomCode?.Length == 10, "Authenticated relay creates a 10-character room code: " + failure);
        }
        else Check(CoopSession.StartHost("Map1", "ranger", 0, "overworld-review", out var error), "A real LAN host opens the actual Map1: " + error);
        yield return Wait(() => CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading, "Host map loading");
        yield return .7f; CheckWorld();
        Signal("port", CoopSession.Instance.ListeningPort.ToString());
        if (CoopSession.IsInternet) Signal("room-code", CoopSession.RoomCode);
        yield return Wait(() => CoopSession.Instance.PlayerCount == 2 && Signalled("client-ready"), "Second process joins");
        Check(Remote.GetComponent<CharacterSkills>().Character.Id == "medic", "Remote character choice reaches the host");
        var remote = Remote.GetComponent<Player>(); var host = Local.GetComponent<Player>();
        Vector3 start = remote.transform.position; Signal("move-now");
        yield return Wait(() => Signalled("client-moved"), "Client sends keyboard input");
        Check(Vector3.Distance(remote.transform.position, start) > .4f, "Client keyboard input moves the host-authoritative actor");
        Place(remote, Forward(1).transform.position + Vector3.back * 4); yield return .2f; Signal("locked-now");
        yield return Wait(() => Signalled("client-locked"), "Client exercises locked portal");
        Check(World.RegionAt(remote.transform.position) == 1 && !Forward(1).IsUnlocked, "Remote player cannot pass the locked forest portal");
        Place(remote, Forward(1).transform.position + Vector3.back * 4);
        for (int i = 0; i < Progress.RequiredItemsFor(1); i++) Collect(1, i % 2 == 0 ? host : remote);
        Check(Progress.GuidanceFor(host) == Forward(1).transform && Forward(1).IsUnlocked, "Teammate pickups unlock shared forest guidance");
        Signal("meadow-complete"); yield return Wait(() => Signalled("client-meadow-complete"), "Client shared keys and guidance");
        Signal("forest-walk"); yield return Wait(() => World.RegionAt(remote.transform.position) == 2 && Signalled("client-forest"), "Client walks through actual forest portal");
        Check(World.RegionAt(host.transform.position) == 1 && remote.IsGrounded, "Remote portal travel preserves the host's separate meadow position");
        host.Teleport(Forward(1).transform.position + Vector3.up * 1.08f, Quaternion.identity);
        yield return Wait(() => World.RegionAt(host.transform.position) == 2, "Host forest travel");
        Place(host, World.RegionBounds[1].center);
        Place(remote, Forward(2).transform.position + Vector3.back * 4);
        for (int i = 0; i < Progress.RequiredItemsFor(2); i++) Collect(2, i % 2 == 0 ? host : remote);
        Signal("forest-complete"); yield return Wait(() => Signalled("client-forest-complete"), "Client ancient forest guidance");
        Signal("ancient-walk"); yield return Wait(() => World.RegionAt(remote.transform.position) == 3 && Signalled("client-ancient"), "Client ancient forest travel");
        Check(remote.IsGrounded && !remote.GetComponent<PlayerHealth>().IsDead, "Remote player reaches safe ancient forest ground");
        host.Teleport(Forward(2).transform.position + Vector3.up * 1.08f, Quaternion.identity);
        yield return Wait(() => World.RegionAt(host.transform.position) == 3, "Host ancient forest travel");
        Place(host, World.RegionBounds[2].center);
        Check(Object.FindFirstObjectByType<NPCSpawner>().TrySpawnNpc(), "Original NPC spawner creates a shared fantasy monster");
        yield return .4f;
        var enemy = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).First(e => e.GetComponent<EnemyCharacterVisual>() != null);
        Signal("enemy-id", enemy.gameObject.GetInstanceID().ToString());
        yield return Wait(() => Signalled("client-monster"), "Client renders the shared monster");
        Check(GameProgressSave.Instance != null && GameProgressSave.Instance.SaveNow(), "Host saves shared keys in the isolated cooperative profile");
        Check(GameProgressSave.TryRead(GameProgressSave.SavePath, out var saved, out _) && saved.floorKeys[1] == Progress.RequiredItemsFor(1) && saved.floorKeys[2] == Progress.RequiredItemsFor(2) && saved.worldLayoutVersion == OverworldDirector.LayoutVersion,
            "Cooperative save persists outdoor layout and both collection goals");
        Check(SingleSlotUnchanged, "Cooperative play preserves the isolated single-player slot byte-for-byte");
        Capture(Local.GetComponent<CameraSwitch>().ActiveCamera, "host-gameplay.png");
        Signal("disconnect-now"); yield return Wait(() => Signalled("client-ready-disconnect"), "Client observes final state");
        CoopSession.Leave(); yield return .5f;
        yield return Wait(() => Signalled("client-disconnected"), "Client cleans up after host departure");
    }
    IEnumerator Client()
    {
        yield return Wait(() => Signalled("port"), "Host announces its ephemeral TCP port");
        string failure = null; bool connected = false;
        if (role.StartsWith("relay"))
        {
            yield return Wait(() => Signalled("room-code"), "Host room code");
            CoopSession.ReviewInternet(new Uri(File.ReadAllText(FilePath("relay-endpoint"))), "Map1", "medic", File.ReadAllText(FilePath("room-code")), "overworld-review", () => connected = true, message => failure = message);
        }
        else CoopSession.Join("Map1", "medic", "127.0.0.1", int.Parse(File.ReadAllText(FilePath("port"))), "overworld-review", () => connected = true, message => failure = message);
        yield return Wait(() => failure != null || connected && CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading && CoopSession.Instance.PlayerCount == 2, "Independent client loading");
        Check(failure == null && CoopSession.IsClient, "Second process joins through the real client scene-loading flow: " + failure);
        yield return Wait(() => Progress != null && Progress.Portals.Count == 4, "Client portal initialization", 5);
        yield return .2f;
        CheckWorld();
        Check(Object.FindFirstObjectByType<ExplorationMinimap>() != null && Object.FindFirstObjectByType<RegionObjectiveUI>() != null,
            "Client initializes the minimap and region objective UI without owning a save");
        Check(Local.Id > 0 && Local.GetComponent<CharacterSkills>().Character.Id == "medic", "Client owns its assigned actor and selected character");
        Check(Local.GetComponent<Rigidbody>().isKinematic && !Local.GetComponent<PickupMagnet>().enabled && GameProgressSave.Instance == null,
            "Client does not simulate movement, collect duplicate loot or own saves");
        Check(Object.FindObjectsByType<NPCSpawner>(FindObjectsSortMode.None).All(s => !s.enabled), "Client NPC generation stays disabled");
        Signal("client-ready"); yield return Wait(() => Signalled("move-now"), "Move stage");
        var views = Local.GetComponent<CameraSwitch>(); views.SetCoopView(true); views.fpsController.SetLook(0, 0);
        Vector3 before = Local.transform.position;
        Key(UnityEngine.InputSystem.Key.D); yield return .7f; Key(); yield return .2f;
        Check(Vector3.Distance(Local.transform.position, before) > .4f, "Keyboard movement returns as a synchronized client pose");
        Signal("client-moved"); yield return Wait(() => Signalled("locked-now"), "Locked portal stage");
        views.fpsController.SetLook(0, 0); Key(UnityEngine.InputSystem.Key.W); yield return 1.3f; Key(); yield return .2f;
        Check(World.RegionAt(Local.transform.position) == 1 && !Forward(1).IsUnlocked, "Locked portal remains locked on the client");
        Check(!Progress.TryUsePortal(Forward(1), Local.GetComponent<Player>()), "Client cannot authorize a local portal teleport");
        Signal("client-locked"); yield return Wait(() => Signalled("meadow-complete") && Progress.IsComplete(1), "Meadow collection snapshot");
        Check(Progress.CollectedFor(1) == Progress.RequiredItemsFor(1) && Forward(1).IsUnlocked && Progress.GuidanceFor(Local.GetComponent<Player>()) == Forward(1).transform,
            "Client displays shared meadow collection and forest portal guidance");
        Signal("client-meadow-complete"); yield return Wait(() => Signalled("forest-walk"), "Forest portal input");
        views.fpsController.SetLook(0, 0); Key(UnityEngine.InputSystem.Key.W);
        yield return Wait(() => World.RegionAt(Local.transform.position) == 2, "Authoritative forest portal arrival", 12); Key(); yield return .3f;
        Check(Local.GetComponent<Player>().IsGrounded && World.RegionAt(Remote.transform.position) == 1, "Forest arrival syncs while the other actor stays in the meadow");
        Signal("client-forest"); yield return Wait(() => Signalled("forest-complete") && Progress.IsComplete(2), "Forest collection snapshot");
        Check(Progress.CollectedFor(2) == Progress.RequiredItemsFor(2) && Forward(2).IsUnlocked && Progress.GuidanceFor(Local.GetComponent<Player>()) == Forward(2).transform,
            "Client displays shared forest collection and ancient forest guidance");
        Signal("client-forest-complete"); yield return Wait(() => Signalled("ancient-walk"), "Ancient forest portal input");
        views.fpsController.SetLook(0, 0); Key(UnityEngine.InputSystem.Key.W);
        yield return Wait(() => World.RegionAt(Local.transform.position) == 3, "Authoritative ancient forest portal arrival", 12); Key(); yield return .3f;
        Check(Local.GetComponent<Player>().IsGrounded && !Local.GetComponent<PlayerHealth>().IsDead, "Ancient forest arrival stays alive and grounded on the client");
        Signal("client-ancient"); yield return Wait(() => Signalled("enemy-id"), "Shared monster identity");
        string name = "Shared Entity " + File.ReadAllText(FilePath("enemy-id"));
        yield return Wait(() => GameObject.Find(name) != null && GameObject.Find(name).GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled && r.GetComponent<MeshFilter>().sharedMesh != null), "Shared monster mesh rendering");
        Check(true, "Client receives and renders the host's actual monster geometry"); Signal("client-monster");
        Capture(views.ActiveCamera, "client-gameplay.png");
        yield return Wait(() => Signalled("disconnect-now"), "Disconnect stage"); Signal("client-ready-disconnect");
        yield return Wait(() => CoopSession.Mode == CoopMode.SinglePlayer && SceneManager.GetActiveScene().name == "MainMenu" && !GameSceneFlow.IsLoading, "Return to real MainMenu on disconnect");
        Check(!string.IsNullOrEmpty(CoopSession.LastError) && Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).Length == 0,
            "Host disconnect returns client to MainMenu and removes stale actors");
        Check(SingleSlotUnchanged, "Client preserves any existing single-player save byte-for-byte"); Signal("client-disconnected");
    }
    IEnumerator Split()
    {
        Check(CoopSession.StartSplit("Map1", "ranger", "medic"), "Actual Map1 starts in local two-player mode");
        yield return Wait(() => CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading && CoopSession.Instance.PlayerCount == 2, "Split-screen loading");
        yield return .8f; CheckWorld();
        var actors = Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).OrderBy(a => a.LocalIndex).ToArray();
        Check(actors.All(a => a.Local) && actors[0].Viewport.x == 0 && actors[1].Viewport.x == .5f, "Both local actors receive independent half-screen viewports");
        Check(actors.All(a => a.GetComponent<CameraSwitch>().ActiveCamera.GetComponent<PixelArtCamera>().outputRect == a.Viewport), "Actual cameras present the two separate viewports");
        var pad = InputSystem.AddDevice<Gamepad>();
        Vector3 first = actors[0].transform.position, second = actors[1].transform.position;
        InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right }); InputSystem.Update(); yield return .5f;
        InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update(); InputSystem.RemoveDevice(pad);
        Check(Vector3.Distance(actors[1].transform.position, second) > .3f && Vector3.Distance(actors[0].transform.position, first) < .15f,
            "Gamepad moves only the second player on outdoor terrain");
        for (int floor = 1; floor <= 2; floor++)
        {
            var one = actors[0].GetComponent<Player>(); var two = actors[1].GetComponent<Player>();
            for (int i = 0; i < Progress.RequiredItemsFor(floor); i++) Collect(floor, i % 2 == 0 ? one : two);
            Check(Progress.IsComplete(floor) && Forward(floor).IsUnlocked, "Both players contribute to the shared region " + floor + " objective");
            two.Teleport(Forward(floor).transform.position + Vector3.up * 1.08f, Quaternion.identity);
            yield return Wait(() => World.RegionAt(two.transform.position) == floor + 1, "Second-player portal trigger");
            Check(World.RegionAt(one.transform.position) == floor, "Second-player portal travel does not move player one");
            one.Teleport(Forward(floor).transform.position + Vector3.up * 1.08f, Quaternion.identity);
            yield return Wait(() => World.RegionAt(one.transform.position) == floor + 1, "First-player portal trigger");
            yield return 1.3f;
        }
        Check(actors.All(a => a.GetComponent<Player>().IsGrounded), "Both split-screen players reach safe ancient forest ground");
        Capture(actors[0].GetComponent<CameraSwitch>().ActiveCamera, "split-player-one.png");
        Capture(actors[1].GetComponent<CameraSwitch>().ActiveCamera, "split-player-two.png");
        Check(GameProgressSave.Instance == actors[0].GetComponent<GameProgressSave>() && SingleSlotUnchanged, "Only the primary actor owns the separate cooperative save; single-player slot is unchanged");
    }
    IEnumerator Solo()
    {
        GameProgressSave.StartFresh = true;
        CharacterRoster.SelectForNewRun("ranger");
        Check(GameSceneFlow.Load("Map1"), "Release player loads actual Map1 through the normal scene flow");
        yield return Wait(() => World != null && !GameSceneFlow.IsLoading, "Standalone outdoor map loading");
        yield return 1f; CheckWorld();
        var player = Object.FindFirstObjectByType<Player>();
        Check(player.IsGrounded && player.GetComponent<GameProgressSave>().Ready, "Standalone player lands safely and initializes saves");
        for (int floor = 1; floor <= 2; floor++)
        {
            for (int i = 0; i < Progress.RequiredItemsFor(floor); i++) Collect(floor, player);
            Check(Progress.GuidanceFor(player) == Forward(floor).transform, "Standalone region " + floor + " completion displays portal guidance");
            player.Teleport(Forward(floor).transform.position + Vector3.up * 1.08f, Quaternion.identity);
            yield return Wait(() => World.RegionAt(player.transform.position) == floor + 1, "Standalone actual portal trigger");
            yield return 1.3f;
            Check(player.IsGrounded && !player.GetComponent<PlayerHealth>().IsDead, "Standalone region " + (floor + 1) + " arrival is safe");
        }
        Check(Object.FindFirstObjectByType<NPCSpawner>().TrySpawnNpc(), "Standalone fantasy monster spawns on baked navigation");
        yield return .4f;
        Check(Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).Any(e => e.TryGetComponent(out NavMeshAgent agent) && agent.enabled && agent.isOnNavMesh), "Standalone monsters have live navigation");
        Check(GameProgressSave.Instance.SaveNow() && GameProgressSave.TryRead(GameProgressSave.SavePath, out var saved, out _) && saved.worldLayoutVersion == OverworldDirector.LayoutVersion,
            "Standalone outdoor progress writes and reads successfully");
        Capture(player.GetComponent<CameraSwitch>().ActiveCamera, "standalone-gameplay.png");
        if (role.StartsWith("guest"))
        {
            var account = CloudAccountService.EnsureInstance(); string guestPath = LocalAccountProgress.ActivePath;
            int requiredOne = Progress.RequiredItemsFor(1), requiredTwo = Progress.RequiredItemsFor(2);
            Check(!account.ExitGuest() && account.IsGuest, "Guest identity cannot change while a map is running");
            Check(GameSceneFlow.Load("MainMenu"), "Guest returns to the actual menu");
            yield return Wait(() => SceneManager.GetActiveScene().name == "MainMenu" && !GameSceneFlow.IsLoading, "Guest return to MainMenu");
            yield return .3f;
            var login = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.GetComponentInChildren<Text>()?.text == "返回登入帳號");
            login.onClick.Invoke();
            yield return Wait(() => SceneManager.GetActiveScene().name == "Login" && !GameSceneFlow.IsLoading, "Guest exits through menu button");
            Check(!account.CanPlay && !account.IsGuest && !account.IsSignedIn, "Exiting guest mode returns to login without retaining guest access");
            account.Login("review@example.com", "isolated-test-password");
            yield return Wait(() => account.IsSignedIn && SceneManager.GetActiveScene().name == "MainMenu" && !GameSceneFlow.IsLoading, "Guest switches to signed-in account");
            yield return .2f;
            Check(!account.IsGuest && LocalAccountProgress.HasAccount && LocalAccountProgress.ActivePath != guestPath, "Guest-to-account transition selects a separate account save");
            Check(GameProgressSave.TryRead(guestPath, out var guestSaved, out _) && guestSaved.floorKeys[1] == requiredOne && guestSaved.floorKeys[2] == requiredTwo,
                "Guest save remains intact after signing into an account");
            var friend = Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.GetComponentInChildren<Text>()?.text == "好友");
            Check(friend.interactable, "Signing in unlocks the actual friend menu button");
        }
    }
    void Capture(Camera camera, string name)
    {
        var previous = RenderTexture.active;
        var target = camera.targetTexture;
        Check(target != null && target.IsCreated(), "Gameplay camera produces a live render target");
        // Hidden Windows test processes may suppress their automatic display render.
        // Render the actual gameplay camera offscreen before reading its pixels.
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
        var pixels = texture.GetPixels32();
        Check(pixels.Count(p => p.r > 8 || p.g > 8 || p.b > 8) > pixels.Length / 10,
            "Offscreen gameplay rendering fills the camera target with visible scene content");
        File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
        RenderTexture.active = previous; Object.Destroy(texture);
    }
}
#endif
