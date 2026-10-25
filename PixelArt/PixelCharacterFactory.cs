using System.Collections.Generic;
using UnityEngine;

/// <summary>Locally authored voxel characters. Geometry is visual only; the existing gameplay collider owns physics.</summary>
public static partial class PixelCharacterFactory
{
    static Mesh cube;
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
    static readonly Color Skin = new Color(.96f, .72f, .49f);
    static readonly Color Navy = new Color(.10f, .16f, .27f);
    static readonly Color Gold = new Color(.95f, .69f, .23f);
    static readonly Color Steel = new Color(.42f, .53f, .64f);

    public static GameObject Create(Transform parent, string appearance)
    {
        if (appearance == "Goblin" || appearance == "Orc" || appearance == "ForestImp") return CreateMonster(parent, appearance);
        bool player = appearance == "Player", boss = appearance == "Boss";
        var root = new GameObject(appearance + " Pixel Model", typeof(Animation));
        root.transform.SetParent(parent, false);
        Transform hips = Joint(root.transform, "Hips", new Vector3(0, .92f, 0));
        Transform chest = Joint(hips, "Chest", new Vector3(0, .18f, 0));
        Color cloth = player ? Navy : boss ? new Color(.50f, .08f, .14f) :
            appearance == "Npc1" ? new Color(.14f, .40f, .39f) :
            appearance == "Npc2" ? new Color(.24f, .30f, .61f) : new Color(.56f, .22f, .16f);
        string armor = boss ? "BossSteel" : "Pixel Steel";
        Color metal = boss ? new Color(.14f, .16f, .24f) : Steel;
        string heraldry = boss ? "BossHeraldry" : "Pixel " + appearance + " Cloth";
        Box(chest, "Body", new Vector3(0, .22f, 0), new Vector3(.56f, .48f, .32f), player ? Navy : metal, player ? "Pixel Navy" : armor);
        Box(hips, "Belt", Vector3.zero, new Vector3(.58f, .12f, .34f), Navy, "Pixel Navy");
        Box(chest, "Tabard", new Vector3(0, .13f, .177f), new Vector3(.32f, .56f, .045f), cloth, heraldry);
        if (player)
        {
            Box(chest, "Shirt", new Vector3(0, .42f, .17f), new Vector3(.38f, .12f, .06f), Color.white, "Pixel Ivory");
            Box(chest, "Bow", new Vector3(0, .34f, .22f), new Vector3(.22f, .10f, .06f), new Color(.19f, .82f, .86f), "Pixel Cyan");
            Box(chest, "Buttons", new Vector3(0, .16f, .21f), new Vector3(.045f, .19f, .025f), Gold, "Pixel Gold");
        }
        else
        {
            Box(chest, "Heraldic Cross Vertical", new Vector3(0, .25f, .21f), new Vector3(.06f, .24f, .03f), Gold, boss ? "BossTrim" : "Pixel Gold");
            Box(chest, "Heraldic Cross Horizontal", new Vector3(0, .30f, .21f), new Vector3(.22f, .06f, .03f), Gold, boss ? "BossTrim" : "Pixel Gold");
        }
        Transform head = Joint(chest, "Head", new Vector3(0, .62f, 0));
        Box(head, "Head Block", Vector3.zero, new Vector3(.44f, .44f, .38f), player ? Skin : metal, player ? "Pixel Skin" : armor);
        if (player)
        {
            Box(head, "Hair Cap", new Vector3(0, .22f, -.025f), new Vector3(.48f, .14f, .43f), Gold, "Pixel Gold");
            Box(head, "Long Hair", new Vector3(0, -.18f, -.20f), new Vector3(.48f, .58f, .10f), Gold, "Pixel Gold");
            for (int x = -2; x <= 2; x++)
                Box(head, "Bangs", new Vector3(x * .085f, .13f - (Mathf.Abs(x) % 2) * .04f, .20f), new Vector3(.085f, .18f, .05f), Gold, "Pixel Gold");
            foreach (float side in new[] { -1f, 1f })
            {
                Box(head, "Eye White", new Vector3(side * .10f, .01f, .199f), new Vector3(.09f, .08f, .025f), Color.white, "Pixel Ivory");
                Box(head, "Amber Eye", new Vector3(side * .10f, .005f, .217f), new Vector3(.045f, .06f, .025f), Navy, "Pixel Navy");
            }
            Box(head, "Mouth", new Vector3(0, -.105f, .20f), new Vector3(.065f, .025f, .025f), new Color(.64f, .25f, .26f), "Pixel Mouth");
            Box(head, "Hairpin", new Vector3(.17f, .18f, .24f), new Vector3(.12f, .025f, .025f), Color.white, "Pixel Ivory");
        }
        else
        {
            Box(head, "Visor", new Vector3(0, .035f, .20f), new Vector3(.38f, .065f, .035f), Navy, "Pixel Navy");
            foreach (float side in new[] { -1f, 1f })
                Box(head, "Eye Slit", new Vector3(side * .10f, .035f, .225f), new Vector3(.10f, .025f, .025f), boss ? new Color(1f, .12f, .03f) : new Color(.35f, .85f, .87f), boss ? "BossEmbers" : "Pixel Cyan");
            Box(head, "Helmet Ridge", new Vector3(0, -.04f, .235f), new Vector3(.045f, .32f, .04f), Steel, boss ? "BossTrim" : "Pixel Steel");
            if (boss)
            {
                Box(head, "Crown Band", new Vector3(0, .24f, 0), new Vector3(.52f, .10f, .44f), Gold, "BossCrownGold");
                for (int i = -2; i <= 2; i++)
                    Box(head, "Crown Point", new Vector3(i * .105f, .34f + (i % 2 == 0 ? .035f : 0), .19f), new Vector3(.065f, .18f, .07f), Gold, "BossCrownGold");
            }
        }
        for (int side = -1; side <= 1; side += 2)
        {
            string prefix = side < 0 ? "Left" : "Right";
            Transform leg = Joint(hips, prefix + "Leg", new Vector3(side * .15f, -.04f, 0));
            Box(leg, "Leg", new Vector3(0, -.36f, 0), new Vector3(.23f, .72f, .25f), player ? Navy : metal, player ? "Pixel Navy" : armor);
            Box(leg, "Boot", new Vector3(0, -.77f, .045f), new Vector3(.25f, .18f, .35f), Navy, "Pixel Navy");
            if (player) Box(leg, "Knee", new Vector3(0, -.26f, .01f), new Vector3(.235f, .16f, .26f), Skin, "Pixel Skin");
            Transform arm = Joint(chest, prefix + "Arm", new Vector3(side * .40f, .40f, 0));
            Box(arm, "Sleeve", new Vector3(0, -.12f, 0), new Vector3(.25f, .26f, .30f), player ? Color.white : metal, player ? "Pixel Ivory" : armor);
            Box(arm, "Forearm", new Vector3(0, -.38f, 0), new Vector3(.19f, .30f, .23f), player ? Skin : metal, player ? "Pixel Skin" : armor);
            Transform hand = Joint(arm, "Hand", new Vector3(0, -.57f, 0));
            Box(hand, "Hand", Vector3.zero, new Vector3(.20f, .16f, .23f), player ? Skin : Navy, player ? "Pixel Skin" : "Pixel Navy");
            if (boss)
                for (int i = 0; i < 3; i++)
                    Box(arm, "Shoulder Spike", new Vector3(side * i * .07f, .12f + i * .045f, 0), new Vector3(.065f, .19f, .075f), Gold, "BossTrim");
            if (!player) Equipment(hand, side > 0, boss);
        }
        Animation animation = root.GetComponent<Animation>();
        foreach (string name in new[] { "Idle", "Walk", "Run", "Jump", "Fall", "Crouch", "CrouchWalk", "Slide", "Death", "Attack", "Shoot", "Cast" })
            animation.AddClip(Clip(name), name);
        animation.playAutomatically = false;
        if (player) ApplyCharacterStyle(root, parent.GetComponentInParent<CharacterSkills>()?.Character ?? CharacterRoster.All[0]);
        return root;
    }

