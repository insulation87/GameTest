using UnityEngine;
using UnityEngine.UI;

/// <summary>Lightweight crisp frame geometry; no texture, animation or raycast overhead.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class GameUIFrame : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = rectTransform.rect;
        const float pixel = 3;
        Quad(mesh, new Rect(r.xMin + pixel, r.yMin, r.width - pixel * 2, pixel), color);
        Quad(mesh, new Rect(r.xMin + pixel, r.yMax - pixel, r.width - pixel * 2, pixel), color);
        Quad(mesh, new Rect(r.xMin, r.yMin + pixel, pixel, r.height - pixel * 2), color);
        Quad(mesh, new Rect(r.xMax - pixel, r.yMin + pixel, pixel, r.height - pixel * 2), color);
        Color corner = color; corner.a = Mathf.Min(1, corner.a * 1.8f);
        float length = Mathf.Min(18, r.width * .1f);
        Quad(mesh, new Rect(r.xMin + pixel, r.yMax - pixel * 2, length, pixel), corner);
        Quad(mesh, new Rect(r.xMax - length - pixel, r.yMin + pixel, length, pixel), corner);
    }

    static void Quad(VertexHelper mesh, Rect r, Color c)
    {
        int i = mesh.currentVertCount;
        mesh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMin, r.yMax), c, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax, r.yMax), c, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector2.zero);
        mesh.AddTriangle(i, i + 1, i + 2);
        mesh.AddTriangle(i, i + 2, i + 3);
    }
}
