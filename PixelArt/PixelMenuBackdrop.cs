using UnityEngine;
using UnityEngine.UI;

/// <summary>Original code-authored pixel key art, in a fixed 480 x 270 logical grid.</summary>
public sealed class PixelMenuBackdrop : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Paint(mesh, 0, 0, 480, 270, new Color32(12, 19, 33, 255));
        for (int band = 0; band < 9; band++)
            Paint(mesh, 235, band * 30, 245, 30, new Color32((byte)(18 + band), (byte)(29 + band * 2), (byte)(43 + band * 3), 255));
        // Stepped stone vaults, large masonry and low-resolution floor tiles.
        for (int arch = 0; arch < 3; arch++)
        {
            int left = 230 + arch * 84;
            Color32 stone = new Color32((byte)(42 - arch * 5), (byte)(57 - arch * 5), (byte)(72 - arch * 5), 255);
            Paint(mesh, left, 66, 12, 160, stone);
            Paint(mesh, left + 60, 66, 12, 160, stone);
            for (int step = 0; step < 4; step++)
            {
                Paint(mesh, left + step * 7, 219 + step * 7, 12, 12, stone);
                Paint(mesh, left + 60 - step * 7, 219 + step * 7, 12, 12, stone);
            }
            for (int row = 0; row < 10; row++)
            {
                Paint(mesh, left, 66 + row * 15, 12, 2, new Color32(18, 29, 43, 255));
                Paint(mesh, left + 60, 66 + row * 15, 12, 2, new Color32(18, 29, 43, 255));
            }
        }
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 10; col++)
                Paint(mesh, 224 + col * 29 + (row % 2) * 14, row * 13, 27, 11,
                    (row + col) % 3 == 0 ? new Color32(36, 52, 65, 255) : new Color32(28, 43, 58, 255));
        // Distant crowned boss with red visor and shoulder spikes.
        Paint(mesh, 294, 94, 42, 52, new Color32(18, 22, 34, 255));
        Paint(mesh, 282, 118, 12, 25, new Color32(18, 22, 34, 255));
        Paint(mesh, 336, 118, 12, 25, new Color32(18, 22, 34, 255));
        Paint(mesh, 301, 146, 28, 27, new Color32(35, 39, 48, 255));
        Paint(mesh, 304, 158, 22, 3, new Color32(248, 77, 52, 255));
        Paint(mesh, 300, 172, 30, 4, new Color32(186, 130, 45, 255));
        for (int spike = 0; spike < 4; spike++) Paint(mesh, 300 + spike * 8, 176, 5, 7, new Color32(211, 160, 70, 255));
        Paint(mesh, 307, 97, 17, 41, new Color32(109, 34, 49, 255));
        // Blonde adventurer mirrors the voxel model palette and outfit.
        Color32 gold = new Color32(240, 179, 62, 255), hairShade = new Color32(182, 117, 37, 255);
        Color32 skin = new Color32(247, 189, 136, 255), shirt = new Color32(229, 231, 215, 255);
        Color32 vest = new Color32(31, 49, 74, 255), ink = new Color32(12, 22, 36, 255);
        Paint(mesh, 368, 66, 54, 108, hairShade);
        Paint(mesh, 365, 160, 58, 57, gold);
        Paint(mesh, 371, 211, 46, 7, gold);
        Paint(mesh, 371, 166, 44, 35, skin);
        for (int i = 0; i < 5; i++) Paint(mesh, 370 + i * 9, 194 - (i % 2) * 4, 9, 15, gold);
        Paint(mesh, 364, 135, 18, 27, shirt);
        Paint(mesh, 408, 135, 18, 27, shirt);
        Paint(mesh, 379, 111, 33, 49, vest);
        Paint(mesh, 384, 152, 22, 9, shirt);
        Paint(mesh, 388, 141, 14, 9, new Color32(65, 201, 209, 255));
        Paint(mesh, 369, 120, 11, 20, skin);
        Paint(mesh, 416, 119, 11, 21, skin);
        Paint(mesh, 379, 93, 34, 20, vest);
        Paint(mesh, 380, 72, 13, 22, skin);
        Paint(mesh, 401, 72, 13, 22, skin);
        Paint(mesh, 380, 39, 13, 34, ink);
        Paint(mesh, 401, 39, 13, 34, ink);
        Paint(mesh, 376, 35, 17, 8, ink);
        Paint(mesh, 400, 35, 19, 8, ink);
        Paint(mesh, 380, 181, 8, 6, shirt);
        Paint(mesh, 401, 181, 8, 6, shirt);
        Paint(mesh, 383, 180, 4, 6, ink);
        Paint(mesh, 402, 180, 4, 6, ink);
        Paint(mesh, 391, 172, 6, 2, new Color32(160, 73, 67, 255));
        Paint(mesh, 406, 203, 10, 2, shirt);
        // Rifle silhouette: squared receiver, long barrel, grip and magazine.
        Paint(mesh, 357, 117, 77, 10, ink);
        Paint(mesh, 347, 119, 20, 5, new Color32(99, 124, 140, 255));
        Paint(mesh, 384, 105, 9, 16, new Color32(68, 87, 111, 255));
        Paint(mesh, 409, 108, 6, 13, ink);
        Paint(mesh, 358, 125, 62, 3, new Color32(109, 139, 153, 255));
        foreach (int torch in new[] { 263, 450 })
        {
            Paint(mesh, torch, 138, 4, 22, new Color32(112, 77, 48, 255));
            Paint(mesh, torch - 3, 158, 10, 13, new Color32(221, 100, 37, 255));
            Paint(mesh, torch, 160, 4, 16, new Color32(255, 218, 99, 255));
        }
        for (int i = 0; i < 24; i++)
            Paint(mesh, 244 + i * 41 % 232, 72 + i * 31 % 154, 1, 2, new Color32(204, 155, 68, 255));
    }

    void Paint(VertexHelper mesh, float x, float y, float width, float height, Color tint)
    {
        Rect r = rectTransform.rect;
        // Cover the viewport without distorting the source grid.
        float scale = Mathf.Max(r.width / 480f, r.height / 270f);
        Vector2 origin = r.center - new Vector2(480, 270) * scale * .5f;
        float left = origin.x + x * scale, bottom = origin.y + y * scale;
        int index = mesh.currentVertCount;
        mesh.AddVert(new Vector3(left, bottom), tint, Vector2.zero);
        mesh.AddVert(new Vector3(left, bottom + height * scale), tint, Vector2.zero);
        mesh.AddVert(new Vector3(left + width * scale, bottom + height * scale), tint, Vector2.zero);
        mesh.AddVert(new Vector3(left + width * scale, bottom), tint, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
    }
}
