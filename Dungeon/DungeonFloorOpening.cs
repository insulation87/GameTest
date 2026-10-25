using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Runtime-only floor surgery: originals remain available for restoration and scene assets stay untouched.
public sealed class DungeonFloorOpening : MonoBehaviour
{
    struct Vertex
    {
        public Vector3 position, cut;
        public Vector2 uv;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
        { position = Vector3.Lerp(a.position,b.position,t), cut = Vector3.Lerp(a.cut,b.cut,t), uv = Vector2.Lerp(a.uv,b.uv,t) };
    }
    readonly List<BoxCollider> originals = new List<BoxCollider>();
    readonly Dictionary<MeshRenderer, MeshFilter> replacements = new Dictionary<MeshRenderer, MeshFilter>();
    readonly List<GameObject> fragments = new List<GameObject>();
    const int MaximumCutouts = 32;
    sealed class Cutout
    {
        public MaterialPropertyBlock original = new MaterialPropertyBlock(), working = new MaterialPropertyBlock();
        public Vector4[] centers = new Vector4[MaximumCutouts], axesX = new Vector4[MaximumCutouts], axesZ = new Vector4[MaximumCutouts];
        public int count;
    }
    readonly Dictionary<MeshRenderer,Cutout> cutouts = new Dictionary<MeshRenderer,Cutout>();

    public bool Cut(BoxCollider floor, Vector3 worldCenter, float width, Transform map)
    {
        if (floor == null || !floor.enabled || floor.isTrigger) return false;
        Vector3 center = floor.transform.InverseTransformPoint(worldCenter);
        Vector3 scale = floor.transform.lossyScale;
        float hx = width / (2f * Mathf.Abs(scale.x)), hz = width / (2f * Mathf.Abs(scale.z));
        Vector3 min = floor.center - floor.size * .5f, max = floor.center + floor.size * .5f;
        float x0 = center.x - hx, x1 = center.x + hx, z0 = center.z - hz, z1 = center.z + hz;
        if (x0 <= min.x + .1f || x1 >= max.x - .1f || z0 <= min.z + .1f || z1 >= max.z - .1f) return false;

        var candidates = new List<MeshRenderer>();
        Bounds vicinity = new Bounds(worldCenter - Vector3.up * 1.9f, new Vector3(width + .2f, 4.4f, width + .2f));
        foreach (MeshRenderer renderer in map.GetComponentsInChildren<MeshRenderer>())
        {
            if (!renderer.enabled || !(IsFloor(renderer.transform) || IsFoundation(renderer.transform))) continue;
            // Render batches have world-sized bounds; test the opening against every candidate's triangles below.
            if (!renderer.bounds.Intersects(vicinity)) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;
            if (!filter.sharedMesh.isReadable &&
                (System.Array.Exists(renderer.sharedMaterials,m=>m == null || m.shader.name != "GameTest/PixelSurface") ||
                 cutouts.TryGetValue(renderer,out var existing) && existing.count >= MaximumCutouts)) return false;
            candidates.Add(renderer);
        }
        if (candidates.Count == 0) return false;

        foreach (MeshRenderer renderer in candidates)
        {
            MeshFilter source = renderer.GetComponent<MeshFilter>();
            if (!source.sharedMesh.isReadable)
            {
                // Static batching replaces readable source meshes. Clip fragments in the same
                // palette shader instead of modifying/importing the player's map assets.
                if (!cutouts.TryGetValue(renderer,out Cutout cutout))
                { cutout = new Cutout(); renderer.GetPropertyBlock(cutout.original); cutouts.Add(renderer,cutout); }
                int index = cutout.count++;
                cutout.centers[index] = new Vector4(worldCenter.x,worldCenter.y-1.9f,worldCenter.z,2.2f);
                Vector3 axisX = floor.transform.right, axisZ = floor.transform.forward;
                cutout.axesX[index] = new Vector4(axisX.x,axisX.y,axisX.z,width*.5f);
                cutout.axesZ[index] = new Vector4(axisZ.x,axisZ.y,axisZ.z,width*.5f);
                renderer.GetPropertyBlock(cutout.working);
                cutout.working.SetInt("_FloorCutoutCount",cutout.count);
                cutout.working.SetVectorArray("_FloorCutoutCenters",cutout.centers);
                cutout.working.SetVectorArray("_FloorCutoutAxesX",cutout.axesX);
                cutout.working.SetVectorArray("_FloorCutoutAxesZ",cutout.axesZ);
                renderer.SetPropertyBlock(cutout.working);
                continue;
            }
            Mesh mesh = CutMesh(source.sharedMesh, source.transform, floor.transform, center, hx, hz);
            var copy = new GameObject("Floor With Opening", typeof(MeshFilter), typeof(MeshRenderer));
            copy.transform.SetParent(source.transform, false);
            copy.layer = source.gameObject.layer;
            copy.GetComponent<MeshFilter>().sharedMesh = mesh;
            copy.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            // A new renderer also works when the source was combined into a static batch.
            renderer.enabled = false;
            replacements.Add(renderer, copy.GetComponent<MeshFilter>());
        }
        CutCollision(floor,worldCenter,width);
        // The first floor also rests on a generated foundation. Remove its support
        // in the opening so a visually open pit can actually be fallen into.
        foreach (BoxCollider foundation in map.GetComponentsInChildren<BoxCollider>())
            if (foundation.enabled && !foundation.isTrigger && IsFoundation(foundation.transform) && foundation.bounds.Intersects(vicinity))
                CutCollision(foundation,worldCenter,width);
        return true;
    }

