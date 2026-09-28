using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Mesh-authored equipment on the existing natural-proportion rig. No primitive bodies.
public static class MedievalEquipmentBuilder
{
    public static void Equip(GameObject model, string output, string variant, Color heraldry)
    {
        bool boss = variant == "Boss";
        Transform rig = model.GetComponentInChildren<Animation>().transform;
        Transform Bone(string name) => rig.GetComponentsInChildren<Transform>().First(node => node.name == "J_Bip_" + name);
        Vector3 Point(Transform bone) => rig.InverseTransformPoint(bone.position);
        var steel = Material(output, variant + "Steel", boss ? new Color(.19f,.22f,.28f) : new Color(.48f,.55f,.62f));
        var leather = Material(output, variant + "Leather", new Color(.13f,.07f,.035f));
        var trim = Material(output, variant + "Trim", boss ? new Color(.95f,.65f,.22f) : new Color(.58f,.42f,.19f));
        var cloth = Material(output, variant + "Heraldry", heraldry);
        var dark = Material(output, variant + "Mail", new Color(.085f,.10f,.12f));
        Material[] palette = { steel, leather, trim, cloth, dark };

        foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (skin.name != "Body") { skin.enabled = false; continue; }
            Mesh mesh = UnityEngine.Object.Instantiate(skin.sharedMesh);
            Material[] slots = skin.sharedMaterials;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                string name = slots[slot].name;
                if (name.Contains("Accessory") || name.Contains("HAIR")) mesh.SetTriangles(Array.Empty<int>(), slot);
                slots[slot] = name.Contains("Shoes") ? leather : dark;
            }
            skin.sharedMesh = Save(mesh, output + "/Meshes/" + variant + "UnderArmor.asset");
            skin.sharedMaterials = slots;
        }
        var outfit = new Geometry();
        var bones = new List<Transform>();
        int Bind(Transform bone) { int index = bones.IndexOf(bone); if (index < 0) { index = bones.Count; bones.Add(bone); } return index; }
        void Sleeve(string side, string upper, string lower, float start, float end, float width, int material)
        {
            Transform bone = Bone(side + "_" + upper), tip = Bone(side + "_" + lower);
            Vector3 from = Vector3.Lerp(Point(bone), Point(tip), start), to = Vector3.Lerp(Point(bone), Point(tip), end);
            outfit.Tube(from, to, new[] {width * .8f,width,width * .85f}, new[] {width * .85f,width,width * .9f}, material, Bind(bone));
        }
        Transform hips = Bone("C_Hips"), chest = Bone("C_Chest"), head = Bone("C_Head");
        Vector3 waist = Point(hips) + Vector3.up * .075f;
        Vector3 collar = Point(Bone("C_Neck")) - Vector3.up * .025f;
        outfit.Tube(waist, collar, new[] {.16f,.195f,.205f,.18f,.105f}, new[] {.12f,.15f,.16f,.135f,.09f}, 0, Bind(chest));
        outfit.Tube(waist - Vector3.up * .025f, waist + Vector3.up * .015f, new[] {.17f,.17f}, new[] {.12f,.12f}, 1, Bind(hips));
        outfit.Beam(waist + new Vector3(-.028f,-.006f,.125f), waist + new Vector3(.028f,-.006f,.125f), .035f,.014f,2,Bind(hips));
        // Front surcoat panel and its gold vertical heraldic stripe.
        outfit.Beam(waist + new Vector3(0,.04f,.175f), collar + new Vector3(0,-.045f,.175f), .13f,.008f,3,Bind(chest));
        outfit.Beam(waist + new Vector3(0,.045f,.184f), collar + new Vector3(0,-.045f,.184f), .018f,.01f,2,Bind(chest));
        foreach (float side in new[] {-1f,1f})
            outfit.Beam(waist + new Vector3(side*.068f,-.025f,.15f),Point(hips) + new Vector3(side*.068f,-.24f,.15f),.13f,.015f,3,Bind(hips));
        foreach (string side in new[] { "L", "R" })
        {
            Sleeve(side,"UpperArm","LowerArm",-.07f,.45f,boss ? .112f : .088f,0);
            Sleeve(side,"LowerArm","Hand",.36f,.98f,.061f,0);
            Sleeve(side,"UpperLeg","LowerLeg",-.04f,.34f,.102f,0);
            Sleeve(side,"LowerLeg","Foot",.19f,.95f,.067f,0);
            Transform hand = Bone(side + "_Hand");
            Vector3 handEnd = Point(hand) + rig.InverseTransformVector(hand.TransformDirection(new Vector3(side == "L" ? -.085f : .085f,0,0)));
            outfit.Tube(Point(hand),handEnd,new[] {.045f,.04f},new[] {.038f,.028f},1,Bind(hand));
        }
        // Great helm with recessed black eye slit, independently weighted to the head.
        Vector3 headOrigin = Point(head);
        outfit.Tube(headOrigin + Vector3.up * -.04f,headOrigin + Vector3.up * .23f,
            new[] {.112f,.12f,.12f,.113f,.09f,.001f},new[] {.125f,.13f,.13f,.123f,.105f,.001f},0,Bind(head),true);
        outfit.Tube(headOrigin + Vector3.up * .068f,headOrigin + Vector3.up * .099f,new[] {.118f,.118f},new[] {.127f,.127f},4,Bind(head));
        outfit.Beam(headOrigin + new Vector3(0,-.025f,.133f),headOrigin + new Vector3(0,.175f,.124f),.014f,.012f,2,Bind(head));
        Mesh armorMesh = outfit.Mesh(variant + " Plate Armor", true);
        armorMesh.bindposes = bones.Select(bone => bone.worldToLocalMatrix * rig.localToWorldMatrix).ToArray();
        armorMesh = Save(armorMesh,output + "/Meshes/" + variant + "Armor.asset");
        var armor = new GameObject("Medieval Armor",typeof(SkinnedMeshRenderer)); armor.transform.SetParent(rig,false);
        var renderer = armor.GetComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = armorMesh; renderer.bones = bones.ToArray();
        renderer.rootBone = hips; renderer.sharedMaterials = palette; renderer.updateWhenOffscreen = true;
        renderer.localBounds = new Bounds(new Vector3(0,.85f,0),new Vector3(2.5f,2.4f,2.5f));

        Transform rightHand = Bone("R_Hand"), leftHand = Bone("L_Hand");
        GameObject sword = Weapon("Sword",Sword(),rightHand,palette,output);
        sword.transform.rotation = rig.rotation * Quaternion.Euler(180,0,0);
        if (boss) sword.transform.localScale = Vector3.one * 1.2f;
        GameObject shield = Weapon("Shield",Shield(),leftHand,palette,output);
        shield.transform.rotation = rig.rotation;
        GameObject crossbow = Weapon("Crossbow",Crossbow(),rightHand,palette,output);
        crossbow.transform.rotation = rig.rotation;
        var muzzle = new GameObject("BoltMuzzle"); muzzle.transform.SetParent(crossbow.transform,false); muzzle.transform.localPosition = new Vector3(0,.07f,.39f);
        crossbow.SetActive(false);
        if (variant == "Npc1")
        {
            GameObject bolt = Weapon("Bolt",Bolt(),null,palette,output);
            PrefabUtility.SaveAsPrefabAsset(bolt,output + "/Weapons/Bolt.prefab");
            UnityEngine.Object.DestroyImmediate(bolt);
        }
    }
    static GameObject Weapon(string name, Geometry geometry, Transform parent, Material[] materials, string output)
    {
        Mesh mesh = Save(geometry.Mesh(name,false),output + "/Meshes/" + name + ".asset");
        var weapon = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); weapon.transform.SetParent(parent,false);
        weapon.GetComponent<MeshFilter>().sharedMesh = mesh; weapon.GetComponent<MeshRenderer>().sharedMaterials = materials;
        return weapon;
    }
    static Geometry Sword()
    {
        var mesh = new Geometry();
        mesh.Tube(new Vector3(0,-.09f,0),new Vector3(0,.07f,0),new[]{.018f,.018f},new[]{.018f,.018f},1);
        mesh.Beam(new Vector3(-.12f,.07f,0),new Vector3(.12f,.07f,0),.025f,.025f,2);
        mesh.Tube(new Vector3(0,.09f,0),new Vector3(0,.79f,0),new[]{.036f,.034f,.027f,0f},new[]{.009f,.009f,.007f,0f},0,0,false,4);
        mesh.Tube(new Vector3(0,-.12f,0),new Vector3(0,-.08f,0),new[]{.029f,.029f},new[]{.025f,.025f},2);
        return mesh;
    }
    static Geometry Shield()
    {
        var mesh = new Geometry();
        mesh.Tube(new Vector3(0,-.35f,.015f),new Vector3(0,.22f,.015f),new[]{.005f,.19f,.23f,.21f},new[]{.02f,.042f,.04f,.02f},3);
        mesh.Beam(new Vector3(0,-.28f,.058f),new Vector3(0,.18f,.058f),.025f,.012f,2);
        mesh.Beam(new Vector3(-.19f,.045f,.058f),new Vector3(.19f,.045f,.058f),.03f,.012f,2);
        return mesh;
    }
    static Geometry Crossbow()
    {
        var mesh = new Geometry();
        mesh.Beam(new Vector3(0,.05f,-.2f),new Vector3(0,.05f,.36f),.055f,.045f,1);
        mesh.Beam(new Vector3(0,.06f,.25f),new Vector3(-.26f,.06f,.12f),.025f,.018f,0);
        mesh.Beam(new Vector3(0,.06f,.25f),new Vector3(.26f,.06f,.12f),.025f,.018f,0);
        mesh.Beam(new Vector3(-.26f,.065f,.12f),new Vector3(0,.065f,-.08f),.004f,.004f,4);
        mesh.Beam(new Vector3(.26f,.065f,.12f),new Vector3(0,.065f,-.08f),.004f,.004f,4);
        mesh.Beam(new Vector3(0,-.04f,0),new Vector3(0,.06f,0),.025f,.025f,1);
        return mesh;
    }
    static Geometry Bolt()
    {
        var mesh = new Geometry();
        mesh.Beam(new Vector3(0,0,-.17f),new Vector3(0,0,.14f),.009f,.009f,1);
        mesh.Tube(new Vector3(0,0,.13f),new Vector3(0,0,.21f),new[]{.021f,0f},new[]{.021f,0f},0,0,false,4);
        mesh.Beam(new Vector3(0,0,-.16f),new Vector3(0,0,-.08f),.05f,.003f,3);
        return mesh;
    }
    static Material Material(string output,string name,Color color)
    {
        var material = new Material(Shader.Find("GameTest/AnimeToon")) {name = name};
        material.SetColor("_Color",color); material.SetFloat("_Cutoff",0); material.SetFloat("_Cull",0);
        return Save(material,output + "/Materials/" + name + ".mat");
    }
    static T Save<T>(T asset,string path) where T : UnityEngine.Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null) { AssetDatabase.CreateAsset(asset,path); return asset; }
        // Clear native vertex/bone buffers before replacing a mesh whose topology changed.
        if (existing is Mesh existingMesh) existingMesh.Clear(false);
        EditorUtility.CopySerialized(asset,existing);
        if (existing is Mesh refreshedMesh) refreshedMesh.UploadMeshData(false);
        EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(asset); return existing;
    }
    sealed class Geometry
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<BoneWeight> weights = new List<BoneWeight>();
        readonly List<int>[] triangles = Enumerable.Range(0,5).Select(_=>new List<int>()).ToArray();
        public void Beam(Vector3 from,Vector3 to,float width,float depth,int material,int bone=0)
        { Tube(from,to,new[]{width * .7071f,width * .7071f},new[]{depth * .7071f,depth * .7071f},material,bone,false,4); }
        public void Tube(Vector3 from,Vector3 to,float[] width,float[] depth,int material,int bone=0,bool visor=false,int sides=20)
        {
            int first = vertices.Count;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up,(to-from).normalized);
            for (int ring=0;ring<width.Length;ring++)
                for (int side=0;side<sides;side++)
                {
                    float angle = side * Mathf.PI * 2 / sides;
                    vertices.Add(Vector3.Lerp(from,to,ring/(float)(width.Length-1)) + rotation * new Vector3(Mathf.Cos(angle)*width[ring],0,Mathf.Sin(angle)*depth[ring]));
                    weights.Add(new BoneWeight {boneIndex0=bone,weight0=1});
                }
            for (int ring=0;ring<width.Length-1;ring++)
                for (int side=0;side<sides;side++)
                {
                    int slot = visor && ring==2 && Mathf.Sin((side+.5f)*Mathf.PI*2/sides)>.45f ? 4 : material;
                    int a=first+ring*sides+side,b=first+ring*sides+(side+1)%sides,c=a+sides,d=b+sides;
                    triangles[slot].AddRange(new[]{a,c,b,b,c,d});
                }
            for (int side=1;side<sides-1;side++)
            { triangles[material].AddRange(new[]{first,first+side,first+side+1}); int last=first+(width.Length-1)*sides; triangles[material].AddRange(new[]{last,last+side+1,last+side}); }
        }
        public Mesh Mesh(string name,bool skin)
        {
            var mesh=new Mesh {name=name}; mesh.SetVertices(vertices); mesh.subMeshCount=5;
            for(int slot=0;slot<5;slot++) mesh.SetTriangles(triangles[slot],slot);
            if(skin) mesh.boneWeights=weights.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
