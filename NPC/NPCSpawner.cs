using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MedievalDungeonKit;

public class NPCSpawner : MonoBehaviour
{
    [Header("References")]
    public GameObject npcPrefab;
    public Transform playerTarget;
    public Transform npcContainer;

    [Header("Spawn Settings")]
    [Min(1)] public int maxNpcCount = 10;
    [Min(0.1f)] public float spawnInterval = 2f;
    [Min(0f)] public float minDistanceFromPlayer = 5f;
    [Min(0f)] public float minNpcSpacing = 1.5f;
    [Min(1)] public int maxPositionAttempts = 30;
    public int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Room Activation")]
    public bool spawnWhenPlayerEntersRoom = true;
    [Min(1)] public int enemiesPerRoom = 3;
    [Tooltip("Keep spawning at Spawn Interval while the player remains inside a room.")]
    public bool continuousSpawningWhilePlayerInRoom = true;
    [Min(0.1f)] public float roomActivationRadius = 20f;
    [Tooltip("Maximum vertical distance for a room to be considered on the player's current floor.")]
    [Min(0.1f)] public float roomFloorTolerance = 6f;
    [Min(0.1f)] public float roomSpawnRadius = 9f;
    [Min(0f)] public float roomSpawnDelay = 1f;
    public string roomMarkerKind = "Room";

    [Header("Random NPC Stats")]
    [Min(1f)] public float minHealth = 50f;
    [Min(1f)] public float maxHealth = 200f;
    [HideInInspector] public float minAttack = 1f; // Legacy serialized values; level now determines damage.
    [HideInInspector] public float maxAttack = 1f;
    [Min(1)] public int maxLevel = 10;
    [Min(0)] public int experiencePerEnemyLevel = 10;

    private readonly List<GameObject> spawnedNpcs = new List<GameObject>();
    private NavMeshTriangulation triangulation;
    private Coroutine spawnRoutine;
    private float nextGlobalSpawnTime;
    private readonly List<RoomSpawnState> rooms = new List<RoomSpawnState>();

    private class RoomSpawnState
    {
        public Transform marker;
        public DungeonRoomBoundary boundary;
        public bool activated;
        public bool playerInside;
        public int spawnedCount;
        public float nextSpawnTime;
    }