    static void Equipment(Transform hand, bool right, bool boss)
    {
        if (right)
        {
            Transform sword = Joint(hand, "Sword", Vector3.zero);
            Box(sword, "Blade", new Vector3(0, .45f, .08f), new Vector3(boss ? .14f : .09f, .78f, .065f), Steel, "Pixel Steel");
            Box(sword, "Blade Tip", new Vector3(0, .88f, .08f), new Vector3(.055f, .10f, .065f), Steel, "Pixel Steel");
            Box(sword, "Guard", new Vector3(0, .05f, .08f), new Vector3(.32f, .065f, .10f), Gold, boss ? "BossTrim" : "Pixel Gold");
            Transform crossbow = Joint(hand, "Crossbow", new Vector3(0, 0, .1f));
            Box(crossbow, "Stock", new Vector3(0, 0, .18f), new Vector3(.13f, .14f, .60f), new Color(.48f, .26f, .14f), "Pixel Wood");
            Box(crossbow, "Bow", new Vector3(0, 0, .35f), new Vector3(.65f, .07f, .08f), Steel, "Pixel Steel");
            Joint(crossbow, "BoltMuzzle", new Vector3(0, .05f, .52f));
        }
        else
        {
            Transform shield = Joint(hand, "Shield", new Vector3(0, 0, .10f));
            Box(shield, "Rim", new Vector3(0, .17f, .08f), new Vector3(.45f, .62f, .09f), Gold, boss ? "BossTrim" : "Pixel Gold");
            Box(shield, "Face", new Vector3(0, .17f, .14f), new Vector3(.37f, .54f, .065f), Navy, "Pixel Navy");
            Box(shield, "Emblem", new Vector3(0, .17f, .18f), new Vector3(.10f, .28f, .03f), Gold, boss ? "BossTrim" : "Pixel Gold");
        }
    }

