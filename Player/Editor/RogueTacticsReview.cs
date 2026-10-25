using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicitly invoked integration checks; use an isolated Unity project.</summary>
[InitializeOnLoad]
public static class RogueTacticsReview
{
    const string Pending = "RogueTacticsReview.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static IEnumerator checks;
    static int assertions;
    static double deadline;
    static string output;
    static GameObject playerNode;
    static PlayerHealth health;
    static WeaponInventory inventory;
    static RogueTactics tactics;
    static PlayerShooter shooter;
    static Projectile prefab;
    static DungeonRoomBoundary room;

    static RogueTacticsReview() { EditorApplication.playModeStateChanged += StateChanged; }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        assertions = 0;
        checks = Checks();
        deadline = EditorApplication.timeSinceStartup + .15;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext())
            {
                File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS {assertions} integration checks");
                Debug.Log($"ROGUE TACTICS PASS: {assertions} integration checks");
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
                return;
            }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .08f);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
            Debug.LogException(ex);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(1);
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
        Debug.Log("PASS " + message);
    }
    static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static void SetPact(string field, string name)
    {
        FieldInfo info = typeof(RogueTactics).GetField(field, Flags);
        info.SetValue(tactics, Enum.Parse(info.FieldType, name));
    }
    static EnemyHealth Enemy(string name, Vector3 position, float hp = 1000)
    {
        var node = new GameObject(name, typeof(BoxCollider));
        node.transform.position = position;
        var enemy = node.AddComponent<EnemyHealth>();
        enemy.SetMaxHealth(hp);
        enemy.showDamageNumbers = false;
        enemy.healthDropChance = enemy.weakNpcWeaponDropChance = enemy.strongNpcWeaponDropChance =
            enemy.progressionDropChance = 0f;
        return enemy;
    }
    static void Record(Vector3 origin, Vector3 direction, float damage = 100f) =>
        tactics.RecordShot(prefab, origin, direction, 30, damage, 1, 0, 0, 0, WeaponType.Rifle);
    static Projectile[] Echoes() => Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)
        .Where(p => p.name == "Tactical Echo Projectile").ToArray();
    static void ClearEchoes() { foreach (Projectile p in Echoes()) Object.DestroyImmediate(p.gameObject); }
    static void ResetMemory() { tactics.ResetTransientState(); Call(tactics, "CheckRoom"); }

    static IEnumerator Checks()
    {
        Object.FindFirstObjectByType<PixelArtDirector>().enabled = false;
        var camera = new GameObject("Test Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 2, -7);
        var map = new GameObject("Test Dungeon");
        room = new GameObject("L1_TestRoom").AddComponent<DungeonRoomBoundary>();
        room.transform.SetParent(map.transform);
        room.halfSize = new Vector2(18, 18);
        playerNode = new GameObject("Test Player");
        playerNode.SetActive(false);
        var save = playerNode.AddComponent<GameProgressSave>();
        save.enabled = false;
        save.StoragePath = Path.Combine(output, "unused-save.json");
        var player = playerNode.AddComponent<Player>();
        player.enabled = false;
        health = playerNode.AddComponent<PlayerHealth>();
        shooter = playerNode.AddComponent<PlayerShooter>();
        shooter.fpsCamera = camera;
        shooter.enabled = false;
        playerNode.SetActive(true);
        playerNode.GetComponent<Rigidbody>().isKinematic = true;
        playerNode.GetComponent<PickupMagnet>().enabled = false;
        playerNode.GetComponent<PlayerCursorController>().enabled = false;
        health.enabled = false; // Test damage without armor regeneration between assertions.
        health.criticalChance = 0;
        health.attackMultiplier = 1;
        inventory = playerNode.GetComponent<WeaponInventory>();
        tactics = playerNode.GetComponent<RogueTactics>();
        playerNode.AddComponent<PlayerHealthBarUI>();
        prefab = new GameObject("Projectile Template", typeof(SphereCollider), typeof(Rigidbody), typeof(Projectile))
            .GetComponent<Projectile>();
        prefab.transform.position = new Vector3(1000, 0, 0);
        prefab.GetComponent<SphereCollider>().radius = .05f;
        prefab.GetComponent<Rigidbody>().useGravity = false;
        prefab.lifetime = 30;
        shooter.projectilePrefab = prefab;
        yield return .2f;
        Check(tactics != null && playerNode.GetComponents<RogueTactics>().Length == 1, "Shooter automatically installs one tactics component");
        Check(GameUIFont.Default != null, "The tactics HUD has the bundled Traditional Chinese font");
        Check(Mathf.Approximately(tactics.Resonance, 50), "New game starts with fifty resonance");
        Check(!tactics.TryReleaseEcho(), "An uncharged echo cannot activate");
        tactics.AddResonance(500);
        Check(Mathf.Approximately(tactics.Resonance, 100), "Resonance has a hard cap");
        Check(!tactics.TryReleaseEcho() && tactics.Resonance == 100, "An empty memory never spends resonance");

        var target = Enemy("Echo Target", new Vector3(0, 1.4f, 6));
        Record(new Vector3(0, 1.4f, 0), Vector3.forward);
        Check(tactics.TryReleaseEcho() && tactics.Resonance == 0, "Echo activation spends exactly one full charge");
        Check(!tactics.TryReleaseEcho(), "An active echo cannot recursively activate");
        yield return .6f;
        Check(Mathf.Abs(target.CurrentHealth - 955f) < .01f, "A real replayed projectile deals forty-five percent damage");
        Check(tactics.Resonance == 0, "Echo hits do not recharge the hit meter");
        Check(!tactics.IsReplaying, "A completed echo exits playback");
        ClearEchoes();
        ResetMemory();
        tactics.AddResonance(100);
        Record(new Vector3(-3, 1.4f, 0), Vector3.left);
        Set(tactics, "clock", (float)Get(tactics, "clock") + 4f);
        Check(!tactics.TryReleaseEcho() && tactics.Resonance == 100, "Memories older than three seconds cannot activate");
        ResetMemory();
        for (int i = 0; i < 90; i++) Record(new Vector3(-3, 1.4f, 0), Vector3.left);
        Check((int)Get(tactics, "memoryCount") == 64, "Sustained fire uses a bounded memory ring");
        Check(tactics.TryReleaseEcho() && (int)Get(tactics, "replayCount") == 30, "Playback retains at most thirty recent salvos");
        Call(tactics, "AdvanceReplay");
        Check((int)Get(tactics, "replayIndex") == 3, "A playback frame processes at most three salvos");
        Time.timeScale = 0;
        int replayBefore = (int)Get(tactics, "replayIndex");
        yield return .2f;
        Check((int)Get(tactics, "replayIndex") == replayBefore, "Paused playback does not advance");
        Time.timeScale = 1;
        ResetMemory();
        ClearEchoes();

        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = new Vector3(-3, 1.4f, 0);
        Physics.SyncTransforms();
        tactics.AddResonance(100);
        Record(blocker.transform.position, Vector3.left);
        Check(tactics.TryReleaseEcho(), "A blocked memory can begin its playback timeline");
        Call(tactics, "AdvanceReplay");
        Check(Echoes().Length == 0, "An echo cannot spawn inside solid geometry");
        Object.DestroyImmediate(blocker);
        ResetMemory();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0, 1.4f, 3);
        wall.transform.localScale = new Vector3(3, 3, .4f);
        Physics.SyncTransforms();
        tactics.AddResonance(100);
        Record(new Vector3(0, 1.4f, 0), Vector3.forward);
        tactics.TryReleaseEcho();
        yield return .4f;
        Check(Mathf.Approximately(target.CurrentHealth, 955f), "A replayed bullet respects walls");
        Object.DestroyImmediate(wall);
        ClearEchoes();
        tactics.RestoreProgress(new RogueTacticsProgress { version = 1, resonance = 0 });
        tactics.OnWeaponHit(target, WeaponType.Rifle);
        float firstCharge = tactics.Resonance;
        tactics.OnWeaponHit(target, WeaponType.Rifle);
        Check(firstCharge == 2 && tactics.Resonance == 2, "Same-weapon pellets cannot flood resonance");
        tactics.OnWeaponHit(target, WeaponType.Pistol);
        Check(tactics.Resonance == 12, "Changing the weapon on a hit grants ten resonance");
        playerNode.GetComponent<WeaponBuildSystem>().OnWeaponSwitched(WeaponType.Shotgun);
        Check(tactics.Resonance == 12, "Empty weapon switching does not grant resonance");

        var a = Enemy("Pact A", new Vector3(4, 1, 4), 10);
        var b = Enemy("Pact B", new Vector3(5, 1, 4), 10);
        var c = Enemy("Pact C", new Vector3(6, 1, 4), 10);
        Call(tactics, "CheckRoom");
        Check(tactics.HasOffer && !tactics.HasActivePact, "A populated room offers a voluntary pact");
        Check(tactics.IncomingDamageMultiplier == 1, "An unaccepted pact has no damage drawback");
        SetPact("offer", "Hunt");
        Check(tactics.TryAcceptPact() && !tactics.HasOffer, "Acceptance starts a twenty-five-second pact");
        Check(tactics.IncomingDamageMultiplier == 1.25f, "Hunt explicitly increases incoming damage");
        Time.timeScale = 0;
        float remaining = (float)Get(tactics, "pactRemaining");
        yield return .15f;
        Check(Mathf.Approximately((float)Get(tactics, "pactRemaining"), remaining), "Paused pact timers preserve the player's remaining time");
        Time.timeScale = 1;
        float armor = health.CurrentArmor;
        health.TakeDamage(4);
        Check(Mathf.Approximately(armor - health.CurrentArmor, 5), "Accepted hunt scales actual armor damage");
        var outsider = Enemy("Other Room", new Vector3(100, 1, 0), 10);
        outsider.TakeDamage(100, health);
        Check((int)Get(tactics, "pactKills") == 0, "Out-of-room kills cannot complete a room pact");
        int gold = inventory.Gold;
        Set(health, "currentHealth", 70f);
        a.TakeDamage(100, health); b.TakeDamage(100, health); c.TakeDamage(100, health);
        Check(!tactics.HasActivePact && tactics.IncomingDamageMultiplier == 1, "Three real kills remove the pact drawback");
        Check(inventory.Gold == gold + 42, "Pact success pays the level-based gold reward once");
        Check(Mathf.Approximately(health.CurrentHealth, 82), "Pact success restores twelve percent max health");
        int paid = inventory.Gold;
        c.TakeDamage(100, health);
        Check(inventory.Gold == paid, "Repeated damage on a dead target cannot duplicate rewards");
        yield return .15f;
        Call(tactics, "CheckRoom");
        Check(!tactics.HasOffer, "A visited room does not repeatedly offer rewards");

        SetPact("offer", "Untouched");
        tactics.TryAcceptPact();
        health.TakeDamage(1);
        Check(!tactics.HasActivePact && inventory.Gold == paid, "Armor damage fails a no-hit pact without charging resources");
        SetPact("offer", "Arsenal");
        tactics.TryAcceptPact();
        tactics.OnWeaponHit(target, WeaponType.Rifle);
        tactics.OnWeaponHit(target, WeaponType.Pistol);
        Check(tactics.HasActivePact, "Two weapon types do not complete an arsenal pact");
        tactics.OnWeaponHit(target, WeaponType.Shotgun);
        Check(!tactics.HasActivePact && inventory.Gold == paid + 42, "Three distinct hit types complete the arsenal pact");
        SetPact("offer", "Hunt");
        tactics.TryAcceptPact();
        Set(tactics, "pactRemaining", .01f);
        yield return .15f;
        Check(!tactics.HasActivePact && tactics.IncomingDamageMultiplier == 1, "Pact timeout removes its damage drawback");

        SetPact("offer", "Hunt"); tactics.TryAcceptPact();
        Record(new Vector3(0, 1.4f, 0), Vector3.forward);
        tactics.AddResonance(100); tactics.TryReleaseEcho();
        playerNode.transform.position = new Vector3(40, 0, 0);
        Call(tactics, "CheckRoom");
        Check(!tactics.HasActivePact && !tactics.IsReplaying && (int)Get(tactics, "memoryCount") == 0,
            "Leaving the room clears the pact and unlaunched echoes");
        playerNode.transform.position = Vector3.zero;
        Call(tactics, "CheckRoom");
        ClearEchoes();
        Call(inventory, "SetOpen", true);
        tactics.AddResonance(100);
        Check(!tactics.TryReleaseEcho(), "Inventory menus block echo activation");
        Record(new Vector3(0, 1.4f, 0), Vector3.forward);
        Check((int)Get(tactics, "memoryCount") == 0, "Inventory menus block shot recording");
        shooter.Fire();
        Check(Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length == 1, "A public Fire call respects inventory input gating");
        Call(inventory, "SetOpen", false);

        var captured = tactics.CaptureProgress();
        string json = JsonUtility.ToJson(new GameProgressState { tactics = captured });
        var decoded = JsonUtility.FromJson<GameProgressState>(json);
        tactics.RestoreProgress(decoded.tactics);
        Check(tactics.Resonance == captured.resonance && tactics.CaptureProgress().visitedRooms.Length == captured.visitedRooms.Length,
            "Tactics energy and visited rooms round-trip with game-progress JSON");
        var diskProgress = new GameProgressState { scene = "TestScene", health = health.CaptureProgress(),
            inventory = inventory.CaptureProgress(), floorKeys = new int[4], tactics = captured };
        string diskPath = Path.Combine(output, "isolated-progress.json");
        Check(GameProgressSave.TryWrite(diskPath, diskProgress, out string writeError), "Extended progress passes the existing checksum save writer: " + writeError);
        Check(GameProgressSave.TryRead(diskPath, out GameProgressState diskRead, out _) &&
            diskRead.tactics.resonance == captured.resonance, "The actual disk save restores tactics energy");
        diskProgress.tactics = null;
        Check(GameProgressSave.TryWrite(diskPath, diskProgress, out _) && GameProgressSave.TryRead(diskPath, out diskRead, out _) &&
            (diskRead.tactics == null || diskRead.tactics.version == 0), "The unchanged save validator accepts legacy progress without tactics");
        tactics.RestoreProgress(diskRead.tactics);
        Check(tactics.Resonance == 50, "Null inline objects materialized by Unity restore legacy defaults");
        tactics.RestoreProgress(JsonUtility.FromJson<GameProgressState>("{\"version\":1}").tactics);
        Check(tactics.Resonance == 50 && tactics.CaptureProgress().visitedRooms.Length == 0, "Old saves use safe tactics defaults");
        tactics.RestoreProgress(new RogueTacticsProgress { version = 1, resonance = float.NaN });
        Check(tactics.Resonance == 50, "Invalid saved resonance cannot poison gameplay math");

        Check(EnemyHealth.ActiveEnemies.Contains(target), "Enemy registry includes enabled live targets");
        target.gameObject.SetActive(false);
        Check(!EnemyHealth.ActiveEnemies.Contains(target), "Enemy registry removes disabled objects");
        target.gameObject.SetActive(true);
        Check(EnemyHealth.ActiveEnemies.Contains(target), "Enemy registry re-registers re-enabled targets");
        var p1 = Object.Instantiate(prefab); var p2 = Object.Instantiate(prefab);
        p1.Launch(Vector3.forward, 10, 5, playerNode);
        p2.Launch(Vector3.forward, 10, 5, playerNode);
        Check(Physics.GetIgnoreCollision(p1.GetComponent<Collider>(), p2.GetComponent<Collider>()), "Registered projectiles still ignore one another");
        Object.DestroyImmediate(p1.gameObject); Object.DestroyImmediate(p2.gameObject);
        var build = playerNode.GetComponent<WeaponBuildSystem>();
        ((System.Collections.IDictionary)Get(build, "armorBreak")).Add(target, 5);
        Set(build, "nextShotBonus", 4f);
        build.RefreshBuild();
        Check(((System.Collections.IDictionary)Get(build, "armorBreak")).Count == 0 && (float)Get(build, "nextShotBonus") == 1,
            "Changing builds clears old target marks and stored damage bonuses");

        Call(tactics, "CheckRoom");
        SetPact("offer", "Hunt");
        Check(tactics.TryAcceptPact(), "The death check begins with a real accepted pact");
        Record(new Vector3(0, 1.4f, 0), Vector3.forward);
        tactics.AddResonance(100);
        Check(tactics.TryReleaseEcho(), "The death check begins with pending echo playback");
        health.respawnDelay = 100;
        health.KillInstantly();
        Check(!tactics.HasActivePact && !tactics.IsReplaying && (int)Get(tactics, "memoryCount") == 0,
            "Actual player death clears wagers and pending replay");
        Check(!tactics.TryReleaseEcho(), "A dead player cannot activate an echo");
        Time.timeScale = 1;
        health.ResetHealth();
        health.respawnCanvas.SetActive(false);
        tactics.RestoreProgress(captured);
        Call(tactics, "CheckRoom");
        SetPact("offer", "Hunt");
        Set(tactics, "offerRemaining", 20f);
        Call(tactics, "RefreshHud");
        camera.transform.LookAt(new Vector3(0, 1, 0));
        yield return .15f;
        var rt = new RenderTexture(1280, 720, 24);
        camera.targetTexture = rt;
        var overlays = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        foreach (Canvas canvas in overlays)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
        }
        yield return .15f;
        Canvas.ForceUpdateCanvases();
        var pactLabel = (Text)Get(tactics, "pactText");
        Check(pactLabel.text.Contains("[Z]") && pactLabel.text.Contains("[X]") &&
            pactLabel.preferredHeight <= pactLabel.rectTransform.rect.height, "The complete offer and both input hints fit the HUD");
        camera.Render();
        RenderTexture previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, "tactics-hud.png"), image.EncodeToPNG());
        foreach (Canvas canvas in overlays) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        RenderTexture.active = previous; camera.targetTexture = null;
        Object.DestroyImmediate(image); Object.DestroyImmediate(rt);
        Check(!File.Exists(Path.Combine(output, "unused-save.json")), "Isolated checks never write a real player save");
    }
}
