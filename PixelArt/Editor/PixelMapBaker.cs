using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Persists the map's shared materials, so prefabs and Scene view use the same pixel art as Play Mode.</summary>
public static class PixelMapBaker
{
    public const string MapPath = "Assets/Scenes/Map1.unity";
    public const string TextureFolder = "Assets/Script/PixelArt/MapTextures";

    [MenuItem("Tools/Pixel Art/Bake Map Materials")]
    public static void BakeMap()
    {
        int changed = Bake();
        SceneView.RepaintAll();
        Debug.Log("Pixel map: saved " + changed + " materials. Map1 and its dungeon prefab now show pixel materials in Edit Mode.");
    }

    [MenuItem("Tools/Pixel Art/Bake Map Materials", true)]
    static bool CanBake() => !EditorApplication.isPlayingOrWillChangePlaymode;

    public static string[] MaterialPaths()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MapPath) == null)
            throw new InvalidOperationException("Map not found: " + MapPath);
        // Scope shared-asset edits to this map's dungeon. Player, weapon and UI materials are separate.
        return AssetDatabase.GetDependencies(MapPath, true)
            .Where(path => path.StartsWith("Assets/MedievalDungeon/", StringComparison.Ordinal) &&
                path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    public static int Bake()
    {
        if (!CanBake()) throw new InvalidOperationException("Bake map materials outside Play Mode.");
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Script/PixelArt/Resources/PixelSurface.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("PixelSurface shader is missing or has compile errors.");
        string[] paths = MaterialPaths();
        if (paths.Length == 0) throw new InvalidOperationException("Map1 has no dungeon material dependencies.");
        var textures = new Dictionary<Texture, Texture2D>();
        int changed = 0;
        foreach (string path in paths)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) throw new InvalidOperationException("Missing map material: " + path);
            if (material.shader == shader) continue; // Re-running never resamples or overwrites hand-edited pixel art.
            if (material.renderQueue >= 2450) continue;
            Color tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
            string property = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            Texture source = material.HasProperty(property) ? material.GetTexture(property) : null;
            Vector2 scale = source != null ? material.GetTextureScale(property) : Vector2.one;
            Vector2 offset = source != null ? material.GetTextureOffset(property) : Vector2.zero;
            Texture2D pixels = null;
            if (source != null && !textures.TryGetValue(source, out pixels))
                textures.Add(source, pixels = SavePixelTexture(source));
            Undo.RecordObject(material, "Bake map pixel material");
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.renderQueue = -1;
            material.SetColor("_Color", tint);
            material.SetTexture("_MainTex", pixels);
            material.SetTextureScale("_MainTex", scale);
            material.SetTextureOffset("_MainTex", offset);
            material.SetFloat("_TexturePixels", 32);
            string name = material.name.ToLowerInvariant();
            material.SetFloat("_WorldPattern", name.Contains("stone") || name.Contains("wall") ||
                name.Contains("floor") || name.Contains("brick") ? 1 : 0);
            // Keep the original GUID: every scene/prefab reference updates without rebuilding the map.
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            changed++;
        }
        return changed;
    }

    static Texture2D SavePixelTexture(Texture source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string guid = AssetDatabase.AssetPathToGUID(sourcePath);
        if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Map texture must be a saved asset: " + source.name);
        Directory.CreateDirectory(TextureFolder);
        string path = TextureFolder + "/" + Path.GetFileNameWithoutExtension(sourcePath) + "_" + guid + ".png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        // Sample in sRGB space so saving/importing the PNG does not double-apply gamma correction.
        RenderTexture target = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D result = null;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            result = new Texture2D(32, 32, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            Color32[] colors = result.GetPixels32();
            for (int i = 0; i < colors.Length; i++)
            {
                Color32 color = colors[i];
                colors[i] = new Color32(Quantize(color.r), Quantize(color.g), Quantize(color.b), 255);
            }
            result.SetPixels32(colors);
            result.Apply();
            File.WriteAllBytes(path, result.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (result != null) UnityEngine.Object.DestroyImmediate(result);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = source.wrapMode;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.anisoLevel = 0;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static byte Quantize(byte value) => (byte)(Mathf.RoundToInt(value / 17f) * 17);
}
