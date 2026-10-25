using System;
using System.Collections.Generic;
using MedievalDungeonKit;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The authored meadow/forest prefab owns geometry and navigation in Edit Mode and Play Mode.
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class OverworldDirector : MonoBehaviour
{
    public const int LayoutVersion = 1;
    public static string[] RegionNames => ExpeditionMaps.Current.Stages;
    [SerializeField] int mapTheme;
    public int ThemeIndex => mapTheme;
    public void SetThemeForAuthoring(int index) { if (!Built && !authoredWorld) mapTheme = Mathf.Clamp(index, 0, ExpeditionMaps.All.Length - 1); }
    public bool Built { get; private set; }
    public readonly Bounds[] RegionBounds = new Bounds[3];
    readonly DungeonRoomBoundary[] enemyRegions = new DungeonRoomBoundary[3];
    public int TreeCount { get; private set; }
    [SerializeField] bool authoredWorld;
    [SerializeField] int authoredTreeCount;
    [SerializeField] NavMeshData[] authoredNavigation = new NavMeshData[0];
    public bool IsAuthored => authoredWorld;
    public IReadOnlyList<NavMeshData> AuthoredNavigation => authoredNavigation;
    readonly List<Material> materials = new List<Material>();
    readonly List<Mesh> meshes = new List<Mesh>();
    readonly List<NavMeshDataInstance> navigation = new List<NavMeshDataInstance>();
    readonly List<NavMeshData> navigationData = new List<NavMeshData>();
    readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
    DungeonViewSettings map;
    Mesh cube, sphere, cylinder;
    readonly Dictionary<Material, List<CombineInstance>> batches = new Dictionary<Material, List<CombineInstance>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
    }
    static void Loaded(Scene scene, LoadSceneMode mode) => EnsureWorld();
    public static OverworldDirector EnsureWorld()
    {
        if (!Application.isPlaying) return null;
        if (FindFirstObjectByType<Player>() == null) return null;
        var settings = FindFirstObjectByType<DungeonViewSettings>();
        if (settings == null) return null;
        var world = settings.GetComponent<OverworldDirector>();
        if (world == null || !world.authoredWorld) return null;
        world.InitializeAuthoredWorld();
        return world;
    }
    void Awake()
    {
        if (Application.isPlaying && authoredWorld) InitializeAuthoredWorld();
    }
    void InitializeAuthoredWorld()
    {
        if (Built) return;
        Built = true; TreeCount = authoredTreeCount;
        RefreshRegionBounds();
        foreach (NavMeshData data in authoredNavigation)
            if (data != null) navigation.Add(NavMesh.AddNavMeshData(data, transform.position, transform.rotation));
    }
    void RefreshRegionBounds()
    {
        map = GetComponent<DungeonViewSettings>();
        GameObject[] levels = { map.level1, map.level2, map.level3 };
        for (int i = 0; i < levels.Length; i++)
            if (levels[i] != null)
                RegionBounds[i] = new Bounds(levels[i].transform.TransformPoint(new Vector3(150, 0, 150)),
                    Vector3.Scale(new Vector3(320, 2, 320), transform.lossyScale));
    }
    public DungeonRoomBoundary EnemyBoundaryAt(Vector3 position)
    {
        if (!authoredWorld || !Built || map == null) return null;
        int index = RegionAt(position) - 1;
        var bounds = RegionBounds[index];
        if (position.x < bounds.min.x || position.x > bounds.max.x ||
            position.z < bounds.min.z || position.z > bounds.max.z) return null;
        if (enemyRegions[index] != null) return enemyRegions[index];
        GameObject[] levels = { map.level1, map.level2, map.level3 };
        if (levels[index] == null) return null;
        var node = new GameObject("Enemy Pursuit Region " + (index + 1));
        node.transform.SetParent(levels[index].transform, false);
        node.transform.localPosition = new Vector3(150, 0, 150);
        var boundary = node.AddComponent<DungeonRoomBoundary>();
        boundary.halfSize = Vector2.one * 160f;
        boundary.wallInset = .45f;
        enemyRegions[index] = boundary;
        return boundary;
    }

    public static int MarkerFloor(Transform marker)
    {
        for (var t = marker; t != null; t = t.parent)
        {
            if (t.name.StartsWith("L3_", StringComparison.Ordinal) || t.name.Contains("Level_3")) return 3;
            if (t.name.StartsWith("L2_", StringComparison.Ordinal) || t.name.Contains("Level_2")) return 2;
            if (t.name.StartsWith("L1_", StringComparison.Ordinal) || t.name.Contains("Level_1")) return 1;
        }
        return 1;
    }
    public int RegionAt(Vector3 point)
    {
        if (authoredWorld && !Built) RefreshRegionBounds();
        int best = 0;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < 3; i++)
        {
            float next = (RegionBounds[i].ClosestPoint(point) - point).sqrMagnitude;
            if (next < distance) { distance = next; best = i; }
        }
        return best + 1;
    }
    public Vector3 MigrateDungeonPosition(Vector3 ground)
    {
        Vector3 local = transform.InverseTransformPoint(ground);
        int floor = Mathf.Clamp(Mathf.RoundToInt(local.y / 12f), 0, 2);
        return ground + transform.TransformVector(new Vector3(floor * 420f, -floor * 12f, 0));
    }

    public void Build()
    {
        if (Application.isPlaying && authoredWorld) InitializeAuthoredWorld();
    }

    // Called explicitly by the editor baker in an isolated project; never generates a map on load.
    public void BuildForAuthoring()
    {
        if (Application.isPlaying || authoredWorld || Built) return;
        Built = true;
        map = GetComponent<DungeonViewSettings>();
        // Hide authored stone geometry and collisions before creating the open terrain.
        foreach (Renderer item in GetComponentsInChildren<Renderer>(true)) item.enabled = false;
        foreach (Collider item in GetComponentsInChildren<Collider>(true)) item.enabled = false;
        foreach (Light item in GetComponentsInChildren<Light>(true)) item.enabled = false;
        foreach (MonoBehaviour item in GetComponentsInChildren<MonoBehaviour>(true))
            if (item.GetType().Name == "NavMeshSurface") item.enabled = false;
        foreach (GameObject old in new[] { map.level1, map.level2, map.level3, map.shell, map.stair1, map.stair2 })
            if (old != null) old.SetActive(false);

        foreach (DungeonMarker marker in GetComponentsInChildren<DungeonMarker>())
        {
            int floor = MarkerFloor(marker.transform);
            if (string.Equals(marker.kind, "Boss", StringComparison.OrdinalIgnoreCase)) floor = 3;
            if (marker.name == "Stair_1_Top" || marker.name == "Stair_2_Bottom") floor = 2;
            if (marker.name == "Stair_2_Top") floor = 3;
            marker.transform.position += transform.TransformVector(new Vector3((floor - 1) * 420f, -(floor - 1) * 12f, 0));
            if (string.Equals(marker.kind, "Room", StringComparison.OrdinalIgnoreCase))
            {
                var territory = DungeonRoomBoundary.GetOrCreate(marker);
                if (territory != null) territory.halfSize = Vector2.one * 38f;
            }
        }
        cube = PrimitiveMesh(PrimitiveType.Cube);
        sphere = PrimitiveMesh(PrimitiveType.Sphere);
        cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
        var levels = new GameObject[3];
        for (int floor = 1; floor <= 3; floor++) levels[floor - 1] = BuildRegion(floor);
        map.level1 = levels[0]; map.level2 = levels[1]; map.level3 = levels[2];
        Physics.SyncTransforms();
        BakeNavigation();
        ConfigureDaylight();
        authoredTreeCount = TreeCount;
        authoredNavigation = navigationData.ToArray();
        authoredWorld = true;
    }

    GameObject BuildRegion(int floor)
    {
        var theme = ExpeditionMaps.All[mapTheme];
        var root = new GameObject("Level_" + floor + " " + theme.Stages[floor - 1]);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3((floor - 1) * 420f, 0, 0);
        RegionBounds[floor - 1] = new Bounds(root.transform.TransformPoint(new Vector3(150, 0, 150)),
            Vector3.Scale(new Vector3(320, 2, 320), transform.lossyScale));
        batches.Clear();
        var grass = new Material[5];
        for (int i = 0; i < grass.Length; i++)
            grass[i] = Palette("Grass " + floor + " " + i, Color.Lerp(
                mapTheme == 0 ? (floor == 1 ? new Color(.40f, .61f, .23f) : new Color(.20f, .43f, .25f)) : Color.Lerp(theme.Ground,theme.Rock,(floor-1)*.08f),
                mapTheme == 0 ? (floor == 3 ? new Color(.19f, .35f, .31f) : new Color(.56f, .69f, .31f)) : theme.Highlight, i / 4f));
        Material earth = Palette("Trail", mapTheme == 0 ? new Color(.61f, .53f, .34f) : Color.Lerp(theme.Ground, theme.Rock, .5f));
        Material bark = Palette("Tree bark " + floor, new Color(.30f, .22f, .14f));
        Material leaf = Palette("Canopy " + floor, mapTheme == 0 ? (floor == 1 ? new Color(.31f, .54f, .21f) :
            floor == 2 ? new Color(.18f, .43f, .23f) : new Color(.15f, .32f, .31f)) : theme.Foliage);
        Material lightLeaf = Palette("Canopy highlight " + floor, mapTheme == 0 ? (floor == 3 ? new Color(.27f, .48f, .40f) : new Color(.42f, .62f, .26f)) : mapTheme == 3 ? new Color(1,.34f,.08f) : theme.Highlight);
        Material stone = Palette("Landscape stone " + floor, theme.Rock);
        Material flower = Palette("Wildflowers " + floor, floor == 3 ? new Color(.70f, .51f, .88f) : new Color(.98f, .83f, .41f));
        var random = new System.Random(7419 + floor * 317 + mapTheme * 6197);
        // Tile palette creates organic grass patches; one continuous collider supports movement.
        for (int x = 0; x < 20; x++)
        for (int z = 0; z < 20; z++)
        {
            int tint = Mathf.Clamp(Mathf.RoundToInt(Mathf.PerlinNoise(x * .29f + floor, z * .29f) * 5), 0, 4);
            Add(cube, new Vector3(-2 + x * 16, -.20f, -2 + z * 16), new Vector3(16, .4f, 16), grass[tint]);
        }
        var ground = new GameObject("Meadow Ground", typeof(BoxCollider));
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0) ground.layer = groundLayer;
        ground.transform.SetParent(root.transform, false);
        var groundCollider = ground.GetComponent<BoxCollider>();
        groundCollider.center = new Vector3(150, -.5f, 150); groundCollider.size = new Vector3(320, 1, 320);
        Source(root.transform, groundCollider.center, groundCollider.size);
        // Curving dirt trails cross broad clearings, with no walls or forced route.
        for (int step = 0; step < 140; step++)
        {
            float z = step * 2.2f;
            Add(sphere, new Vector3(TrailX(z), .012f, z), new Vector3(7, .03f, 5), earth);
            Add(sphere, new Vector3(z, .012f, TrailZ(z)), new Vector3(5, .03f, 7), earth);
        }
        int trees = mapTheme == 1 ? 90 + floor * 18 : mapTheme == 3 ? 95 + floor * 25 : floor == 1 ? 90 : floor == 2 ? 230 : 310;
        for (int i = 0; i < trees; i++)
        {
            Vector3 p = new Vector3(Next(random, 3, 297), 0, Next(random, 3, 297));
            if (Protected(root.transform.TransformPoint(p)) || OnTrail(p)) continue;
            float height = Next(random, 5, floor == 1 ? 8 : 11);
            if (mapTheme > 0) { BuildThemedObstacle(root.transform,p,height,leaf,lightLeaf,stone); TreeCount++; continue; }
            Add(cylinder, p + Vector3.up * height * .40f, new Vector3(.85f, height * .40f, .85f), bark);
            Add(sphere, p + Vector3.up * height * .77f, new Vector3(5.5f, height * .58f, 5.5f), leaf);
            Add(sphere, p + new Vector3(-1, height * .9f, -.6f), new Vector3(3.8f, height * .38f, 3.8f), lightLeaf);
            var trunk = new GameObject("Tree Trunk", typeof(CapsuleCollider));
            trunk.transform.SetParent(root.transform, false); trunk.transform.localPosition = p;
            var collision = trunk.GetComponent<CapsuleCollider>();
            collision.radius = .52f; collision.height = height; collision.center = Vector3.up * height * .5f;
            Source(root.transform, p + Vector3.up * height * .5f, new Vector3(1.2f, height, 1.2f));
            TreeCount++;
        }
        for (int i = 0; i < 180; i++)
        {
            Vector3 p = new Vector3(Next(random, 2, 298), 0, Next(random, 2, 298));
            if (Protected(root.transform.TransformPoint(p)) || OnTrail(p)) continue;
            if (i % 4 == 0)
            {
                float size = Next(random, .6f, 1.7f);
                Add(sphere, p + Vector3.up * size * .28f, new Vector3(size * 1.4f, size, size), stone);
                var rock = new GameObject("Moss Rock", typeof(SphereCollider));
                rock.transform.SetParent(root.transform, false); rock.transform.localPosition = p + Vector3.up * size * .3f;
                rock.GetComponent<SphereCollider>().radius = size * .65f;
                Source(root.transform, p + Vector3.up * size * .5f, Vector3.one * size * 1.5f);
            }
            else
                for (int petal = 0; petal < 4; petal++)
                    Add(cube, p + new Vector3(petal * .22f, .28f, Mathf.Sin(petal) * .3f), new Vector3(.19f, .12f, .19f), flower);
        }
        if (mapTheme > 0) BuildLandmarks(root.transform, floor, stone, lightLeaf);
        // Border hills close the outer edge naturally; the playable interior remains open.
        for (int edge = 0; edge < 4; edge++)
        for (int i = 0; i <= 20; i++)
        {
            Vector3 p = edge < 2 ? new Vector3(i * 16 - 10, 0, edge == 0 ? -12 : 312) :
                new Vector3(edge == 2 ? -12 : 312, 0, i * 16 - 10);
            Add(sphere, p, new Vector3(28, 18 + i % 3 * 4, 28), i % 3 == 0 ? stone : grass[0]);
            var hill = new GameObject("Wooded Border Hill", typeof(BoxCollider));
            hill.transform.SetParent(root.transform, false); hill.transform.localPosition = p + Vector3.up * 5;
            hill.GetComponent<BoxCollider>().size = new Vector3(16, 12, 16);
            Source(root.transform, p + Vector3.up * 5, new Vector3(18, 16, 18), 1);
        }
        foreach (var batch in batches)
        {
            var mesh = new Mesh { name = batch.Key.name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(batch.Value.ToArray()); meshes.Add(mesh);
            var node = new GameObject(batch.Key.name, typeof(MeshFilter), typeof(MeshRenderer));
            node.transform.SetParent(root.transform, false);
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            node.GetComponent<MeshRenderer>().sharedMaterial = batch.Key;
        }
        return root;
    }

    bool Protected(Vector3 position)
    {
        if ((transform.TransformPoint(new Vector3(36, 0, 5)) - position).sqrMagnitude < 12 * 12) return true;
        foreach (DungeonMarker marker in GetComponentsInChildren<DungeonMarker>())
            if ((marker.transform.position - position).sqrMagnitude <
                (marker.kind == "Boss" ? 22 * 22 : marker.kind == "Room" ? 7 * 7 : 10 * 10)) return true;
        foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
            if ((player.transform.position - position).sqrMagnitude < 12 * 12) return true;
        return false;
    }
    float TrailX(float z) => 150 + Mathf.Sin(z / (52f + mapTheme * 8) + mapTheme) * (17 + mapTheme * 4);
    float TrailZ(float x) => 150 + Mathf.Sin(x / (65f - mapTheme * 6) + mapTheme * .5f) * (15 + mapTheme * 3);
    bool OnTrail(Vector3 p) => Mathf.Abs(p.x - TrailX(p.z)) < 7 || Mathf.Abs(p.z - TrailZ(p.x)) < 7;
    void BuildThemedObstacle(Transform root, Vector3 p, float height, Material leaf, Material snow, Material rock)
    {
        float radius = mapTheme == 3 ? 1.25f : .6f;
        if (mapTheme == 1) {
            Add(cube,p+Vector3.up*height*.4f,new Vector3(.9f,height*.8f,.9f),leaf);
            Add(cube,p+new Vector3(1.1f,height*.45f,0),new Vector3(2.3f,.65f,.7f),leaf);
            Add(cube,p+new Vector3(2,height*.58f,0),new Vector3(.65f,height*.3f,.7f),leaf);
        } else if (mapTheme == 2) {
            Add(cylinder,p+Vector3.up*height*.35f,new Vector3(.65f,height*.35f,.65f),rock);
            for (int i=0;i<3;i++) {
                float width=5-i*1.1f;
                Add(sphere,p+Vector3.up*(height*.43f+i*height*.19f),new Vector3(width,height*.38f,width),leaf);
                Add(sphere,p+Vector3.up*(height*.51f+i*height*.19f),new Vector3(width*.88f,height*.18f,width*.88f),snow);
            }
        } else {
            Add(cylinder,p+Vector3.up*height*.5f,new Vector3(2.4f,height*.5f,2.4f),rock);
            Add(cylinder,p+Vector3.up*(height+.02f),new Vector3(2.1f,.04f,2.1f),leaf);
        }
        var node=new GameObject(mapTheme==1?"Cactus":mapTheme==2?"Snow Pine":"Basalt Pillar",typeof(CapsuleCollider));
        node.transform.SetParent(root,false); node.transform.localPosition=p;
        var collision=node.GetComponent<CapsuleCollider>(); collision.radius=radius; collision.height=height; collision.center=Vector3.up*height*.5f;
        Source(root,p+Vector3.up*height*.5f,new Vector3(radius*2.4f,height,radius*2.4f),1);
    }
    void BuildLandmarks(Transform root,int floor,Material stone,Material accent)
    {
        for(int i=0;i<16+floor*3;i++) {
            var p=new Vector3(25+(i*71+mapTheme*31)%250,0,25+(i*43+floor*29)%250);
            if(Protected(root.TransformPoint(p))||OnTrail(p)) continue;
            float h=mapTheme==1?8:mapTheme==2?5:4;
            Add(cube,p+Vector3.up*h*.5f,new Vector3(2,h,2),stone);
            Add(cube,p+new Vector3(0,h,0),new Vector3(mapTheme==1?7:3,.8f,3),accent);
            var node=new GameObject(mapTheme==1?"Ruined Temple Column":mapTheme==2?"Ice Monolith":"Obsidian Shrine",typeof(BoxCollider));
            node.transform.SetParent(root,false);node.transform.localPosition=p;
            var collider=node.GetComponent<BoxCollider>();collider.center=Vector3.up*h*.5f;collider.size=new Vector3(2,h,2);
            Source(root,collider.center+p,new Vector3(2.4f,h,2.4f),1);
            if(mapTheme==3) for(int j=0;j<5;j++) Add(cube,p+new Vector3(j*1.3f,.018f,-3+j*.7f),new Vector3(2,.025f,.5f),accent);
        }
    }
    static float Next(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    Mesh PrimitiveMesh(PrimitiveType type)
    {
        var node = GameObject.CreatePrimitive(type); node.SetActive(false);
        Mesh mesh = node.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(node); else DestroyImmediate(node);
        return mesh;
    }
    Material Palette(string label, Color color)
    {
        var material = new Material(Resources.Load<Shader>("PixelSurface")) { name = label };
        material.SetColor("_Color", color); material.SetFloat("_ToneSteps", 64); materials.Add(material); return material;
    }
    void Add(Mesh mesh, Vector3 position, Vector3 size, Material material)
    {
        if (!batches.TryGetValue(material, out var items)) batches[material] = items = new List<CombineInstance>();
        items.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position, Quaternion.identity, size) });
    }
    void Source(Transform root, Vector3 center, Vector3 size, int area = 0) => sources.Add(new NavMeshBuildSource {
        shape = NavMeshBuildSourceShape.Box, transform = root.localToWorldMatrix * Matrix4x4.Translate(center), size = size, area = area
    });
    void BakeNavigation()
    {
        // The old baked dungeon surface must not allow agents through removed walls/floors.
        if (Application.isPlaying) NavMesh.RemoveAllNavMeshData();
        var agentTypes = new HashSet<int> { 0 };
        foreach (NPCSpawner spawner in FindObjectsByType<NPCSpawner>(FindObjectsSortMode.None))
            if (spawner.npcPrefab != null && spawner.npcPrefab.TryGetComponent(out NavMeshAgent agent)) agentTypes.Add(agent.agentTypeID);
        foreach (int type in agentTypes)
        {
            var settings = NavMesh.GetSettingsByID(type);
            if (settings.agentTypeID < 0) continue;
            var bounds = RegionBounds[0]; bounds.Encapsulate(RegionBounds[2]); bounds.Expand(new Vector3(40, 60, 40));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data != null) navigationData.Add(data);
        }
        foreach (NPCSpawner spawner in FindObjectsByType<NPCSpawner>(FindObjectsSortMode.None)) spawner.RefreshNavMeshData();
    }
    void ConfigureDaylight()
    {
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = ExpeditionMaps.All[mapTheme].Sky; RenderSettings.fogStartDistance = 80; RenderSettings.fogEndDistance = 220;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.68f, .75f, .65f);
        var sun = new GameObject("Meadow Sun", typeof(Light)); sun.transform.SetParent(transform, false);
        sun.transform.rotation = Quaternion.Euler(48, -32, 0);
        var light = sun.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.color = new Color(1, .95f, .81f);
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (camera.targetTexture == null) { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor; camera.farClipPlane = 280; }
    }
    void OnDestroy()
    {
        foreach (var item in navigation) if (item.valid) item.Remove();
        // Authored meshes, materials and NavMeshData are project assets with persistent ownership.
        if (!authoredWorld)
        {
            foreach (var item in navigationData) if (item != null) Destroy(item);
            foreach (var item in materials) if (item != null) Destroy(item);
            foreach (var item in meshes) if (item != null) Destroy(item);
        }
    }
}
