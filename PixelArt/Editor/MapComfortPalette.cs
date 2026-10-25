using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit saved material edits, scoped to the actual Map1 dependency list.</summary>
public static class MapComfortPalette
{
    [Serializable] public sealed class Entry { public string name, color; public float textureInfluence; }
    [Serializable] public sealed class Palette
    {
        public int version;
        public float textureSaturation, minimumShade, mortarContrast, grainStrength, toneSteps;
        public Entry[] materials;
    }
    public static Palette Read()
    {
        var source = Resources.Load<TextAsset>("MapComfortPalette");
        if (source == null) throw new InvalidOperationException("MapComfortPalette.json is missing.");
        var palette = JsonUtility.FromJson<Palette>(source.text);
        if (palette == null || palette.version != 1 || palette.materials == null || palette.materials.Length != 9)
            throw new InvalidOperationException("Map palette must define all nine map materials.");
        var names = new HashSet<string>();
        foreach (var item in palette.materials)
            if (item == null || !names.Add(item.name) || !ColorUtility.TryParseHtmlString(item.color,out _) ||
                float.IsNaN(item.textureInfluence) || item.textureInfluence < 0 || item.textureInfluence > 1)
                throw new InvalidOperationException("Invalid map palette entry.");
        return palette;
    }
    [MenuItem("Tools/Pixel Art/Apply Comfortable Map Palette")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Apply the map palette outside Play Mode.");
        var palette = Read();
        var entries = new Dictionary<string,Entry>();
        foreach (var entry in palette.materials) entries.Add(entry.name,entry);
        string[] paths = PixelMapBaker.MaterialPaths();
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Script/PixelArt/Resources/PixelSurface.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Map shader is missing or invalid.");
        var materials = new List<Material>();
        foreach (string path in paths)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader != shader || !entries.ContainsKey(material.name))
                throw new InvalidOperationException("Unexpected map material: " + path);
            materials.Add(material);
        }
        if (materials.Count != entries.Count) throw new InvalidOperationException("Map material count does not match the palette.");
        foreach (var material in materials)
        {
            var entry = entries[material.name]; ColorUtility.TryParseHtmlString(entry.color,out Color color);
            Undo.RecordObject(material,"Comfortable map palette");
            material.SetColor("_Color",color);
            material.SetFloat("_TextureInfluence",entry.textureInfluence);
            material.SetFloat("_TextureSaturation",palette.textureSaturation);
            material.SetFloat("_MinimumShade",palette.minimumShade);
            material.SetFloat("_MortarContrast",palette.mortarContrast);
            material.SetFloat("_GrainStrength",palette.grainStrength);
            material.SetFloat("_ToneSteps",palette.toneSteps);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        }
        SceneView.RepaintAll();
        Debug.Log("Comfortable map palette saved to " + materials.Count + " existing map materials.");
    }
    [MenuItem("Tools/Pixel Art/Apply Comfortable Map Palette",true)]
    static bool CanApply() => !EditorApplication.isPlayingOrWillChangePlaymode;
}
