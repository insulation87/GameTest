using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Checks persisted map assets and renders the real dungeon without entering Play Mode.</summary>
public static class PixelMapReview
{
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Map review must run in Edit Mode.");
        string[] paths = PixelMapBaker.MaterialPaths();
        string[] guids = paths.Select(AssetDatabase.AssetPathToGUID).ToArray();
        int changed = PixelMapBaker.Bake();
        int assertions = 0;
        Shader shader = Resources.Load<Shader>("PixelSurface");
        Require(paths.Length == 9, "All nine Map1 materials found", ref assertions);
        Require(PixelMapBaker.Bake() == 0, "Repeated baking leaves edited pixel assets intact", ref assertions);
        foreach (string path in paths)
        {
            // Reimport the saved file: do not pass just because an in-memory material was changed.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Require(material.shader == shader && EditorUtility.IsPersistent(material), "Saved pixel material " + path, ref assertions);
            Texture texture = material.GetTexture("_MainTex");
            if (texture == null) continue;
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            Require(EditorUtility.IsPersistent(texture) && texture.width == 32 && texture.height == 32 &&
                texture.filterMode == FilterMode.Point && !importer.mipmapEnabled &&
                importer.textureCompression == TextureImporterCompression.Uncompressed,
                "Saved point-filtered 32x32 map texture", ref assertions);
        }
        Require(guids.SequenceEqual(paths.Select(AssetDatabase.AssetPathToGUID)), "Existing map material GUIDs retained", ref assertions);
        string prefabPath = AssetDatabase.GetDependencies(PixelMapBaker.MapPath, true)
            .Single(path => path.StartsWith("Assets/MedievalDungeon/", StringComparison.Ordinal) && path.EndsWith("/MedievalDungeon.prefab"));
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject map = null, cameraNode = null;
        try
        {
            map = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
            MeshRenderer[] renderers = map.GetComponentsInChildren<MeshRenderer>(true);
            Require(renderers.Length > 100, "Real dungeon geometry loaded", ref assertions);
            foreach (MeshRenderer renderer in renderers)
                foreach (Material material in renderer.sharedMaterials)
                    Require(material != null && material.shader == shader && EditorUtility.IsPersistent(material),
                        "Prefab renderer already uses saved pixels in Edit Mode: " + renderer.name, ref assertions);
            Require(map.GetComponentsInChildren<Terrain>(true).Length == 0, "Map1 uses mesh floors, not Unity Terrain", ref assertions);
            Require(map.GetComponentInChildren<PixelArtDirector>() == null && map.GetComponentInChildren<PixelTerrainStyle>() == null,
                "Preview has no runtime material conversion", ref assertions);
            cameraNode = new GameObject("Map Edit Mode Review", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraNode, scene);
            Camera camera = cameraNode.GetComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = new Vector3(31, 2.2f, 27);
            camera.transform.LookAt(new Vector3(39, 1.8f, 49));
            camera.nearClipPlane = .1f; camera.farClipPlane = 160;
            camera.fieldOfView = 70; camera.aspect = 16f / 9;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .045f, .065f);
            camera.allowHDR = camera.allowMSAA = false;
            Render(camera);
            Require(!ShaderUtil.ShaderHasError(shader), "Map shader compiled after actual render", ref assertions);
            Debug.Log("PIXEL_MAP_REVIEW_PASS: " + assertions + " checks; " + changed +
                " materials saved; " + renderers.Length + " prefab renderers already pixel-styled in Edit Mode.");
        }
        finally
        {
            if (cameraNode != null) UnityEngine.Object.DestroyImmediate(cameraNode);
            if (map != null) UnityEngine.Object.DestroyImmediate(map);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void Render(Camera camera)
    {
        var target = new RenderTexture(1280, 720, 24);
        RenderTexture previous = RenderTexture.active;
        Texture2D capture = null;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            capture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            capture.Apply();
            Color32[] colors = capture.GetPixels32();
            if (colors.Distinct().Count() < 12) throw new InvalidOperationException("Map preview is blank or missing materials.");
            Directory.CreateDirectory("Assets/Script/PixelArt/Previews");
            File.WriteAllBytes("Assets/Script/PixelArt/Previews/MapEditMode.png", capture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
        }
    }

    static void Require(bool result, string message, ref int count)
    {
        count++;
        if (!result) throw new InvalidOperationException("Map review: " + message);
    }
}
