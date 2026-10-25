using System.Collections.Generic;
using UnityEngine;

/// <summary>Render-only hit tint; restores each slot before normal appearance updates.</summary>
[DefaultExecutionOrder(-10000), DisallowMultipleComponent]
public sealed class EnemyHitFlash : MonoBehaviour
{
    sealed class Slot
    {
        public Renderer renderer;
        public int index;
        public Material material;
        public bool applied;
        public readonly MaterialPropertyBlock original = new MaterialPropertyBlock();
        public readonly MaterialPropertyBlock flash = new MaterialPropertyBlock();
    }
    readonly List<Slot> slots = new List<Slot>();
    float remaining;
    bool applied, cached;
    static readonly int Tint = Shader.PropertyToID("_Color");
    static readonly int BaseTint = Shader.PropertyToID("_BaseColor");

    public void Pulse()
    {
        if (!cached)
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is LineRenderer || renderer is TrailRenderer || renderer is ParticleSystemRenderer) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && (materials[i].HasProperty(Tint) || materials[i].HasProperty(BaseTint)))
                        slots.Add(new Slot { renderer = renderer, index = i, material = materials[i] });
            }
            cached = slots.Count > 0;
        }
        remaining = .085f;
        enabled = true;
    }

    void Update()
    {
        Restore();
        remaining = Mathf.Max(0f, remaining - Time.deltaTime);
        if (remaining <= 0f) enabled = false;
    }

    void LateUpdate()
    {
        if (remaining <= 0f) return;
        foreach (Slot slot in slots)
        {
            if (slot.renderer == null || !slot.renderer.enabled || slot.material == null) continue;
            slot.renderer.GetPropertyBlock(slot.original, slot.index);
            slot.renderer.GetPropertyBlock(slot.flash, slot.index);
            if (slot.flash.isEmpty) slot.renderer.GetPropertyBlock(slot.flash);
            if (slot.material.HasProperty(Tint)) slot.flash.SetColor(Tint, new Color(1f, .92f, .62f));
            if (slot.material.HasProperty(BaseTint)) slot.flash.SetColor(BaseTint, new Color(1f, .92f, .62f));
            slot.renderer.SetPropertyBlock(slot.flash, slot.index);
            slot.applied = true;
        }
        applied = true;
    }

    public void StopFlash() { Restore(); remaining = 0f; enabled = false; }

    void Restore()
    {
        if (!applied) return;
        foreach (Slot slot in slots)
        {
            if (slot.applied && slot.renderer != null) slot.renderer.SetPropertyBlock(slot.original, slot.index);
            slot.applied = false;
        }
        applied = false;
    }

    void OnDisable() { Restore(); remaining = 0f; }
}
