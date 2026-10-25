using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MedievalDungeonKit;

public partial class FloorProgressionManager : MonoBehaviour
{
    public static FloorProgressionManager Instance { get; private set; }

    [Header("Required Items")]
    [Min(1)] public int floorOneRequired = 10;
    [Min(1)] public int floorTwoRequired = 20;
    [Min(1)] public int floorThreeRequired = 9;

    [Header("Automatic Air Walls")]
    public Vector3 stairWallSize = new Vector3(5f, 4f, 1f);
    public Vector3 bossWallSize = new Vector3(6f, 4f, 1f);

    private readonly int[] collected = new int[4];
    private GameObject floorTwoWall;
    private GameObject floorThreeWall;
    private GameObject bossWall;
    private BossSpawner bossSpawner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EnsureInstance();
    }

    public static FloorProgressionManager EnsureInstance()
    {
        if (Instance != null)
            return Instance;
        if (FindFirstObjectByType<Player>() == null || FindFirstObjectByType<DungeonMarker>() == null)
            return null;

        FloorProgressionManager existing = FindFirstObjectByType<FloorProgressionManager>();
        return existing != null ? existing :
            new GameObject("Floor Progression Manager").AddComponent<FloorProgressionManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildProgressionObjects();
    }

    public void Collect(int floor)
    {
        floor = Mathf.Clamp(floor, 1, 3);
        collected[floor]++;
        Debug.Log($"Floor {floor} progression items: {collected[floor]}/{RequiredFor(floor)}");

        if (floor == 1 && collected[1] >= floorOneRequired)
            SetWallLocked(floorTwoWall, false);
        else if (floor == 2 && collected[2] >= floorTwoRequired)
            SetWallLocked(floorThreeWall, false);
        else if (floor == 3 && collected[3] >= floorThreeRequired)
        {
            SetWallLocked(bossWall, false);
            if (bossSpawner != null)
                bossSpawner.SpawnBoss();
        }
    }

    public bool TryPrepareBossTest(Player player, out BossController boss,
        out Vector3 playerPosition, out Quaternion playerRotation)
    {
        boss = null;
        playerPosition = default;
        playerRotation = Quaternion.identity;
        DungeonMarker marker = FindBossMarker();
        if (player == null || marker == null || bossSpawner == null)
        {
            Debug.LogWarning("無法傳送：目前場景沒有首領房間或首領生成器。", this);
            return false;
        }

        DungeonRoomBoundary room = DungeonRoomBoundary.FindContaining(marker.transform.position);
        if (room == null)
        {
            Debug.LogWarning("無法傳送：找不到首領房間的安全邊界。", this);
            return false;
        }

        Vector3 bossPosition = bossSpawner.CurrentBoss != null
            ? bossSpawner.CurrentBoss.transform.position : marker.transform.position;
        bool foundPosition = false;
        var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        // Search the current floor's room, away from the boss, using a full standing collider.
        foreach (float distance in new[] { 8f, 12f, 5f })
        {
            for (int direction = 0; direction < 12; direction++)
            {
                Vector3 offset = Quaternion.Euler(0f, direction * 30f, 0f) * -marker.transform.forward;
                if (!room.TryGetNavMeshPoint(bossPosition + offset * distance, 1f, filter, out Vector3 ground))
                    continue;
                Vector3 facing = bossPosition - ground;
                facing.y = 0f;
                if (facing.sqrMagnitude < 16f)
                    continue;
                Quaternion rotation = Quaternion.LookRotation(facing);
                if (!player.TryGetTeleportPosition(ground, rotation, out Vector3 position))
                    continue;
                playerPosition = position;
                playerRotation = rotation;
                foundPosition = true;
                break;
            }
            if (foundPosition) break;
        }

        if (!foundPosition)
        {
            Debug.LogWarning("無法傳送：首領房間內沒有可安全站立的位置，請確認導覽網格。", this);
            return false;
        }
        if (!bossSpawner.TrySpawnBoss(true))
            return false;

        boss = bossSpawner.CurrentBoss;
        SetWallLocked(bossWall, false);
        return true;
    }

    int RequiredFor(int floor)
    {
        return floor == 1 ? floorOneRequired : floor == 2 ? floorTwoRequired : floorThreeRequired;
    }

    void BuildProgressionObjects()
    {
        OverworldDirector world = OverworldDirector.EnsureWorld();
        Transform stairOneTop = FindNamedTransform("Stair_1_Top");
        Transform stairTwoTop = FindNamedTransform("Stair_2_Top");
        DungeonMarker bossMarker = FindBossMarker();

        if (world == null)
        {
            floorTwoWall = CreateAirWall("Floor 2 Air Wall", stairOneTop, stairWallSize);
            floorThreeWall = CreateAirWall("Floor 3 Air Wall", stairTwoTop, stairWallSize);
            bossWall = CreateAirWall("Boss Room Air Wall", bossMarker != null ? bossMarker.transform : null, bossWallSize);
        }

        if (bossMarker != null)
        {
            bossSpawner = bossMarker.GetComponent<BossSpawner>();
            if (bossSpawner == null)
                bossSpawner = bossMarker.gameObject.AddComponent<BossSpawner>();
        }
    }

    static DungeonMarker FindBossMarker()
    {
        foreach (DungeonMarker marker in FindObjectsByType<DungeonMarker>(FindObjectsSortMode.None))
            if (string.Equals(marker.kind, "Boss", System.StringComparison.OrdinalIgnoreCase))
                return marker;
        return null;
    }

    static Transform FindNamedTransform(string objectName)
    {
        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (candidate.name == objectName)
                return candidate;
        return null;
    }

    static GameObject CreateAirWall(string wallName, Transform anchor, Vector3 size)
    {
        if (anchor == null)
        {
            Debug.LogWarning($"Could not create {wallName}: anchor not found.");
            return null;
        }

        GameObject wall = new GameObject(wallName);
        wall.transform.SetPositionAndRotation(anchor.position + Vector3.up * (size.y * 0.5f), anchor.rotation);
        BoxCollider collider = wall.AddComponent<BoxCollider>();
        collider.size = size;
        return wall;
    }

    static void SetWallLocked(GameObject wall, bool locked)
    {
        if (wall != null && wall.activeSelf && !locked) GameAudio.UI(GameSound.Unlock);
        if (wall != null)
            wall.SetActive(locked);
    }
}