    void CutCollision(BoxCollider source,Vector3 worldCenter,float width)
    {
        Vector3 center=source.transform.InverseTransformPoint(worldCenter), scale=source.transform.lossyScale;
        Vector3 min=source.center-source.size*.5f,max=source.center+source.size*.5f;
        float hx=width/(2*Mathf.Abs(scale.x)),hz=width/(2*Mathf.Abs(scale.z));
        float x0=Mathf.Max(min.x,center.x-hx),x1=Mathf.Min(max.x,center.x+hx);
        float z0=Mathf.Max(min.z,center.z-hz),z1=Mathf.Min(max.z,center.z+hz);
        if(x0>=x1 || z0>=z1)return;
        source.enabled=false;originals.Add(source);
        Fragment(source,min.x,x0,min.z,max.z);Fragment(source,x1,max.x,min.z,max.z);
        Fragment(source,x0,x1,min.z,z0);Fragment(source,x0,x1,z1,max.z);
    }

    static bool IsFoundation(Transform item)
    {
        for(Transform t=item;t!=null;t=t.parent) if(t.name=="Foundation")return true;
        return false;
    }

    public static bool IsFloor(Transform item)
    {
        for (Transform t = item; t != null; t = t.parent)
            if (t.name == "Floor") return true;
        return false;
    }

    void Fragment(BoxCollider source, float x0, float x1, float z0, float z1)
    {
        if(x1-x0<.001f || z1-z0<.001f)return;
        var node = new GameObject("Floor Collision Around Pit");
        node.transform.SetParent(source.transform, false);
        node.layer = source.gameObject.layer;
        var box = node.AddComponent<BoxCollider>();
        box.center = new Vector3((x0+x1)*.5f, source.center.y, (z0+z1)*.5f);
        box.size = new Vector3(x1-x0, source.size.y, z1-z0);
        box.sharedMaterial = source.sharedMaterial;
        fragments.Add(node);
    }

    static Mesh CutMesh(Mesh source, Transform renderTransform, Transform cutTransform, Vector3 center, float hx, float hz)
    {
        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;
        var output = new List<Vector3>(); var tex = new List<Vector2>();
        var submeshes = new List<int[]>();
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            int[] triangles = source.GetTriangles(sub);
            var indices = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var remaining = new List<Vertex>();
                for (int j = 0; j < 3; j++)
                {
                    int index = triangles[i+j];
                    remaining.Add(new Vertex { position = vertices[index], uv = uv.Length == vertices.Length ? uv[index] : Vector2.zero,
                        cut = cutTransform.InverseTransformPoint(renderTransform.TransformPoint(vertices[index])) - center });
                }
                bool atHeight = true;
                foreach (Vertex v in remaining)
                {
                    float vertical = v.cut.y * Mathf.Abs(cutTransform.lossyScale.y);
                    if (vertical < -4.1f || vertical > .3f) atHeight = false;
                }
                if (!atHeight) { Append(remaining,output,tex,indices); continue; }
                for (int plane = 0; plane < 4 && remaining.Count > 0; plane++)
                {
                    Append(Clip(remaining,plane,hx,hz,false),output,tex,indices);
                    remaining = Clip(remaining,plane,hx,hz,true);
                }
                // The final remaining polygon is inside the opening and is discarded.
            }
            submeshes.Add(indices.ToArray());
        }
        var mesh = new Mesh { name = source.name + " (Runtime Opening)", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(output); mesh.SetUVs(0,tex); mesh.subMeshCount = submeshes.Count;
        for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i],i);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }
    static float Distance(Vertex v,int plane,float hx,float hz) => plane == 0 ? v.cut.x + hx :
        plane == 1 ? hx - v.cut.x : plane == 2 ? v.cut.z + hz : hz - v.cut.z;
    static List<Vertex> Clip(List<Vertex> polygon,int plane,float hx,float hz,bool inside)
    {
        var result = new List<Vertex>();
        Vertex previous = polygon[polygon.Count-1];
        float pd = Distance(previous,plane,hx,hz) * (inside ? 1f : -1f);
        foreach (Vertex current in polygon)
        {
            float cd = Distance(current,plane,hx,hz) * (inside ? 1f : -1f);
            if ((pd >= 0) != (cd >= 0)) result.Add(Vertex.Lerp(previous,current,pd/(pd-cd)));
            if (cd >= 0) result.Add(current);
            previous = current; pd = cd;
        }
        return result;
    }
    static void Append(List<Vertex> polygon,List<Vector3> vertices,List<Vector2> uv,List<int> triangles)
    {
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            foreach (Vertex v in new[] { polygon[0],polygon[i],polygon[i+1] })
            { triangles.Add(vertices.Count); vertices.Add(v.position); uv.Add(v.uv); }
        }
    }
    void OnDestroy()
    {
        foreach (var pair in cutouts) if (pair.Key != null) pair.Key.SetPropertyBlock(pair.Value.original);
        foreach (BoxCollider original in originals) if (original != null) original.enabled = true;
        foreach (var pair in replacements)
        {
            if (pair.Key != null) pair.Key.enabled = true;
            if (pair.Value == null) continue;
            Destroy(pair.Value.sharedMesh); Destroy(pair.Value.gameObject);
        }
        foreach (GameObject fragment in fragments) if (fragment != null) Destroy(fragment);
    }
}
