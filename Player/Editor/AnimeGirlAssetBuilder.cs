using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Offline conversion of the documented VRoid sample. Runtime uses ordinary Unity assets.
public static class AnimeGirlAssetBuilder
{
    const string DefaultOutput = "Assets/Script/Player/Resources/Characters/AnimeGirl";
    const string DefaultSource = "Assets/Script/Player/CharacterArt/SendagayaShino.vrm.bytes";
    static Document document;
    static byte[] binary;
    static int binaryOffset;
    static string output;
    static Transform[] nodes;
    static readonly Dictionary<string, Transform> humanBones = new Dictionary<string, Transform>();
    static Vector3[] restPositions;
    static Quaternion[] restRotations;
    static Material[] materials;
    static GameObject character;
    static float originalHeight;
    static Vector3 leftFootRest, rightFootRest, hipsRest;

    [MenuItem("Tools/Character/Rebuild Blonde Anime Girl")]
    public static void Build()
    {
        output = Argument("-animeOutput", DefaultOutput);
        string source = Argument("-animeSource", DefaultSource);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(output + "/Textures");
        Directory.CreateDirectory(output + "/Materials");
        Directory.CreateDirectory(output + "/Meshes");
        Directory.CreateDirectory(output + "/Animations");
        binary = File.ReadAllBytes(source);
        if (BitConverter.ToUInt32(binary, 0) != 0x46546C67)
            throw new InvalidDataException("Expected the documented GLB/VRM source.");
        int jsonLength = BitConverter.ToInt32(binary, 12);
        document = JsonUtility.FromJson<Document>(Encoding.UTF8.GetString(binary, 20, jsonLength));
        binaryOffset = 28 + jsonLength;
        character = new GameObject("Blonde Anime Girl");
        try
        {
            ImportSkeleton();
            ImportMaterials();
            ImportMeshes();
            restPositions = nodes.Select(node => node.localPosition).ToArray();
            restRotations = nodes.Select(node => node.localRotation).ToArray();
            hipsRest = Bone("hips").position;
            leftFootRest = Bone("leftFoot").position;
            rightFootRest = Bone("rightFoot").position;
            originalHeight = BoundsOf(character).max.y;
            BuildAnimations();
            BuildFirstPersonArms();
            ResetPose();
            ApplyPose("Idle", 0f);
            // Uniform scaling preserves the source model's natural head and limb proportions.
            var wrapper = new GameObject("AnimeGirlModel");
            character.transform.SetParent(wrapper.transform, false);
            character.transform.localScale = Vector3.one * (2f / originalHeight);
            PrefabUtility.SaveAsPrefabAsset(wrapper, output + "/AnimeGirlModel.prefab");
            character.transform.SetParent(null, false);
            character.transform.localScale = Vector3.one;
            UnityEngine.Object.DestroyImmediate(wrapper);
            AssetDatabase.SaveAssets();
            Validate();
            RenderPreviews();
            Debug.Log("ANIME_GIRL_BUILD_COMPLETE " + output);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(character);
            humanBones.Clear();
        }
    }

