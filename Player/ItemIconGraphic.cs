using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Orthographic illustration of the actual mesh: no camera, texture or per-frame rendering.
public class ItemIconGraphic : MaskableGraphic
{
    int item;
    public void SetItem(int value)
    {
        item = value;
        SetVerticesDirty();
    }

    public static ItemIconGraphic Create(Transform parent, int item, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Item Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(ItemIconGraphic));
        go.transform.SetParent(parent, false);
        var icon = go.GetComponent<ItemIconGraphic>();
        icon.rectTransform.anchoredPosition = position;
        icon.rectTransform.sizeDelta = size;
        icon.raycastTarget = false;
        icon.SetItem(item);
        return icon;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Mesh mesh = ItemAppearance.GetMesh(item);
        Vector3[] vertices = mesh.vertices;
        Color[] colors = mesh.colors;
        int[] triangles = mesh.triangles;
        Quaternion view = item == ItemAppearance.HealthPack
            ? Quaternion.Euler(0,0,-4) : Quaternion.Euler(10,-72,-8);
        var bounds = new Bounds(view*vertices[0], Vector3.zero);
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = view*vertices[i];
            bounds.Encapsulate(vertices[i]);
        }
        Rect rect = rectTransform.rect;
        float scale = Mathf.Min(rect.width/Mathf.Max(.001f,bounds.size.x), rect.height/Mathf.Max(.001f,bounds.size.y))*.92f;
        var faces = new List<int>();
        for (int i = 0; i < triangles.Length; i+=3)
        {
            Vector3 normal = Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]], vertices[triangles[i+2]]-vertices[triangles[i]]);
            if (normal.z < 0) faces.Add(i);
        }
        // Painter's order: the viewer is on -Z, so nearer faces are drawn last.
        faces.Sort((a,b) => (vertices[triangles[b]].z+vertices[triangles[b+1]].z+vertices[triangles[b+2]].z)
            .CompareTo(vertices[triangles[a]].z+vertices[triangles[a+1]].z+vertices[triangles[a+2]].z));
        foreach (int face in faces)
        {
            Vector3 a = vertices[triangles[face]], b = vertices[triangles[face+1]], c = vertices[triangles[face+2]];
            float shade = .72f+.28f*Mathf.Max(0,Vector3.Dot(Vector3.Cross(b-a,c-a).normalized,new Vector3(-.3f,.7f,-.6f).normalized));
            int start = vh.currentVertCount;
            for (int j = 0; j < 3; j++)
            {
                int index = triangles[face+j];
                Vector3 p = (vertices[index]-bounds.center)*scale;
                Color tint = colors[index]*shade;
                tint.a = 1;
                const float pixel = 3f;
                vh.AddVert(new Vector3(Mathf.Round((p.x+rect.center.x)/pixel)*pixel,
                    Mathf.Round((p.y+rect.center.y)/pixel)*pixel,0),tint,Vector2.zero);
            }
            vh.AddTriangle(start,start+1,start+2);
        }
    }
}