    void OnEnable()
    {
        FindPlayerIfNeeded();
        RefreshNavMeshData();
        CacheRooms();
        nextGlobalSpawnTime = Time.time;
        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    void OnDisable()
    {
        if (spawnRoutine != null)
            StopCoroutine(spawnRoutine);

        spawnRoutine = null;
    }

    IEnumerator SpawnLoop()
    {
        while (enabled)
        {
            RemoveDestroyedNpcs();

            if (spawnWhenPlayerEntersRoom)
            {
                ActivateEnteredRoom();
                SpawnForActivatedRoom();
            }
            else if (spawnedNpcs.Count < maxNpcCount && Time.time >= nextGlobalSpawnTime)
            {
                TrySpawnNpc();
                nextGlobalSpawnTime = Time.time + spawnInterval;
            }

            yield return null;
        }
    }

    public void RefreshNavMeshData()
    {
        triangulation = NavMesh.CalculateTriangulation();
    }

    public bool TrySpawnNpc()
    {
        if (rooms.Count == 0) CacheRooms();
        if (rooms.Count == 0) return false;
        int first = Random.Range(0, rooms.Count);
        for (int i = 0; i < rooms.Count; i++)
            if (TrySpawnNpcInRoom(rooms[(first + i) % rooms.Count].boundary)) return true;
        return false;
    }

    bool TrySpawnNpcInRoom(DungeonRoomBoundary room)
    {
        if (room == null) return false;
        Transform roomMarker = room.transform;
        if (npcPrefab == null)
        {
            Debug.LogWarning("NPCSpawner requires an NPC prefab.", this);
            return false;
        }

        if (triangulation.indices == null || triangulation.indices.Length < 3)
            RefreshNavMeshData();

        if (triangulation.indices == null || triangulation.indices.Length < 3)
        {
            Debug.LogWarning("NPCSpawner could not find a baked NavMesh.", this);
            return false;
        }

        for (int attempt = 0; attempt < maxPositionAttempts; attempt++)
        {
            if (!TryGetSpawnPoint(room, out Vector3 spawnPosition))
                continue;

            if (!IsValidSpawnPosition(spawnPosition))
                continue;

            GameObject npc = Instantiate(
                npcPrefab,
                spawnPosition,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                npcContainer
            );

            EnemyAI enemyAI = npc.GetComponent<EnemyAI>();
            if (enemyAI != null) enemyAI.AssignRoom(room);
            if (enemyAI != null && playerTarget != null)
                enemyAI.target = playerTarget;

            EnemyHealth enemyHealth = npc.GetComponent<EnemyHealth>();
            if (enemyHealth != null && roomMarker != null)
                enemyHealth.SetDungeonFloor(GetFloorNumber(roomMarker));

            ApplyRandomStats(npc, enemyAI);
            EnemyCharacterVisual.Ensure(npc);

            spawnedNpcs.Add(npc);
            return true;
        }

        return false;
    }

    static int GetFloorNumber(Transform roomMarker)
    {
        for (Transform current = roomMarker; current != null; current = current.parent)
        {
            if (current.name.Contains("Level_3")) return 3;
            if (current.name.Contains("Level_2")) return 2;
            if (current.name.Contains("Level_1")) return 1;
        }

        return 1;
    }

    bool TryGetSpawnPoint(DungeonRoomBoundary room, out Vector3 point)
    {
        NavMeshAgent prefabAgent = npcPrefab.GetComponent<NavMeshAgent>();
        float clearance = prefabAgent != null ? prefabAgent.radius : .5f;
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = prefabAgent != null ? prefabAgent.agentTypeID : 0,
            areaMask = navMeshAreaMask & (prefabAgent != null ? prefabAgent.areaMask : NavMesh.AllAreas)
        };

        for (int attempt = 0; attempt < maxPositionAttempts; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * roomSpawnRadius;
            Vector3 candidate = room.transform.position + new Vector3(offset.x, 0f, offset.y);
            if (room.TryGetNavMeshPoint(candidate, clearance, filter, out point))
            {
                return true;
            }
        }

        point = default;
        return false;
    }

    void FindPlayerIfNeeded()
    {
        if (playerTarget != null)
            return;

        Player player = FindFirstObjectByType<Player>();
        if (player != null)
            playerTarget = player.transform;
    }

    void CacheRooms()
    {
        rooms.Clear();
        DungeonMarker[] markers = FindObjectsByType<DungeonMarker>(FindObjectsSortMode.None);
        foreach (DungeonMarker marker in markers)
        {
            if (marker != null &&
                string.Equals(marker.kind, roomMarkerKind, System.StringComparison.OrdinalIgnoreCase))
            {
                DungeonRoomBoundary boundary = DungeonRoomBoundary.GetOrCreate(marker);
                if (boundary != null)
                    rooms.Add(new RoomSpawnState { marker = marker.transform, boundary = boundary });
            }
        }

        if (spawnWhenPlayerEntersRoom && rooms.Count == 0)
            Debug.LogWarning("NPCSpawner could not find any Room markers.", this);
    }

    void ActivateEnteredRoom()
    {
        if (playerTarget == null)
        {
            FindPlayerIfNeeded();
            if (playerTarget == null)
                return;
        }

        RoomSpawnState closestRoom = null;
        float closestDistance = roomActivationRadius * roomActivationRadius;
        foreach (RoomSpawnState room in rooms)
        {
            room.playerInside = false;

            if (room.marker == null)
                continue;
            if (room.boundary == null || !room.boundary.Contains(playerTarget.position))
                continue;

            Vector3 difference = room.marker.position - playerTarget.position;
            if (Mathf.Abs(difference.y) > roomFloorTolerance)
                continue;

            difference.y = 0f;
            float distance = difference.sqrMagnitude;
            if (distance <= closestDistance)
            {
                closestDistance = distance;
                closestRoom = room;
            }
        }

        if (closestRoom != null)
        {
            closestRoom.playerInside = true;
            if (!closestRoom.activated)
            {
                closestRoom.activated = true;
                closestRoom.nextSpawnTime = Time.time + roomSpawnDelay;
            }
        }
    }

