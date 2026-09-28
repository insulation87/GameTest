using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Derive medieval armor and equipment on the existing naturally proportioned rig.
public static class EnemyAppearanceBuilder
{
    const string DefaultOutput = "Assets/Script/NPC/Resources/Characters/Enemies";
    static string output;
    static Scene reviewScene;

    [MenuItem("Tools/Character/Rebuild NPC and Boss Appearances")]
    public static void Build()
    {
        output = Argument("-enemyOutput", DefaultOutput);
        string sourcePath = Argument("-enemySource", "Assets/Script/Player/Resources/Characters/AnimeGirl/AnimeGirlModel.prefab");
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null) throw new InvalidDataException("Build the anime player model first: " + sourcePath);
        Directory.CreateDirectory(output + "/Materials");
        Directory.CreateDirectory(output + "/Animations");
        Directory.CreateDirectory(output + "/Meshes");
        Directory.CreateDirectory(output + "/Weapons");
        AssetDatabase.Refresh();
        BuildVariant(source, "Npc1", new Color(0.33f, 0.16f, 0.08f), new Color(0.16f, 0.45f, 0.4f), new Color(0.75f, 0.8f, 0.62f));
        BuildVariant(source, "Npc2", new Color(0.12f, 0.15f, 0.21f), new Color(0.28f, 0.34f, 0.58f), new Color(0.63f, 0.76f, 0.95f));
        BuildVariant(source, "Npc3", new Color(0.52f, 0.24f, 0.18f), new Color(0.38f, 0.21f, 0.43f), new Color(0.88f, 0.64f, 0.3f));
        BuildVariant(source, "Boss", new Color(0.92f, 0.94f, 1f), new Color(0.56f, 0.07f, 0.13f), new Color(1f, 0.68f, 0.2f));
        AssetDatabase.SaveAssets();
        ValidateAndRender();
        ValidateCombatRules();
        Debug.Log("ENEMY_APPEARANCES_BUILD_OK: medieval guard/crossbowmen/boss; sword, shield, crossbow, bolt and 6 clips.");
    }

    [MenuItem("Tools/Character/Validate Enemy Combat Rules")]
    public static void ValidateCombatRules()
    {
        foreach (int level in new[] {1,20,21,100})
        {
            float expected = 1f + (level - 1) / 5f;
            if (Mathf.Abs(EnemyCombatRules.DamageAtLevel(level) - expected) > 0.00001f)
                throw new InvalidDataException("Incorrect damage for level " + level);
        }
        for (int level = 1; level < 100; level++)
            if (Mathf.Abs(EnemyCombatRules.DamageAtLevel(level + 1) - EnemyCombatRules.DamageAtLevel(level) - .2f) > .00001f)
                throw new InvalidDataException("Incorrect per-level damage increment.");
        if (EnemyCombatRules.AllowsRanged(20) || !EnemyCombatRules.AllowsRanged(21))
            throw new InvalidDataException("Incorrect ranged-weapon level gate.");
        Debug.Log("ENEMY_LEVEL_RULES_OK");
    }

    static void BuildVariant(GameObject source, string name, Color hair, Color clothing, Color accent)
    {
        GameObject model = UnityEngine.Object.Instantiate(source);
        model.name = name;
        try
        {
            var replacements = new Dictionary<Material, Material>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                Material[] slots = renderer.sharedMaterials;
                for (int index = 0; index < slots.Length; index++)
                {
                    Material original = slots[index];
                    if (!replacements.TryGetValue(original, out Material replacement))
                    {
                        Color tint;
                        float recolor = 1f;
                        if (original.name.Contains("HAIR") || original.name.Contains("FaceBrow")) tint = hair;
                        else if (original.name.Contains("Tops")) tint = clothing;
                        else if (original.name.Contains("Accessory")) tint = accent;
                        else if (original.name.Contains("Athletic") || original.name.Contains("Athletic Shorts"))
                        { tint = Color.Lerp(clothing, Color.black, 0.75f); recolor = 0; }
                        else if (original.name.Contains("Shoes")) tint = new Color(0.15f, 0.17f, 0.2f);
                        else { replacements.Add(original, original); continue; }
                        replacement = new Material(original) { name = name + " " + original.name };
                        replacement.SetColor("_Color", tint);
                        replacement.SetFloat("_Recolor", recolor);
                        replacement = Save(replacement, output + "/Materials/" + name + "_" + original.name + ".mat");
                        replacements.Add(original, replacement);
                    }
                    slots[index] = replacement;
                }
                renderer.sharedMaterials = slots;
            }
            Animation animation = model.GetComponentInChildren<Animation>();
            animation["Idle"].clip.SampleAnimation(animation.gameObject, 0f);
            if (name == "Npc1")
            {
                BuildAction(animation, "Attack", 0.65f);
                BuildAction(animation, "Cast", 1.1f);
                BuildAction(animation, "Shoot", 0.75f);
            }
            // Enemy movement reuses the source rig's baked clips, not a second copy of its meshes/textures.
            foreach (string unused in new[] { "Crouch", "CrouchWalk", "Jump", "Fall", "Slide", "Death" }) animation.RemoveClip(unused);
            foreach (string action in new[] { "Attack", "Cast", "Shoot" })
                animation.AddClip(AssetDatabase.LoadAssetAtPath<AnimationClip>(output + "/Animations/" + action + ".anim"), action);
            animation["Idle"].clip.SampleAnimation(animation.gameObject, 0f);
            if (name == "Boss") AddCrown(model, accent);
            MedievalEquipmentBuilder.Equip(model, output, name, clothing);
            PrefabUtility.SaveAsPrefabAsset(model, output + "/" + name + ".prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(model); }
    }

    static void BuildAction(Animation animation, string name, float duration)
    {
        AnimationClip idle = animation["Idle"].clip;
        idle.SampleAnimation(animation.gameObject, 0f);
        Transform[] bones = animation.GetComponentsInChildren<Transform>().Where(node => node.name.StartsWith("J_")).ToArray();
        Quaternion[] restRotations = bones.Select(bone => bone.localRotation).ToArray();
        Vector3[] restPositions = bones.Select(bone => bone.localPosition).ToArray();
        var curves = new AnimationCurve[bones.Length, 7];
        for (int bone = 0; bone < bones.Length; bone++)
            for (int channel = 0; channel < 7; channel++) curves[bone, channel] = new AnimationCurve();
        int frames = Mathf.CeilToInt(duration * 30f);
        for (int frame = 0; frame <= frames; frame++)
        {
            float progress = frame / (float)frames;
            float strike = Mathf.Sin(progress * Mathf.PI);
            for (int index = 0; index < bones.Length; index++)
            {
                Transform bone = bones[index];
                bone.localRotation = restRotations[index];
                bone.localPosition = restPositions[index];
                bool left = bone.name.Contains("_L_");
                if (bone.name.EndsWith("UpperArm"))
                {
                    float angle = name == "Cast" ? 105f : name == "Shoot" ? 80f : left ? 25f : 100f;
                    bone.Rotate(Vector3.right, -angle * strike, Space.World);
                    if (name == "Cast") bone.Rotate(Vector3.forward, (left ? -30 : 30) * strike, Space.World);
                }
                if (bone.name.EndsWith("LowerArm")) bone.Rotate(Vector3.right, -25f * strike, Space.World);
                if (bone.name.EndsWith("Chest")) bone.Rotate(Vector3.up, (name == "Attack" ? -18f : 0f) * strike, Space.World);
                Quaternion rotation = bone.localRotation;
                Vector3 position = bone.localPosition;
                float[] values = { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w };
                for (int channel = 0; channel < 7; channel++) curves[index, channel].AddKey(progress * duration, values[channel]);
            }
        }
        var clip = new AnimationClip { name = name, legacy = true, frameRate = 30, wrapMode = WrapMode.ClampForever };
        string[] properties = { "localPosition.x", "localPosition.y", "localPosition.z", "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
        for (int bone = 0; bone < bones.Length; bone++)
            for (int channel = 0; channel < 7; channel++)
                clip.SetCurve(AnimationUtility.CalculateTransformPath(bones[bone], animation.transform), typeof(Transform), properties[channel], curves[bone, channel]);
        clip.EnsureQuaternionContinuity();
        Save(clip, output + "/Animations/" + name + ".anim");
        idle.SampleAnimation(animation.gameObject, 0f);
    }

    static void AddCrown(GameObject model, Color gold)
    {
        Transform head = model.GetComponentsInChildren<Transform>().First(node => node.name == "J_Bip_C_Head");
        const int segments = 48;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int index = 0; index <= segments; index++)
        {
            float angle = index * Mathf.PI * 2f / segments;
            float peak = index % 8 == 0 ? 0.055f : 0f;
            Vector3 rim = new Vector3(Mathf.Cos(angle) * 0.107f, 0, Mathf.Sin(angle) * 0.101f);
            vertices.Add(rim); vertices.Add(rim + Vector3.up * (0.024f + peak));
            if (index == segments) continue;
            int start = index * 2;
            triangles.AddRange(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 });
        }
        var mesh = new Mesh { name = "Boss Crown" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        mesh = Save(mesh, output + "/Meshes/BossCrown.asset");
        var material = new Material(Shader.Find("GameTest/AnimeToon")) { name = "Boss Crown Gold" };
        material.SetColor("_Color", gold); material.SetFloat("_Cull", 0); material.SetFloat("_Cutoff", 0);
        material = Save(material, output + "/Materials/BossCrownGold.mat");
        var crown = new GameObject("Boss Crown", typeof(MeshFilter), typeof(MeshRenderer));
        crown.transform.SetParent(head, false); crown.transform.localPosition = new Vector3(0, 0.19f, 0.005f);
        crown.GetComponent<MeshFilter>().sharedMesh = mesh;
        crown.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void ValidateAndRender()
    {
        string review = Argument("-enemyReview", "Temp/EnemyAppearanceReview");
        Directory.CreateDirectory(review);
        var instances = new List<GameObject>();
        reviewScene = EditorSceneManager.NewPreviewScene();
        try
        {
            string[] names = { "Npc1", "Npc2", "Npc3", "Boss" };
            for (int index = 0; index < names.Length; index++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(output + "/" + names[index] + ".prefab");
                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(instance, reviewScene);
                instances.Add(instance);
                if (instance.GetComponentsInChildren<SkinnedMeshRenderer>().Count(skin => skin.enabled) != 2 || instance.GetComponentsInChildren<Collider>().Length != 0)
                    throw new InvalidDataException("Invalid visual-only prefab: " + names[index]);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                    if (renderer.sharedMaterials.Any(material => material == null || material.shader == null || !material.shader.isSupported))
                        throw new InvalidDataException("Invalid material: " + names[index]);
                Animation animation = instance.GetComponentInChildren<Animation>();
                foreach (string clipName in new[] { "Idle", "Walk", "Run", "Attack", "Cast", "Shoot" })
                {
                    if (animation[clipName] == null) throw new InvalidDataException("Missing clip " + clipName);
                    AnimationClip clip = animation[clipName].clip;
                    clip.SampleAnimation(animation.gameObject, clip.length * 0.5f);
                    Transform rightArm = animation.GetComponentsInChildren<Transform>().First(node => node.name == "J_Bip_R_UpperArm");
                    Debug.Log("ENEMY_POSE " + names[index] + " " + clipName + " arm=" + rightArm.localEulerAngles);
                }
                animation["Idle"].clip.SampleAnimation(animation.gameObject, 0f);
                if (index == 1 || index == 2)
                {
                    foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == "Crossbow") child.gameObject.SetActive(true);
                        if (child.name == "Sword" || child.name == "Shield") child.gameObject.SetActive(false);
                    }
                }
                instance.transform.position = new Vector3((index - 1.5f) * 1.05f, 0, 0);
                Debug.Log("ENEMY_PREFAB_OK " + names[index]);
            }
            Render(review + "/Lineup.png", 1800, 1000, new Vector3(0, 1.05f, 0), 1.2f, instances);
            for (int index = 0; index < instances.Count; index++)
            {
                foreach (GameObject instance in instances) instance.SetActive(false);
                GameObject selected = instances[index]; selected.SetActive(true); selected.transform.position = Vector3.zero;
                Render(review + "/" + names[index] + ".png", 700, 1000, new Vector3(0, 1.05f, 0), 1.18f, instances);
            }
            GameObject boss = instances[3];
            Animation cast = boss.GetComponentInChildren<Animation>();
            cast["Cast"].clip.SampleAnimation(cast.gameObject, 0.55f);
            Render(review + "/BossCast.png", 900, 1000, new Vector3(0, 1.05f, 0), 1.3f, instances);
        }
        finally { EditorSceneManager.ClosePreviewScene(reviewScene); }
    }

    static void Render(string path, int width, int height, Vector3 center, float size, List<GameObject> models)
    {
        // Bake after sampling: edit-mode Camera.Render can reuse a previous skin pose.
        var snapshots = new List<GameObject>();
        SkinnedMeshRenderer[] skins = models.Where(model => model.activeInHierarchy)
            .SelectMany(model => model.GetComponentsInChildren<SkinnedMeshRenderer>()).Where(skin => skin.enabled).ToArray();
        foreach (SkinnedMeshRenderer skin in skins)
        {
            if (!skin.enabled) continue;
            var mesh = new Mesh(); skin.BakeMesh(mesh, true);
            var snapshot = new GameObject("Enemy Pose Snapshot", typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(snapshot, reviewScene);
            snapshot.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            snapshot.transform.localScale = skin.transform.lossyScale;
            snapshot.GetComponent<MeshFilter>().sharedMesh = mesh;
            snapshot.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            snapshots.Add(snapshot); skin.enabled = false;
        }
        var view = new GameObject("Enemy Preview Camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(view, reviewScene);
        Camera camera = view.GetComponent<Camera>();
        camera.scene = reviewScene;
        camera.orthographic = true; camera.orthographicSize = size; camera.nearClipPlane = 0.01f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.09f, 0.12f, 0.17f);
        camera.transform.position = center + new Vector3(0, 0.12f, 5); camera.transform.LookAt(center);
        var target = new RenderTexture(width, height, 24);
        RenderTexture previous = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(view);
            foreach (GameObject snapshot in snapshots)
            {
                UnityEngine.Object.DestroyImmediate(snapshot.GetComponent<MeshFilter>().sharedMesh);
                UnityEngine.Object.DestroyImmediate(snapshot);
            }
            foreach (SkinnedMeshRenderer skin in skins) skin.enabled = true;
        }
    }

    static T Save<T>(T asset, string path) where T : UnityEngine.Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null) { AssetDatabase.CreateAsset(asset, path); return asset; }
        EditorUtility.CopySerialized(asset, existing); EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(asset); return existing;
    }
    static string Argument(string key, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
