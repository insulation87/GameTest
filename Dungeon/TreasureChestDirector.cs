using System;
using System.Collections.Generic;
using MedievalDungeonKit;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

/// <summary>One chest every 3–5 gameplay minutes in the current room's 3x3 neighbourhood.</summary>
[DisallowMultipleComponent]
public sealed class TreasureChestDirector : MonoBehaviour
{
    [Header("Spawn interval (seconds of gameplay)")]
    [Min(1)] public float minimumInterval = 180f;
    [Min(1)] public float maximumInterval = 300f;
    [Min(1)] public int maximumChests = 48;
    [Min(1)] public float chestHealth = 120f;
    [Min(1)] public float weaponLifetime = 180f;
    public float SecondsUntilSpawn { get; private set; }
    readonly List<Room> rooms = new List<Room>();
    readonly List<TreasureChest> chests = new List<TreasureChest>();
    Player player;
    PlayerHealth playerHealth;
    float retryRemaining;
    Vector2 spacing;

    sealed class Room
    {
        public DungeonRoomBoundary boundary;
        public Vector3 center;
        public int floor;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (FindFirstObjectByType<Player>() == null) return;
        foreach (DungeonViewSettings map in FindObjectsByType<DungeonViewSettings>(FindObjectsSortMode.None))
            if (map.gameObject.scene == scene && map.GetComponent<TreasureChestDirector>() == null)
                map.gameObject.AddComponent<TreasureChestDirector>();
    }

    void Start()
    {
        RefreshRooms();
        ScheduleNext();
    }

