using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// One local slot per account. Only stable gameplay state is serialized; scene objects are rebuilt on load.
public sealed class GameProgressSave : MonoBehaviour
{
    public static string SavePath => CoopSession.SavePath;
    public static GameProgressSave Instance { get; private set; }
    public static bool StartFresh { get; set; }
    public string StoragePath { get; set; } // Also allows isolated tests without touching player saves.
    public string LastMessage { get; private set; } = "每 30 秒自動儲存";
    public bool Ready { get; private set; }
    Player player;
    PlayerHealth health;
    WeaponInventory inventory;
    Vector3 safeGround;
    bool hasSafeGround, blockOverwrite, skipSaving;
    float nextSave, nextGroundCheck;
    string sessionPath, initialAccountPath;
    // Pin this scene's save file so an account change cannot redirect a late save.
    string PathName => sessionPath ?? (string.IsNullOrEmpty(StoragePath) ? SavePath : StoragePath);

    [Serializable] sealed class Envelope { public string payload, checksum; }

    void Awake() { if (!CoopSession.IsMultiplayer) Instance = this; initialAccountPath = SavePath; }
    IEnumerator Start()
    {
        sessionPath = string.IsNullOrEmpty(StoragePath) ? initialAccountPath : StoragePath;
        // Wait for Player.Start, inventory initialization and the map's hazards.
        yield return null;
        player = GetComponent<Player>(); health = GetComponent<PlayerHealth>();
        inventory = GetComponent<WeaponInventory>();
        if (CoopSession.IsMultiplayer)
        {
            var actor = GetComponent<CoopActor>();
            if (actor == null || !actor.Local || actor.LocalIndex != 0 || CoopSession.IsClient)
            { skipSaving = true; Ready = true; yield break; }
            Instance = this;
        }
        if (player == null || health == null || inventory == null) yield break;
        FloorProgressionManager.EnsureInstance();
        if (FindFirstObjectByType<MedievalDungeonKit.DungeonViewSettings>() != null)
        {
            if (FindFirstObjectByType<ExplorationMinimap>() == null)
                new GameObject("Exploration Minimap").AddComponent<ExplorationMinimap>();
            if (GetComponent<FallDeathZone>() == null) gameObject.AddComponent<FallDeathZone>();
        }
        bool fresh = StartFresh; StartFresh = false;
        bool hasProgress = TryRead(PathName, out GameProgressState data, out string error);
        if (hasProgress && data.runEnded) fresh = true;
        if (hasProgress)
        {
            // A fresh run retains only its wallet and unlock count; default weapons stay fresh.
            if (fresh) inventory.RestoreWallet(data.inventory.gold);
        }
        if (!fresh && hasProgress && data.preparingRun && data.scene == gameObject.scene.name)
        {
            // The camp checkpoint contains this run's new loadout, never the previous map/health.
            inventory.RestoreProgress(data.inventory);
            GetComponent<CharacterSkills>()?.RestoreProgress(data.character);
            fresh = true;
        }
        else if (!fresh && hasProgress)
        {
            if (data.scene == gameObject.scene.name) Restore(data);
            else { blockOverwrite = true; LastMessage = "存檔屬於其他地圖，已保留原存檔"; }
        }
        else if (!fresh && (File.Exists(PathName) || File.Exists(PathName + ".bak")))
        {
            blockOverwrite = true;
            LastMessage = "存檔無法讀取，已保留原檔；可從主選單開始新遊戲";
            Debug.LogWarning(LastMessage);
        }
        if (hasProgress) inventory.RestoreShopProgress(data.weaponPurchases);
        Ready = true;
        UpdateSafeGround();
        nextSave = Time.unscaledTime + 30f;
        if (fresh) SaveNow();
    }

    void Update()
    {
        if (!Ready) return;
        if (Time.unscaledTime >= nextGroundCheck)
        { nextGroundCheck = Time.unscaledTime + .5f; UpdateSafeGround(); }
        if (Time.timeScale > 0f && Time.unscaledTime >= nextSave)
        { nextSave = Time.unscaledTime + 30f; SaveNow(); }
    }

