using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MedievalDungeonKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Explicit entry point for an isolated copy, never automatically edits a user scene.
[InitializeOnLoad]
public static class OverworldReview
{
    const string Pending = "OverworldReview.Pending";
    static readonly List<string> results = new List<string>(), errors = new List<string>();
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static double nextTick, deadline;
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    [Serializable] class Ids { public string DungeonMarker, DungeonViewSettings, DungeonTestWalker; }
    static OverworldReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void RunAuthored()
    {
        RunScene(OverworldBaker.MapPath);
    }
    public static void Run()
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(Output, "source-guids.json")))
            throw new InvalidOperationException("Run this review only in an isolated batch project with copied map assets.");
        var ids = JsonUtility.FromJson<Ids>(File.ReadAllText(Path.Combine(Output, "source-guids.json")));
        string prefab = File.ReadAllText("Assets/MapSource/MedievalDungeon.prefab");
        Remap(ref prefab, typeof(DungeonMarker), ids.DungeonMarker);
        Remap(ref prefab, typeof(DungeonViewSettings), ids.DungeonViewSettings);
        Remap(ref prefab, typeof(DungeonTestWalker), ids.DungeonTestWalker);
        const string path = "Assets/OverworldMap.prefab"; File.WriteAllText(path, prefab);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        throw new InvalidOperationException("Runtime conversion has been removed. Use OverworldBaker.Run on an isolated project copy.");
    }
    static void RunScene(string path)
    {
        EditorSceneManager.playModeStartScene = null;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
        var playerNode = new GameObject("Review Player"); playerNode.SetActive(false);
        playerNode.transform.position = new Vector3(36, 1.08f, 5);
        var save = playerNode.AddComponent<GameProgressSave>(); save.enabled = false;
        save.StoragePath = Path.Combine(Output, "isolated-save.json");
        var health = playerNode.AddComponent<PlayerHealth>(); health.maxHealth = 10000;
        health.startingMaxArmor = health.startingArmorRegeneration = 0; health.pauseWorldWhileDead = false;
        playerNode.AddComponent<WeaponInventory>();
        playerNode.AddComponent<PlayerShooter>().enabled = false;
        playerNode.AddComponent<Player>(); playerNode.GetComponent<BoxCollider>().size = new Vector3(1, 2, 1);
        playerNode.SetActive(true);
        var template = new GameObject("Monster Template"); template.SetActive(false);
        template.AddComponent<BoxCollider>().size = new Vector3(1, 2, 1);
        var agent = template.AddComponent<NavMeshAgent>(); agent.baseOffset = 1; agent.radius = .4f;
        var enemyHealth = template.AddComponent<EnemyHealth>(); enemyHealth.showDamageNumbers = false;
        enemyHealth.weakNpcWeaponDropChance = enemyHealth.strongNpcWeaponDropChance = enemyHealth.healthDropChance =
            enemyHealth.armorEquipmentDropChance = 0;
        template.AddComponent<EnemyAI>(); template.SetActive(true);
        PrefabUtility.SaveAsPrefabAsset(template, "Assets/ReviewMonster.prefab"); Object.DestroyImmediate(template);
        EditorSceneManager.SaveScene(scene, "Assets/OverworldReview.unity");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Remap(ref string text, Type type, string original)
    {
        var node = new GameObject("Remap"); var component = (MonoBehaviour)node.AddComponent(type);
        var script = MonoScript.FromMonoBehaviour(component);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string guid, out long id);
        text = text.Replace("{fileID: 11500000, guid: " + original + ", type: 3}", "{fileID: " + id + ", guid: " + guid + ", type: 3}")
            .Replace("Assembly-CSharp::" + type.FullName, type.Assembly.GetName().Name + "::" + type.FullName);
        Object.DestroyImmediate(node);
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); results.Clear(); errors.Clear(); routines.Clear(); routines.Push(Checks());
        nextTick = EditorApplication.timeSinceStartup + .2; deadline = EditorApplication.timeSinceStartup + 120;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    {
        if (stack.Contains("UnityEditor.Search") || message.Contains("burst JIT compiler")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextTick) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Overworld review timed out.");
            while (routines.Count > 0)
            {
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                nextTick = EditorApplication.timeSinceStartup + (current.Current is float seconds ? seconds : .02f); return;
            }
            Finish(0, "PASS");
        }
        catch (Exception ex) { Finish(1, "FAIL " + ex); }
    }
    static void Finish(int code, string status)
    {
        File.WriteAllText(Path.Combine(Output, "overworld-result.txt"), status + "\n" + string.Join("\n", results) + "\n" + string.Join("\n", errors));
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code);
    }
    static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        results.Add(label); Debug.Log("PASS " + label);
    }
    static IEnumerator Wait(Func<bool> condition)
    {
        double until = EditorApplication.timeSinceStartup + 8;
        while (!condition()) { if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Waiting for gameplay state"); yield return .03f; }
    }
    static void Place(Player player, Vector3 ground)
    {
        Check(player.TryGetTeleportPosition(ground, Quaternion.identity, out var position), "Safe fixture placement at " + ground);
        player.Teleport(position, Quaternion.identity);
    }
    static IEnumerator Checks()
    {
        yield return .4f;
        Player player = Object.FindFirstObjectByType<Player>();
        // StoragePath is a runtime property and does not survive the EnterPlayMode domain reload.
        player.GetComponent<GameProgressSave>().StoragePath = Path.Combine(Output, "isolated-save.json");
        player.enabled = false;
        player.GetComponent<PlayerCursorController>().enabled = false;
        player.GetComponent<BossTestShortcut>().enabled = false;
        var world = Object.FindFirstObjectByType<OverworldDirector>();
        var manager = FloorProgressionManager.Instance;
        var map = Object.FindFirstObjectByType<DungeonViewSettings>();
        Check(Application.isPlaying && world != null && world.Built && world.IsAuthored, "Saved outdoor map loads in Play Mode without runtime geometry conversion");
        Check(manager.Portals.Count == 4, "Both region links have outward and return portals");
        Check(world.TreeCount > 300 && map.level1.name.Contains("風語草原") && map.level2.name.Contains("翠葉森林"), "Meadow and forest geometry is generated with hundreds of trees");
        Check(Object.FindFirstObjectByType<DungeonHazardDirector>().TotalCount == 0, "Indoor dungeon mechanisms are absent from the outdoor world");
        Check(map.shell == null && map.stair1 == null && map.stair2 == null, "Dungeon shell and stair geometry are removed from the saved map");
        foreach (int floor in new[] { 1, 2, 3 })
        {
            Vector3 center = world.RegionBounds[floor - 1].center;
            Check(world.RegionAt(center) == floor && NavMesh.SamplePosition(center, out var hit, 3, NavMesh.AllAreas), "Region " + floor + " has live outdoor navigation");
            Check(Physics.Raycast(center + Vector3.up * 3, Vector3.down, out var ground, 5) && ground.collider.name == "Meadow Ground", "Region " + floor + " has physical ground");
        }
        // Physically walk through a former dungeon corridor, using the player's actual Rigidbody.
        Place(player, new Vector3(150, 0, 154));
        var body = player.GetComponent<Rigidbody>(); body.useGravity = true;
        body.linearVelocity = new Vector3(10, 0, 0); yield return 1f;
        Check(player.transform.position.x > 157 && player.transform.position.y > .9f, "Player Rigidbody traverses open grass without the former maze walls");
        body.linearVelocity = Vector3.zero;
        var outward = manager.Portals.First(p => p.SourceRegion == 1);
        Place(player, outward.transform.position + Vector3.back * 4); yield return .1f;
        player.Teleport(outward.transform.position + Vector3.up * 1.08f, Quaternion.identity); yield return .2f;
        Check(world.RegionAt(player.transform.position) == 1 && !outward.IsUnlocked, "Entering a locked portal does not skip region progression");
        Place(player, outward.transform.position + Vector3.back * 4);
        Check(manager.GuidanceFor(player) == null, "Portal guidance stays hidden before the collection objective is complete");
        for (int i = 0; i < manager.RequiredItemsFor(1); i++)
        {
            var key = FloorKeyPickup.Create(player.transform.position, 1, 120);
            Check(key.TryCollect(player.GetComponent<BoxCollider>()), "Floor-one drop is collected through the real pickup component");
            Check(!key.TryCollect(player.GetComponent<BoxCollider>()), "The same progression pickup cannot count twice");
            yield return .05f;
        }
        Check(outward.IsUnlocked && manager.GuidanceFor(player) == outward.transform, "Completing meadow collection unlocks and guides to the forest portal");
        Time.timeScale = 0;
        Check(!manager.TryUsePortal(outward, player), "Paused gameplay cannot activate a portal"); Time.timeScale = 1;
        player.Teleport(outward.transform.position + Vector3.up * 1.08f, Quaternion.identity);
        yield return Wait(() => world.RegionAt(player.transform.position) == 2);
        Check(Vector3.Distance(player.transform.position, outward.Destination.transform.position) > 2.5f, "Forest arrival is outside the return portal trigger");
        yield return 1.3f;
        Check(world.RegionAt(player.transform.position) == 2, "Waiting after arrival does not bounce the player between portals");
        player.Teleport(outward.Destination.transform.position + Vector3.up * 1.08f, Quaternion.identity);
        yield return Wait(() => world.RegionAt(player.transform.position) == 1);
        Check(manager.CollectedFor(1) == manager.RequiredItemsFor(1), "Return travel preserves collection progress");
        yield return 1.3f;
        player.Teleport(outward.transform.position + Vector3.up * 1.08f, Quaternion.identity);
        yield return Wait(() => world.RegionAt(player.transform.position) == 2);
        var third = manager.Portals.First(p => p.SourceRegion == 2 && p.TargetRegion == 3);
        Place(player, third.transform.position + Vector3.back * 4);
        FloorKeyPickup.Create(player.transform.position, 1, 120).TryCollect(player.GetComponent<BoxCollider>());
        Check(manager.CollectedFor(2) == 0 && !third.IsUnlocked, "A meadow key cannot complete the forest objective");
        for (int i = 0; i < manager.RequiredItemsFor(2); i++)
            FloorKeyPickup.Create(player.transform.position, 2, 120).TryCollect(player.GetComponent<BoxCollider>());
        yield return .1f;
        Check(third.IsUnlocked && manager.GuidanceFor(player) == third.transform, "Forest completion guides to the ancient woodland portal");
        yield return 1.3f;
        player.Teleport(third.transform.position + Vector3.up * 1.08f, Quaternion.identity);
        yield return Wait(() => world.RegionAt(player.transform.position) == 3);
        Check(!player.GetComponent<PlayerHealth>().IsDead, "The player arrives alive on safe ancient forest ground");
        int[] progress = manager.CaptureProgress();
        manager.RestoreProgress(new int[4]); Check(!outward.IsUnlocked && !third.IsUnlocked, "Cleared progression locks forward portals");
        manager.RestoreProgress(progress); Check(outward.IsUnlocked && third.IsUnlocked, "Restored save progress immediately restores portal unlocks");
        Check(world.RegionAt(world.MigrateDungeonPosition(new Vector3(36, 12, 36))) == 2 &&
            world.RegionAt(world.MigrateDungeonPosition(new Vector3(36, 24, 36))) == 3, "Old floor-two and floor-three save coordinates migrate to the correct outdoor regions");
        yield return TestEnemies(player, world);
        var minimap = Object.FindFirstObjectByType<ExplorationMinimap>();
        Check(minimap.GetComponentInChildren<UnityEngine.UI.Text>().text == "暮光古林", "Minimap switches by outdoor region rather than floor height");
        var save = player.GetComponent<GameProgressSave>();
        foreach (var field in new[] { ("player", (object)player), ("health", (object)player.GetComponent<PlayerHealth>()),
            ("inventory", (object)player.GetComponent<WeaponInventory>()) })
            typeof(GameProgressSave).GetField(field.Item1, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(save, field.Item2);
        typeof(GameProgressSave).GetField("<Ready>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(save, true);
        Check(save.SaveNow(), "Actual game save writes successfully to the isolated test save path: " + save.LastMessage);
        Check(GameProgressSave.TryRead(save.StoragePath, out var saved, out string saveError), "Actual game save can be read with checksum verification: " + saveError);
        Check(saved.worldLayoutVersion == OverworldDirector.LayoutVersion && saved.floorKeys[2] == manager.CollectedFor(2), "Saved world layout and portal collection progress round-trip through JSON");
        saved.worldLayoutVersion = 0; saved.ground = new Vector3(36, 12, 36);
        typeof(GameProgressSave).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(save, new object[] { saved });
        Check(world.RegionAt(player.transform.position) == 2, "Actual save restore moves an old floor-two save into the new forest");
        var shader = Resources.Load<Shader>("PixelSurface");
        Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Terrain and monster surface shader compiles");
        yield return Capture(world.RegionBounds[0].center, "meadow-preview.png");
        yield return Capture(world.RegionBounds[1].center, "forest-preview.png");
        Check(errors.Count == 0, "No unexpected Unity gameplay errors: " + string.Join(" | ", errors));
    }
    static IEnumerator TestEnemies(Player player, OverworldDirector world)
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ReviewMonster.prefab");
        var node = new GameObject("Monster Spawn Review"); node.SetActive(false);
        var spawner = node.AddComponent<NPCSpawner>(); spawner.npcPrefab = template;
        spawner.playerTarget = player.transform; spawner.maxNpcCount = 1; spawner.roomSpawnDelay = 1000; spawner.experiencePerEnemyLevel = 0;
        node.SetActive(true); spawner.enabled = false;
        var spawn = typeof(NPCSpawner).GetMethod("TrySpawnNpcInRoom", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (int floor in new[] { 1, 2, 3 })
        {
            var room = Object.FindObjectsByType<DungeonRoomBoundary>(FindObjectsSortMode.None).First(r => r.name.StartsWith("L" + floor + "_R01"));
            Check((bool)spawn.Invoke(spawner, new object[] { room }), "Real NPC spawner creates a monster in region " + floor);
            yield return .15f;
            var enemy = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).First(e => e.gameObject != template && e.GetComponent<BossController>() == null);
            var ai = enemy.GetComponent<EnemyAI>(); ai.SetCombatPaused(true);
            string species = floor == 1 ? "Goblin" : floor == 2 ? "Orc" : "ForestImp";
            Check(enemy.DungeonFloor == floor && enemy.GetComponentInChildren<Animation>().name.Contains(species), "Region " + floor + " uses " + species + " and the correct drop region");
            Check(enemy.GetComponentsInChildren<Transform>().Any(t => t.name == "Pointed Ear") && enemy.GetComponent<NavMeshAgent>().isOnNavMesh, "Monster appearance and live NavMeshAgent initialize");
            Place(player, room.transform.position);
            Check(NavMesh.SamplePosition(room.transform.position + Vector3.right * 25, out var start, 3, NavMesh.AllAreas), "Monster chase start has walkable terrain");
            var liveAgent = enemy.GetComponent<NavMeshAgent>(); liveAgent.Warp(start.position);
            ai.target = player.transform; ai.SetCombatPaused(false);
            float beforeDistance = Vector3.Distance(enemy.transform.position, player.transform.position);
            yield return 1f;
            Check(Vector3.Distance(enemy.transform.position, player.transform.position) < beforeDistance - .3f, "Region " + floor + " monster follows a real path toward the player on outdoor terrain");
            ai.SetCombatPaused(true);
            if (floor == 3) yield return Capture(enemy.transform.position, "monster-preview.png", 7);
            int before = Object.FindObjectsByType<FloorKeyPickup>(FindObjectsSortMode.None).Length;
            enemy.TakeDamage(enemy.maxHealth + 1); yield return .1f;
            Check(Object.FindObjectsByType<FloorKeyPickup>(FindObjectsSortMode.None).Length == before + 1, "Defeating region " + floor + " monster drops a progression key");
        }
        for (int i = 0; i < FloorProgressionManager.Instance.RequiredItemsFor(3); i++)
            FloorKeyPickup.Create(player.transform.position, 3, 120).TryCollect(player.GetComponent<BoxCollider>());
        yield return .15f;
        var boss = Object.FindFirstObjectByType<BossController>();
        Check(boss != null && FloorProgressionManager.Instance.GuidanceFor(player) == boss.transform, "Completing ancient forest collection spawns and guides to the boss");
        boss.GetComponent<EnemyAI>().SetCombatPaused(true);
        Object.Destroy(node);
    }
    static IEnumerator Capture(Vector3 point, string filename, float distance = 50)
    {
        var node = new GameObject("Outdoor Preview", typeof(Camera)); var camera = node.GetComponent<Camera>();
        camera.transform.position = point + new Vector3(distance, distance * .65f, -distance);
        camera.transform.LookAt(point + Vector3.up * (distance < 10 ? .9f : 1));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
        camera.nearClipPlane = .1f; camera.farClipPlane = 300;
        var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
        yield return .05f; camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(Output, filename), texture.EncodeToPNG());
        RenderTexture.active = old; camera.targetTexture = null;
        Object.Destroy(texture); Object.Destroy(target); Object.Destroy(node);
    }
}
