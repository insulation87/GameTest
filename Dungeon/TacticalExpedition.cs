using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

[Serializable]
public sealed class ExpeditionProgress
{
    public int version = 1;
    public bool[] completed = new bool[6];
    public bool merchantRescued, treeRescued, altarDestroyed;
}

// Runtime encounters on the authored map. No scene/prefab asset or player save is edited on load.
[DefaultExecutionOrder(50)]
public sealed class TacticalExpedition : MonoBehaviour
{
    public static TacticalExpedition Instance { get; private set; }
    public ExpeditionProgress Progress { get; private set; } = new ExpeditionProgress();
    public string Objective { get; private set; }
    public Transform ObjectiveTarget { get; private set; }
    public int ActiveEncounter => active;
    public int CurrentWave => wave;
    int clientEnemyCount;
    public IReadOnlyList<EnemyHealth> EncounterEnemies => enemies;
    public bool Ready { get; private set; }
    static readonly string[] Names = { "回聲側襲試煉", "回聲包圍試煉", "草原試煉首領", "救援森林商人", "解救古樹", "摧毀魔王祭壇" };
    static readonly int[] Regions = { 1,1,1,2,3,3 };
    readonly TacticalMarker[] markers = new TacticalMarker[6];
    readonly List<EnemyHealth> enemies = new List<EnemyHealth>();
    readonly List<GameObject> transient = new List<GameObject>();
    OverworldDirector world;
    GameObject enemyPrefab;
    int active = -1, wave;
    float nextCheck, nextWave;
    bool wavePending;
    bool shuttingDown;
    TacticalMarker supplies;
    float nextSupply;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register() { Instance = null; SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded; }
    static void Loaded(Scene scene, LoadSceneMode mode) { Ensure(); }
    public static TacticalExpedition Ensure()
    {
        if (Instance != null) return Instance;
        var world = OverworldDirector.EnsureWorld();
        if (world == null) return null;
        return world.GetComponent<TacticalExpedition>() ?? world.gameObject.AddComponent<TacticalExpedition>();
    }
    void Awake() { Instance = this; }
    void Start()
    {
        world = GetComponent<OverworldDirector>();
        var spawner = FindFirstObjectByType<NPCSpawner>(); enemyPrefab = spawner != null ? spawner.npcPrefab : null;
        if (world == null || enemyPrefab == null) return;
        Vector3 start = FindFirstObjectByType<Player>()?.transform.position ?? world.RegionBounds[0].center;
        // The first course begins close to the actual spawn; later stations form a visible route.
        Vector3 forward = transform.forward;
        Vector3[] desired = { start + forward * 20, start + forward * 55, start + forward * 95,
            world.RegionBounds[1].center, world.RegionBounds[2].center - transform.right * 25,
            world.RegionBounds[2].center + transform.right * 25 };
        for (int i = 0; i < markers.Length; i++)
        {
            if (!Safe(desired[i], Regions[i], out Vector3 point))
                point = world.RegionBounds[Regions[i]-1].center;
            markers[i] = TacticalMarker.Create(Names[i], point, forward, i == 5 ? Color.magenta : GameUITheme.Gold, 7);
            markers[i].transform.SetParent(transform, true);
            if (i >= 3) BuildLandmark(markers[i].transform, i);
        }
        Ready = true; RefreshLandmarks();
    }
    bool Safe(Vector3 desired, int region, out Vector3 point)
    {
        point = desired;
        for (int ring = 0; ring < 8; ring++) for (int i = 0; i < 8; i++)
        {
            Vector3 probe = desired + Quaternion.Euler(0,i*45,0) * Vector3.forward * ring * 2.5f;
            if (!NavMesh.SamplePosition(probe, out var hit, 2, NavMesh.AllAreas) || world.RegionAt(hit.position) != region) continue;
            if (!Physics.Raycast(hit.position + Vector3.up * 2, Vector3.down, out var floor, 4, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            if (Vector3.Dot(floor.normal,Vector3.up) < .95f) continue;
            if (Physics.CheckCapsule(floor.point + Vector3.up * .7f, floor.point + Vector3.up * 1.5f, .5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            point = floor.point; return true;
        }
        return false;
    }
    void BuildLandmark(Transform parent,int id)
    {
        if (id == 3)
        {
            var model = PixelCharacterFactory.Create(parent,"Npc1"); model.name = "受困森林商人";
            model.transform.localPosition = new Vector3(0,0,2);
        }
        else if (id == 4)
        {
            Prop(parent,PrimitiveType.Cylinder,new Vector3(0,3,-5),new Vector3(1.7f,3,1.7f),new Color(.28f,.18f,.1f));
            Prop(parent,PrimitiveType.Sphere,new Vector3(0,6,-5),new Vector3(8,5,7),new Color(.2f,.65f,.45f));
        }
        else
        {
            Prop(parent,PrimitiveType.Cube,new Vector3(0,.4f,2),new Vector3(3,.8f,3),new Color(.2f,.15f,.3f));
            Prop(parent,PrimitiveType.Cube,new Vector3(0,1.5f,2),new Vector3(.8f,1.8f,.8f),new Color(.75f,.2f,.85f));
        }
    }
    static void Prop(Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Color color)
    {
        var node=GameObject.CreatePrimitive(type); node.transform.SetParent(parent,false); node.transform.localPosition=position; node.transform.localScale=scale;
        // Event art does not add invisible navigation or firing obstacles.
        node.GetComponent<Collider>().enabled=false;
        var renderer=node.GetComponent<Renderer>(); var tint=new MaterialPropertyBlock(); tint.SetColor("_Color",color); tint.SetColor("_BaseColor",color); renderer.SetPropertyBlock(tint);
    }
    public void GrantStarterKit(Player player)
    {
        var health = player.GetComponent<PlayerHealth>(); var inventory = player.GetComponent<WeaponInventory>();
        if (health == null || inventory == null || health.ExpeditionKitReceived || CoopSession.IsClient) return;
        health.ExpeditionKitReceived = true;
        string id = player.GetComponent<CharacterSkills>()?.Character.Id;
        WeaponType[] kit = id == "breaker" || id == "frost" ? new[] { WeaponType.FrostStaff, WeaponType.WarHammer, WeaponType.Longbow } :
            id == "engineer" || id == "pyromancer" || id == "sentinel" ? new[] { WeaponType.ArcaneFocus, WeaponType.FireStaff, WeaponType.SwordShield } :
            id == "ranger" || id == "gunner" ? new[] { WeaponType.Longbow, WeaponType.RepeatingCrossbow, WeaponType.FrostStaff } :
            new[] { CharacterWeapons.ForCharacter(id), WeaponType.Longbow, WeaponType.RepeatingCrossbow };
        // Only fill empty slots. Keep the player's own selected weapon and owned equipment.
        foreach (var type in kit)
        {
            WeaponItem existing = null;
            foreach (var item in inventory.Weapons) if (item.weaponType == type) { existing = item; break; }
            if (existing == null)
            {
                existing = CharacterWeapons.Create(type, 1, .35f);
                if (!inventory.AddWeapon(existing)) { WeaponLoot.Drop(existing, player.transform.position + player.transform.forward * 2); continue; }
            }
            bool equipped = false;
            foreach (var slot in inventory.EquippedSlots) if (slot == existing) equipped = true;
            if (!equipped) for (int slot = 0; slot < 3; slot++)
                if (inventory.EquippedSlots[slot] == null) { inventory.EquipToSlot(existing, slot); break; }
        }
        player.GetComponent<RogueTactics>()?.AddResonance(100);
        player.GetComponent<RogueTactics>()?.ShowNotice("草原戰術試煉：先射擊 → 留下回聲 → 滑行側襲 → 啟動。背包可查看武器連招。");
        if (health.CurrentLevel == 1) health.AddExperience(health.ExperienceToNextLevel);
    }
    void Update()
    {
        if (!Ready || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen) return;
        if (Time.time < nextCheck) return; nextCheck = Time.time + .2f;
        Player nearest = null; float best = float.PositiveInfinity;
        foreach (var player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (!CoopSession.IsClient && world.RegionAt(player.transform.position) == 1) GrantStarterKit(player);
            if (!CoopSession.LocalUI(player)) continue;
            var hp = player.GetComponent<PlayerHealth>(); if (hp == null || hp.IsDead) continue;
            float distance = (player.transform.position - markers[0].transform.position).sqrMagnitude;
            if (distance < best) { nearest = player; best = distance; }
        }
        RefreshObjective(nearest);
        if (CoopSession.IsClient) return;
        if (active >= 0)
        {
            bool livingPlayer = false;
            foreach (var player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                if (!player.IsDead && (player.transform.position - markers[active].transform.position).sqrMagnitude < 3600) livingPlayer = true;
            if (!livingPlayer) { AbortEncounter(); return; }
            enemies.RemoveAll(enemy => enemy == null || enemy.IsDead);
            if (enemies.Count == 0)
            {
                if (wave < WaveCount(active))
                {
                    if (!wavePending) { wavePending = true; nextWave = Time.time + 1.5f; }
                    if (Time.time >= nextWave) { SpawnWave(); wavePending = false; }
                }
                else CompleteEncounter();
            }
            return;
        }
        foreach (var player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            var hp = player.GetComponent<PlayerHealth>(); var inventory = player.GetComponent<WeaponInventory>();
            if (hp == null || hp.IsDead || hp.IsChoosingUpgrade || inventory != null && (inventory.IsOpen || inventory.IsPaused)) continue;
            for (int i = 0; i < markers.Length; i++)
                if (Available(i) && (player.transform.position - markers[i].transform.position).sqrMagnitude < 81)
                { TryBeginEncounter(i, player); return; }
            if (Progress.merchantRescued && world.RegionAt(player.transform.position) == 3 && supplies != null && Time.time >= nextSupply &&
                (player.transform.position - supplies.transform.position).sqrMagnitude < 16 && hp.CurrentHealth < hp.maxHealth)
            { hp.Heal(hp.maxHealth * .35f); nextSupply = Time.time + 20; player.GetComponent<RogueTactics>()?.ShowNotice("森林商人已抵達古林 · 補給恢復 35% 生命，20 秒後可再次補給"); }
        }
    }
    bool Available(int id) => !Progress.completed[id] && (id == 0 || id == 3 || id == 4 || id == 5 || Progress.completed[id-1]);
    static int WaveCount(int id) => id == 0 ? 2 : id == 1 ? 3 : 1;
    public bool TryBeginEncounter(int id, Player player)
    {
        if (CoopSession.IsClient || !Ready || id < 0 || id >= 6 || active >= 0 || !Available(id) || player == null ||
            world.RegionAt(player.transform.position) != Regions[id] || (player.transform.position - markers[id].transform.position).sqrMagnitude > 100) return false;
        if (id == 2 && !Progress.completed[1]) return false;
        active = id; wave = 0; wavePending = false;
        if (id < 2) BuildCover(markers[id].transform.position);
        SpawnWave(); RefreshLandmarks(); return true;
    }
    void BuildCover(Vector3 center)
    {
        // Two flanking rocks are useful both for echo ambushes and boss-charge practice.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 desired = center + transform.right * (side * 5f);
            if (!Safe(desired,1,out var point)) continue;
            var rock = GameObject.CreatePrimitive(PrimitiveType.Cube); rock.name = "試煉掩體 · 可誘導衝刺";
            rock.transform.position = point + Vector3.up * 1.25f; rock.transform.localScale = new Vector3(2,2.5f,4);
            rock.transform.rotation = transform.rotation;
            var obstacle = rock.AddComponent<NavMeshObstacle>(); obstacle.carving = true; obstacle.size = Vector3.one;
            rock.AddComponent<TacticalEncounterCover>();
            transient.Add(rock);
        }
    }
    void SpawnWave()
    {
        int id = active; wave++;
        int count = id == 2 ? 1 : id == 1 ? 6 : 5;
        Vector3 center = markers[id].transform.position;
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            float angle = id == 0 ? (i - (count - 1) * .5f) * 22f : i * 360f/count + wave*40f;
            Vector3 desired = center + Quaternion.AngleAxis(angle,Vector3.up) * transform.forward * (id == 2 ? 9 : 12);
            if (!Safe(desired, Regions[id], out var point)) continue;
            var node = Instantiate(enemyPrefab, point, Quaternion.identity); node.name = Names[id] + " 守衛 " + i;
            var enemy = node.GetComponent<EnemyHealth>();
            if (enemy == null) { Destroy(node); continue; }
            enemy.SetMaxHealth((id == 2 ? 900 : id < 2 ? 100 + wave*25 : 250) * 1.2f * ExpeditionMaps.Current.Health);
            enemy.SetLevelAndExperience(id == 2 ? 8 : id < 2 ? 3 + wave : 12 + id * 4, 5);
            enemy.SetDungeonFloor(Regions[id]); enemy.progressionDropChance = 0; // Mainline keys are granted once on completion.
            var ai = node.GetComponent<EnemyAI>(); ai?.AssignRoom(world.EnemyBoundaryAt(point));
            ai?.ConfigureLevel(enemy.EnemyLevel);
            if (ai != null) ai.target = CoopSession.ClosestLiving(point, FindFirstObjectByType<Player>()?.transform, null);
            enemies.Add(enemy); spawned++;
            if (id == 2)
            {
                node.AddComponent<TacticalTrialBoss>();
                var boss = node.GetComponent<BossController>() ?? node.AddComponent<BossController>();
                boss.Initialize(FindFirstObjectByType<Player>(), enemy, ai);
                node.GetComponent<EnemyHealthBarUI>()?.SetBossPhases(boss);
                BuildCover(center);
            }
        }
        if (spawned == 0) { wave--; wavePending = true; nextWave = Time.time + 3; }
        foreach (var player in FindObjectsByType<Player>(FindObjectsSortMode.None))
            player.GetComponent<RogueTactics>()?.ShowNotice(id == 0 ? "前方火線：先留下回聲，再從掩體側面繞背" :
                id == 1 ? "包圍戰：回聲守住一側，用滑行與主武器突破另一側" : id == 2 ? "試煉首領：繞背重擊破綻；把衝刺引向兩側掩體" : "擊敗事件守衛：必要鑰石保底，探索選擇會留下影響");
        RefreshLandmarks();
    }
    void CompleteEncounter()
    {
        int id = active; active = -1; Progress.completed[id] = true;
        if (id == 3) Progress.merchantRescued = true;
        if (id == 4) Progress.treeRescued = true;
        if (id == 5) Progress.altarDestroyed = true;
        var progress = FloorProgressionManager.Instance;
        int region = Regions[id];
        if (progress != null)
        {
            int reward = id == 0 || id == 1 ? 3 : id == 5 ? 0 : Mathf.Max(0,progress.RequiredItemsFor(region)-progress.CollectedFor(region));
            for (int i = 0; i < reward; i++) progress.Collect(region);
        }
        foreach (var player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            var inventory = player.GetComponent<WeaponInventory>(); inventory?.AddGold(id == 2 ? 150 : 60);
            player.GetComponent<RogueTactics>()?.AddResonance(100);
            player.GetComponent<RogueTactics>()?.ShowNotice(Names[id] + "完成 · 鑰石進度保底，選擇已記錄");
            if (id == 0 || id == 1)
            {
                var item = TacticalEquipment.Special(id == 0 ? ArmorSlot.Shoes : ArmorSlot.Pants, 3 + id*2);
                if (inventory == null || !inventory.AddArmor(item)) ArmorLoot.Drop(item, player.transform.position + Vector3.forward * 2);
            }
        }
        ClearTransient(); RefreshLandmarks();
        GameProgressSave.Instance?.SaveNow();
    }
    void ClearTransient() { foreach (var node in transient) if (node != null) Destroy(node); transient.Clear(); }
    void AbortEncounter()
    {
        foreach (var enemy in enemies) if (enemy != null) Destroy(enemy.gameObject);
        enemies.Clear(); active = -1; wavePending = false; ClearTransient(); RefreshLandmarks();
    }
    void RefreshLandmarks()
    {
        if (!Ready || shuttingDown) return;
        for (int i=0;i<markers.Length;i++) markers[i].SetText(Names[i] + (Progress.completed[i] ? "\n已完成" : i == active ? $"\n第 {wave}/{WaveCount(i)} 波" : Available(i) ? "\n靠近金圈開始 · 據點鑰石保底" : "\n先完成前一項試煉"));
        if (Progress.merchantRescued && supplies == null)
        {
            Vector3 point = world.RegionBounds[2].center - transform.forward * 20;
            if (Safe(point,3,out var safe)) point = safe;
            supplies = TacticalMarker.Create("獲救商人的古林補給攤",point,transform.forward,Color.green,3);
            supplies.transform.SetParent(transform,true); supplies.SetText("你救下的商人已抵達\n靠近恢復 35% 生命 · 冷卻 20 秒");
            var merchant=PixelCharacterFactory.Create(supplies.transform,"Npc1"); merchant.transform.localPosition=Vector3.right*1.5f;
            Prop(supplies.transform,PrimitiveType.Cube,new Vector3(0,.65f,1),new Vector3(3,1.3f,1),new Color(.45f,.27f,.12f));
        }
        if (!Progress.merchantRescued && supplies != null) { Destroy(supplies.gameObject); supplies = null; }
    }
    void RefreshObjective(Player player)
    {
        ObjectiveTarget = null; Objective = "";
        if (player == null) return;
        int region = world.RegionAt(player.transform.position);
        for (int i=0;i<markers.Length;i++)
            if (Regions[i] == region && (i == active || Available(i)))
            {
                ObjectiveTarget = markers[i].transform;
                Objective = Names[i] + (i == active ? $" · 第 {wave}/{WaveCount(i)} 波 · 剩餘 {(CoopSession.IsClient ? clientEnemyCount : enemies.Count)} 名" : " · 靠近開始") +
                    $" · {Vector3.Distance(player.transform.position, markers[i].transform.position):0}m";
                return;
            }
    }
    public ExpeditionProgress CaptureProgress() => JsonUtility.FromJson<ExpeditionProgress>(JsonUtility.ToJson(Progress));
    public void RestoreProgress(ExpeditionProgress data)
    {
        AbortEncounter();
        Progress = data != null && data.version == 1 && data.completed != null && data.completed.Length == 6 ?
            JsonUtility.FromJson<ExpeditionProgress>(JsonUtility.ToJson(data)) : new ExpeditionProgress();
        // Derive consequences from completed events; malformed optional flags cannot unlock rewards.
        Progress.merchantRescued = Progress.completed[3]; Progress.treeRescued = Progress.completed[4]; Progress.altarDestroyed = Progress.completed[5];
        RefreshLandmarks();
    }
    public void ApplyCoopState(ExpeditionProgress data, int encounter, int currentWave, int enemyCount)
    {
        if (!CoopSession.IsClient || data == null || data.completed == null || data.completed.Length != 6) return;
        Progress = data; active = Mathf.Clamp(encounter,-1,5); wave = Mathf.Clamp(currentWave,0,3); clientEnemyCount = Mathf.Clamp(enemyCount,0,64);
        RefreshLandmarks();
    }
    void OnDestroy()
    {
        shuttingDown=true; AbortEncounter();
        foreach(var marker in markers) if(marker!=null) Destroy(marker.gameObject);
        if(supplies!=null) Destroy(supplies.gameObject);
        if (Instance == this) Instance = null;
    }
}

public sealed class TacticalTrialBoss : MonoBehaviour { }
public sealed class TacticalEncounterCover : MonoBehaviour { }