    bool FindSafeGround(Vector3 near, out Vector3 ground)
    {
        ground = default;
        RaycastHit[] hits = Physics.RaycastAll(near + Vector3.up * .3f, Vector3.down, 4f,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.normal.y < .7f || hit.collider.attachedRigidbody != null ||
                DungeonHazardDirector.IsUnsafe(hit.point, 1f)) return false;
            if (!player.TryGetTeleportPosition(hit.point, transform.rotation, out _)) return false;
            ground = hit.point;
            return true;
        }
        return false;
    }

    void UpdateSafeGround()
    {
        if (health == null || health.IsDead) return;
        if (FindSafeGround(transform.position, out Vector3 ground))
        { safeGround = ground; hasSafeGround = true; }
    }

    public bool SaveNow()
    {
        if (skipSaving || CoopSession.IsClient || CoopSession.IsMultiplayer && Instance != this) return false;
        if (!Ready || blockOverwrite || health == null || health.IsDead)
        { if (!blockOverwrite) LastMessage = "目前無法儲存，已保留上次進度"; return false; }
        UpdateSafeGround();
        if (!hasSafeGround) { LastMessage = "請站在安全地面後再儲存"; return false; }
        var data = new GameProgressState {
            worldLayoutVersion = FindFirstObjectByType<OverworldDirector>() != null ? OverworldDirector.LayoutVersion : 0,
            scene = gameObject.scene.name, savedUtc = DateTime.UtcNow.ToString("O"),
            ground = safeGround, yaw = transform.eulerAngles.y,
            health = health.CaptureProgress(), inventory = inventory.CaptureProgress(),
            floorKeys = FloorProgressionManager.Instance != null ? FloorProgressionManager.Instance.CaptureProgress() : new int[4],
            explored = FindFirstObjectByType<ExplorationMinimap>()?.CaptureProgress(),
            tactics = GetComponent<RogueTactics>()?.CaptureProgress(),
            character = GetComponent<CharacterSkills>()?.CaptureProgress(),
            expedition = TacticalExpedition.Instance?.CaptureProgress(),
            weaponPurchases = inventory.WeaponPurchaseCount
        };
        BossSpawner spawner = FindFirstObjectByType<BossSpawner>();
        if (spawner != null)
        {
            data.bossSpawned = spawner.HasSpawned;
            EnemyHealth boss = spawner.CurrentBoss != null ? spawner.CurrentBoss.GetComponent<EnemyHealth>() : null;
            data.bossDefeated = spawner.HasSpawned && (boss == null || boss.IsDead);
            if (boss != null) { data.bossHealth = boss.CurrentHealth; data.bossMaxHealth = boss.maxHealth; data.bossPhase = spawner.CurrentBoss.Phase; }
        }
        bool success = TryWrite(PathName, data, out string error);
        LastMessage = success ? "進度已儲存 " + DateTime.Now.ToString("HH:mm:ss") : "儲存失敗，請檢查磁碟空間";
        if (!success) Debug.LogWarning("無法儲存遊戲進度：" + error);
        return success;
    }

    void Restore(GameProgressState data)
    {
        TacticalExpedition.Ensure()?.RestoreProgress(data.expedition);
        FloorProgressionManager.Instance?.RestoreProgress(data.floorKeys, data.bossSpawned);
        inventory.RestoreProgress(data.inventory);
        Quaternion rotation = Quaternion.Euler(0, data.yaw, 0);
        var world = FindFirstObjectByType<OverworldDirector>();
        Vector3 savedGround = world != null && data.worldLayoutVersion == 0
            ? world.MigrateDungeonPosition(data.ground) : data.ground;
        // Re-check collision after the current map's traps have been built.
        if (FindSafeGround(savedGround + Vector3.up, out Vector3 ground) &&
            player.TryGetTeleportPosition(ground, rotation, out Vector3 position))
        {
            player.Teleport(position, rotation);
            health.SetSavedRespawn(position, rotation);
            safeGround = ground; hasSafeGround = true;
        }
        FindFirstObjectByType<BossSpawner>()?.RestoreProgress(data);
        FindFirstObjectByType<ExplorationMinimap>()?.RestoreProgress(data.explored);
        health.RestoreProgress(data.health);
        GetComponent<RogueTactics>()?.RestoreProgress(data.tactics);
        GetComponent<CharacterSkills>()?.RestoreProgress(data.character);
        GetComponent<CombatMomentum>()?.ResetCombat();
        LastMessage = "已讀取遊戲進度";
    }

    // Death/unsafe ground can prevent a full map save. Still settle the latest wallet and unlocks
    // into the last valid snapshot, without reviving a dead character or replacing safe positions.
    public bool SaveEconomyNow()
    {
        if (!Ready || skipSaving || blockOverwrite || inventory == null || CoopSession.IsMultiplayer ||
            !TryRead(PathName, out var data, out _)) return false;
        data.inventory.gold = inventory.Gold;
        data.weaponPurchases = inventory.WeaponPurchaseCount;
        data.inventory.weaponPurchases = inventory.WeaponPurchaseCount;
        data.savedUtc = DateTime.UtcNow.ToString("O");
        return TryWrite(PathName, data, out _);
    }
    public bool EndRun()
    {
        if (!SaveNow() || !TryRead(PathName, out var data, out _)) return false;
        data.runEnded = true;
        return TryWrite(PathName, data, out _);
    }
    void OnApplicationPause(bool paused) { if (paused && Ready && !SaveNow()) SaveEconomyNow(); }
    void OnApplicationQuit() { if (Ready && !SaveNow()) SaveEconomyNow(); }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public static bool HasSave => TryRead(SavePath, out _, out _);
    public static bool HasActiveRun => TryRead(SavePath, out var data, out _) && !data.runEnded;
    static string Hash(string value)
    {
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
    public const int MaximumSaveBytes = 4000000;
    // Local saves share version, content and checksum validation, including backup recovery.
    public static bool TryDecode(string json, out GameProgressState data, out string error)
    {
        data = null; error = null;
        try
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaximumSaveBytes)
                throw new InvalidDataException("存檔大小無效");
            Envelope envelope = JsonUtility.FromJson<Envelope>(json);
            if (envelope == null || string.IsNullOrEmpty(envelope.payload) || Hash(envelope.payload) != envelope.checksum)
                throw new InvalidDataException("存檔檢查失敗");
            var parsed = JsonUtility.FromJson<GameProgressState>(envelope.payload);
            if (!Valid(parsed)) throw new InvalidDataException("存檔版本或內容不相容");
            data = parsed; return true;
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException)
        { error = ex.Message; return false; }
    }
    public static bool TryEncode(GameProgressState data, out string json, out string error)
    {
        json = null; error = null;
        try
        {
            if (!Valid(data)) throw new InvalidDataException("進度內容無效");
            string payload = JsonUtility.ToJson(data);
            string encoded = JsonUtility.ToJson(new Envelope { payload = payload, checksum = Hash(payload) });
            if (Encoding.UTF8.GetByteCount(encoded) > MaximumSaveBytes) throw new InvalidDataException("存檔過大");
            json = encoded; return true;
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException)
        { error = ex.Message; return false; }
    }
    public static bool TryRead(string path, out GameProgressState data, out string error)
    {
        data = null; error = null;
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                if (new FileInfo(candidate).Length > MaximumSaveBytes) throw new InvalidDataException("存檔過大");
                if (TryDecode(File.ReadAllText(candidate), out data, out error)) return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { error = ex.Message; }
        }
        return false;
    }
    public static bool TryWrite(string path, GameProgressState data, out string error)
    {
        error = null;
        try
        {
            if (!TryEncode(data, out string json, out error)) return false;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { byte[] bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(path))
            {
                // A damaged primary must never replace the good backup during recovery.
                bool primaryValid = false;
                try {
                    primaryValid = new FileInfo(path).Length <= MaximumSaveBytes && TryDecode(File.ReadAllText(path), out _, out _);
                } catch (ArgumentException) { }
                File.Replace(path + ".tmp", path, primaryValid ? path + ".bak" : null);
            }
            else File.Move(path + ".tmp", path);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        { error = ex.Message; return false; }
    }
    static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
    static bool Valid(GameProgressState d)
    {
        if (d == null || d.version != 1 || d.weaponPurchases < 0 || string.IsNullOrEmpty(d.scene) || d.health == null || d.inventory == null ||
            d.floorKeys == null || d.floorKeys.Length != 4 || !Finite(d.ground.x) || !Finite(d.ground.y) || !Finite(d.ground.z) || !Finite(d.yaw)) return false;
        PlayerProgress h = d.health;
        if (h.health <= 0 || h.maxHealth < 1 || h.level < 1 || !Finite(h.health) || !Finite(h.maxHealth) || !Finite(h.armor) ||
            !Finite(h.maxArmor) || !Finite(h.armorRegeneration) || !Finite(h.attack) || !Finite(h.speed) || !Finite(h.criticalChance) || !Finite(h.criticalDamage)) return false;
        InventoryProgress i = d.inventory;
        if (i.weaponPurchases < 0 || i.weapons == null || i.weapons.Length == 0 || i.weapons.Length > 10000 || i.slots == null || i.slots.Length != 3 ||
            i.keys == null || i.keys.Length != 4 || !Finite(i.healAmount)) return false;
        bool equipped = false;
        foreach (int slot in i.slots) { if (slot < -1 || slot >= i.weapons.Length) return false; equipped |= slot >= 0; }
        if (!equipped) return false;
        foreach (WeaponItem w in i.weapons)
            if (w == null || !Enum.IsDefined(typeof(WeaponType), w.weaponType) || !Finite(w.damage) || !Finite(w.quality) ||
                !Finite(w.fireInterval) || !Finite(w.projectileSpeed) || !Finite(w.spreadAngle) || !Finite(w.explosionRadius)) return false;
        if (i.armorItems != null)
        {
            if (i.armorItems.Length > 10000 || i.armorSlots == null || i.armorSlots.Length != 4) return false;
            foreach (ArmorItem item in i.armorItems)
                if (item == null || !Enum.IsDefined(typeof(ArmorSlot), item.slot) || item.level < 1 || item.level > 100 ||
                    !Enum.IsDefined(typeof(TacticalAffix),item.affix) || !Finite(item.quality) || item.quality < 0 || item.quality > 1 || !Finite(item.armorBonus) || item.armorBonus < 0) return false;
            for (int slot = 0; slot < 4; slot++)
                if (i.armorSlots[slot] < -1 || i.armorSlots[slot] >= i.armorItems.Length ||
                    i.armorSlots[slot] >= 0 && i.armorItems[i.armorSlots[slot]].slot != (ArmorSlot)slot) return false;
        }
        return !d.bossSpawned || d.bossDefeated || (d.bossPhase >= 1 && d.bossPhase <= 3 &&
            Finite(d.bossHealth) && Finite(d.bossMaxHealth) && d.bossHealth > 0 && d.bossMaxHealth >= d.bossHealth);
    }
}
