using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ExpeditionMapDefinition
{
    public readonly string Scene, Name, Description;
    public readonly string[] Stages;
    public readonly int Index;
    public readonly float Health, Damage, SpawnRate;
    public readonly Color Ground, Highlight, Foliage, Rock, Sky;
    public ExpeditionMapDefinition(int index, string scene, string name, string description, string[] stages,
        float health, float damage, float spawnRate, Color ground, Color highlight, Color foliage, Color rock, Color sky)
    {
        Index = index; Scene = scene; Name = name; Description = description; Stages = stages;
        Health = health; Damage = damage; SpawnRate = spawnRate;
        Ground = ground; Highlight = highlight; Foliage = foliage; Rock = rock; Sky = sky;
    }
}

/// <summary>All maps are available immediately. Scene identity owns difficulty and save restoration.</summary>
public static class ExpeditionMaps
{
    public static readonly ExpeditionMapDefinition[] All = {
        new ExpeditionMapDefinition(0,"Map1","風語林地","開闊草原、林間小徑與古樹領地。",new[]{"風語草原","翠葉森林","暮光古林"},
            1,1,1,new Color(.40f,.61f,.23f),new Color(.56f,.69f,.31f),new Color(.18f,.43f,.23f),new Color(.39f,.44f,.36f),new Color(.66f,.79f,.76f)),
        new ExpeditionMapDefinition(1,"Map2","赤砂遺跡","仙人掌石原、殘破石柱與沙海神殿。",new[]{"赤砂邊境","斷柱遺城","黃金沙殿"},
            1.5f,1.25f,1.15f,new Color(.69f,.49f,.27f),new Color(.91f,.72f,.43f),new Color(.25f,.44f,.28f),new Color(.60f,.39f,.25f),new Color(.83f,.71f,.53f)),
        new ExpeditionMapDefinition(2,"Map3","霜雪山境","積雪松林、冰晶隘口與寒霜聖所。",new[]{"雪松山麓","冰晶隘口","霜王聖所"},
            2.1f,1.5f,1.3f,new Color(.69f,.82f,.87f),new Color(.91f,.96f,.98f),new Color(.20f,.38f,.43f),new Color(.34f,.60f,.74f),new Color(.65f,.78f,.87f)),
        new ExpeditionMapDefinition(3,"Map4","燼火荒原","玄武岩柱、熔光裂紋與黑曜王座。",new[]{"灰燼石原","熔光裂谷","黑曜王座"},
            2.9f,1.8f,1.5f,new Color(.19f,.17f,.19f),new Color(.32f,.26f,.25f),new Color(.35f,.18f,.15f),new Color(.22f,.23f,.28f),new Color(.37f,.23f,.21f))
    };
    static string preparedScene;
    public static ExpeditionMapDefinition Current => ForScene(SceneManager.GetActiveScene().name);
    public static bool IsMap(string scene) { foreach (var map in All) if (map.Scene == scene) return true; return false; }
    public static ExpeditionMapDefinition ForScene(string scene) { foreach (var map in All) if (map.Scene == scene) return map; return All[0]; }
    public static void Prepare(string scene) => preparedScene = ForScene(scene).Scene;
    public static string TakePreparation() { string scene = preparedScene; preparedScene = null; return scene; }
    public static void ClearPreparation() => preparedScene = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => preparedScene = null;
}
