using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ModelExportBuilder
{
    const string Root = "Assets/Script/ModelExports";
    const string CharacterFolder = Root + "/Character";
    const string WeaponFolder = Root + "/Weapons";
    const string ItemFolder = Root + "/Items";
    const string MeshFolder = Root + "/Meshes";
    const string MaterialFolder = Root + "/Materials";
    const string PackageFolder = Root + "/Packages";
    const string CharacterSource =
        "Assets/Script/Player/Resources/Characters/AnimeGirl/AnimeGirlModel.prefab";
    const string ArmsSource =
        "Assets/Script/Player/Resources/Characters/AnimeGirl/AnimeGirlArms.prefab";
    const string SurfaceShader =
        "Assets/Script/Player/Resources/ItemSurface.shader";

    static readonly string[] WeaponNames =
    {
        "Rifle", "SubmachineGun", "Shotgun", "RPG",
        "Pistol", "SniperRifle", "LightMachineGun"
    };

    [MenuItem("Tools/Export/Build Character and Equipment Models")]
    public static void Build()
    {
        EnsureFolders();
        string playerPrefab = ExportCharacter(CharacterSource,
            CharacterFolder + "/PlayerCharacter.prefab", "Player Character Model");
        string armsPrefab = ExportCharacter(ArmsSource,
            CharacterFolder + "/FirstPersonArms.prefab", "First Person Arms Model");

        Material material = BuildEquipmentMaterial();
        var equipmentAssets = new List<string> { SurfaceShader, AssetDatabase.GetAssetPath(material) };
        for (int item = 0; item <= ItemAppearance.HealthPack; item++)
        {
            string displayName = item == ItemAppearance.HealthPack
                ? "MedicalKit" : WeaponNames[item];
            string meshPath = MeshFolder + "/" + displayName + ".asset";
            string prefabPath = (item == ItemAppearance.HealthPack ? ItemFolder : WeaponFolder) +
                                "/" + displayName + ".prefab";
            Mesh mesh = SaveMesh(ItemAppearance.GetMesh(item), meshPath, displayName);
            SaveEquipmentPrefab(mesh, material, prefabPath, displayName);
            equipmentAssets.Add(meshPath);
            equipmentAssets.Add(prefabPath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        WriteManifest();
        ModelExportVerifier.Verify();

        ExportPackage(new[] { playerPrefab, armsPrefab, Root + "/README.txt",
            "Assets/Script/Player/CharacterArt/README.md" },
            PackageFolder + "/CharacterModels.unitypackage");
        ExportPackage(equipmentAssets.ToArray(),
            PackageFolder + "/WeaponAndItemModels.unitypackage");
        ModelExportVerifier.RenderPreview();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("MODEL_EXPORT_OK " + Root);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets/Script", "ModelExports");
        EnsureFolder(Root, "Character");
        EnsureFolder(Root, "Weapons");
        EnsureFolder(Root, "Items");
        EnsureFolder(Root, "Meshes");
        EnsureFolder(Root, "Materials");
        EnsureFolder(Root, "Packages");
    }

    static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }

    static string ExportCharacter(string sourcePath, string destinationPath, string modelName)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null) throw new FileNotFoundException("Missing character prefab", sourcePath);
        GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (instance == null) throw new InvalidOperationException("Could not instantiate " + sourcePath);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance,
                PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = modelName;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, destinationPath);
            if (prefab == null) throw new InvalidOperationException("Could not save " + destinationPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
        return destinationPath;
    }

    static Material BuildEquipmentMaterial()
    {
        string path = MaterialFolder + "/EquipmentSurface.mat";
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(SurfaceShader);
        if (shader == null) throw new FileNotFoundException("Missing equipment shader", SurfaceShader);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = "Equipment Surface" };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    static Mesh SaveMesh(Mesh source, string path, string name)
    {
        if (source == null) throw new InvalidOperationException("Missing generated mesh " + name);
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = name;
            mesh.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(mesh, path);
        }
        else
        {
            EditorUtility.CopySerialized(source, mesh);
            mesh.name = name;
            mesh.hideFlags = HideFlags.None;
            EditorUtility.SetDirty(mesh);
        }
        return mesh;
    }

    static void SaveEquipmentPrefab(Mesh mesh, Material material, string path, string name)
    {
        var model = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        try
        {
            model.GetComponent<MeshFilter>().sharedMesh = mesh;
            model.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(model, path);
            if (prefab == null) throw new InvalidOperationException("Could not save " + path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(model);
        }
    }

    static void ExportPackage(string[] assets, string packagePath)
    {
        string absolutePath = Path.GetFullPath(packagePath);
        AssetDatabase.ExportPackage(assets, absolutePath,
            ExportPackageOptions.IncludeDependencies | ExportPackageOptions.Recurse);
        if (!File.Exists(absolutePath))
            throw new IOException("Unity package was not created: " + absolutePath);
    }

    static void WriteManifest()
    {
        string path = Path.GetFullPath(Root + "/README.txt");
        File.WriteAllText(path,
            "GameTest model exports\n\n" +
            "Character/PlayerCharacter.prefab   Complete third-person player character\n" +
            "Character/FirstPersonArms.prefab   First-person arms\n" +
            "Weapons/*.prefab                   Seven independent firearm models\n" +
            "Items/MedicalKit.prefab            Independent medical-kit model\n" +
            "Meshes/*.asset                     Baked equipment mesh assets\n" +
            "Materials/EquipmentSurface.mat     Shared vertex-colour material\n\n" +
            "Packages/CharacterModels.unitypackage\n" +
            "  Portable character package with all required dependencies.\n\n" +
            "Packages/WeaponAndItemModels.unitypackage\n" +
            "  Portable weapon/item package with meshes, material and shader.\n\n" +
            "The equipment prefabs contain visual models only. Gameplay scripts,\n" +
            "colliders and pickup behaviour are intentionally excluded.\n");
        AssetDatabase.ImportAsset(Root + "/README.txt", ImportAssetOptions.ForceUpdate);
    }
}
