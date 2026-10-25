using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MedievalDungeonKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Authors independent scene/prefab/mesh/navigation assets in an isolated project.</summary>
public static class ExpeditionMapBaker
{
    public const string AssetsPath = "Assets/Script/Dungeon/ExpeditionWorlds";
    public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
    public static void Run()
    {
        if(!Application.isBatchMode || Application.productName!="MapSelectionReview20261023")
            throw new InvalidOperationException("Author maps only in the isolated MapSelectionReview20261023 project.");
        foreach(var map in ExpeditionMaps.All.Skip(1)) Bake(map);
        var scenes=EditorBuildSettings.scenes.ToList();
        foreach(var map in ExpeditionMaps.All.Skip(1)) {
            string path="Assets/Scenes/"+map.Scene+".unity";
            int index=scenes.FindIndex(s=>s.path==path);
            if(index<0) scenes.Add(new EditorBuildSettingsScene(path,true)); else scenes[index].enabled=true;
        }
        EditorBuildSettings.scenes=scenes.ToArray(); AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(Output,"map-authoring-result.txt"),"PASS: three separate themed scenes, three regions per map, persistent meshes/materials/navigation.");
        ExpeditionMapReview.Run();
    }
    static void Bake(ExpeditionMapDefinition map)
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/Map1.unity",OpenSceneMode.Single);
        var original=Object.FindFirstObjectByType<OverworldDirector>();
        if(original==null || !original.IsAuthored) throw new InvalidOperationException("Source Map1 is missing its authored world.");
        var root=new GameObject(map.Name+" World",typeof(DungeonViewSettings),typeof(OverworldDirector));
        var groups=new Transform[3];
        for(int i=0;i<groups.Length;i++) { groups[i]=new GameObject("L"+(i+1)+"_Markers").transform;groups[i].SetParent(root.transform,false); }
        foreach(var marker in original.GetComponentsInChildren<DungeonMarker>(true)) {
            int floor=OverworldDirector.MarkerFloor(marker.transform);
            if(marker.kind=="Boss") floor=3;
            if(marker.name=="Stair_1_Top"||marker.name=="Stair_2_Bottom") floor=2;
            if(marker.name=="Stair_2_Top") floor=3;
            var node=new GameObject(marker.name,typeof(DungeonMarker));node.transform.SetParent(groups[floor-1],false);
            node.transform.localPosition=original.transform.InverseTransformPoint(marker.transform.position)-new Vector3((floor-1)*420,-(floor-1)*12,0);
            node.transform.localRotation=Quaternion.Inverse(original.transform.rotation)*marker.transform.rotation;
            node.GetComponent<DungeonMarker>().kind=marker.kind;
        }
        var world=root.GetComponent<OverworldDirector>();world.SetThemeForAuthoring(map.Index);world.BuildForAuthoring();
        string folder=AssetsPath+"/"+map.Scene;Directory.CreateDirectory(folder+"/Meshes");Directory.CreateDirectory(folder+"/Materials");Directory.CreateDirectory(folder+"/Navigation");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var meshes=new HashSet<Mesh>();var materials=new HashSet<Material>();int meshIndex=0,materialIndex=0,navIndex=0;
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            if(filter.sharedMesh!=null&&!AssetDatabase.Contains(filter.sharedMesh)&&meshes.Add(filter.sharedMesh))
                AssetDatabase.CreateAsset(filter.sharedMesh,folder+"/Meshes/Terrain_"+(meshIndex++)+".asset");
        foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach(var material in renderer.sharedMaterials)
                if(material!=null&&!AssetDatabase.Contains(material)&&materials.Add(material))
                    AssetDatabase.CreateAsset(material,folder+"/Materials/Palette_"+(materialIndex++)+".mat");
        foreach(var navigation in world.AuthoredNavigation)
            if(navigation!=null&&!AssetDatabase.Contains(navigation)) AssetDatabase.CreateAsset(navigation,folder+"/Navigation/Navigation_"+(navIndex++)+".asset");
        AssetDatabase.SaveAssets();
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/"+map.Scene+".prefab");
        if(prefab==null) throw new IOException("Could not save "+map.Scene);
        Object.DestroyImmediate(root);
        string scenePath="Assets/Scenes/"+map.Scene+".unity";
        EditorSceneManager.SaveScene(scene,scenePath,true);
        scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
        original=Object.FindFirstObjectByType<OverworldDirector>();
        Vector3 position=original.transform.position,scale=original.transform.localScale;Quaternion rotation=original.transform.rotation;
        Object.DestroyImmediate(original.gameObject);
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
        instance.transform.SetPositionAndRotation(position,rotation);instance.transform.localScale=scale;
        foreach(var enemy in Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None)) Object.DestroyImmediate(enemy.gameObject);
        RenderSettings.fogColor=map.Sky;RenderSettings.ambientLight=Color.Lerp(map.Sky,Color.white,.2f);
        foreach(var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            camera.backgroundColor=map.Sky;PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene,scenePath)) throw new IOException("Could not save scene "+scenePath);
        var settings=instance.GetComponent<DungeonViewSettings>();
        if(settings.level1==null||settings.level2==null||settings.level3==null||instance.GetComponent<OverworldDirector>().AuthoredNavigation.Count==0)
            throw new InvalidOperationException("The authored map does not contain all three stages/navigation: "+map.Scene);
        Debug.Log("AUTHORED "+map.Name+" with "+meshes.Count+" meshes, "+materials.Count+" materials and "+navIndex+" navigation assets.");
    }
}
