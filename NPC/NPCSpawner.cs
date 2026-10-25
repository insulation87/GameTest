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
    [Min(1)] public int maxNpcCount = 150;
    [Min(0.1f)] public float spawnInterval = .35f;
    [Min(1)] public int initialSpawnBatchSize = 3;
    [Min(0f)] public float minDistanceFromPlayer = 5f;
    [Min(0f)] public float minNpcSpacing = 1.5f;
    [Min(1)] public int maxPositionAttempts = 30;
    public int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Time-Based Spawn Growth")]
    public bool increaseSpawningOverTime = true;
    [Tooltip("Active gameplay seconds per difficulty step. Pauses and single-player menus do not count.")]
    [Min(1f)] public float difficultyStepSeconds = 45f;
    [Tooltip("Spawn rate increases by this fraction of the starting rate per difficulty step.")]
    [Min(0f)] public float spawnRateGrowthPerStep = .65f;
    [Min(.1f)] public float minimumSpawnInterval = .1f;
    [Min(0)] public int additionalEnemiesPerStep = 15;
    [Tooltip("Maximum alive enemies after growth; never reduces the starting Max NPC Count.")]
    [Min(1)] public int maximumScaledNpcCount = 240;
    [Tooltip("Active gameplay seconds before adding another enemy to each regular spawn batch.")]
    [Min(1f)] public float spawnBatchGrowthSeconds = 90f;
    [Min(1)] public int maximumSpawnBatchSize = 6;

    public float ActiveGameplaySeconds { get; private set; }
    public float CurrentSpawnInterval => (increaseSpawningOverTime
        ? Mathf.Max(Mathf.Min(spawnInterval, minimumSpawnInterval),
            spawnInterval / (1f + ActiveGameplaySeconds / Mathf.Max(1f, difficultyStepSeconds) * Mathf.Max(0f, spawnRateGrowthPerStep)))
        : spawnInterval) / ExpeditionMaps.Current.SpawnRate;
    public int CurrentMaxNpcCount => increaseSpawningOverTime
        ? Mathf.Min(Mathf.Max(maxNpcCount, maximumScaledNpcCount),
            maxNpcCount + Mathf.FloorToInt(ActiveGameplaySeconds / Mathf.Max(1f, difficultyStepSeconds)) * Mathf.Max(0, additionalEnemiesPerStep))
        : maxNpcCount;
    public int CurrentSpawnBatchSize => increaseSpawningOverTime
        ? Mathf.Min(Mathf.Max(1, maximumSpawnBatchSize),
            Mathf.Max(1, initialSpawnBatchSize) + Mathf.FloorToInt(ActiveGameplaySeconds / Mathf.Max(1f, spawnBatchGrowthSeconds)))
        : Mathf.Clamp(initialSpawnBatchSize, 1, Mathf.Max(1, maximumSpawnBatchSize));

    [Header("Room Activation")]
    public bool spawnWhenPlayerEntersRoom = true;
    [Min(1)] public int enemiesPerRoom = 6;
    [Tooltip("Keep spawning at Spawn Interval while the player remains inside a room.")]
    public bool continuousSpawningWhilePlayerInRoom = true;
    [Min(0.1f)] public float roomActivationRadius = 20f;
    [Tooltip("Maximum vertical distance for a room to be considered on the player's current floor.")]
    [Min(0.1f)] public float roomFloorTolerance = 6f;
    [Min(0.1f)] public float roomSpawnRadius = 12f;
    [Min(0f)] public float roomSpawnDelay = .15f;
    public string roomMarkerKind = "Room";

    [Header("Opening Wave")]
    [Tooltip("Spawn the first few enemies closer together, still respecting the alive cap and safe distances.")]
    [Min(1)] public int openingWaveSize = 6;
    [Min(.1f)] public float openingWaveInterval = .15f;

    [Header("Random NPC Stats")]
    [Min(1f)] public float minHealth = 60f;
    [Min(1f)] public float maxHealth = 240f;
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
        OverworldDirector.EnsureWorld();
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
            PlayerHealth health = playerTarget != null ? playerTarget.GetComponentInParent<PlayerHealth>(true) : null;
            WeaponInventory inventory = health != null ? health.GetComponent<WeaponInventory>() : null;
            if (Time.timeScale <= 0f || DungeonVictoryUI.IsOpen ||
                (!CoopSession.IsMultiplayer && health != null && (health.IsDead || health.IsChoosingUpgrade)) ||
                (!CoopSession.IsMultiplayer && inventory != null && (inventory.IsOpen || inventory.IsPaused)))
            {
                yield return null;
                continue;
            }

            ActiveGameplaySeconds += Time.deltaTime;

            if (spawnWhenPlayerEntersRoom)
            {
                ActivateEnteredRoom();
                SpawnForActivatedRoom();
            }
            else if (spawnedNpcs.Count < CurrentMaxNpcCount && Time.time >= nextGlobalSpawnTime)
            {
                int batchSize = CurrentSpawnBatchSize;
                for (int i = 0; i < batchSize && spawnedNpcs.Count < CurrentMaxNpcCount; i++)
                    if (!TrySpawnNpc()) break;
                nextGlobalSpawnTime = Time.time + CurrentSpawnInterval;
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
        RemoveDestroyedNpcs();
        if (rooms.Count == 0) CacheRooms();
        if (rooms.Count == 0) return false;
        int first = Random.Range(0, rooms.Count);
        for (int i = 0; i < rooms.Count; i++)
            if (TrySpawnNpcInRoom(rooms[(first + i) % rooms.Count].boundary)) return true;
        return false;
    }

    bool TrySpawnNpcInRoom(DungeonRoomBoundary room)
    {
        if (room == null || spawnedNpcs.Count >= CurrentMaxNpcCount) return false;
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
            {
                enemyHealth.SetDungeonFloor(GetFloorNumber(roomMarker));
                enemyHealth.progressionDropChance = EnemyHealth.DefaultProgressionDropChance;
            }

            ApplyRandomStats(npc, enemyAI);
            EnemyCharacterVisual.Ensure(npc);

            spawnedNpcs.Add(npc);
            return true;
        }

        return false;
    }

    static int GetFloorNumber(Transform roomMarker)
    {
        return OverworldDirector.MarkerFloor(roomMarker);
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
        if (CoopSession.IsMultiplayer)
        {
            foreach (RoomSpawnState room in rooms)
            {
                room.playerInside = false;
                if (room.boundary == null || room.marker == null) continue;
                foreach (Player teammate in CoopSession.Players())
                {
                    if (teammate.GetComponent<PlayerHealth>().IsDead || !room.boundary.Contains(teammate.transform.position)) continue;
                    room.playerInside = true;
                    if (!room.activated) { room.activated = true; room.nextSpawnTime = Time.time + roomSpawnDelay; }
                    break;
                }
            }
            return;
        }
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
                GameAudio.Local(GameSound.RoomEnter);
                closestRoom.activated = true;
                closestRoom.nextSpawnTime = Time.time + roomSpawnDelay;
            }
        }
    }

    void SpawnForActivatedRoom()
    {
        if (spawnedNpcs.Count >= CurrentMaxNpcCount)
            return;

        foreach (RoomSpawnState room in rooms)
        {
            bool reachedRoomLimit = !continuousSpawningWhilePlayerInRoom &&
                                    room.spawnedCount >= enemiesPerRoom;
            if (!room.playerInside || reachedRoomLimit ||
                room.marker == null || Time.time < room.nextSpawnTime)
                continue;

            int openingCount = Mathf.Min(Mathf.Max(1, openingWaveSize), Mathf.Max(1, enemiesPerRoom));
            int batchSize = CurrentSpawnBatchSize;
            if (room.spawnedCount < openingCount)
                batchSize = Mathf.Min(batchSize, openingCount - room.spawnedCount);
            if (!continuousSpawningWhilePlayerInRoom)
                batchSize = Mathf.Min(batchSize, enemiesPerRoom - room.spawnedCount);
            bool spawned = false;
            for (int i = 0; i < batchSize && spawnedNpcs.Count < CurrentMaxNpcCount; i++)
            {
                if (!TrySpawnNpcInRoom(room.boundary)) break;
                room.spawnedCount++;
                spawned = true;
            }

            room.nextSpawnTime = Time.time + (spawned && room.spawnedCount < openingCount
                ? Mathf.Min(CurrentSpawnInterval, Mathf.Max(.1f, openingWaveInterval)) : CurrentSpawnInterval);

            return;
        }
    }

    void ApplyRandomStats(GameObject npc, EnemyAI enemyAI)
    {
        float rolledHealth = Random.Range(minHealth, maxHealth);

        EnemyHealth enemyHealth = npc.GetComponent<EnemyHealth>();
        if (enemyHealth != null)
            enemyHealth.SetMaxHealth(rolledHealth * ExpeditionMaps.Current.Health);

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
        if (DungeonHazardDirector.IsUnsafe(position, 1f)) return false;
        if (CoopSession.IsMultiplayer)
            foreach (Player teammate in CoopSession.Players())
                if (Vector3.SqrMagnitude(position - teammate.transform.position) < minDistanceFromPlayer * minDistanceFromPlayer) return false;
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
        initialSpawnBatchSize = Mathf.Max(1, initialSpawnBatchSize);
        difficultyStepSeconds = Mathf.Max(1f, difficultyStepSeconds);
        spawnRateGrowthPerStep = Mathf.Max(0f, spawnRateGrowthPerStep);
        minimumSpawnInterval = Mathf.Max(.1f, minimumSpawnInterval);
        additionalEnemiesPerStep = Mathf.Max(0, additionalEnemiesPerStep);
        maximumScaledNpcCount = Mathf.Max(1, maximumScaledNpcCount);
        spawnBatchGrowthSeconds = Mathf.Max(1f, spawnBatchGrowthSeconds);
        maximumSpawnBatchSize = Mathf.Max(1, maximumSpawnBatchSize);
        openingWaveSize = Mathf.Max(1, openingWaveSize);
        openingWaveInterval = Mathf.Max(.1f, openingWaveInterval);
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
