using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedievalDungeonKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Tests the saved Map1 copy with its original player, cameras, spawner and prefab offset.
[InitializeOnLoad]
public static class AuthoredMapSceneReview
{
    const string Pending = "AuthoredMapSceneReview.Pending";
    static readonly List<string> results = new List<string>(), errors = new List<string>();
    static IEnumerator routine;
    static double nextTick, deadline;
    static AuthoredMapSceneReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(OverworldBaker.Output, "authored-world-review.json")))
            throw new InvalidOperationException("Run only in an isolated project copy.");
        LoginSceneSetup.PlayFromCurrentScene();
        EditorSceneManager.OpenScene(OverworldBaker.ScenePath, OpenSceneMode.Single);
        var world = Object.FindFirstObjectByType<OverworldDirector>();
        if (world == null || !world.IsAuthored) throw new InvalidOperationException("Map1 has no saved outdoor map.");
        SessionState.SetInt(Pending + ".renderers", world.GetComponentsInChildren<Renderer>(true).Length);
        // Disable saves only in memory; never save this testing override to Map1.
        foreach (var player in Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            var save = player.GetComponent<GameProgressSave>();
            if (save == null) save = player.gameObject.AddComponent<GameProgressSave>();
            save.enabled = false;
        }
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        results.Clear(); errors.Clear(); routine = Checks();
        nextTick = EditorApplication.timeSinceStartup + .1;
        deadline = EditorApplication.timeSinceStartup + 60;
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
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Map1 review timed out.");
            if (!routine.MoveNext()) { Finish(0, "PASS"); return; }
            nextTick = EditorApplication.timeSinceStartup + (routine.Current is float seconds ? seconds : .03f);
        }
        catch (Exception ex) { Finish(1, "FAIL " + ex); }
    }
    static void Finish(int code, string status)
    {
        File.WriteAllText(Path.Combine(OverworldBaker.Output, "map1-play-result.txt"), status + "\n" + string.Join("\n", results) + "\n" + string.Join("\n", errors));
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code);
    }
    static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        results.Add(label); Debug.Log("PASS " + label);
    }
    static IEnumerator Checks()
    {
        yield return 1f;
        var world = Object.FindFirstObjectByType<OverworldDirector>();
        var player = Object.FindFirstObjectByType<Player>();
        var settings = world.GetComponent<DungeonViewSettings>();
        Check(player.gameObject.scene.name == "Map1" && world.Built && world.IsAuthored, "Original Map1 player loads the saved outdoor prefab");
        Check(world.GetComponentsInChildren<Renderer>(true).Length == SessionState.GetInt(Pending + ".renderers", -1), "Entering Play Mode does not create or replace terrain renderers");
        Check(settings.shell == null && settings.stair1 == null && settings.stair2 == null, "Former maze shell and stairs are absent from Map1");
        Check(!player.GetComponent<GameProgressSave>().enabled, "Original player saves remain disabled throughout this isolated test");
        Check(player.IsGrounded && !player.GetComponent<PlayerHealth>().IsDead, "Original player lands safely at the preserved meadow start");
        Check(player.tpsCamera != null && player.fpsCamera != null, "Original third-person and first-person camera references survive map replacement");
        foreach (var surface in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (surface.GetType().Name == "NavMeshSurface") Check(!surface.enabled, "Obsolete dungeon navigation remains disabled in Map1");
        for (int i = 0; i < 3; i++)
        {
            var expected = world.transform.TransformPoint(new Vector3(150 + 420 * i, 0, 150));
            Check(Vector3.Distance(world.RegionBounds[i].center, expected) < .01f && world.RegionAt(expected) == i + 1,
                "Map1 region " + (i + 1) + " bounds respect the preserved prefab offset");
            Check(NavMesh.SamplePosition(expected, out var nav, 5, NavMesh.AllAreas), "Map1 region " + (i + 1) + " has navigation at the scene offset");
            Check(player.TryGetTeleportPosition(nav.position, Quaternion.identity, out var safe), "Original player finds safe ground in region " + (i + 1));
            player.Teleport(safe, Quaternion.identity);
            yield return .35f;
            Check(player.IsGrounded && !player.GetComponent<PlayerHealth>().IsDead, "Original player remains grounded in region " + (i + 1));
        }
        var spawner = Object.FindFirstObjectByType<NPCSpawner>();
        Check(spawner != null && spawner.npcPrefab != null && spawner.playerTarget != null, "Original NPC spawner retains its prefab and player references");
        Check(spawner.TrySpawnNpc(), "Original Map1 NPC prefab spawns on the authored navigation");
        yield return .5f;
        Check(Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).Any(e => {
            var agent = e.GetComponent<NavMeshAgent>();
            return agent != null && agent.enabled && agent.isOnNavMesh && e.GetComponent<EnemyCharacterVisual>() != null;
        }), "Original Map1 monsters initialize their visual and live navigation");
        Check(Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).All(e => e.transform.position.y < 6), "Previously placed upper-floor monsters relocate to the outdoor ground");
        Capture(world.RegionBounds[0].center, "map1-meadow-preview.png");
        Capture(world.RegionBounds[1].center, "map1-forest-preview.png");
        yield return .1f;
        Check(errors.Count == 0, "No unexpected Map1 gameplay errors: " + string.Join("; ", errors));
    }
    static void Capture(Vector3 center, string filename)
    {
        var node = new GameObject("Map1 Review Camera", typeof(Camera));
        var camera = node.GetComponent<Camera>();
        camera.transform.position = center + new Vector3(50, 32.5f, -50);
        camera.transform.LookAt(center + Vector3.up);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
        camera.nearClipPlane = .1f; camera.farClipPlane = 300;
        var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(OverworldBaker.Output, filename), texture.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        Object.Destroy(texture); Object.Destroy(target); Object.Destroy(node);
    }
}
