using System.Collections.Generic;
using MedievalDungeonKit;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runtime movement collision for generated columns. Decorative plinths no longer catch feet.
[DefaultExecutionOrder(-300), DisallowMultipleComponent]
public sealed class DungeonPillarCollisions : MonoBehaviour
{
    readonly List<BoxCollider> originals = new List<BoxCollider>();
    readonly List<CapsuleCollider> pillars = new List<CapsuleCollider>();
    public IReadOnlyList<CapsuleCollider> Pillars => pillars;
    public bool Built { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }
    static void SceneLoaded(Scene scene,LoadSceneMode mode)
    {
        DungeonViewSettings map = FindFirstObjectByType<DungeonViewSettings>();
        if (map != null && map.GetComponent<DungeonPillarCollisions>() == null)
            map.gameObject.AddComponent<DungeonPillarCollisions>();
    }
    void Start() => Build();

    public void Build()
    {
        if (Built) return;
        if (OverworldDirector.EnsureWorld() != null) { Built = true; return; }
        Built = true;
        Physics.SyncTransforms();
        BoxCollider[] boxes = GetComponentsInChildren<BoxCollider>();
        foreach (BoxCollider shaft in boxes)
        {
            if (!IsColumnPart(shaft) || shaft.size.y < 3 || shaft.size.x > 1.5f || shaft.size.z > 1.5f ||
                shaft.size.x < .3f || shaft.size.z < .3f || Mathf.Abs(shaft.size.x-shaft.size.z) > .05f ||
                Vector3.Dot(shaft.transform.up,Vector3.up) < .99f) continue;
            BoxCollider plinth = null;
            Vector3 shaftCenter = shaft.bounds.center;
            foreach (BoxCollider candidate in boxes)
            {
                if (!IsColumnPart(candidate) || candidate.transform.parent != shaft.transform.parent ||
                    candidate.size.y > .6f || candidate.size.x < shaft.size.x || candidate.size.z < shaft.size.z) continue;
                Vector3 delta = candidate.bounds.center-shaftCenter;
                if (new Vector2(delta.x,delta.z).sqrMagnitude < .001f &&
                    Mathf.Abs(candidate.bounds.max.y-shaft.bounds.min.y) < .04f)
                { plinth = candidate; break; }
            }
            // Only known paired base/shaft geometry is changed; other room details remain solid.
            if (plinth == null) continue;
            Vector3 lower = shaft.transform.InverseTransformPoint(new Vector3(shaftCenter.x,plinth.bounds.min.y,shaftCenter.z));
            float upper = shaft.center.y + shaft.size.y*.5f;
            var node = new GameObject("Smooth Pillar Collision");
            node.layer = shaft.gameObject.layer; node.transform.SetParent(shaft.transform,false);
            var rounded = node.AddComponent<CapsuleCollider>(); rounded.direction=1;
            rounded.radius=Mathf.Max(shaft.size.x,shaft.size.z)*.5f;
            rounded.height=upper-lower.y;
            rounded.center=new Vector3(shaft.center.x,(upper+lower.y)*.5f,shaft.center.z);
            rounded.sharedMaterial=shaft.sharedMaterial;
            rounded.includeLayers=shaft.includeLayers; rounded.excludeLayers=shaft.excludeLayers;
            rounded.layerOverridePriority=shaft.layerOverridePriority;
            shaft.enabled=false;plinth.enabled=false;
            originals.Add(shaft);originals.Add(plinth);pillars.Add(rounded);
        }
        Physics.SyncTransforms();
    }
    static bool IsColumnPart(BoxCollider box)
    {
        return box != null && box.enabled && !box.isTrigger && box.attachedRigidbody == null &&
            box.name.StartsWith("Collider_",System.StringComparison.Ordinal) &&
            box.transform.parent != null && box.transform.parent.name == "Details";
    }
    void OnDestroy()
    {
        foreach (BoxCollider original in originals) if (original != null) original.enabled=true;
        foreach (CapsuleCollider pillar in pillars) if (pillar != null) Destroy(pillar.gameObject);
    }
}
