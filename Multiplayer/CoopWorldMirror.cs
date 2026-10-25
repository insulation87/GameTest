using System;
using System.Collections.Generic;
using UnityEngine;

// Replicate generated visual geometry once, then poses/colors. Gameplay runs only on the host.
public sealed class CoopWorldMirror : MonoBehaviour
{
    static readonly HashSet<string> Kinds = new HashSet<string> {
        "EnemyHealth", "Projectile", "EnemyProjectile", "TreasureChest", "DungeonHazard",
        "WeaponPickup", "ExperiencePickup", "HealthPickup", "ArmorPickup", "FloorKeyPickup", "SkillTrace", "TacticalEncounterCover"
    };
    readonly Dictionary<int, CoopMesh> definitions = new Dictionary<int, CoopMesh>();
    readonly Dictionary<int, int> meshIds = new Dictionary<int, int>();
    readonly Dictionary<string, int> geometryIds = new Dictionary<string, int>();
    readonly Dictionary<int, Mesh> clientMeshes = new Dictionary<int, Mesh>();
    readonly Dictionary<int, Visual> visuals = new Dictionary<int, Visual>();
    readonly List<Material> materials = new List<Material>();
    int nextMesh;
    GameObject root;
    public IEnumerable<CoopMesh> Meshes => definitions.Values;
    sealed class Visual
    {
        public GameObject root;
        public readonly List<MeshRenderer> parts = new List<MeshRenderer>();
        public Vector3 position;
        public Quaternion rotation;
    }
    public CoopWorldState[] Capture()
    {
        var result = new List<CoopWorldState>();
        var visited = new HashSet<int>();
        foreach (MonoBehaviour item in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (!Kinds.Contains(item.GetType().Name) || !visited.Add(item.gameObject.GetInstanceID())) continue;
            var parts = new List<CoopPart>();
            foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(false))
            {
                if (!filter.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled || filter.sharedMesh == null) continue;
                int id = MeshId(filter.sharedMesh);
                if (id == 0) continue;
                var material = renderer.sharedMaterial;
                Color color = material != null && material.HasProperty("_Color") ? material.GetColor("_Color") : Color.gray;
                if (material != null && material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
                // Imported root scale is retained in each part; the proxy root itself has unit scale.
                parts.Add(new CoopPart { mesh = id, position = Quaternion.Inverse(item.transform.rotation) * (filter.transform.position - item.transform.position),
                    rotation = Quaternion.Inverse(item.transform.rotation) * filter.transform.rotation, scale = filter.transform.lossyScale, color = color });
            }
            result.Add(new CoopWorldState { id = item.gameObject.GetInstanceID(), position = item.transform.position,
                rotation = item.transform.rotation, parts = parts.ToArray() });
        }
        return result.ToArray();
    }
    int MeshId(Mesh mesh)
    {
        if (meshIds.TryGetValue(mesh.GetInstanceID(), out int id)) return id;
        if (!mesh.isReadable || mesh.vertexCount > 30000) return 0;
        var data = new CoopMesh { vertices = mesh.vertices, normals = mesh.normals, uv = mesh.uv, triangles = mesh.triangles };
        string key;
        using (var hash = System.Security.Cryptography.SHA256.Create())
            key = Convert.ToBase64String(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(data))));
        if (geometryIds.TryGetValue(key, out id)) { meshIds[mesh.GetInstanceID()] = id; return id; }
        data.id = ++nextMesh; geometryIds[key] = data.id;
        meshIds[mesh.GetInstanceID()] = data.id; definitions[data.id] = data;
        return data.id;
    }
    public void ReceiveMesh(CoopMesh data)
    {
        if (data.id <= 0 || clientMeshes.ContainsKey(data.id) || clientMeshes.Count > 8192 || data.vertices == null ||
            data.vertices.Length > 30000 || data.triangles == null || data.triangles.Length > 180000 || data.triangles.Length % 3 != 0) return;
        foreach (int index in data.triangles) if (index < 0 || index >= data.vertices.Length) return;
        foreach (Vector3 vertex in data.vertices) if (!Finite(vertex.x) || !Finite(vertex.y) || !Finite(vertex.z)) return;
        var mesh = new Mesh { name = "Coop Visual Mesh " + data.id };
        mesh.vertices = data.vertices; mesh.triangles = data.triangles;
        if (data.normals != null && data.normals.Length == data.vertices.Length) mesh.normals = data.normals; else mesh.RecalculateNormals();
        if (data.uv != null && data.uv.Length == data.vertices.Length) mesh.uv = data.uv;
        mesh.RecalculateBounds(); clientMeshes[data.id] = mesh;
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public void HideClientSimulation()
    {
        foreach (MonoBehaviour item in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            string kind = item.GetType().Name;
            if (Kinds.Contains(kind))
            {
                foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                item.enabled = false;
                if (item is EnemyHealth)
                {
                    foreach (MonoBehaviour script in item.GetComponents<MonoBehaviour>()) script.enabled = false;
                    if (item.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent)) agent.enabled = false;
                }
            }
            if (kind == "TreasureChestDirector" || kind == "BossSpawner" || kind == "NPCSpawner") item.enabled = false;
        }
    }
    public void Apply(CoopWorldState[] states)
    {
        if (states == null || states.Length > 4096) return;
        if (root == null) root = new GameObject("Host World Visuals");
        var seen = new HashSet<int>();
        foreach (var state in states)
        {
            if (state == null || state.parts == null || state.parts.Length > 512) continue;
            seen.Add(state.id);
            if (!visuals.TryGetValue(state.id, out var visual))
            {
                visual = new Visual { root = new GameObject("Shared Entity " + state.id) };
                visual.root.transform.SetParent(root.transform); visual.root.transform.SetPositionAndRotation(state.position, state.rotation);
                visuals[state.id] = visual;
            }
            visual.position = state.position; visual.rotation = state.rotation;
            while (visual.parts.Count < state.parts.Length)
            {
                var child = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer)); child.transform.SetParent(visual.root.transform, false);
                var material = new Material(Shader.Find("GameTest/PixelSurface")); materials.Add(material);
                child.GetComponent<MeshRenderer>().sharedMaterial = material; visual.parts.Add(child.GetComponent<MeshRenderer>());
            }
            for (int i = 0; i < visual.parts.Count; i++)
            {
                var renderer = visual.parts[i]; renderer.enabled = i < state.parts.Length;
                if (!renderer.enabled) continue;
                var part = state.parts[i];
                renderer.enabled = clientMeshes.TryGetValue(part.mesh, out var mesh);
                renderer.GetComponent<MeshFilter>().sharedMesh = mesh;
                renderer.transform.SetLocalPositionAndRotation(part.position, part.rotation); renderer.transform.localScale = part.scale;
                renderer.sharedMaterial.color = part.color;
            }
        }
        foreach (int id in new List<int>(visuals.Keys))
            if (!seen.Contains(id))
            {
                foreach (var part in visuals[id].parts)
                { materials.Remove(part.sharedMaterial); Destroy(part.sharedMaterial); }
                Destroy(visuals[id].root); visuals.Remove(id);
            }
    }
    void LateUpdate()
    {
        float blend = 1 - Mathf.Exp(-20 * Time.unscaledDeltaTime);
        foreach (var visual in visuals.Values)
        {
            if (visual.root == null) continue;
            var target = visual.root.transform;
            target.position = Vector3.Lerp(target.position, visual.position, blend);
            target.rotation = Quaternion.Slerp(target.rotation, visual.rotation, blend);
        }
    }
    void OnDestroy()
    {
        if (root != null) Destroy(root);
        foreach (var mesh in clientMeshes.Values) Destroy(mesh);
        foreach (var material in materials) Destroy(material);
    }
}
