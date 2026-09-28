using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ModelExportVerifier
{
    const string Root = "Assets/Script/ModelExports";
    static string importFolder;
    static int importedPackages;
    static readonly string[] Equipment = {
        "Weapons/Rifle", "Weapons/SubmachineGun", "Weapons/Shotgun", "Weapons/RPG",
        "Weapons/Pistol", "Weapons/SniperRifle", "Weapons/LightMachineGun", "Items/MedicalKit"
    };

    public static void Verify()
    {
        ValidatePrefab("Character/PlayerCharacter", true);
        ValidatePrefab("Character/FirstPersonArms", false);
        foreach (string path in Equipment) ValidatePrefab(path, false);
        Debug.Log("MODEL_EXPORT_VERIFIED: 10 prefabs, meshes, materials, skeleton and animation references.");
    }

    static void ValidatePrefab(string path, bool skinned)
    {
        string assetPath = Root + "/" + path + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null) throw new InvalidOperationException("Missing prefab: " + assetPath);
        foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            if (component == null) throw new InvalidOperationException("Missing component in " + path);
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("No renderers: " + path);
        foreach (Renderer renderer in renderers)
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null || material.shader.name.Contains("InternalErrorShader"))
                    throw new InvalidOperationException("Missing material or shader: " + path);
            }
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0 || !AssetDatabase.Contains(mesh))
                throw new InvalidOperationException("Missing persistent mesh: " + path);
            if (renderer is SkinnedMeshRenderer rig)
            {
                if (rig.bones.Length != mesh.bindposes.Length)
                    throw new InvalidOperationException("Invalid skin binding: " + path);
                foreach (Transform bone in rig.bones)
                    if (bone == null) throw new InvalidOperationException("Missing bone: " + path);
            }
        }
        if (skinned)
        {
            Animation animation = prefab.GetComponentInChildren<Animation>(true);
            if (animation == null || animation.GetClipCount() < 9)
                throw new InvalidOperationException("Missing character animations: " + path);
            foreach (AnimationState state in animation)
                if (state.clip == null) throw new InvalidOperationException("Missing animation clip: " + path);
        }
        foreach (string dependency in AssetDatabase.GetDependencies(assetPath, true))
        {
            if (dependency.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Model depends on gameplay script: " + dependency);
            if (dependency.StartsWith("Assets/", StringComparison.Ordinal) && !File.Exists(dependency))
                throw new FileNotFoundException("Missing export dependency", dependency);
        }
        Debug.Log("MODEL_PREFAB_OK " + path);
    }

    public static void RenderPreview()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var cameraObject = new GameObject("Export Preview Camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.scene = scene;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = 2.1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.055f, .075f, .1f);
        var texture = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
        RenderTexture previous = RenderTexture.active;
        Texture2D capture = null;
        try
        {
            camera.targetTexture = texture;
            GameObject character = Spawn("Character/PlayerCharacter", scene);
            character.transform.rotation = Quaternion.Euler(0, 180, 0);
            character.transform.localScale = Vector3.one * 1.3f;
            character.transform.position = new Vector3(-2.25f, -1.5f, 0);
            Animation animation = character.GetComponentInChildren<Animation>();
            if (animation != null && animation.clip != null)
                animation.clip.SampleAnimation(animation.gameObject, 0f);
            Label("PLAYER CHARACTER", new Vector3(-3.1f, 1.65f, -1), scene);

            for (int i = 0; i < Equipment.Length; i++)
            {
                GameObject model = Spawn(Equipment[i], scene);
                model.transform.rotation = i == 7 ? Quaternion.Euler(12,-24,0) : Quaternion.Euler(10,-72,-8);
                Bounds bounds = model.GetComponent<Renderer>().bounds;
                float scale = Mathf.Min(1.18f / bounds.size.x, .7f / bounds.size.y);
                model.transform.localScale = Vector3.one * scale;
                model.transform.position = new Vector3(-.55f + i % 3 * 1.35f, 1.05f - i / 3 * 1.15f, 0) - bounds.center * scale;
                string label = Path.GetFileName(Equipment[i]);
                Label(label, new Vector3(-1.13f + i % 3 * 1.35f, .52f - i / 3 * 1.15f, -1), scene);
            }
            camera.Render();
            RenderTexture.active = texture;
            capture = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            capture.Apply();
            File.WriteAllBytes(Root + "/Preview.png", capture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            if (capture != null) UnityEngine.Object.DestroyImmediate(capture);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static GameObject Spawn(string name, UnityEngine.SceneManagement.Scene scene)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + name + ".prefab");
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
    }

    static void Label(string text, Vector3 position, UnityEngine.SceneManagement.Scene scene)
    {
        var go = new GameObject(text);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = position;
        TextMesh label = go.AddComponent<TextMesh>();
        label.text = text;
        label.fontSize = 40;
        label.characterSize = .026f;
        label.anchor = TextAnchor.MiddleLeft;
    }

    public static void ImportAndVerify()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-modelPackageFolder");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing -modelPackageFolder");
        importFolder = args[index + 1];
        importedPackages = 0;
        AssetDatabase.importPackageCompleted += OnPackageImported;
        AssetDatabase.importPackageFailed += OnPackageFailed;
        AssetDatabase.importPackageCancelled += OnPackageCancelled;
        AssetDatabase.ImportPackage(Path.Combine(importFolder, "CharacterModels.unitypackage"), false);
    }

    static void OnPackageImported(string packageName)
    {
        importedPackages++;
        if (importedPackages == 1)
        {
            EditorApplication.delayCall += () => AssetDatabase.ImportPackage(
                Path.Combine(importFolder, "WeaponAndItemModels.unitypackage"), false);
            return;
        }
        Unsubscribe();
        EditorApplication.delayCall += () =>
        {
            try
            {
                Verify();
                RenderPreview();
                Debug.Log("MODEL_CLEAN_IMPORT_OK");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        };
    }

    static void OnPackageFailed(string packageName, string error)
    {
        Unsubscribe();
        Debug.LogError("Model package import failed: " + packageName + ": " + error);
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }

    static void OnPackageCancelled(string packageName) => OnPackageFailed(packageName, "Cancelled");

    static void Unsubscribe()
    {
        AssetDatabase.importPackageCompleted -= OnPackageImported;
        AssetDatabase.importPackageFailed -= OnPackageFailed;
        AssetDatabase.importPackageCancelled -= OnPackageCancelled;
    }
}
