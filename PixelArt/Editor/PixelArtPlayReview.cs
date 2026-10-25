using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Runs presentation integration checks in a new, isolated Play Mode scene.</summary>
[InitializeOnLoad]
public static class PixelArtPlayReview
{
    const string Key = "PixelArtPlayReview.Pending";
    static Camera first, third, external;
    static RenderTexture externalTarget;
    static GameObject guard, bossNode, floor, savedFloor;
    static Material floorMaterial, savedMapMaterial;
    static BossController boss;
    static Terrain terrain;
    static TerrainData terrainSource;
    static Texture2D terrainTexture;
    static int stage, assertions;
    static double deadline;
    static int minimumFrame;

    static PixelArtPlayReview() { EditorApplication.playModeStateChanged += StateChanged; }

    public static void Run()
    {
        PixelArtReview.Run();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        stage = assertions = 0;
        minimumFrame = Time.frameCount;
        deadline = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline || Time.frameCount < minimumFrame) return;
        try
        {
            if (stage == 0)
            {
                third = new GameObject("Third Person", typeof(Camera)).GetComponent<Camera>();
                third.transform.position = new Vector3(0, 2, -6);
                first = new GameObject("First Person", typeof(Camera)).GetComponent<Camera>();
                first.gameObject.SetActive(false);
                external = new GameObject("External Texture Camera", typeof(Camera)).GetComponent<Camera>();
                externalTarget = new RenderTexture(64, 64, 16); external.targetTexture = externalTarget;
                guard = Enemy("Guard", 30, false); bossNode = Enemy("Boss", 100, true);
                boss = bossNode.GetComponent<BossController>();
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Dynamic Stone Floor";
                floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "StoneWall" };
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                savedMapMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/MedievalDungeon/Generated_20260914_214258/Materials/Flagstone.mat");
                Check(savedMapMaterial != null && savedMapMaterial.shader.name == "GameTest/PixelSurface",
                    "Map1 floor is saved as a pixel material before runtime scanning");
                savedFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                savedFloor.GetComponent<Renderer>().sharedMaterial = savedMapMaterial;
                savedFloor.transform.position = Vector3.right * 5;
                var canvas = new GameObject("UI", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var button = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(canvas.transform, false);
                terrainSource = new TerrainData { heightmapResolution = 33, size = new Vector3(10, 2, 10) };
                terrainTexture = new Texture2D(4, 4); terrainTexture.SetPixels(new Color[16]); terrainTexture.Apply();
                terrainSource.terrainLayers = new[] { new TerrainLayer { diffuseTexture = terrainTexture } };
                terrain = Terrain.CreateTerrainGameObject(terrainSource).GetComponent<Terrain>();
                terrain.transform.position = Vector3.right * 30;
                deadline = EditorApplication.timeSinceStartup + 1.6;
            }
            else if (stage == 1)
            {
                Check(third.targetTexture != null && third.targetTexture.filterMode == FilterMode.Point, "Main camera pixel render target");
                CheckScreenOutput(third);
                Check(third.GetComponent<UniversalAdditionalCameraData>() == null || !third.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing, "Post-processing disabled");
                Check(external.targetTexture == externalTarget && external.GetComponent<PixelArtCamera>() == null, "Application-owned camera target preserved");
                Check(guard.transform.Find("NPC Appearance") != null, "Spawned enemy pixel appearance");
                Check(guard.GetComponentsInChildren<Collider>().Length == 1, "Original enemy collision shape only");
                Check(guard.GetComponent<EnemyHealth>().EnemyLevel == 30, "Enemy level retained");
                Check(floor.GetComponent<Renderer>().sharedMaterial == floorMaterial && floorMaterial.shader.name == "Universal Render Pipeline/Lit",
                    "Runtime presentation honors assigned scenery materials without replacing them");
                Check(savedFloor.GetComponent<Renderer>().sharedMaterial == savedMapMaterial && EditorUtility.IsPersistent(savedMapMaterial),
                    "Map floor keeps its saved material asset without a runtime clone");
                Check(terrain.terrainData == terrainSource && terrain.GetComponent<PixelTerrainStyle>() == null,
                    "Runtime presentation does not replace terrain data or attach a texture converter");
                Check(terrainSource.terrainLayers[0].diffuseTexture == terrainTexture && terrainTexture.width == 4, "Terrain source unchanged");
                Check(terrain.GetComponent<TerrainCollider>().terrainData == terrain.terrainData, "Terrain collision uses matching data");
                Check(UnityEngine.Object.FindFirstObjectByType<Button>().transform.Find("Theme Frame") != null, "Runtime UI frame");
                third.gameObject.SetActive(false); first.gameObject.SetActive(true);
                Time.timeScale = 0;
                boss.RestoreProgress(2);
                deadline = EditorApplication.timeSinceStartup + .35;
            }
            else if (stage == 2)
            {
                Check(third.targetTexture == null && third.transform.Find("Pixel World Display") == null, "Inactive camera releases output");
                Check(first.targetTexture != null && first.transform.Find("Pixel World Display") != null, "First-person output while paused");
                CheckScreenOutput(first);
                Check(third.transform.Find("Pixel Screen Output") == null, "Inactive view removes its screen camera");
                Check(first.transform.Find("Pixel World Display/World Pixels").GetComponent<RawImage>().raycastTarget == false, "World display cannot intercept UI input");
                Check(first.transform.Find("Pixel World Display").GetComponent<Canvas>().sortingOrder == short.MinValue, "World output behind HUD");
                bool redArmor = false;
                foreach (Renderer renderer in bossNode.GetComponentsInChildren<Renderer>())
                {
                    Material[] mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i].name != "BossSteel") continue;
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, i);
                        redArmor |= block.GetColor("_Color").r > .4f;
                    }
                }
                Check(redArmor, "Boss second-phase armor palette");
                Check(!bossNode.transform.Find("Boss Appearance/Hips/Chest/LeftArm/Hand/Shield").gameObject.activeSelf, "Boss second-phase shield removed");
                boss.RestoreProgress(3);
                first.gameObject.SetActive(false); third.gameObject.SetActive(true);
                deadline = EditorApplication.timeSinceStartup + .35;
            }
            else
            {
                Check(bossNode.transform.Find("Boss Appearance/深淵光環") != null, "Third-phase pixel halo");
                Check(first.targetTexture == null && third.targetTexture != null, "Switch back to third person");
                CheckScreenOutput(third);
                Check(first.transform.Find("Pixel Screen Output") == null, "First-person screen camera cleaned up after switch");
                Check(bossNode.GetComponentsInChildren<Collider>().Length == 1, "Boss visual adds no collisions");
                Check(savedFloor.GetComponent<Renderer>().sharedMaterial == savedMapMaterial &&
                    floor.GetComponent<Renderer>().sharedMaterial == floorMaterial,
                    "Scenery material assignments survive paused scans and camera switching");
                Debug.Log("PIXEL_ART_PLAY_PASS: " + assertions + " integration checks");
                Finish(0); return;
            }
            stage++;
            // Editor startup/import work can consume the wall-clock delay before a game
            // frame runs. Give Start/LateUpdate and deferred destruction actual frames.
            minimumFrame = Time.frameCount + 3;
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    static GameObject Enemy(string name, int level, bool isBoss)
    {
        var node = new GameObject(name, typeof(CapsuleCollider), typeof(EnemyHealth));
        node.GetComponent<CapsuleCollider>().height = 2;
        EnemyHealth health = node.GetComponent<EnemyHealth>(); health.SetLevelAndExperience(level, 1); health.SetMaxHealth(100);
        if (isBoss) node.AddComponent<BossController>().Initialize(null, health, null);
        EnemyCharacterVisual.Ensure(node);
        return node;
    }

    static void Check(bool success, string message)
    {
        assertions++;
        if (!success) throw new InvalidOperationException("Pixel integration failed: " + message);
    }

    static void CheckScreenOutput(Camera owner)
    {
        Transform node = owner.transform.Find("Pixel Screen Output");
        Check(node != null, "Active view owns a display camera");
        Camera output = node.GetComponent<Camera>();
        Check(output.isActiveAndEnabled && output.targetTexture == null && output.targetDisplay == owner.targetDisplay,
            "Enabled camera renders to the active display");
        Check(output.cullingMask == 0 && output.GetComponent<PixelArtCamera>() == null,
            "Display camera does not render scenery or acquire pixel rendering");
        int count = 0;
        foreach (Camera camera in Camera.allCameras)
            if (camera.targetTexture == null && camera.targetDisplay == owner.targetDisplay) count++;
        Check(count == 1, "Exactly one display camera after scanning and switching");
    }

    static void Finish(int exit)
    {
        Time.timeScale = 1;
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(exit); else EditorApplication.ExitPlaymode();
    }
}