    void SpawnForActivatedRoom()
    {
        if (spawnedNpcs.Count >= maxNpcCount)
            return;

        foreach (RoomSpawnState room in rooms)
        {
            bool reachedRoomLimit = !continuousSpawningWhilePlayerInRoom &&
                                    room.spawnedCount >= enemiesPerRoom;
            if (!room.playerInside || reachedRoomLimit ||
                room.marker == null || Time.time < room.nextSpawnTime)
                continue;

            if (TrySpawnNpcInRoom(room.boundary))
                room.spawnedCount++;

            room.nextSpawnTime = Time.time + spawnInterval;

            return;
        }
    }

    void ApplyRandomStats(GameObject npc, EnemyAI enemyAI)
    {
        float rolledHealth = Random.Range(minHealth, maxHealth);

        EnemyHealth enemyHealth = npc.GetComponent<EnemyHealth>();
        if (enemyHealth != null)
            enemyHealth.SetMaxHealth(rolledHealth);

        int floor = enemyHealth != null ? enemyHealth.DungeonFloor : 1;
        int level = GetRandomEnemyLevel(floor);
        if (enemyAI != null) enemyAI.ConfigureLevel(level);
        float totalStrength = Mathf.InverseLerp(1f, 99f, level);

        EnemyHealthBarUI healthBarUI = npc.GetComponent<EnemyHealthBarUI>();
        if (healthBarUI != null)
            healthBarUI.SetLevel(level);

        if (enemyHealth != null)
        {
            enemyHealth.SetLevelAndExperience(level, experiencePerEnemyLevel);
            enemyHealth.SetStrength(totalStrength);
        }
    }

    static int GetRandomEnemyLevel(int floor)
    {
        switch (floor)
        {
            case 2: return Random.Range(21, 51);
            case 3: return Random.Range(51, 100);
            default: return Random.Range(1, 21);
        }
    }

    bool IsValidSpawnPosition(Vector3 position)
    {
        if (playerTarget != null &&
            Vector3.SqrMagnitude(position - playerTarget.position) <
            minDistanceFromPlayer * minDistanceFromPlayer)
        {
            return false;
        }

        float minSpacingSquared = minNpcSpacing * minNpcSpacing;
        foreach (GameObject npc in spawnedNpcs)
        {
            if (npc != null &&
                Vector3.SqrMagnitude(position - npc.transform.position) < minSpacingSquared)
            {
                return false;
            }
        }

        return true;
    }

    void RemoveDestroyedNpcs()
    {
        spawnedNpcs.RemoveAll(npc => npc == null);
    }

    void OnValidate()
    {
        maxNpcCount = Mathf.Max(1, maxNpcCount);
        spawnInterval = Mathf.Max(0.1f, spawnInterval);
        minDistanceFromPlayer = Mathf.Max(0f, minDistanceFromPlayer);
        minNpcSpacing = Mathf.Max(0f, minNpcSpacing);
        maxPositionAttempts = Mathf.Max(1, maxPositionAttempts);
        enemiesPerRoom = Mathf.Max(1, enemiesPerRoom);
        roomActivationRadius = Mathf.Max(0.1f, roomActivationRadius);
        roomFloorTolerance = Mathf.Max(0.1f, roomFloorTolerance);
        roomSpawnRadius = Mathf.Max(0.1f, roomSpawnRadius);
        roomSpawnDelay = Mathf.Max(0f, roomSpawnDelay);
        minHealth = Mathf.Max(1f, minHealth);
        maxHealth = Mathf.Max(minHealth, maxHealth);
        minAttack = Mathf.Max(0f, minAttack);
        maxAttack = Mathf.Max(minAttack, maxAttack);
        maxLevel = Mathf.Max(1, maxLevel);
        experiencePerEnemyLevel = Mathf.Max(0, experiencePerEnemyLevel);
    }
}