    static string Argument(string key, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    static void ImportSkeleton()
    {
        nodes = document.nodes.Select(node => new GameObject(node.name).transform).ToArray();
        for (int index = 0; index < nodes.Length; index++)
        {
            Node source = document.nodes[index];
            nodes[index].SetParent(character.transform, false);
            nodes[index].localPosition = Vector(source.translation);
            if (source.rotation != null)
                nodes[index].localRotation = new Quaternion(-source.rotation[0], -source.rotation[1], source.rotation[2], source.rotation[3]);
            if (source.scale != null)
                nodes[index].localScale = new Vector3(source.scale[0], source.scale[1], source.scale[2]);
        }
        for (int index = 0; index < nodes.Length; index++)
            if (document.nodes[index].children != null)
                foreach (int child in document.nodes[index].children)
                    nodes[child].SetParent(nodes[index], false);
        humanBones.Clear();
        foreach (HumanBone bone in document.extensions.VRM.humanoid.humanBones)
            humanBones.Add(bone.bone, nodes[bone.node]);
    }

    static void ImportMaterials()
    {
        Shader shader = Shader.Find("GameTest/AnimeToon");
        if (shader == null) throw new InvalidOperationException("AnimeToon shader is missing.");
        var textures = new Dictionary<int, Texture2D>();
        materials = new Material[document.materials.Length + 1];
        for (int index = 0; index < document.materials.Length; index++)
        {
            MaterialInfo source = document.materials[index];
            int imageIndex = document.textures[source.pbrMetallicRoughness.baseColorTexture.index].source;
            if (!textures.TryGetValue(imageIndex, out Texture2D texture))
            {
                BufferView imageView = document.bufferViews[document.images[imageIndex].bufferView];
                var imageBytes = new byte[imageView.byteLength];
                Array.Copy(binary, binaryOffset + imageView.byteOffset, imageBytes, 0, imageBytes.Length);
                string texturePath = output + "/Textures/Texture" + imageIndex + ".png";
                File.WriteAllBytes(texturePath, imageBytes);
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                textures.Add(imageIndex, texture);
            }
            var material = new Material(shader) { name = source.name, mainTexture = texture };
            float[] factor = source.pbrMetallicRoughness.baseColorFactor;
            material.color = factor != null ? new Color(factor[0], factor[1], factor[2], factor[3]) : Color.white;
            material.SetFloat("_Cull", source.doubleSided ? 0 : 2);
            material.SetFloat("_Cutoff", source.alphaMode == "OPAQUE" ? 0 : 0.15f);
            if (source.name.Contains("HAIR"))
            {
                material.color = new Color(1f, 0.79f, 0.34f);
                material.SetFloat("_Recolor", 1);
            }
            if (source.name.Contains("FaceBrow"))
            {
                material.color = new Color(0.54f, 0.34f, 0.12f);
                material.SetFloat("_Recolor", 1);
            }
            materials[index] = SaveAsset(material, output + "/Materials/" + source.name + ".mat");
        }
        var shorts = new Material(shader) { name = "Navy Athletic Shorts", color = new Color(0.055f, 0.065f, 0.105f) };
        shorts.SetFloat("_Cutoff", 0);
        materials[materials.Length - 1] = SaveAsset(shorts, output + "/Materials/NavyAthleticShorts.mat");
    }

    static void ImportMeshes()
    {
        for (int meshIndex = 0; meshIndex < document.meshes.Length; meshIndex++)
        {
            MeshInfo source = document.meshes[meshIndex];
            string nodeName = source.name.Split('.')[0];
            int nodeIndex = Array.FindIndex(document.nodes, node => node.name == nodeName && node.mesh == meshIndex);
            if (nodeIndex < 0) throw new InvalidDataException("Missing mesh node " + nodeName);
            Skin skin = document.skins[document.nodes[nodeIndex].skin];
            Transform meshTransform = nodes[nodeIndex];
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var offsets = new Dictionary<int, int>();
            var triangles = new SortedDictionary<int, List<int>>();
            foreach (Primitive primitive in source.primitives)
            {
                // This combat outfit replaces the loose skirt with fitted opaque shorts
                // cut from the original weighted body mesh; no primitive geometry is added.
                if (document.materials[primitive.material].name.Contains("Bottoms")) continue;
                Attributes attributes = primitive.attributes;
                if (!offsets.TryGetValue(attributes.POSITION, out int vertexOffset))
                {
                    vertexOffset = vertices.Count;
                    offsets.Add(attributes.POSITION, vertexOffset);
                    int count = document.accessors[attributes.POSITION].count;
                    for (int vertex = 0; vertex < count; vertex++)
                    {
                        vertices.Add(ReadVector(attributes.POSITION, vertex));
                        normals.Add(ReadVector(attributes.NORMAL, vertex));
                        uvs.Add(new Vector2(Read(attributes.TEXCOORD_0, vertex, 0), 1f - Read(attributes.TEXCOORD_0, vertex, 1)));
                        weights.Add(new BoneWeight {
                            boneIndex0 = (int)Read(attributes.JOINTS_0, vertex, 0), boneIndex1 = (int)Read(attributes.JOINTS_0, vertex, 1),
                            boneIndex2 = (int)Read(attributes.JOINTS_0, vertex, 2), boneIndex3 = (int)Read(attributes.JOINTS_0, vertex, 3),
                            weight0 = Read(attributes.WEIGHTS_0, vertex, 0), weight1 = Read(attributes.WEIGHTS_0, vertex, 1),
                            weight2 = Read(attributes.WEIGHTS_0, vertex, 2), weight3 = Read(attributes.WEIGHTS_0, vertex, 3)
                        });
                    }
                }
                int indexCount = document.accessors[primitive.indices].count;
                for (int triangle = 0; triangle < indexCount; triangle += 3)
                {
                    int first = vertexOffset + (int)Read(primitive.indices, triangle, 0);
                    int second = vertexOffset + (int)Read(primitive.indices, triangle + 2, 0);
                    int third = vertexOffset + (int)Read(primitive.indices, triangle + 1, 0);
                    float height = (vertices[first].y + vertices[second].y + vertices[third].y) / 3f;
                    int material = primitive.material;
                    if (nodeName == "Body" && document.materials[material].name.Contains("Body_00_SKIN") && height > 0.63f && height < 1.02f)
                        material = materials.Length - 1;
                    if (!triangles.TryGetValue(material, out List<int> indices))
                        triangles.Add(material, indices = new List<int>());
                    indices.Add(first); indices.Add(second); indices.Add(third);
                }
            }
            var mesh = new Mesh { name = nodeName, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
            mesh.boneWeights = weights.ToArray();
            Transform[] bones = skin.joints.Select(joint => nodes[joint]).ToArray();
            mesh.bindposes = bones.Select(bone => bone.worldToLocalMatrix * meshTransform.localToWorldMatrix).ToArray();
            mesh.subMeshCount = triangles.Count;
            int submesh = 0;
            foreach (List<int> indices in triangles.Values) mesh.SetTriangles(indices, submesh++);
            AddBlinkShape(mesh, source, offsets);
            mesh.RecalculateBounds();
            mesh = SaveAsset(mesh, output + "/Meshes/" + nodeName + ".asset");
            var renderer = meshTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = Bone("hips");
            renderer.sharedMaterials = triangles.Keys.Select(material => materials[material]).ToArray();
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = new Bounds(new Vector3(0, 0.8f, 0), new Vector3(3, 3, 3));
            Debug.Log("Imported " + nodeName + ": " + vertices.Count + " vertices, " + triangles.Count + " materials");
        }
    }

    static void AddBlinkShape(Mesh mesh, MeshInfo source, Dictionary<int, int> offsets)
    {
        var deltas = new Vector3[mesh.vertexCount];
        var normalDeltas = new Vector3[mesh.vertexCount];
        var processed = new HashSet<int>();
        bool found = false;
        foreach (Primitive primitive in source.primitives)
        {
            if (primitive.extras == null || primitive.extras.targetNames == null || primitive.targets == null) continue;
            int blink = Array.FindIndex(primitive.extras.targetNames, name => name.EndsWith("_EYE_Close", StringComparison.Ordinal));
            if (blink < 0 || !processed.Add(primitive.attributes.POSITION)) continue;
            int offset = offsets[primitive.attributes.POSITION];
            int count = document.accessors[primitive.attributes.POSITION].count;
            for (int vertex = 0; vertex < count; vertex++)
            {
                deltas[offset + vertex] = ReadVector(primitive.targets[blink].POSITION, vertex);
                normalDeltas[offset + vertex] = ReadVector(primitive.targets[blink].NORMAL, vertex);
            }
            found = true;
        }
        if (found) mesh.AddBlendShapeFrame("Blink", 100, deltas, normalDeltas, null);
    }

    static float Read(int accessorIndex, int element, int component)
    {
        Accessor accessor = document.accessors[accessorIndex];
        BufferView view = document.bufferViews[accessor.bufferView];
        int bytes = accessor.componentType == 5126 || accessor.componentType == 5125 ? 4 : accessor.componentType == 5123 ? 2 : 1;
        int width = accessor.type == "VEC3" ? 3 : accessor.type == "VEC4" ? 4 : accessor.type == "VEC2" ? 2 : 1;
        int stride = view.byteStride > 0 ? view.byteStride : bytes * width;
        int location = binaryOffset + view.byteOffset + accessor.byteOffset + element * stride + component * bytes;
        switch (accessor.componentType)
        {
            case 5126: return BitConverter.ToSingle(binary, location);
            case 5125: return BitConverter.ToUInt32(binary, location);
            case 5123: return BitConverter.ToUInt16(binary, location) / (accessor.normalized ? 65535f : 1f);
            case 5121: return binary[location] / (accessor.normalized ? 255f : 1f);
            default: throw new InvalidDataException("Unsupported accessor component " + accessor.componentType);
        }
    }

    static Vector3 ReadVector(int accessor, int index) => new Vector3(Read(accessor, index, 0), Read(accessor, index, 1), -Read(accessor, index, 2));
    static Vector3 Vector(float[] value) => value == null ? Vector3.zero : new Vector3(value[0], value[1], -value[2]);
    static Transform Bone(string name) => humanBones[name];

    static T SaveAsset<T>(T asset, string path) where T : UnityEngine.Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing == null) { AssetDatabase.CreateAsset(asset, path); return asset; }
        EditorUtility.CopySerialized(asset, existing);
        UnityEngine.Object.DestroyImmediate(asset);
        return existing;
    }