    public static GameObject CreateArms(Transform parent)
    {
        Transform root = Joint(parent, "Pixel First Person Arms", Vector3.zero);
        for (int side = -1; side <= 1; side += 2)
        {
            Transform arm = Joint(root, "Arm", new Vector3(side * .16f, -.26f, .24f));
            arm.localRotation = Quaternion.Euler(-65f, side * -12f, 0);
            Box(arm, "Sleeve", Vector3.zero, new Vector3(.115f, .14f, .13f), Color.white, "Pixel Ivory");
            Box(arm, "Forearm", new Vector3(0, -.14f, 0), new Vector3(.085f, .20f, .09f), Skin, "Pixel Skin");
            Box(arm, "Grip", new Vector3(0, -.26f, 0), new Vector3(.10f, .075f, .10f), Skin, "Pixel Skin");
        }
        ApplyArmStyle(root, parent.GetComponentInParent<CharacterSkills>()?.Character ?? CharacterRoster.All[0]);
        return root.gameObject;
    }

    public static void ApplyArmStyle(Transform arms, CharacterDefinition character)
    {
        var block = new MaterialPropertyBlock();
        foreach (var renderer in arms.GetComponentsInChildren<Renderer>(true))
            if (renderer.name == "Sleeve")
            { renderer.GetPropertyBlock(block); block.SetColor("_Color",character.Color); renderer.SetPropertyBlock(block); }
    }

