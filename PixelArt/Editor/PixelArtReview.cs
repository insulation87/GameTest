using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>Repeatable geometry/animation/shader checks and actual Unity preview renders.</summary>
public static class PixelArtReview
{
    [MenuItem("Tools/Pixel Art/Validate and Render Previews")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Pixel Review");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        string output = Path.GetFullPath("Assets/Script/PixelArt/Previews");
        Directory.CreateDirectory(output);
        int assertions = 0;
        try
        {
            Shader shader = Resources.Load<Shader>("PixelSurface");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Pixel surface shader", ref assertions);
            Shader itemShader = Resources.Load<Shader>("ItemSurface");
            Require(itemShader != null && !ShaderUtil.ShaderHasError(itemShader), "Item shader", ref assertions);
            string[] appearances = { "Player", "Npc1", "Npc2", "Npc3", "Boss" };
            for (int i = 0; i < appearances.Length; i++)
            {
                GameObject character = PixelCharacterFactory.Create(root.transform, appearances[i]);
                character.transform.localPosition = new Vector3((i - 2) * 1.45f, 0, 0);
                character.transform.localRotation = Quaternion.Euler(0, -15, 0);
                Require(character.GetComponentsInChildren<Collider>().Length == 0, appearances[i] + " has no physics", ref assertions);
                Animation animation = character.GetComponent<Animation>();
                foreach (string clip in new[] { "Idle", "Walk", "Run", "Jump", "Fall", "Crouch", "CrouchWalk", "Slide", "Death", "Attack", "Shoot", "Cast" })
                    Require(animation[clip] != null && animation[clip].clip.legacy, appearances[i] + " " + clip, ref assertions);
                animation["Walk"].clip.SampleAnimation(character, .25f);
                Require(Mathf.Abs(character.transform.Find("Hips/LeftLeg").localRotation.x) > .05f, "Walk rotates leg", ref assertions);
                animation["Crouch"].clip.SampleAnimation(character, .2f);
                Require(character.transform.Find("Hips").localPosition.y < .7f, "Crouch lowers body", ref assertions);
                animation["Idle"].clip.SampleAnimation(character, 0);
                foreach (Renderer renderer in character.GetComponentsInChildren<Renderer>())
                    Require(renderer.sharedMaterial != null && renderer.sharedMaterial.shader == shader, "Character pixel material", ref assertions);
                if (appearances[i] != "Player")
                {
                    Require(character.transform.Find("Hips/Chest/RightArm/Hand/Crossbow/BoltMuzzle") != null, "Ranged muzzle preserved", ref assertions);
                    character.transform.Find("Hips/Chest/RightArm/Hand/Crossbow").gameObject.SetActive(false);
                }
            }
            for (int item = 0; item <= ItemAppearance.ChestFragment; item++)
            {
                Mesh mesh = ItemAppearance.GetMesh(item);
                Require(mesh.vertexCount > 0 && mesh.bounds.size.sqrMagnitude > 0, "Item " + item, ref assertions);
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int j = 0; j < triangles.Length; j += 3)
                    Require(Vector3.Cross(vertices[triangles[j+1]]-vertices[triangles[j]], vertices[triangles[j+2]]-vertices[triangles[j]]).sqrMagnitude > 1e-15f,
                        "Non-degenerate item triangle", ref assertions);
                GameObject model = ItemAppearance.Create(item, root.transform);
                model.transform.localPosition = new Vector3((item - 4.5f) * .9f, .28f, -1.2f);
                model.transform.localRotation = Quaternion.Euler(0, 70, 0);
                model.transform.localScale = Vector3.one * (item >= 8 ? .32f : .65f);
            }
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0, -.15f, 0);
            floor.transform.localScale = new Vector3(16, .25f, 8);
            var floorMaterial = new Material(shader);
            floorMaterial.SetColor("_Color", new Color(.35f, .43f, .51f));
            floorMaterial.SetFloat("_WorldPattern", 1);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var cameraNode = new GameObject("Review Camera", typeof(Camera));
            cameraNode.transform.SetParent(root.transform, false);
            Camera camera = cameraNode.GetComponent<Camera>();
            camera.scene = scene;
            camera.transform.position = new Vector3(0, 4.1f, 9.3f);
            camera.transform.LookAt(new Vector3(0, .9f, -.3f));
            camera.fieldOfView = 43;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .055f, .085f);
            camera.allowHDR = camera.allowMSAA = false;
            Render(camera, output + "/Lineup.png");
            foreach (Transform child in root.transform) if (child != camera.transform) child.gameObject.SetActive(false);
            var cover = new GameObject("Cover", typeof(RectTransform), typeof(Canvas));
            cover.transform.SetParent(root.transform, false);
            Canvas canvas = cover.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            cover.GetComponent<RectTransform>().sizeDelta = new Vector2(640, 360);
            cover.transform.localScale = Vector3.one * .01f;
            camera.transform.SetPositionAndRotation(new Vector3(0, 0, -10), Quaternion.identity);
            camera.orthographic = true; camera.orthographicSize = 1.8f;
            var art = new GameObject("Art", typeof(RectTransform), typeof(PixelMenuBackdrop));
            art.transform.SetParent(cover.transform, false);
            GameUITheme.Stretch(art.GetComponent<RectTransform>());
            Canvas.ForceUpdateCanvases();
            // Preview scenes do not submit Canvas renderers to Camera.Render in URP.
            // Render the actual graphic's generated vertices as a mesh in this isolated scene.
            var artMesh = new Mesh();
            using (var vertices = new VertexHelper())
            {
                typeof(PixelMenuBackdrop).GetMethod("OnPopulateMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, new[] { typeof(VertexHelper) }, null)
                    .Invoke(art.GetComponent<PixelMenuBackdrop>(), new object[] { vertices });
                vertices.FillMesh(artMesh);
            }
            var artNode = new GameObject("Pixel Art Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            artNode.transform.SetParent(root.transform, false); artNode.transform.localScale = Vector3.one * .01f;
            artNode.GetComponent<MeshFilter>().sharedMesh = artMesh;
            var artMaterial = new Material(Shader.Find("UI/Default"));
            artMaterial.mainTexture = Texture2D.whiteTexture;
            artNode.GetComponent<MeshRenderer>().sharedMaterial = artMaterial;
            cover.SetActive(false);
            Render(camera, output + "/Menu.png");
            UnityEngine.Object.DestroyImmediate(artMaterial); UnityEngine.Object.DestroyImmediate(artMesh);
            UnityEngine.Object.DestroyImmediate(floorMaterial);
            Debug.Log("PIXEL_ART_REVIEW_PASS: " + assertions + " assertions; renders at " + output);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void Render(Camera camera, string path)
    {
        var target = new RenderTexture(640, 360, 24) { filterMode = FilterMode.Point, antiAliasing = 1 };
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.aspect = 16f / 9;
            camera.Render();
            RenderTexture.active = target;
            var capture = new Texture2D(640, 360, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); capture.Apply();
            Color32[] pixels = capture.GetPixels32();
            bool hasArt = false;
            for (int i = 0; i < pixels.Length; i += 101)
                if (!pixels[i].Equals(pixels[0])) { hasArt = true; break; }
            if (!hasArt) throw new InvalidOperationException("Preview rendered an empty frame: " + path);
            File.WriteAllBytes(path, capture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(capture);
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static void Require(bool success, string message, ref int assertions)
    {
        assertions++;
        if (!success) throw new InvalidOperationException("Pixel validation failed: " + message);
    }
}