    static void ResetPose()
    {
        for (int index = 0; index < nodes.Length; index++)
        {
            nodes[index].localPosition = restPositions[index];
            nodes[index].localRotation = restRotations[index];
        }
    }

    static void BuildAnimations()
    {
        var animation = character.AddComponent<Animation>();
        animation.playAutomatically = false;
        foreach (string name in new[] { "Idle", "Walk", "Run", "Jump", "Fall", "Crouch", "CrouchWalk", "Slide", "Death" })
        {
            float duration = name == "Run" ? 0.65f : name == "Walk" ? 1f : name == "Idle" ? 3f : 1.2f;
            bool loop = name == "Idle" || name == "Walk" || name == "Run" || name == "Crouch" || name == "CrouchWalk";
            var clip = new AnimationClip { name = name, legacy = true, frameRate = 30, wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever };
            Transform[] animated = nodes.Where(node => !node.TryGetComponent<Renderer>(out _)).ToArray();
            var curves = new AnimationCurve[animated.Length, 7];
            for (int bone = 0; bone < animated.Length; bone++)
                for (int channel = 0; channel < 7; channel++) curves[bone, channel] = new AnimationCurve();
            int frameCount = Mathf.CeilToInt(duration * 30);
            for (int frame = 0; frame <= frameCount; frame++)
            {
                float phase = (float)frame / frameCount;
                ResetPose();
                ApplyPose(name, phase);
                for (int bone = 0; bone < animated.Length; bone++)
                {
                    Vector3 position = animated[bone].localPosition;
                    Quaternion rotation = animated[bone].localRotation;
                    float[] values = { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w };
                    for (int channel = 0; channel < 7; channel++) curves[bone, channel].AddKey(phase * duration, values[channel]);
                }
            }
            string[] properties = { "localPosition.x", "localPosition.y", "localPosition.z", "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
            for (int bone = 0; bone < animated.Length; bone++)
                for (int channel = 0; channel < 7; channel++)
                {
                    AnimationCurve curve = curves[bone, channel];
                    int nodeIndex = Array.IndexOf(nodes, animated[bone]);
                    float restValue = channel < 3 ? restPositions[nodeIndex][channel] : restRotations[nodeIndex][channel - 3];
                    // Retain neutral rotations in every clip so crossfades cannot leave a
                    // joint stuck in the previous pose. Only hips translation is animated.
                    if (channel < 3 && animated[bone] != Bone("hips") && curve.keys.All(key => Mathf.Abs(key.value - restValue) < 0.000001f)) continue;
                    if (curve.keys.All(key => Mathf.Abs(key.value - curve.keys[0].value) < 0.000001f))
                        curve = AnimationCurve.Constant(0, duration, curve.keys[0].value);
                    clip.SetCurve(AnimationUtility.CalculateTransformPath(animated[bone], character.transform), typeof(Transform), properties[channel], curve);
                }
            clip.EnsureQuaternionContinuity();
            clip = SaveAsset(clip, output + "/Animations/" + name + ".anim");
            animation.AddClip(clip, name);
            if (name == "Idle") animation.clip = clip;
        }
    }

    static void ApplyPose(string state, float phase)
    {
        float cycle = Mathf.Sin(phase * Mathf.PI * 2);
        bool running = state == "Run";
        bool walking = state == "Walk" || state == "CrouchWalk";
        bool crouching = state == "Crouch" || state == "CrouchWalk";
        bool sliding = state == "Slide";
        bool airborne = state == "Jump" || state == "Fall";
        float crouchDrop = crouching ? 0.39f : sliding ? 0.67f : 0f;
        float bounce = running ? 0.025f * Mathf.Cos(phase * 4 * Mathf.PI) : walking ? 0.01f * Mathf.Cos(phase * 4 * Mathf.PI) : 0.004f * cycle;
        Bone("hips").position = hipsRest + new Vector3(0, -crouchDrop + bounce - 0.025f, crouching ? -0.08f : 0);
        Bone("hips").rotation = Quaternion.Euler(sliding ? -45 : crouching ? 22 : running ? 9 : 0, (walking || running) ? cycle * 3 : 0, 0);
        Bone("chest").rotation = Quaternion.Euler(sliding ? -30 : crouching ? 25 : running ? 12 : 2, 0, 0);
        Bone("head").rotation = Quaternion.Euler(sliding ? -5 : crouching ? 5 : 0, 0, 0);
        foreach (string side in new[] { "left", "right" })
        {
            float sign = side == "left" ? -1f : 1f;
            float stride = cycle * sign;
            Vector3 foot = side == "left" ? leftFootRest : rightFootRest;
            if (running || walking)
            {
                foot.z += stride * (running ? 0.36f : crouching ? 0.12f : 0.24f);
                foot.y += Mathf.Max(0, Mathf.Cos(phase * 2 * Mathf.PI) * sign) * (running ? 0.18f : 0.07f);
            }
            if (crouching) foot.x += sign * 0.035f;
            if (sliding) foot += new Vector3(sign * 0.05f, 0.02f, side == "left" ? 0.66f : 0.42f);
            if (airborne) foot += new Vector3(sign * 0.02f, state == "Jump" ? 0.2f : 0.08f, sign * 0.1f);
            SolveLimb(Bone(side + "UpperLeg"), Bone(side + "LowerLeg"), Bone(side + "Foot"), foot, Vector3.forward);
            Bone(side + "Foot").rotation = Quaternion.Euler(sliding ? -12 : 0, sign * 5, 0);

            Transform upperArm = Bone(side + "UpperArm");
            Transform lowerArm = Bone(side + "LowerArm");
            Transform hand = Bone(side + "Hand");
            Vector3 shoulder = upperArm.position;
            Vector3 elbowDirection = new Vector3(sign * 0.055f, -0.22f, (running || walking) ? -stride * 0.14f : crouching ? 0.01f : -0.005f);
            Aim(upperArm, lowerArm, shoulder + elbowDirection);
            Vector3 handDirection = new Vector3(sign * 0.025f, running ? -0.06f : -0.19f, running ? 0.20f : crouching ? 0.12f : 0.045f);
            if (sliding) handDirection = new Vector3(sign * 0.07f, -0.15f, -0.12f);
            if (airborne) handDirection = new Vector3(sign * 0.08f, -0.12f, 0.1f);
            Aim(lowerArm, hand, lowerArm.position + handDirection);
            // Match the wrist to the forearm; retain the source model's relaxed finger layout.
            hand.localRotation = Quaternion.identity;
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
                foreach (string segment in new[] { "Proximal", "Intermediate", "Distal" })
                    Bone(side + finger + segment).localRotation = Quaternion.Euler(0, 0, -sign * (running ? 45 : 15));
        }
        // The weighted long hair follows movement gently.
        foreach (Transform node in nodes)
        {
            if (node.name.StartsWith("HairJoint", StringComparison.Ordinal) && node.parent != null && !node.parent.name.StartsWith("HairJoint", StringComparison.Ordinal))
                node.localRotation *= Quaternion.Euler(running ? 8 + cycle * 2 : 0, cycle * 1.2f, 0);
        }
        if (state == "Death")
        {
            float fall = Mathf.SmoothStep(0, 1, Mathf.Clamp01(phase * 1.6f));
            Transform hips = Bone("hips");
            hips.rotation = Quaternion.Euler(-85 * fall, 0, 10 * fall);
            hips.position = Vector3.Lerp(hipsRest, new Vector3(0, 0.18f, -0.15f), fall);
        }
    }

    static void Aim(Transform start, Transform end, Vector3 target)
    {
        start.rotation = Quaternion.FromToRotation(end.position - start.position, target - start.position) * start.rotation;
    }

    static void SolveLimb(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
    {
        float upperLength = Vector3.Distance(upper.position, lower.position);
        float lowerLength = Vector3.Distance(lower.position, end.position);
        Vector3 direction = (target - upper.position).normalized;
        float distance = Mathf.Clamp(Vector3.Distance(upper.position, target), Mathf.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2 * distance);
        float bend = Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
        Vector3 bendDirection = Vector3.ProjectOnPlane(pole, direction).normalized;
        Vector3 joint = upper.position + direction * along + bendDirection * bend;
        Aim(upper, lower, joint);
        Aim(lower, end, upper.position + direction * distance);
    }

    static void BuildFirstPersonArms()
    {
        ResetPose();
        foreach (string side in new[] { "left", "right" })
        {
            float sign = side == "left" ? -1f : 1f;
            Transform upper = Bone(side + "UpperArm"), lower = Bone(side + "LowerArm"), hand = Bone(side + "Hand");
            upper.position = side == "left" ? new Vector3(-0.16f, -0.36f, 0.32f) : new Vector3(0.25f, -0.4f, 0.12f);
            Vector3 grip = side == "left" ? new Vector3(0.15f, -0.22f, 0.61f) : new Vector3(0.28f, -0.27f, 0.46f);
            SolveLimb(upper, lower, hand, grip, new Vector3(sign, -1, 0));
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Little" })
                foreach (string segment in new[] { "Proximal", "Intermediate", "Distal" })
                    Bone(side + finger + segment).localRotation = Quaternion.Euler(0, 0, -sign * (segment == "Intermediate" ? 80f : 65f));
        }
        SkinnedMeshRenderer body = character.GetComponentsInChildren<SkinnedMeshRenderer>().First(renderer => renderer.name == "Body");
        Mesh original = body.sharedMesh;
        var baked = new Mesh();
        body.BakeMesh(baked);
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
        var remap = new Dictionary<int, int>(); var submeshes = new List<List<int>>(); var armMaterials = new List<Material>();
        Vector3[] positions = baked.vertices, sourceNormals = baked.normals;
        Vector2[] sourceUvs = baked.uv; BoneWeight[] weights = original.boneWeights;
        bool IsArm(int index)
        {
            string name = body.bones[index].name;
            return name.Contains("Arm") || name.Contains("Hand") || name.Contains("Thumb") || name.Contains("Index") || name.Contains("Middle") || name.Contains("Ring") || name.Contains("Little");
        }
        bool Keep(int vertex)
        {
            BoneWeight weight = weights[vertex];
            return (IsArm(weight.boneIndex0) ? weight.weight0 : 0) + (IsArm(weight.boneIndex1) ? weight.weight1 : 0) +
                   (IsArm(weight.boneIndex2) ? weight.weight2 : 0) + (IsArm(weight.boneIndex3) ? weight.weight3 : 0) > 0.85f;
        }
        int Vertex(int old)
        {
            if (remap.TryGetValue(old, out int mapped)) return mapped;
            mapped = vertices.Count; remap.Add(old, mapped);
            vertices.Add(body.transform.TransformPoint(positions[old]));
            normals.Add(body.transform.TransformDirection(sourceNormals[old])); uvs.Add(sourceUvs[old]); return mapped;
        }
        for (int submesh = 0; submesh < original.subMeshCount; submesh++)
        {
            int[] triangles = original.GetTriangles(submesh); var selected = new List<int>();
            for (int index = 0; index < triangles.Length; index += 3)
                if (Keep(triangles[index]) && Keep(triangles[index + 1]) && Keep(triangles[index + 2]))
                    for (int corner = 0; corner < 3; corner++) selected.Add(Vertex(triangles[index + corner]));
            if (selected.Count == 0) continue;
            submeshes.Add(selected); armMaterials.Add(body.sharedMaterials[submesh]);
        }
        var mesh = new Mesh { name = "AnimeGirlArms", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.subMeshCount = submeshes.Count;
        for (int index = 0; index < submeshes.Count; index++) mesh.SetTriangles(submeshes[index], index);
        mesh.RecalculateBounds(); mesh = SaveAsset(mesh, output + "/Meshes/FirstPersonArms.asset");
        var arms = new GameObject("AnimeGirlArms", typeof(MeshFilter), typeof(MeshRenderer));
        arms.GetComponent<MeshFilter>().sharedMesh = mesh;
        arms.GetComponent<MeshRenderer>().sharedMaterials = armMaterials.ToArray();
        arms.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        PrefabUtility.SaveAsPrefabAsset(arms, output + "/AnimeGirlArms.prefab");
        UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(arms);
    }

    static Bounds BoundsOf(GameObject model)
    {
        bool first = true; Bounds bounds = new Bounds();
        foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // Compensate for the renderer's scale before TransformPoint applies it.
            var mesh = new Mesh(); renderer.BakeMesh(mesh, true);
            foreach (Vector3 vertex in mesh.vertices)
            {
                Vector3 point = renderer.transform.TransformPoint(vertex);
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
            }
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        return bounds;
    }

    static void Validate()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(output + "/AnimeGirlModel.prefab");
        if (prefab == null || prefab.GetComponentsInChildren<SkinnedMeshRenderer>().Length != 3) throw new InvalidDataException("Incomplete character prefab.");
        Animation animation = prefab.GetComponentInChildren<Animation>();
        foreach (string clip in new[] { "Idle", "Walk", "Run", "Jump", "Fall", "Crouch", "CrouchWalk", "Slide", "Death" })
            if (animation[clip] == null) throw new InvalidDataException("Missing animation " + clip);
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>())
            if (renderer.sharedMaterials.Any(material => material == null || material.shader == null)) throw new InvalidDataException("Missing character material.");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            Animation loadedAnimation = instance.GetComponentInChildren<Animation>();
            foreach (string name in new[] { "Idle", "Run", "Crouch", "Slide", "Walk", "Jump", "Fall", "Death", "Idle" })
            {
                AnimationClip clip = loadedAnimation[name].clip;
                clip.SampleAnimation(loadedAnimation.gameObject, clip.length * 0.25f);
                Bounds bounds = BoundsOf(instance);
                if (float.IsNaN(bounds.size.y) || bounds.size.y < 0.3f || bounds.size.y > 2.5f)
                    throw new InvalidDataException("Invalid bounds for " + name + ": " + bounds);
                Debug.Log("ANIME_POSE " + name + " height=" + bounds.size.y.ToString("F3"));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        Debug.Log("ANIME_VALIDATION_OK: 3 skinned meshes, 9 skeletal clips, complete materials; original height " + originalHeight);
    }

    static void RenderPreviews()
    {
        string reviewPath = Argument("-animeReview", "Temp/AnimeGirlReview");
        Directory.CreateDirectory(reviewPath);
        foreach (string pose in new[] { "Idle", "Run", "Crouch", "Slide" })
        {
            ResetPose();
            AnimationClip clip = character.GetComponent<Animation>()[pose].clip;
            clip.SampleAnimation(character, clip.length * 0.25f);
            var snapshots = new List<GameObject>();
            foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh(); renderer.BakeMesh(mesh);
                var snapshot = new GameObject("PreviewMesh", typeof(MeshFilter), typeof(MeshRenderer));
                snapshot.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                snapshot.transform.localScale = renderer.transform.lossyScale;
                snapshot.GetComponent<MeshFilter>().sharedMesh = mesh;
                snapshot.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                snapshots.Add(snapshot); renderer.enabled = false;
            }
            var cameraObject = new GameObject("ReviewCamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>(); camera.orthographic = true;
            camera.orthographicSize = originalHeight * 0.59f; camera.nearClipPlane = 0.01f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.16f, 0.22f);
            Vector3 center = new Vector3(0, originalHeight * 0.49f, 0);
            camera.transform.position = center + new Vector3(0.9f, 0.25f, 3.5f);
            camera.transform.LookAt(center);
            var target = new RenderTexture(900, 1100, 24); camera.targetTexture = target; camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(900, 1100, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 900, 1100), 0, 0); image.Apply();
            File.WriteAllBytes(reviewPath + "/" + pose + ".png", image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
            foreach (GameObject snapshot in snapshots) { UnityEngine.Object.DestroyImmediate(snapshot.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(snapshot); }
            foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>()) renderer.enabled = true;
        }
        foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        GameObject arms = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(output + "/AnimeGirlArms.prefab"));
        var view = new GameObject("ArmsReviewCamera", typeof(Camera));
        Camera armsCamera = view.GetComponent<Camera>();
        armsCamera.fieldOfView = 60; armsCamera.nearClipPlane = 0.3f;
        armsCamera.clearFlags = CameraClearFlags.SolidColor;
        armsCamera.backgroundColor = new Color(0.12f, 0.16f, 0.22f);
        var armsTarget = new RenderTexture(1280, 720, 24);
        armsCamera.targetTexture = armsTarget; armsCamera.Render();
        RenderTexture previousTarget = RenderTexture.active;
        RenderTexture.active = armsTarget;
        var armsImage = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        armsImage.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); armsImage.Apply();
        File.WriteAllBytes(reviewPath + "/FirstPersonArms.png", armsImage.EncodeToPNG());
        RenderTexture.active = previousTarget; armsCamera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(armsImage); UnityEngine.Object.DestroyImmediate(armsTarget);
        UnityEngine.Object.DestroyImmediate(view); UnityEngine.Object.DestroyImmediate(arms);
    }

    #pragma warning disable 0649
    [Serializable] class Document { public Node[] nodes; public MeshInfo[] meshes; public Skin[] skins; public Accessor[] accessors; public BufferView[] bufferViews; public MaterialInfo[] materials; public ImageInfo[] images; public TextureInfo[] textures; public Extensions extensions; }
    [Serializable] class Node { public string name; public float[] translation, rotation, scale; public int[] children; public int mesh, skin; }
    [Serializable] class Skin { public int[] joints; }
    [Serializable] class MeshInfo { public string name; public Primitive[] primitives; }
    [Serializable] class Primitive { public Attributes attributes; public int indices, material; public Attributes[] targets; public Extras extras; }
    [Serializable] class Extras { public string[] targetNames; }
    [Serializable] class Attributes { public int POSITION, NORMAL, TEXCOORD_0, JOINTS_0, WEIGHTS_0; }
    [Serializable] class Accessor { public int bufferView, byteOffset, componentType, count; public string type; public bool normalized; }
    [Serializable] class BufferView { public int byteOffset, byteLength, byteStride; }
    [Serializable] class MaterialInfo { public string name, alphaMode; public bool doubleSided; public Pbr pbrMetallicRoughness; }
    [Serializable] class Pbr { public TextureIndex baseColorTexture; public float[] baseColorFactor; }
    [Serializable] class TextureIndex { public int index; }
    [Serializable] class TextureInfo { public int source; }
    [Serializable] class ImageInfo { public int bufferView; }
    [Serializable] class Extensions { public Vrm VRM; }
    [Serializable] class Vrm { public Humanoid humanoid; }
    [Serializable] class Humanoid { public HumanBone[] humanBones; }
    [Serializable] class HumanBone { public string bone; public int node; }
    #pragma warning restore 0649
}