    void Update()
    {
        if (player == null) player = FindFirstObjectByType<Player>();
        if (player == null) return;
        if (playerHealth == null) playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth == null || playerHealth.IsDead || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen) return;
        SecondsUntilSpawn = Mathf.Max(0, SecondsUntilSpawn - Time.deltaTime);
        if (SecondsUntilSpawn > 0) return;
        retryRemaining -= Time.deltaTime;
        if (retryRemaining > 0) return;
        // No backlog: a corridor, crowded room or full chest budget waits for a safe opportunity.
        if (TrySpawnChest()) ScheduleNext();
        else retryRemaining = 5f;
    }

    void ScheduleNext()
    {
        float minimum = Mathf.Max(1, minimumInterval);
        SecondsUntilSpawn = Random.Range(minimum, Mathf.Max(minimum, maximumInterval));
        retryRemaining = 0;
    }

    public void RefreshRooms()
    {
        rooms.Clear();
        foreach (DungeonMarker marker in GetComponentsInChildren<DungeonMarker>())
        {
            DungeonRoomBoundary boundary = DungeonRoomBoundary.GetOrCreate(marker);
            if (boundary == null) continue;
            rooms.Add(new Room { boundary = boundary, center = transform.InverseTransformPoint(marker.transform.position),
                floor = FloorNumber(marker.transform) });
        }
        // Measure the grid in dungeon-local coordinates, retaining missing cells at map edges.
        spacing = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        foreach (Room a in rooms)
            foreach (Room b in rooms)
            {
                if (a.floor != b.floor) continue;
                Vector3 delta = a.center - b.center;
                if (Mathf.Abs(delta.x) > .1f) spacing.x = Mathf.Min(spacing.x, Mathf.Abs(delta.x));
                if (Mathf.Abs(delta.z) > .1f) spacing.y = Mathf.Min(spacing.y, Mathf.Abs(delta.z));
            }
        if (float.IsInfinity(spacing.x)) spacing.x = 0;
        if (float.IsInfinity(spacing.y)) spacing.y = 0;
    }

    static int FloorNumber(Transform marker)
    {
        for (Transform t = marker; t != null; t = t.parent)
        {
            if (t.name.StartsWith("L3_", StringComparison.Ordinal) || t.name.Contains("Level_3")) return 3;
            if (t.name.StartsWith("L2_", StringComparison.Ordinal) || t.name.Contains("Level_2")) return 2;
            if (t.name.StartsWith("L1_", StringComparison.Ordinal) || t.name.Contains("Level_1")) return 1;
        }
        return 1;
    }

    public List<DungeonRoomBoundary> GetNearbyRooms(Vector3 playerPosition)
    {
        var result = new List<DungeonRoomBoundary>();
        Room current = rooms.Find(room => room.boundary != null && room.boundary.isActiveAndEnabled && room.boundary.Contains(playerPosition));
        if (current == null) return result;
        foreach (Room room in rooms)
        {
            if (room.boundary == null || !room.boundary.isActiveAndEnabled || room.floor != current.floor) continue;
            Vector3 delta = room.center - current.center;
            if (Mathf.Abs(delta.y) < .5f && Mathf.Abs(delta.x) <= spacing.x + .1f && Mathf.Abs(delta.z) <= spacing.y + .1f)
                result.Add(room.boundary);
        }
        return result;
    }

    public bool TrySpawnChest()
    {
        if (player == null) player = FindFirstObjectByType<Player>();
        if (player == null || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen) return false;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health == null || health.IsDead) return false;
        chests.RemoveAll(chest => chest == null || chest.IsBroken);
        if (chests.Count >= Mathf.Max(1, maximumChests)) return false;
        List<DungeonRoomBoundary> candidates = GetNearbyRooms(player.transform.position);
        // Shuffle instead of picking a start index so fallback rooms remain unbiased.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int other = Random.Range(0, i + 1);
            DungeonRoomBoundary swap = candidates[i]; candidates[i] = candidates[other]; candidates[other] = swap;
        }
        foreach (DungeonRoomBoundary room in candidates)
        {
            if (chests.Exists(chest => chest.Room == room)) continue;
            if (!TryPosition(room, out Vector3 position)) continue;
            int level = health.CurrentLevel;
            WeaponInventory inventory = player.GetComponent<WeaponInventory>();
            if (inventory != null)
                foreach (WeaponItem weapon in inventory.EquippedSlots)
                    if (weapon != null) level = Mathf.Max(level, weapon.level);
            var node = new GameObject("秘藏寶箱");
            node.transform.SetPositionAndRotation(position, room.transform.rotation);
            // Preserve metre-sized geometry even when the map is scaled.
            node.transform.SetParent(transform, true);
            TreasureChest chest = node.AddComponent<TreasureChest>();
            chest.Initialize(room, WeaponLoot.RollChest(FloorNumber(room.transform), level), chestHealth, weaponLifetime);
            chests.Add(chest);
            return true;
        }
        return false;
    }

    bool TryPosition(DungeonRoomBoundary room, out Vector3 position)
    {
        position = default;
        var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector3 desired = room.transform.TransformPoint(new Vector3(
                Random.Range(-room.halfSize.x * .7f, room.halfSize.x * .7f), 0,
                Random.Range(-room.halfSize.y * .7f, room.halfSize.y * .7f)));
            if (!room.TryGetNavMeshPoint(desired, TreasureChest.Clearance, filter, out Vector3 nav)) continue;
            if (!Physics.Raycast(nav + Vector3.up * .6f, Vector3.down, out RaycastHit ground, 1.2f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore) || ground.normal.y < .95f ||
                ground.rigidbody != null || !IsChestGround(ground.transform)) continue;
            Vector3 point = ground.point + Vector3.up * .03f;
            if (!room.Contains(point, TreasureChest.Clearance) || DungeonHazardDirector.IsUnsafe(point, TreasureChest.Clearance) ||
                (point - player.transform.position).sqrMagnitude < 16f) continue;
            if (Physics.CheckBox(point + Vector3.up * .65f, TreasureChest.Size * .5f + new Vector3(.25f, 0, .25f),
                room.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore)) continue;
            position = point;
            return true;
        }
        return false;
    }

    static bool IsChestGround(Transform surface)
    {
        if (DungeonFloorOpening.IsFloor(surface)) return true;
        // Authored outdoor regions use one ground collider instead of dungeon floor tiles.
        return surface.name == "Meadow Ground" && surface.GetComponentInParent<OverworldDirector>() != null;
    }

    void OnValidate()
    {
        minimumInterval = Mathf.Max(1, minimumInterval);
        maximumInterval = Mathf.Max(minimumInterval, maximumInterval);
        maximumChests = Mathf.Max(1, maximumChests);
        chestHealth = Mathf.Max(1, chestHealth);
        weaponLifetime = Mathf.Max(1, weaponLifetime);
    }
}
