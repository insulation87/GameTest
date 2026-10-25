using System.Collections.Generic;
using UnityEngine;

// One mesh definition supplies held models, loot and inventory silhouettes.
// Models face +Z, with +Y up. All geometry is decorative (no colliders).
public static class ItemAppearance
{
    public const int HealthPack = 7;
    public const int TreasureChest = 8;
    public const int ChestFragment = 9;
    public const int ArmorStart = 20;
    static readonly Mesh[] meshes = new Mesh[24];
    static Material material;
    static readonly Color Steel = new Color(.34f, .41f, .49f);
    static readonly Color Dark = new Color(.075f, .095f, .12f);
    static readonly Color Rubber = new Color(.13f, .16f, .18f);
    static readonly Color Silver = new Color(.68f, .75f, .8f);
    static readonly Color Wood = new Color(.48f, .23f, .105f);
    static readonly Color Brass = new Color(.87f, .64f, .23f);
    static readonly Color Glass = new Color(.12f, .75f, .85f);

    public static GameObject Create(int item, Transform parent)
    {
        if (material == null)
        {
            material = new Material(Resources.Load<Shader>("ItemSurface"));
            material.name = "Equipment shared surface";
            material.hideFlags = HideFlags.HideAndDontSave;
        }
        var model = new GameObject(item == HealthPack ? "Medical Kit" : item == TreasureChest ? "Treasure Chest" :
            item == ChestFragment ? "Chest Fragment" : item >= ArmorStart ? ArmorLoot.SlotName((ArmorSlot)(item - ArmorStart)) + " Model" : ((WeaponType)item) + " Model",
            typeof(MeshFilter), typeof(MeshRenderer));
        model.transform.SetParent(parent, false);
        model.GetComponent<MeshFilter>().sharedMesh = GetMesh(item);
        model.GetComponent<MeshRenderer>().sharedMaterial = material;
        return model;
    }

    public static Mesh GetMesh(int item)
    {
        item = Mathf.Clamp(item, 0, meshes.Length - 1);
        if (item < HealthPack) item = (int)CharacterWeapons.Migrate((WeaponType)item);
        if (meshes[item] == null)
        {
            var shape = new Shape();
            if (item == HealthPack) MedicalKit(shape);
            else if (item == TreasureChest) Chest(shape);
            else if (item == ChestFragment) shape.Box(Vector3.zero, new Vector3(.28f, .1f, .18f), Wood);
            else if (item >= ArmorStart) Armor(shape, (ArmorSlot)(item - ArmorStart));
            else Weapon(shape, (WeaponType)item);
            meshes[item] = shape.ToMesh(item == HealthPack ? "Medical Kit" : item == TreasureChest ? "Treasure Chest" :
                item == ChestFragment ? "Chest Fragment" : item >= ArmorStart ? ((ArmorSlot)(item - ArmorStart)).ToString() : ((WeaponType)item).ToString());
        }
        return meshes[item];
    }

    static void Chest(Shape s)
    {
        // Oak planks, iron feet, brass straps and a turquoise lock. Origin at floor level.
        s.Box(new Vector3(0, .47f, 0), new Vector3(1.6f, .78f, 1.04f), Dark);
        for (int i = 0; i < 4; i++)
            s.Box(new Vector3((i - 1.5f) * .385f, .47f, 0), new Vector3(.37f, .73f, 1.06f), Wood);
        s.Box(new Vector3(0, .89f, 0), new Vector3(1.62f, .12f, 1.08f), Brass);
        s.Box(new Vector3(0, 1.02f, 0), new Vector3(1.56f, .18f, .78f), Wood);
        foreach (float side in new[] { -1f, 1f })
        {
            s.Box(new Vector3(0, .98f, side * .43f), new Vector3(1.56f, .14f, .3f), Wood, new Vector3(side * 32, 0, 0));
            s.Box(new Vector3(side * .56f, .52f, 0), new Vector3(.12f, .86f, 1.1f), Brass);
            s.Box(new Vector3(side * .56f, 1.02f, 0), new Vector3(.13f, .22f, .82f), Brass);
            foreach (float z in new[] { -.4f, .4f })
                s.Box(new Vector3(side * .66f, .08f, z), new Vector3(.22f, .16f, .24f), Steel);
            for (int i = 0; i < 3; i++)
                s.Box(new Vector3(side * .56f, .27f + i * .24f, -.57f), Vector3.one * .055f, Silver);
        }
        s.Box(new Vector3(0, .78f, -.58f), new Vector3(.27f, .3f, .09f), Brass);
        s.Box(new Vector3(0, .78f, -.635f), new Vector3(.1f, .13f, .025f), Glass, new Vector3(0, 0, 45));
    }

