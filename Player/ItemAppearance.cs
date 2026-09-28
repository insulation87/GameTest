using System.Collections.Generic;
using UnityEngine;

// One mesh definition supplies held models, loot and inventory silhouettes.
// Models face +Z, with +Y up. All geometry is decorative (no colliders).
public static class ItemAppearance
{
    public const int HealthPack = 7;
    static readonly Mesh[] meshes = new Mesh[8];
    static Material material;
    static readonly Color Steel = new Color(.34f, .41f, .49f);
    static readonly Color Dark = new Color(.075f, .095f, .12f);
    static readonly Color Rubber = new Color(.13f, .16f, .18f);
    static readonly Color Silver = new Color(.68f, .75f, .8f);
    static readonly Color Wood = new Color(.48f, .23f, .105f);
    static readonly Color Olive = new Color(.34f, .4f, .18f);
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
        var model = new GameObject(item == HealthPack ? "Medical Kit" : ((WeaponType)item) + " Model",
            typeof(MeshFilter), typeof(MeshRenderer));
        model.transform.SetParent(parent, false);
        model.GetComponent<MeshFilter>().sharedMesh = GetMesh(item);
        model.GetComponent<MeshRenderer>().sharedMaterial = material;
        return model;
    }

    public static Mesh GetMesh(int item)
    {
        item = Mathf.Clamp(item, 0, HealthPack);
        if (meshes[item] == null)
        {
            var shape = new Shape();
            if (item == HealthPack) MedicalKit(shape);
            else Weapon(shape, (WeaponType)item);
            meshes[item] = shape.ToMesh(item == HealthPack ? "Medical Kit" : ((WeaponType)item).ToString());
        }
        return meshes[item];
    }

    static void Weapon(Shape s, WeaponType type)
    {
        if (type == WeaponType.RPG)
        {
            s.Tube(new Vector3(0,.055f,-.05f), .092f, .96f, Olive);
            s.Tube(new Vector3(0,.055f,-.52f), .115f, .08f, Steel);
            s.Tube(new Vector3(0,.055f,.43f), .105f, .07f, Steel);
            s.Tube(new Vector3(0,.055f,.473f), .078f, .008f, Dark);
            s.Tube(new Vector3(0,.055f,.51f), .048f, .1f, Brass);
            s.Tube(new Vector3(0,.055f,.63f), .09f, .19f, Olive, .022f);
            s.Box(new Vector3(0,-.105f,-.13f), new Vector3(.12f,.12f,.34f), Wood);
            Grip(s, .13f, Rubber);
            s.Box(new Vector3(0,.19f,.08f), new Vector3(.045f,.15f,.035f), Steel);
            s.Box(new Vector3(0,.27f,.08f), new Vector3(.1f,.025f,.035f), Silver);
            s.Box(new Vector3(0,-.09f,-.38f), new Vector3(.16f,.07f,.16f), Rubber);
            return;
        }

        bool pistol = type == WeaponType.Pistol;
        bool smg = type == WeaponType.SubmachineGun;
        bool shotgun = type == WeaponType.Shotgun;
        bool sniper = type == WeaponType.SniperRifle;
        bool lmg = type == WeaponType.LightMachineGun;
        Color accent = shotgun ? Wood : sniper || lmg ? Olive : smg ? new Color(.22f,.42f,.46f) : Steel;
        float receiverLength = pistol ? .25f : smg ? .31f : .39f;
        float barrelEnd = pistol ? .23f : smg ? .4f : sniper ? .91f : shotgun ? .77f : lmg ? .79f : .64f;

        s.Box(new Vector3(0,.045f,0), new Vector3(pistol ? .075f : .105f,.115f,receiverLength), accent);
        s.Box(new Vector3(0,.11f,0), new Vector3(.073f,.027f,receiverLength * .94f), Silver);
        s.Box(new Vector3(.055f,.048f,.055f), new Vector3(.009f,.045f,.095f), Dark);
        s.Box(new Vector3(.063f,.05f,-.04f), new Vector3(.035f,.022f,.038f), Silver);
        Grip(s, -.08f, Rubber);
        // Open trigger guard, with an inset trigger.
        s.Box(new Vector3(0,-.128f,.055f), new Vector3(.045f,.018f,.13f), Steel);
        s.Box(new Vector3(0,-.081f,.12f), new Vector3(.045f,.11f,.018f), Steel);
        s.Box(new Vector3(0,-.065f,.035f), new Vector3(.016f,.06f,.015f), Dark, new Vector3(-18,0,0));
        float barrelStart = receiverLength * .42f;
        s.Tube(new Vector3(0,.06f,(barrelStart + barrelEnd) * .5f), shotgun ? .035f : .023f,
            barrelEnd - barrelStart, Steel);
        s.Tube(new Vector3(0,.06f,barrelEnd), pistol ? .03f : .039f, .055f, Dark);
        s.Tube(new Vector3(0,.06f,barrelEnd + .028f), pistol ? .02f : .026f, .003f, new Color(.025f,.03f,.035f));

        if (pistol)
        {
            // Short slide, muzzle, grip panels and iron sights.
            s.Box(new Vector3(0,.06f,.135f), new Vector3(.077f,.1f,.11f), Silver);
            for (int i = 0; i < 5; i++)
                s.Box(new Vector3(0,.066f,-.1f+i*.017f), new Vector3(.08f,.067f,.006f), Steel);
            s.Box(new Vector3(0,-.244f,-.052f), new Vector3(.085f,.024f,.105f), Silver);
            Sights(s, -.09f, .16f);
            return;
        }

        // Shoulder stock and butt pad, visibly separate from the receiver.
        float stockLength = smg ? .17f : .27f;
        s.Box(new Vector3(0,-.01f,-receiverLength*.5f-stockLength*.5f),
            new Vector3(.085f,smg ? .045f : .12f,stockLength), shotgun ? Wood : accent, new Vector3(-7,0,0));
        s.Box(new Vector3(0,-.035f,-receiverLength*.5f-stockLength), new Vector3(.105f,.18f,.038f), Rubber);
        if (shotgun)
        {
            // Twin tubes and ribbed pump make the shotgun identifiable at a glance.
            s.Tube(new Vector3(0,-.015f,.43f), .024f, .55f, Dark);
            s.Box(new Vector3(0,-.014f,.36f), new Vector3(.12f,.1f,.24f), Wood);
            for (int i = 0; i < 7; i++)
                s.Box(new Vector3(0,-.014f,.265f+i*.032f), new Vector3(.124f,.105f,.008f), Rubber);
            for (int i = 0; i < 4; i++)
            {
                s.Tube(new Vector3(-.066f,.01f,-.13f+i*.055f), .018f, .08f, new Color(.65f,.13f,.09f), -1, Quaternion.Euler(90,0,0));
                s.Tube(new Vector3(-.066f,.056f,-.13f+i*.055f), .019f, .014f, Brass, -1, Quaternion.Euler(90,0,0));
            }
        }
        else
        {
            float foreLength = smg ? .12f : sniper ? .28f : .27f;
            s.Box(new Vector3(0,.045f,.19f+foreLength*.5f), new Vector3(.115f,.1f,foreLength), accent);
            for (int i = 0; i < (smg ? 3 : 6); i++)
                s.Box(new Vector3(0,.048f,.22f+i*.038f), new Vector3(.119f,.039f,.016f), Dark);
            if (lmg)
            {
                s.Box(new Vector3(0,-.16f,.075f), new Vector3(.23f,.23f,.2f), Olive);
                s.Box(new Vector3(0,-.28f,.075f), new Vector3(.24f,.025f,.21f), Dark);
                for (int i = 0; i < 6; i++)
                    s.Tube(new Vector3(-.07f-i*.022f,.005f,.02f), .012f, .085f, Brass);
                s.Box(new Vector3(0,.22f,-.015f), new Vector3(.04f,.035f,.21f), Rubber);
                for (int i = -1; i <= 1; i+=2)
                    s.Box(new Vector3(0,.16f,-.015f+i*.09f), new Vector3(.03f,.1f,.025f), Steel);
            }
            else
            {
                s.Box(new Vector3(0,-.12f,.095f), new Vector3(.075f,sniper ? .11f : .23f,smg ? .065f : .1f), Dark, new Vector3(smg ? 0 : -12,0,0));
                if (!sniper)
                    s.Box(new Vector3(0,-.245f,.121f), new Vector3(.087f,.035f,smg ? .076f : .11f), accent);
            }
        }
        if (sniper)
        {
            for (int i = -1; i <= 1; i+=2)
                s.Box(new Vector3(0,.16f,i*.105f), new Vector3(.055f,.085f,.04f), Dark);
            s.Tube(new Vector3(0,.23f,0), .034f, .31f, Dark);
            s.Tube(new Vector3(0,.23f,.17f), .06f, .1f, Steel);
            s.Tube(new Vector3(0,.23f,-.17f), .046f, .08f, Rubber);
            s.Tube(new Vector3(0,.23f,.222f), .049f, .004f, Glass);
            s.Tube(new Vector3(0,.23f,-.212f), .035f, .004f, Glass);
            s.Box(new Vector3(0,.28f,0), new Vector3(.05f,.06f,.05f), Steel);
        }
        else Sights(s, -.12f, barrelEnd-.075f);
        if (sniper || lmg)
            for (int side = -1; side <= 1; side+=2)
                s.Box(new Vector3(side*.065f,-.13f,barrelEnd-.17f), new Vector3(.022f,.28f,.03f), Steel, new Vector3(0,0,side*24));
    }

    static void Grip(Shape s, float z, Color c)
    {
        s.Box(new Vector3(0,-.135f,z), new Vector3(.076f,.22f,.09f), c, new Vector3(-14,0,0));
        for (int i = 0; i < 4; i++)
            s.Box(new Vector3(0,-.095f-i*.036f,z-.01f+i*.009f), new Vector3(.08f,.01f,.082f), Steel, new Vector3(-14,0,0));
    }

    static void Sights(Shape s, float back, float front)
    {
        s.Box(new Vector3(0,.14f,back), new Vector3(.062f,.04f,.022f), Dark);
        s.Box(new Vector3(0,.125f,front), new Vector3(.02f,.067f,.025f), Dark);
        s.Box(new Vector3(0,.16f,front), new Vector3(.014f,.01f,.015f), Glass);
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
            Quaternion rotation = Quaternion.Euler(euler);
            Vector3 h = size*.5f;
            float b = Mathf.Min(.012f, Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.2f);
            Vector2[] outline = {
                new Vector2(-h.x+b,-h.y), new Vector2(h.x-b,-h.y),
                new Vector2(h.x,-h.y+b), new Vector2(h.x,h.y-b),
                new Vector2(h.x-b,h.y), new Vector2(-h.x+b,h.y),
                new Vector2(-h.x,h.y-b), new Vector2(-h.x,-h.y+b)
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
            const int sides = 12;
            for (int i = 0; i < sides; i++)
            {
                float a = i*Mathf.PI*2/sides, b = (i+1)*Mathf.PI*2/sides;
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
