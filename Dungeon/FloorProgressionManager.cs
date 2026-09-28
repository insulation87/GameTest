using System.Collections.Generic;
using UnityEngine;
using MedievalDungeonKit;

public class FloorProgressionManager : MonoBehaviour
{
    public static FloorProgressionManager Instance { get; private set; }

    [Header("Required Items")]
    [Min(1)] public int floorOneRequired = 3;
    [Min(1)] public int floorTwoRequired = 6;
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
        if (FindFirstObjectByType<Player>() == null || FindFirstObjectByType<DungeonMarker>() == null)
            return;

        if (FindFirstObjectByType<FloorProgressionManager>() == null)
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

    int RequiredFor(int floor)
    {
        return floor == 1 ? floorOneRequired : floor == 2 ? floorTwoRequired : floorThreeRequired;
    }

    void BuildProgressionObjects()
    {
        Transform stairOneTop = FindNamedTransform("Stair_1_Top");
        Transform stairTwoTop = FindNamedTransform("Stair_2_Top");
        DungeonMarker bossMarker = FindBossMarker();

        floorTwoWall = CreateAirWall("Floor 2 Air Wall", stairOneTop, stairWallSize);
        floorThreeWall = CreateAirWall("Floor 3 Air Wall", stairTwoTop, stairWallSize);
        bossWall = CreateAirWall("Boss Room Air Wall", bossMarker != null ? bossMarker.transform : null, bossWallSize);

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
        if (wall != null)
            wall.SetActive(locked);
    }
}
