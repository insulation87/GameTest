using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent crown matching the dungeon's gold linework.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class VictoryCrownGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Block(mesh, -.40f, -.22f, .80f, .24f);
        Block(mesh, -.40f, .02f, .16f, .28f);
        Block(mesh, -.32f, .02f, .16f, .12f);
        Block(mesh, -.08f, .02f, .16f, .40f);
        Block(mesh, .24f, .02f, .16f, .28f);
        Block(mesh, .16f, .02f, .16f, .12f);
        Block(mesh, -.32f, -.34f, .64f, .06f);
    }

    void Vertex(VertexHelper mesh, Vector2 point, Color tint)
    {
        Rect r = rectTransform.rect;
        mesh.AddVert(new Vector3(r.center.x + point.x * r.width, r.center.y + point.y * r.height), tint, Vector2.zero);
    }

    void Block(VertexHelper mesh, float x, float y, float width, float height)
    {
        Quad(mesh, new Vector2(x, y), new Vector2(x, y + height),
            new Vector2(x + width, y + height), new Vector2(x + width, y), color);
    }

    void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        int i = mesh.currentVertCount;
        Vertex(mesh, a, tint); Vertex(mesh, b, tint); Vertex(mesh, c, tint); Vertex(mesh, d, tint);
        mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
    }
}
