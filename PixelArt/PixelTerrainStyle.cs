using System.Collections.Generic;
using UnityEngine;

/// <summary>Runtime copies of Terrain layers; original height maps, textures and project assets remain editable.</summary>
[RequireComponent(typeof(Terrain)), DisallowMultipleComponent]
public sealed class PixelTerrainStyle : MonoBehaviour
{
    Terrain terrain;
    TerrainData original, styled;
    TerrainCollider terrainCollider;
    readonly List<Object> owned = new List<Object>();

    void Start()
    {
        terrain = GetComponent<Terrain>(); original = terrain.terrainData;
        if (original == null) return;
        styled = Instantiate(original);
        styled.name = original.name + " Pixel Runtime";
        TerrainLayer[] layers = original.terrainLayers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == null) continue;
            layers[i] = Instantiate(layers[i]); owned.Add(layers[i]);
            layers[i].diffuseTexture = PixelTexture(layers[i].diffuseTexture);
            layers[i].normalMapTexture = null;
            layers[i].maskMapTexture = null;
            layers[i].metallic = 0; layers[i].smoothness = 0;
        }
        styled.terrainLayers = layers;
        terrain.terrainData = styled;
        terrainCollider = GetComponent<TerrainCollider>();
        if (terrainCollider != null && terrainCollider.terrainData == original) terrainCollider.terrainData = styled;
    }

    Texture2D PixelTexture(Texture2D source)
    {
        if (source == null) return null;
        RenderTexture target = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            var texture = new Texture2D(32, 32, TextureFormat.RGB24, false)
            { name = source.name + " Pixel Runtime", filterMode = FilterMode.Point, wrapMode = source.wrapMode };
            texture.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            Color[] colors = texture.GetPixels();
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new Color(Mathf.Round(colors[i].r * 15) / 15, Mathf.Round(colors[i].g * 15) / 15, Mathf.Round(colors[i].b * 15) / 15);
            texture.SetPixels(colors); texture.Apply(false, true); owned.Add(texture);
            return texture;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
    }

    void OnDestroy()
    {
        if (terrain != null && terrain.terrainData == styled) terrain.terrainData = original;
        if (terrainCollider != null && terrainCollider.terrainData == styled) terrainCollider.terrainData = original;
        if (styled != null) Destroy(styled);
        foreach (Object asset in owned) if (asset != null) Destroy(asset);
    }
}
