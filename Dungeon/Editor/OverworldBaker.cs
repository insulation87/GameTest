using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedievalDungeonKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit authoring entry point. Batch automation must run in an isolated project copy.
public static class OverworldBaker
{
    public const string MapPath = "Assets/MedievalDungeon/Generated_20260914_214258/MedievalDungeon.prefab";
    public const string ScenePath = "Assets/Scenes/Map1.unity";
    public const string AssetsPath = "Assets/Script/Dungeon/AuthoredWorld";
    public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

    public static void Run()
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(Output, "authored-world-review.json")))
            throw new InvalidOperationException("Use an isolated project copy for automatic map replacement.");
        BakePrefab();
        PrepareMapScene();
        File.WriteAllText(Path.Combine(Output, "authored-edit-result.txt"), "PASS: saved outdoor prefab, meshes, materials, navigation and Map1 scene in Edit Mode.");
        OverworldReview.RunAuthored();
    }

    public static void BakePrefab()
    {
        var root = PrefabUtility.LoadPrefabContents(MapPath);
        try
        {
            var settings = root.GetComponent<DungeonViewSettings>();
            if (settings == null) throw new InvalidOperationException("Source map has no view settings.");
            var world = root.GetComponent<OverworldDirector>();
            if (world == null) world = root.AddComponent<OverworldDirector>();
            if (world.IsAuthored) return;
            world.BuildForAuthoring();
            var keep = new HashSet<GameObject> { settings.level1, settings.level2, settings.level3 };
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                if (!keep.Contains(child.gameObject) && child.GetComponentInChildren<DungeonMarker>(true) == null &&
                    child.name != "Meadow Sun") Object.DestroyImmediate(child.gameObject);
            settings.shell = settings.stair1 = settings.stair2 = null;
            foreach (var behaviour in root.GetComponents<MonoBehaviour>())
                if (behaviour != null && behaviour.GetType().Name == "NavMeshSurface") Object.DestroyImmediate(behaviour);
            Directory.CreateDirectory(AssetsPath + "/Meshes");
            Directory.CreateDirectory(AssetsPath + "/Materials");
            Directory.CreateDirectory(AssetsPath + "/Navigation");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var meshAssets = new HashSet<Mesh>();
            var materialAssets = new HashSet<Material>();
            int meshIndex = 0, materialIndex = 0, navIndex = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && !AssetDatabase.Contains(filter.sharedMesh) && meshAssets.Add(filter.sharedMesh))
                    AssetDatabase.CreateAsset(filter.sharedMesh, AssetsPath + "/Meshes/Outdoor_" + meshIndex++ + ".asset");
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && !AssetDatabase.Contains(material) && materialAssets.Add(material))
                        AssetDatabase.CreateAsset(material, AssetsPath + "/Materials/Outdoor_" + materialIndex++ + ".mat");
            foreach (var data in world.AuthoredNavigation)
                if (data != null && !AssetDatabase.Contains(data))
                    AssetDatabase.CreateAsset(data, AssetsPath + "/Navigation/Outdoor_" + navIndex++ + ".asset");
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root, MapPath, out bool saved);
            if (!saved) throw new IOException("Outdoor prefab did not save.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        // Reload the saved prefab to catch references to transient meshes/materials before Play Mode.
        root = PrefabUtility.LoadPrefabContents(MapPath);
        try
        {
            var world = root.GetComponent<OverworldDirector>();
            if (world == null || !world.IsAuthored || world.AuthoredNavigation.Count == 0)
                throw new InvalidOperationException("Authored world metadata/navigation did not persist.");
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !AssetDatabase.Contains(filter.sharedMesh))
                    throw new InvalidOperationException("Outdoor geometry has a transient or missing mesh.");
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material == null || !AssetDatabase.Contains(material))
                        throw new InvalidOperationException("Outdoor geometry has a transient or missing material.");
            if (root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Ceiling" || t.name.StartsWith("Stair_1_to_") || t.name == "Shell"))
                throw new InvalidOperationException("The saved map still contains indoor geometry.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void PrepareMapScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var world = Object.FindFirstObjectByType<OverworldDirector>();
        var player = Object.FindFirstObjectByType<Player>();
        var spawner = Object.FindFirstObjectByType<NPCSpawner>();
        if (world == null || !world.IsAuthored || player == null || spawner == null || spawner.npcPrefab == null)
            throw new InvalidOperationException("Map1 lost a required map/player/spawner reference.");
        foreach (Player actor in Object.FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            actor.transform.position = world.MigrateDungeonPosition(actor.transform.position);
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
            var health = actor.GetComponent<PlayerHealth>();
            if (health != null && health.respawnPoint != null)
            {
                health.respawnPoint.position = world.MigrateDungeonPosition(health.respawnPoint.position);
                PrefabUtility.RecordPrefabInstancePropertyModifications(health.respawnPoint);
            }
        }
        foreach (EnemyHealth enemy in Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            enemy.transform.position = world.MigrateDungeonPosition(enemy.transform.position);
            enemy.SetDungeonFloor(world.RegionAt(enemy.transform.position));
            PrefabUtility.RecordPrefabInstancePropertyModifications(enemy.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(enemy);
        }
        // The former dungeon NavigationManager must not register its obsolete baked surface.
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (component.GetType().Name == "NavMeshSurface")
            { component.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.66f, .79f, .76f);
        RenderSettings.fogStartDistance = 80; RenderSettings.fogEndDistance = 220;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.68f, .75f, .65f);
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (camera.targetTexture == null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor; camera.farClipPlane = 280;
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            }
        foreach (Transform candidate in world.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(candidate.gameObject) > 0)
                throw new InvalidOperationException("Authored map contains missing scripts.");
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Map1 did not save.");
        // Re-open the disk scene: it must already be outdoors before any Play Mode callbacks run.
        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        world = Object.FindFirstObjectByType<OverworldDirector>();
        var settings = world.GetComponent<DungeonViewSettings>();
        if (!settings.level1.activeSelf || !settings.level2.activeSelf || !settings.level3.activeSelf ||
            settings.level1.GetComponentsInChildren<Renderer>().Length == 0)
            throw new InvalidOperationException("The saved scene is not an outdoor map in Edit Mode.");
        File.WriteAllText(Path.Combine(Output, "authored-scene-inspection.txt"),
            "Map1 root: " + world.transform.position + "\n" +
            "Player preserved: " + Object.FindFirstObjectByType<Player>().transform.position + "\n" +
            "Outdoor renderer count: " + world.GetComponentsInChildren<Renderer>().Length + "\n" +
            "Root objects: " + string.Join(", ", scene.GetRootGameObjects().Select(g => g.name)));
    }
}