    public static void ApplyCharacterStyle(GameObject model, CharacterDefinition character)
    {
        int index = System.Array.IndexOf(CharacterRoster.All,character);
        Color hair = index % 3 == 0 ? Gold : index % 3 == 1 ? new Color(.25f,.18f,.20f) : new Color(.84f,.87f,.93f);
        var block = new MaterialPropertyBlock();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            string name = renderer.name;
            if (name != "Tabard" && name != "Bow" && name != "Sleeve" && name != "Hair Cap" && name != "Long Hair" && name != "Bangs") continue;
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color",name == "Hair Cap" || name == "Long Hair" || name == "Bangs" ? hair : character.Color);
            renderer.SetPropertyBlock(block);
        }
        Transform head = model.transform.Find("Hips/Chest/Head");
        if (head == null) return;
        Transform previous = head.Find("Character Headgear");
        if (previous != null)
        {
            // Detach before rebuilding the visibility cache; Destroy is deferred until end of frame.
            previous.gameObject.SetActive(false); previous.SetParent(null,false);
            if (Application.isPlaying) Object.Destroy(previous.gameObject); else Object.DestroyImmediate(previous.gameObject);
        }
        Transform gear = Joint(head,"Character Headgear",Vector3.zero);
        string palette = "Character " + character.Id;
        Box(gear,"Band",new Vector3(0,.17f,.215f),new Vector3(.50f,.08f,.07f),character.Color,palette);
        switch (character.Id)
        {
            case "sentinel":
                Box(gear,"Helmet",new Vector3(0,.26f,0),new Vector3(.52f,.18f,.45f),character.Color,palette);
                for (int side = -1; side <= 1; side += 2) Box(gear,"Guard",new Vector3(side*.245f,.015f,0),new Vector3(.08f,.42f,.43f),Steel,"Pixel Steel"); break;
            case "gunner": case "engineer":
                for (int side = -1; side <= 1; side += 2) Box(gear,"Goggles",new Vector3(side*.11f,.16f,.265f),new Vector3(.17f,.12f,.08f),Navy,"Pixel Navy"); break;
            case "medic":
                Box(gear,"Cap",new Vector3(0,.27f,0),new Vector3(.50f,.15f,.43f),Color.white,"Pixel Ivory");
                Box(gear,"Cross",new Vector3(0,.27f,.235f),new Vector3(.05f,.12f,.04f),character.Color,palette);
                Box(gear,"Cross",new Vector3(0,.27f,.235f),new Vector3(.14f,.04f,.04f),character.Color,palette); break;
            case "assassin":
                for (int side = -1; side <= 1; side += 2) Box(gear,"Hood",new Vector3(side*.25f,-.02f,0),new Vector3(.09f,.55f,.44f),character.Color,palette);
                Box(gear,"Hood Top",new Vector3(0,.30f,0),new Vector3(.55f,.15f,.44f),character.Color,palette); break;
            case "pyromancer": case "frost":
                for (int i = -1; i <= 1; i++) Box(gear,"Crest",new Vector3(i*.16f,.31f,.16f),new Vector3(.08f,.18f+(i==0?.1f:0),.08f),character.Color,palette); break;
            case "reaper":
                Box(gear,"Mask",new Vector3(0,-.12f,.235f),new Vector3(.42f,.18f,.07f),character.Color,palette); break;
            case "ranger":
                Box(gear,"Feather",new Vector3(.26f,.40f,0),new Vector3(.07f,.40f,.09f),character.Color,palette); break;
        }
    }

    public static GameObject CreateBolt(Transform parent, float length = .30f)
    {
        Transform root = Joint(parent, "Pixel Bolt", Vector3.zero);
        Box(root, "Shaft", Vector3.zero, new Vector3(.035f, .035f, length), Gold, "Pixel Gold");
        Box(root, "Tip", new Vector3(0, 0, length * .5f), new Vector3(.055f, .055f, .065f), Steel, "Pixel Steel");
        Box(root, "Fletching", new Vector3(0, 0, -length * .4f), new Vector3(.11f, .025f, .06f), Navy, "Pixel Navy");
        return root.gameObject;
    }

    static Transform Joint(Transform parent, string name, Vector3 position)
    {
        var node = new GameObject(name).transform;
        node.SetParent(parent, false);
        node.localPosition = position;
        return node;
    }

    static void Box(Transform parent, string name, Vector3 center, Vector3 size, Color tint, string materialName)
    {
        if (cube == null)
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = primitive.GetComponent<MeshFilter>().sharedMesh;
            primitive.SetActive(false);
            if (Application.isPlaying) Object.Destroy(primitive); else Object.DestroyImmediate(primitive);
        }
        if (!materials.TryGetValue(materialName, out Material material) || material == null)
        {
            material = new Material(Resources.Load<Shader>("PixelSurface")) { name = materialName };
            material.SetColor("_Color", tint);
            materials[materialName] = material;
        }
        Transform node = Joint(parent, name, center);
        node.localScale = size;
        node.gameObject.AddComponent<MeshFilter>().sharedMesh = cube;
        node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static AnimationClip Clip(string name)
    {
        if (clips.TryGetValue(name, out AnimationClip cached) && cached != null) return cached;
        bool walk = name == "Walk" || name == "Run" || name == "CrouchWalk";
        bool crouch = name == "Crouch" || name == "CrouchWalk" || name == "Slide";
        float length = name == "Run" ? .5f : name == "Attack" || name == "Shoot" ? .6f : 1f;
        var clip = new AnimationClip { name = "Pixel " + name, legacy = true, frameRate = 12f };
        for (int axis = 0; axis < 3; axis++)
        {
            string component = axis == 0 ? "x" : axis == 1 ? "y" : "z";
            var keys = new Keyframe[13];
            for (int frame = 0; frame <= 12; frame++)
            {
                float t = frame / 12f;
                float value = axis == 1 ? .92f - (crouch ? .32f : 0) + (walk ? Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2)) * .035f : 0) : 0;
                if (name == "Death" && axis == 1) value = Mathf.Lerp(.92f, .24f, t);
                keys[frame] = new Keyframe(t * length, value, float.PositiveInfinity, float.PositiveInfinity);
            }
            clip.SetCurve("Hips", typeof(Transform), "localPosition." + component, new AnimationCurve(keys));
        }
        foreach (string part in new[] { "LeftLeg", "RightLeg", "Chest/LeftArm", "Chest/RightArm", "Chest" })
        {
            var x = new Keyframe[13]; var y = new Keyframe[13]; var z = new Keyframe[13]; var w = new Keyframe[13];
            for (int frame = 0; frame <= 12; frame++)
            {
                float t = frame / 12f;
                float sign = part.Contains("Left") ? 1f : -1f;
                float angle = walk ? Mathf.Sin(t * Mathf.PI * 2) * sign * (name == "Run" ? 38f : 24f) : 0;
                if (part.Contains("Arm")) angle = -angle;
                if (crouch) angle += part.Contains("Leg") ? -35f : part == "Chest" ? 20f : 0;
                if (name == "Jump" || name == "Fall") angle = part.Contains("Arm") ? -55f : part.Contains("Leg") ? 20f * sign : 0;
                if (name == "Attack" && part.Contains("RightArm")) angle = -100f + Mathf.Sin(t * Mathf.PI) * 130f;
                if (name == "Shoot" && part.Contains("Arm")) angle = -85f;
                if (name == "Cast" && part.Contains("Arm")) angle = -140f;
                if (name == "Death" && part == "Chest") angle = -80f * t;
                if (part == "Chest" && !crouch && name != "Death") angle = 0;
                Quaternion q = Quaternion.Euler(angle, 0, 0);
                float time = t * length;
                x[frame] = Step(time, q.x); y[frame] = Step(time, q.y); z[frame] = Step(time, q.z); w[frame] = Step(time, q.w);
            }
            string path = "Hips/" + part;
            clip.SetCurve(path, typeof(Transform), "localRotation.x", new AnimationCurve(x));
            clip.SetCurve(path, typeof(Transform), "localRotation.y", new AnimationCurve(y));
            clip.SetCurve(path, typeof(Transform), "localRotation.z", new AnimationCurve(z));
            clip.SetCurve(path, typeof(Transform), "localRotation.w", new AnimationCurve(w));
        }
        clips[name] = clip;
        return clip;
    }

    static Keyframe Step(float time, float value) => new Keyframe(time, value, float.PositiveInfinity, float.PositiveInfinity);
}
