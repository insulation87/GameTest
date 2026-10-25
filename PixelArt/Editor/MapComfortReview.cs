using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Explicit isolated edit-mode visual review of the actual dungeon geometry.</summary>
public static class MapComfortReview
{
    public static void Run()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        string prefabPath = "Assets/MedievalDungeon/Generated_20260914_214258/MedievalDungeon.prefab";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var map = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),scene);
        if (map == null) throw new InvalidOperationException("Actual dungeon prefab is missing.");
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Map1.unity");
        var paths = PixelMapBaker.MaterialPaths();
        var guids = paths.Select(AssetDatabase.AssetPathToGUID).ToArray();
        var textures = paths.Select(p=>AssetDatabase.LoadAssetAtPath<Material>(p).GetTexture("_MainTex")).ToArray();
        var camera = new GameObject("Map Palette Preview",typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(31,2.2f,27);
        camera.transform.LookAt(new Vector3(39,1.8f,49));
        camera.nearClipPlane = .1f; camera.farClipPlane = 160; camera.fieldOfView = 70; camera.aspect = 16f/9f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f,.13f,.16f);
        camera.allowHDR = camera.allowMSAA = false;
        Render(camera,Path.Combine(output,"map-before.png"));
        MapComfortPalette.Apply();
        foreach (var path in paths) AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        if (paths.Length != 9 || !guids.SequenceEqual(paths.Select(AssetDatabase.AssetPathToGUID)))
            throw new InvalidOperationException("Map material identity changed.");
        for (int i=0;i<paths.Length;i++)
            if (AssetDatabase.LoadAssetAtPath<Material>(paths[i]).GetTexture("_MainTex") != textures[i])
                throw new InvalidOperationException("Palette replacement changed a texture reference.");
        Render(camera,Path.Combine(output,"map-comfort.png"));
        var shader = Resources.Load<Shader>("PixelSurface");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("The actual map shader has compile errors.");
        File.WriteAllText(Path.Combine(output,"map-result.txt"),"PASS actual map rendering, shader compilation, nine saved materials, GUID and texture preservation");
        Debug.Log("MAP_COMFORT_REVIEW_PASS");
    }
    static void Render(Camera camera,string path)
    {
        var target = new RenderTexture(1280,720,24) {filterMode=FilterMode.Point};
        var previous = RenderTexture.active; Texture2D capture = null;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            capture = new Texture2D(1280,720,TextureFormat.RGB24,false);
            capture.ReadPixels(new Rect(0,0,1280,720),0,0); capture.Apply();
            if (capture.GetPixels32().Distinct().Count()<12) throw new InvalidOperationException("Map preview is blank.");
            File.WriteAllBytes(path,capture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
        }
    }
}