    static void Weapon(Shape s, WeaponType type)
    {
        type = CharacterWeapons.Migrate(type);
        Color accent = CharacterRoster.All[(int)type - 10].Color;
        if (type == WeaponType.WarHammer)
        {
            s.Box(new Vector3(0, -.08f, 0), new Vector3(.09f, .85f, .09f), Wood);
            s.Box(new Vector3(0, .42f, 0), new Vector3(.55f, .28f, .25f), Steel);
            s.Box(new Vector3(0, .42f, -.14f), new Vector3(.2f, .15f, .035f), accent);
            s.Box(new Vector3(0, -.35f, 0), new Vector3(.12f, .25f, .12f), Dark);
        }
        else if (type == WeaponType.SwordShield || type == WeaponType.DualDaggers)
        {
            int count = type == WeaponType.DualDaggers ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                float x = count == 2 ? (i == 0 ? -.18f : .18f) : .2f;
                s.Box(new Vector3(x, -.2f, 0), new Vector3(.075f, .28f, .075f), Wood);
                s.Box(new Vector3(x, -.035f, 0), new Vector3(.28f, .045f, .09f), Brass);
                s.Tube(new Vector3(x, .25f, 0), .075f, count == 2 ? .48f : .7f, Silver, .008f, Quaternion.Euler(-90, 0, 0));
            }
            if (type == WeaponType.SwordShield)
            {
                s.Box(new Vector3(-.2f, .05f, 0), new Vector3(.4f, .62f, .09f), Steel);
                s.Box(new Vector3(-.2f, .05f, -.055f), new Vector3(.28f, .45f, .025f), accent);
                s.Box(new Vector3(-.2f, .05f, -.08f), new Vector3(.08f, .34f, .03f), Brass);
            }
        }
        else if (type == WeaponType.Scythe)
        {
            s.Box(Vector3.zero, new Vector3(.065f, 1.1f, .065f), Wood);
            for (int i = 0; i < 5; i++)
                s.Box(new Vector3(-.08f - i * .10f, .5f - i * i * .013f, 0), new Vector3(.16f, .09f - i * .012f, .04f), Silver, new Vector3(0, 0, -i * 9));
            s.Box(new Vector3(0, .42f, 0), new Vector3(.13f, .14f, .13f), accent);
        }
        else if (type == WeaponType.Longbow || type == WeaponType.RepeatingCrossbow)
        {
            bool crossbow = type == WeaponType.RepeatingCrossbow;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    Vector3 pos = crossbow ? new Vector3(side * (.12f + i * .13f), 0, .16f + i * .07f) :
                        new Vector3(.06f + i * i * .025f, side * (.12f + i * .16f), 0);
                    s.Box(pos, crossbow ? new Vector3(.19f, .055f, .065f) : new Vector3(.055f, .22f, .055f), Wood,
                        crossbow ? new Vector3(0, side * -23, 0) : new Vector3(0, 0, side * -18));
                }
            s.Box(crossbow ? new Vector3(0, 0, .08f) : Vector3.zero,
                crossbow ? new Vector3(.10f, .10f, .72f) : new Vector3(.08f, .2f, .08f), accent);
            s.Box(crossbow ? new Vector3(0, 0, .30f) : new Vector3(.20f, 0, 0),
                crossbow ? new Vector3(.86f, .008f, .008f) : new Vector3(.008f, 1f, .008f), Silver);
            s.Tube(new Vector3(0, .05f, .25f), .018f, .8f, Brass, .001f);
        }
        else
        {
            // Four staves have distinct crowns; the focus is a floating mechanical crystal.
            bool focus = type == WeaponType.ArcaneFocus;
            if (!focus) s.Box(new Vector3(0, -.1f, 0), new Vector3(.075f, .9f, .075f), Wood);
            s.Box(new Vector3(0, focus ? .05f : .47f, 0), new Vector3(.22f, .3f, .18f), accent, new Vector3(0, 0, 45));
            if (type == WeaponType.LightStaff)
            {
                s.Box(new Vector3(0, .47f, -.11f), new Vector3(.06f, .3f, .04f), Silver);
                s.Box(new Vector3(0, .47f, -.11f), new Vector3(.3f, .06f, .04f), Silver);
            }
            else if (type == WeaponType.FireStaff)
                for (int i = -1; i <= 1; i++)
                    s.Tube(new Vector3(i * .12f, .62f, 0), .06f, .26f, Brass, .001f, Quaternion.Euler(-90, 0, i * 20));
            else if (type == WeaponType.FrostStaff)
                for (int i = 0; i < 3; i++)
                    s.Box(new Vector3(0, .47f, 0), new Vector3(.45f, .035f, .05f), Silver, new Vector3(0, 0, i * 60));
            else
                for (int side = -1; side <= 1; side += 2)
                    s.Box(new Vector3(side * .2f, .05f, 0), new Vector3(.07f, .42f, .12f), Brass, new Vector3(0, 0, side * 18));
            if (!focus) s.Box(new Vector3(0, -.32f, 0), new Vector3(.10f, .25f, .10f), Dark);
        }
    }

    static void Armor(Shape s, ArmorSlot slot)
    {
        if (slot == ArmorSlot.Helmet)
        {
            s.Box(Vector3.zero, new Vector3(.45f, .32f, .35f), Steel);
            s.Box(new Vector3(0, .2f, 0), new Vector3(.36f, .12f, .3f), Silver);
            s.Box(new Vector3(0, -.02f, -.18f), new Vector3(.32f, .045f, .03f), Dark);
            s.Box(new Vector3(0, .30f, 0), new Vector3(.05f, .15f, .28f), Brass);
        }
        else if (slot == ArmorSlot.Chest)
        {
            s.Box(Vector3.zero, new Vector3(.46f, .56f, .22f), Steel);
            s.Box(new Vector3(0, .07f, -.13f), new Vector3(.25f, .24f, .04f), Brass);
            for (int side = -1; side <= 1; side += 2)
                s.Box(new Vector3(side * .3f, .22f, 0), new Vector3(.20f, .18f, .28f), Silver);
        }
        else
        {
            for (int side = -1; side <= 1; side += 2)
            {
                s.Box(new Vector3(side * .13f, 0, 0), new Vector3(.21f, slot == ArmorSlot.Pants ? .58f : .30f, .2f), slot == ArmorSlot.Pants ? Steel : Wood);
                s.Box(new Vector3(side * .13f, -.12f, -.07f), new Vector3(.22f, .12f, .25f), Silver);
            }
            if (slot == ArmorSlot.Pants) s.Box(new Vector3(0, .27f, 0), new Vector3(.50f, .08f, .24f), Brass);
        }
    }

    static void MedicalKit(Shape s)
    {
        Color red = new Color(.78f,.075f,.105f), white = new Color(.94f,.95f,.89f);
        s.Box(Vector3.zero, new Vector3(.54f,.36f,.24f), red);
        s.Box(new Vector3(0,.135f,0), new Vector3(.55f,.075f,.245f), white);
        s.Box(new Vector3(0,-.165f,0), new Vector3(.55f,.035f,.25f), Rubber);
        // Raised medical emblem on both faces, visible when the dropped kit tumbles.
        for (int side = -1; side <= 1; side+=2)
        {
            s.Box(new Vector3(0,-.025f,side*.126f), new Vector3(.2f,.22f,.015f), white);
            s.Box(new Vector3(0,-.025f,side*.139f), new Vector3(.048f,.15f,.009f), red);
            s.Box(new Vector3(0,-.025f,side*.14f), new Vector3(.145f,.048f,.009f), red);
            for (int x = -1; x <= 1; x+=2)
                s.Box(new Vector3(x*.195f,.087f,side*.128f), new Vector3(.048f,.08f,.02f), Silver);
        }
        for (int side = -1; side <= 1; side+=2)
            s.Box(new Vector3(side*.095f,.218f,0), new Vector3(.032f,.1f,.05f), Rubber);
        s.Box(new Vector3(0,.267f,0), new Vector3(.22f,.035f,.055f), Rubber);
    }

    // Bevelled extruded panels and faceted tubes, combined into one draw call per item.
    sealed class Shape
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> indices = new List<int>();

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            colors.Add(color); colors.Add(color); colors.Add(color);
            indices.Add(start); indices.Add(start+1); indices.Add(start+2);
        }

        public void Box(Vector3 center, Vector3 size, Color color, Vector3 euler = default)
        {
            const float grid = .0125f;
            center = new Vector3(Mathf.Round(center.x / grid), Mathf.Round(center.y / grid), Mathf.Round(center.z / grid)) * grid;
            size = new Vector3(Mathf.Max(1, Mathf.Round(size.x / grid)), Mathf.Max(1, Mathf.Round(size.y / grid)), Mathf.Max(1, Mathf.Round(size.z / grid))) * grid;
            Quaternion rotation = Quaternion.Euler(euler);
            Vector3 h = size*.5f;
            Vector2[] outline = {
                new Vector2(-h.x,-h.y), new Vector2(h.x,-h.y),
                new Vector2(h.x,h.y), new Vector2(-h.x,h.y)
            };
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], n = outline[(i+1)%outline.Length];
                Vector3 p = center+rotation*new Vector3(a.x,a.y,-h.z);
                Vector3 q = center+rotation*new Vector3(n.x,n.y,-h.z);
                Vector3 r = center+rotation*new Vector3(n.x,n.y,h.z);
                Vector3 t = center+rotation*new Vector3(a.x,a.y,h.z);
                Triangle(center+rotation*new Vector3(0,0,-h.z),q,p,color);
                Triangle(center+rotation*new Vector3(0,0,h.z),t,r,color);
                Triangle(p,q,r,color); Triangle(p,r,t,color);
            }
        }

        public void Tube(Vector3 center, float radius, float length, Color color,
            float endRadius = -1, Quaternion rotation = default)
        {
            if (rotation == default(Quaternion)) rotation = Quaternion.identity;
            if (endRadius < 0) endRadius = radius;
            const int sides = 4;
            for (int i = 0; i < sides; i++)
            {
                float a = (i+.5f)*Mathf.PI*2/sides, b = (i+1.5f)*Mathf.PI*2/sides;
                Vector3 p = center+rotation*new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,-length*.5f);
                Vector3 q = center+rotation*new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,-length*.5f);
                Vector3 r = center+rotation*new Vector3(Mathf.Cos(b)*endRadius,Mathf.Sin(b)*endRadius,length*.5f);
                Vector3 t = center+rotation*new Vector3(Mathf.Cos(a)*endRadius,Mathf.Sin(a)*endRadius,length*.5f);
                Triangle(p,q,r,color); Triangle(p,r,t,color);
                Triangle(center+rotation*new Vector3(0,0,-length*.5f),q,p,color);
                Triangle(center+rotation*new Vector3(0,0,length*.5f),t,r,color);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
