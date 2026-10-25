using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1100)]
public sealed partial class CoopSession : MonoBehaviour
{
    public static CoopSession Instance { get; private set; }
    public static CoopMode Mode { get; private set; }
    public static bool IsMultiplayer => IsOnline;
    public static bool IsOnline => Mode == CoopMode.Host || IsClient;
    public static bool IsClient => Mode == CoopMode.Client;
    public static string LastError { get; private set; }
    public static bool IsInternet { get; private set; }
    public static string RoomCode { get; private set; }
    public static string SavePath => IsMultiplayer ? Path.Combine(Path.GetDirectoryName(LocalAccountProgress.ActivePath), "coop-v1.json") : LocalAccountProgress.ActivePath;
    public static bool ApplyingSnapshot { get; private set; }
    public static bool LocalUI(Component owner) => !IsMultiplayer || CoopInput.Actor(owner) == null || CoopInput.Actor(owner).Local;
    public static bool Simulates(Component owner) => !IsClient;
    public static bool Presents(Camera camera)
    {
        if (!IsMultiplayer || Instance == null) return false;
        foreach (var actor in Instance.actors.Values)
            if (actor != null && actor.Local)
            {
                var views = actor.GetComponent<CameraSwitch>();
                if (views != null && views.ActiveCamera == camera) return true;
            }
        return false;
    }
    readonly Dictionary<int, CoopActor> actors = new Dictionary<int, CoopActor>();
    readonly Dictionary<int, string> joined = new Dictionary<int, string>();
    readonly Dictionary<int, float> pendingPeers = new Dictionary<int, float>();
    readonly Dictionary<int, float> lastCommand = new Dictionary<int, float>();
    readonly Dictionary<int, CoopClientPolicy.Rate> clientRates = new Dictionary<int, CoopClientPolicy.Rate>();
    readonly Dictionary<int, HashSet<int>> sentMeshes = new Dictionary<int, HashSet<int>>();
    ICoopTransport transport;
    CoopWorldMirror mirror;
    GameObject roomHud;
    string sceneName, password, firstCharacter;
    GameObject template;
    CoopActor localActor;
    int localId;
    bool sceneReady, connected, welcomeReceived;
    bool mapRequested, returningToMenu;
    float nextSnapshot, connectionDeadline;
    Action onConnected;
    Action<string> onFailure;
    CoopInputFrame outgoingInput = new CoopInputFrame();
    float nextInput;
    CoopSnapshot pendingSnapshot;
    readonly Dictionary<int, string> lastInventory = new Dictionary<int, string>();
    public int PlayerCount => InCamp ? campPlayers.Count : actors.Count;
    public int ListeningPort => transport != null ? transport.BoundPort : 0;
    public bool SceneReady => sceneReady;
    internal string RoomInvitePassword => password;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Instance = null; Mode = CoopMode.SinglePlayer; LastError = null; ApplyingSnapshot = false; IsInternet = false; RoomCode = null; }
    public static CoopSession Ensure()
    {
        if (Instance == null) new GameObject("Cooperative Session").AddComponent<CoopSession>();
        return Instance;
    }
    void Awake() { Instance = this; DontDestroyOnLoad(gameObject); SceneManager.sceneLoaded += SceneLoaded; }
    public static void SinglePlayer()
    {
        if (Instance != null) Instance.StopSession();
        Mode = CoopMode.SinglePlayer;
    }
    [Obsolete("Local split-screen has been removed. Use SinglePlayer or StartHost/Join.")]
    public static bool StartSplit(string scene, string first, string second)
    {
        // Old editor review entry points still compile, but cannot launch the removed mode.
        LastError = "同機雙人模式已移除，請選擇單人或網路合作。";
        return false;
    }
    public static bool StartHost(string scene, string character, int port, string roomPassword, out string error)
    {
        error = null;
        if (!CloudAccountService.EnsureInstance().CanPlay) { error = "請登入帳號或選擇遊客模式。"; return false; }
        var session = Ensure(); session.StopSession();
        try { var lan = new CoopTransport(); session.transport = lan; lan.Listen(port); }
        catch (Exception ex) { session.StopSession(); error = "無法開啟房間：" + ex.Message; return false; }
        Mode = CoopMode.Host; session.Configure(scene, character, roomPassword);
        if (session.Launch()) return true;
        session.StopSession(); error = "地圖尚未加入 Build Settings"; return false;
    }
    public static void Join(string scene, string character, string host, int port, string roomPassword, Action success, Action<string> failure)
    {
        if (!CloudAccountService.EnsureInstance().CanPlay) { failure?.Invoke("請登入帳號或選擇遊客模式。"); return; }
        var session = Ensure(); session.StopSession(); Mode = CoopMode.Client;
        session.Configure(scene, character, roomPassword);
        session.onConnected = success; session.onFailure = failure;
        session.connectionDeadline = Time.unscaledTime + 60;
        var lan = new CoopTransport(); session.transport = lan; lan.Connect(host, port);
    }
    public static bool InternetConfigured => CloudRelayConfig.Load().IsValid;
    public static void StartInternetHost(string scene, string character, string roomPassword, Action success, Action<string> failure)
        => Internet(scene, character, null, roomPassword, success, failure);
    public static void JoinInternet(string scene, string character, string code, string roomPassword, Action success, Action<string> failure)
        => Internet(scene, character, code, roomPassword, success, failure);
    static void Internet(string scene, string character, string code, string roomPassword, Action success, Action<string> failure)
    {
        var account = CloudAccountService.EnsureInstance();
        if (!account.IsSignedIn || account.IsBusy) { failure?.Invoke("登入帳號後才能使用網際網路房間。"); return; }
        var config = CloudRelayConfig.Load();
        if (!config.IsValid) { failure?.Invoke("網際網路房間服務尚未啟用，請聯絡遊戲管理員。"); return; }
        OpenInternet(new Uri(config.relayUrl), scene, character, code, roomPassword, success, failure);
    }
#if UNITY_EDITOR || OVERWORLD_VALIDATION
    internal static void ReviewInternet(Uri endpoint, string scene, string character, string code, string roomPassword, Action success, Action<string> failure)
    {
        if (endpoint.Scheme != "ws" || !System.Net.IPAddress.TryParse(endpoint.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip) ||
            !CloudAccountService.EnsureInstance().IsSignedIn) throw new InvalidOperationException("Isolated authenticated loopback review only.");
        OpenInternet(endpoint, scene, character, code, roomPassword, success, failure);
    }
#endif
    static void OpenInternet(Uri endpoint, string scene, string character, string code, string roomPassword, Action success, Action<string> failure)
    {
        if (!Application.CanStreamedLevelBeLoaded(scene) || !ValidCharacter(character)) { failure?.Invoke("地圖或角色無效。"); return; }
        if (code != null)
        {
            code = code.Trim().ToUpperInvariant();
            if (code.Length != 10) { failure?.Invoke("請輸入 10 碼房間代碼。"); return; }
            foreach (char c in code) if ("23456789ABCDEFGHJKLMNPQRSTUVWXYZ".IndexOf(c) < 0) { failure?.Invoke("房間代碼無效。"); return; }
        }
        var session = Ensure(); session.StopSession(); Mode = code == null ? CoopMode.Host : CoopMode.Client; IsInternet = true;
        session.Configure(scene, character, roomPassword); session.onConnected = success; session.onFailure = failure;
        session.connectionDeadline = Time.unscaledTime + 60;
        session.transport = new CloudRelayTransport(endpoint, CloudAccountService.EnsureInstance().AccessToken, code == null, code);
    }
    void Configure(string scene, string first, string roomPassword)
    {
        sceneName = GameSceneFlow.TradingHubScene; firstCharacter = first; password = roomPassword ?? ""; LastError = null;
        inCamp = true;
        if (!IsClient) AddCampPlayer(0, first);
    }
    bool Launch()
    {
        CharacterRoster.SelectForNewRun(firstCharacter);
        // Cooperative sessions start a separate run. Never reset the single-player slot.
        GameProgressSave.StartFresh = true;
        mapRequested = true;
        if (GameSceneFlow.Load(sceneName)) return true;
        StopSession(); return false;
    }
    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsMultiplayer) return;
        if (scene.name == GameSceneFlow.LoginScene || scene.name == GameSceneFlow.MenuScene)
        { StopSession(); return; }
        if (scene.name == sceneName)
        {
            if (InCamp) StartCoroutine(PrepareCamp()); else StartCoroutine(PrepareScene());
        }
    }
    IEnumerator PrepareScene()
    {
        sceneReady = false;
        Player primary = FindFirstObjectByType<Player>();
        if (primary == null) { Fail("地圖缺少玩家物件"); yield break; }
        // Include detached FPS/TPS rigs in a dormant template; Unity remaps their references.
        Transform fps = primary.fpsCamera != null ? primary.fpsCamera.transform.root : null;
        Transform tps = primary.tpsCamera != null ? primary.tpsCamera.transform.root : null;
        Transform fpsParent = fps != null ? fps.parent : null, tpsParent = tps != null ? tps.parent : null;
        if (fps != null && fps != primary.transform) fps.SetParent(primary.transform, true);
        if (tps != null && tps != primary.transform && tps != fps) tps.SetParent(primary.transform, true);
        var holder = new GameObject("Dormant Coop Template"); holder.SetActive(false);
        template = Instantiate(primary.gameObject, holder.transform); template.SetActive(false);
        foreach (Canvas canvas in template.GetComponentsInChildren<Canvas>(true)) if (canvas != null) DestroyImmediate(canvas.gameObject);
        foreach (PixelArtCamera pixels in template.GetComponentsInChildren<PixelArtCamera>(true)) DestroyImmediate(pixels);
        if (fps != null && fps != primary.transform) fps.SetParent(fpsParent, true);
        if (tps != null && tps != primary.transform && tps != fps) tps.SetParent(tpsParent, true);
        localId = IsClient ? localId : 0;
        localActor = primary.gameObject.AddComponent<CoopActor>();
        localActor.Id = localId; localActor.Local = true; localActor.LocalIndex = 0; localActor.CharacterId = firstCharacter;
        actors.Add(localId, localActor);
        // Clients skip save initialization, so their map objectives and minimap must
        // be initialized by the session before the first progression snapshot arrives.
        FloorProgressionManager.EnsureInstance();
        if (FindFirstObjectByType<MedievalDungeonKit.DungeonViewSettings>() != null &&
            FindFirstObjectByType<ExplorationMinimap>() == null)
            new GameObject("Exploration Minimap").AddComponent<ExplorationMinimap>();
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            bool owned = false;
            foreach (var actor in actors.Values) foreach (var view in actor.Cameras()) if (camera == view) owned = true;
            if (!owned && camera.targetTexture == null && camera.GetComponent<PixelArtCamera>() == null)
            { camera.enabled = false; if (camera.TryGetComponent(out AudioListener audio)) audio.enabled = false; }
        }
        if (IsClient)
        {
            foreach (NPCSpawner spawner in FindObjectsByType<NPCSpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
            foreach (BossSpawner spawner in FindObjectsByType<BossSpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
        }
        if (mirror == null) mirror = gameObject.AddComponent<CoopWorldMirror>();
        yield return null;
        yield return null;
        if (!IsClient) ApplyCampUpgrade(localActor);
        if (IsClient) mirror.HideClientSimulation();
        sceneReady = true;
        roomHud = new GameObject("Cooperative Room HUD", typeof(RectTransform), typeof(CoopRoomHud));
        roomHud.transform.SetParent(transform, false);
        if (Mode == CoopMode.Host)
            foreach (var peer in new List<int>(joined.Keys)) Welcome(peer);
        if (pendingSnapshot != null) { Apply(pendingSnapshot); pendingSnapshot = null; }
    }
    public CoopActor SpawnActor(int id, string character, bool local = false, int localIndex = 0)
    {
        if (local || localIndex != 0)
            throw new InvalidOperationException("Each computer supports one local player; additional actors must be remote peers.");
        if (actors.TryGetValue(id, out var existing)) return existing;
        var clone = Instantiate(template); clone.name = "Coop Player " + (id + 1);
        var health = clone.GetComponent<PlayerHealth>(); health.respawnCanvas = null; health.respawnText = null; health.respawnPoint = null;
        var actor = clone.AddComponent<CoopActor>(); actor.Id = id; actor.Local = false; actor.LocalIndex = 0; actor.CharacterId = character;
        Vector3 spawn = localActor.transform.position;
        // Find a nearby unoccupied location instead of stacking teammate colliders.
        var original = localActor.GetComponent<Player>();
        for (int i = 0; i < 16; i++)
        {
            Vector3 near = spawn + Quaternion.Euler(0, i * 45, 0) * Vector3.forward * (2 + i / 8);
            if (Physics.Raycast(near + Vector3.up * 2, Vector3.down, out var hit, 5, Physics.AllLayers, QueryTriggerInteraction.Ignore) &&
                original.TryGetTeleportPosition(hit.point, original.transform.rotation, out var safe)) { spawn = safe; break; }
        }
        clone.transform.position = spawn;
        actors.Add(id, actor); clone.SetActive(true);
        if (!IsClient) StartCoroutine(ApplyCampUpgradeAfterStart(actor));
        return actor;
    }
    void Update()
    {
        if (!IsMultiplayer || returningToMenu) return;
        if (IsInternet)
        {
            var account = CloudAccountService.EnsureInstance();
            if (!account.IsSignedIn) { Fail("登入已失效，請重新登入後再加入房間。"); return; }
            (transport as CloudRelayTransport)?.SetAccessToken(account.AccessToken);
            if (!sceneReady && Time.unscaledTime > connectionDeadline) { Fail("網際網路房間連線逾時。"); return; }
        }
        if (transport != null)
        {
            int budget = 128;
            while (budget-- > 0 && transport != null && transport.TryPoll(out var item)) Handle(item);
            if (transport == null) return;
            foreach (var peer in new List<int>(pendingPeers.Keys))
                if (Time.unscaledTime - pendingPeers[peer] > 12) { transport.Disconnect(peer); pendingPeers.Remove(peer); }
        }
        if (IsClient && !welcomeReceived && Time.unscaledTime > connectionDeadline) { Fail("房間尚未就緒或連線逾時"); return; }
        if (InCamp) { UpdateCamp(); return; }
        if (!sceneReady || GameSceneFlow.IsLoading) return;
        if (IsClient && localActor != null)
        {
            var frame = localActor.Frame;
            outgoingInput.move = frame.move; outgoingInput.held = frame.held;
            outgoingInput.pressed |= frame.pressed; outgoingInput.released |= frame.released;
            var views = localActor.GetComponent<CameraSwitch>();
            outgoingInput.firstPerson = views.IsFirstPerson;
            outgoingInput.yaw = views.IsFirstPerson ? views.fpsController.GetYaw() : views.tpsController.GetYaw();
            outgoingInput.pitch = views.IsFirstPerson ? views.fpsController.GetPitch() : views.tpsController.GetPitch();
            if (Time.unscaledTime >= nextInput)
            { nextInput = Time.unscaledTime + 1f / 30; Send(0, new CoopPacket { kind = "input", input = outgoingInput }); outgoingInput.ClearEdges(); }
        }
        if (Mode == CoopMode.Host && Time.unscaledTime >= nextSnapshot)
        {
            nextSnapshot = Time.unscaledTime + .05f;
            var snapshot = Capture();
            foreach (int peer in new List<int>(joined.Keys))
            {
                if (!sentMeshes.TryGetValue(peer, out var sent)) sentMeshes[peer] = sent = new HashSet<int>();
                foreach (var mesh in mirror.Meshes)
                    if (sent.Add(mesh.id)) Send(peer, new CoopPacket { kind = "mesh", mesh = mesh });
                Send(peer, new CoopPacket { kind = "state", snapshot = snapshot });
            }
        }
    }
    void Handle(CoopTransport.Event item)
    {
        if (item.fatal) { Fail(item.error); return; }
        if (!string.IsNullOrEmpty(item.roomCode))
        {
            RoomCode = item.roomCode;
            if (Mode == CoopMode.Host)
            {
                var callback = onConnected; onConnected = null;
                if (Launch()) callback?.Invoke(); else Fail("目前無法載入地圖。");
            }
            return;
        }
        if (item.disconnected)
        {
            pendingPeers.Remove(item.peer); joined.Remove(item.peer); sentMeshes.Remove(item.peer); lastCommand.Remove(item.peer); clientRates.Remove(item.peer);
            if (InCamp && Mode == CoopMode.Host) { campPlayers.Remove(item.peer); ResetCampReady(); BroadcastCamp(); }
            if (Mode == CoopMode.Host && actors.TryGetValue(item.peer, out var actor)) { Destroy(actor.gameObject); actors.Remove(item.peer); }
            if (IsClient) Fail("連線中斷：" + item.error);
            return;
        }
        if (item.connected)
        {
            if (IsClient) { connected = true; Send(0, new CoopPacket { kind = "hello", character = firstCharacter, scene = sceneName, password = password }); }
            else
            {
                pendingPeers[item.peer] = Time.unscaledTime;
                clientRates[item.peer] = new CoopClientPolicy.Rate(Time.realtimeSinceStartupAsDouble);
            }
            return;
        }
        if (Mode == CoopMode.Host)
        {
            // Check before JSON parsing, and stop processing a kicked peer while relay cleanup is pending.
            if (!clientRates.TryGetValue(item.peer, out var rate)) return;
            if (item.text == null || item.text.Length > CoopClientPolicy.MaximumMessageBytes ||
                Encoding.UTF8.GetByteCount(item.text) > CoopClientPolicy.MaximumMessageBytes || !rate.Accept(Time.realtimeSinceStartupAsDouble))
            { RejectClient(item.peer); return; }
        }
        CoopPacket packet;
        try { packet = JsonUtility.FromJson<CoopPacket>(item.text); }
        catch (Exception) { RejectClient(item.peer); return; }
        if (packet == null || packet.version != CoopPacket.Version) { RejectClient(item.peer); return; }
        if (Mode == CoopMode.Host)
        {
            if (!CoopClientPolicy.Valid(packet, joined.ContainsKey(item.peer))) { RejectClient(item.peer); return; }
            if (packet.kind == "hello" && !joined.ContainsKey(item.peer))
            {
                if (joined.Count >= 3)
                { Send(item.peer, new CoopPacket { kind = "reject", message = "房間已滿（最多 4 人）" }); clientRates.Remove(item.peer); transport.DisconnectAfterSend(item.peer); return; }
                if (!InCamp || packet.password != password || packet.scene != GameSceneFlow.TradingHubScene || !ValidCharacter(packet.character))
                { Send(item.peer, new CoopPacket { kind = "reject", message = "房間密碼、遊戲版本或地圖不相符" }); clientRates.Remove(item.peer); transport.DisconnectAfterSend(item.peer); return; }
                pendingPeers.Remove(item.peer); joined[item.peer] = packet.character;
                AddCampPlayer(item.peer, packet.character);
                if (sceneReady) Welcome(item.peer);
                return;
            }
            if (InCamp && joined.ContainsKey(item.peer)) { HandleCampCommand(item.peer, packet); return; }
            if (!joined.ContainsKey(item.peer) || !actors.TryGetValue(item.peer, out var actor)) return;
            if (packet.kind == "input") actor.ReceiveInput(packet.input);
            if (packet.kind == "command" && Time.unscaledTime - (lastCommand.TryGetValue(item.peer, out var last) ? last : -1) >= .03f)
            { lastCommand[item.peer] = Time.unscaledTime; actor.GetComponent<WeaponInventory>().ApplyCoopCommand(packet.action, packet.value, packet.extra, packet.itemId); }
            if (packet.kind == "upgrade") actor.GetComponent<PlayerHealth>().SelectCoopUpgrade(packet.value);
        }
        else if (IsClient)
        {
            if (packet.kind == "reject") { Fail(packet.message); return; }
            if (packet.kind == "campState" && InCamp) { ReceiveCamp(packet.camp); return; }
            if (packet.kind == "depart" && InCamp && welcomeReceived) { ReceiveDeparture(packet); return; }
            if (packet.kind == "welcome" && !welcomeReceived && packet.scene == sceneName && packet.id > 0)
            { localId = packet.id; welcomeReceived = true; onConnected?.Invoke(); onConnected = null; Launch(); }
            if (packet.kind == "mesh" && packet.mesh != null)
            { if (mirror == null) mirror = gameObject.AddComponent<CoopWorldMirror>(); mirror.ReceiveMesh(packet.mesh); }
            if (packet.kind == "state" && packet.snapshot != null)
            { if (sceneReady) Apply(packet.snapshot); else pendingSnapshot = packet.snapshot; }
        }
    }
    void Welcome(int peer)
    {
        if (InCamp)
        {
            Send(peer, new CoopPacket { kind = "welcome", id = peer, scene = sceneName });
            BroadcastCamp(); return;
        }
        SpawnActor(peer, joined[peer]);
        Send(peer, new CoopPacket { kind = "welcome", id = peer, scene = sceneName });
    }
    static bool ValidCharacter(string id)
    { foreach (var character in CharacterRoster.All) if (character.Id == id) return true; return false; }
    void RejectClient(int peer) { clientRates.Remove(peer); transport?.Disconnect(peer); }
    void Send(int peer, CoopPacket packet) => transport?.Send(peer, JsonUtility.ToJson(packet));
    CoopSnapshot Capture()
    {
        var players = new List<CoopPlayerState>();
        foreach (var actor in actors.Values)
        {
            if (actor == null) continue;
            var player = actor.GetComponent<Player>();
            players.Add(new CoopPlayerState { id = actor.Id, position = actor.transform.position, rotation = actor.transform.rotation,
                health = actor.GetComponent<PlayerHealth>().CaptureProgress(), inventory = actor.GetComponent<WeaponInventory>().CaptureProgress(),
                character = actor.GetComponent<CharacterSkills>().CaptureProgress(), effect = actor.GetComponent<CharacterSkills>().EffectRemaining,
                tactics = actor.GetComponent<RogueTactics>().CaptureCoopState(),
                grounded = player.IsGrounded, running = player.IsRunning, crouching = player.IsCrouching, sliding = player.IsSliding,
                movement = player.MovementAmount, verticalSpeed = player.VerticalSpeed, cameraHeight = player.CameraHeightOffset,
                upgradeLabels = actor.GetComponent<PlayerHealth>().CoopUpgradeLabels() });
        }
        return new CoopSnapshot { players = players.ToArray(), world = mirror.Capture(),
            keys = FloorProgressionManager.Instance?.CaptureProgress(), victory = DungeonVictoryUI.IsOpen,
            expedition = TacticalExpedition.Instance?.CaptureProgress(),
            expeditionActive = TacticalExpedition.Instance != null ? TacticalExpedition.Instance.ActiveEncounter : -1,
            expeditionWave = TacticalExpedition.Instance != null ? TacticalExpedition.Instance.CurrentWave : 0,
            expeditionEnemies = TacticalExpedition.Instance != null ? TacticalExpedition.Instance.EncounterEnemies.Count : 0,
            tacticalVisuals = TacticalNetworkVisuals.Capture() };
    }
    void Apply(CoopSnapshot snapshot)
    {
        if (snapshot.players == null || snapshot.players.Length > 4) return;
        ApplyingSnapshot = true;
        try
        {
            var present = new HashSet<int>();
            foreach (var state in snapshot.players)
            {
                if (state == null || state.health == null || state.inventory == null || state.character == null) continue;
                present.Add(state.id);
                var actor = SpawnActor(state.id, state.character.id);
                actor.ReceivePose(state.position, state.rotation);
                actor.GetComponent<Player>().ApplyCoopAnimationState(state);
                actor.GetComponent<PlayerHealth>().ApplyCoopState(state.health, state.upgradeLabels);
                string inventory = JsonUtility.ToJson(state.inventory);
                if (!lastInventory.TryGetValue(state.id, out var old) || old != inventory)
                { lastInventory[state.id] = inventory; actor.GetComponent<WeaponInventory>().RestoreProgress(state.inventory); }
                actor.GetComponent<CharacterSkills>().ApplyCoopState(state.character, state.effect);
                actor.GetComponent<RogueTactics>().ApplyCoopState(state.tactics);
            }
            foreach (int id in new List<int>(actors.Keys))
                if (id != localId && !present.Contains(id)) { Destroy(actors[id].gameObject); actors.Remove(id); lastInventory.Remove(id); }
            if (snapshot.keys != null) FloorProgressionManager.Instance?.RestoreProgress(snapshot.keys);
            mirror.Apply(snapshot.world);
            TacticalExpedition.Ensure()?.ApplyCoopState(snapshot.expedition,snapshot.expeditionActive,snapshot.expeditionWave,snapshot.expeditionEnemies);
            TacticalNetworkVisuals.Apply(snapshot.tacticalVisuals);
            if (snapshot.victory && !DungeonVictoryUI.IsOpen) DungeonVictoryUI.Show(localActor.GetComponent<PlayerHealth>());
        }
        finally { ApplyingSnapshot = false; }
    }
    public static bool Command(Component owner, string action, int value = 0, int extra = 0, string itemId = null)
    {
        if (!IsClient || ApplyingSnapshot || Instance == null) return false;
        var actor = CoopInput.Actor(owner);
        if (actor == null || !actor.Local) return false;
        Instance.Send(0, new CoopPacket { kind = "command", action = action, value = value, extra = extra, itemId = itemId });
        return true;
    }
    public static bool Upgrade(Component owner, int index)
    {
        if (!IsClient || ApplyingSnapshot || Instance == null) return false;
        Instance.Send(0, new CoopPacket { kind = "upgrade", value = index }); return true;
    }
    public static Transform ClosestLiving(Vector3 origin, Transform fallback = null, DungeonRoomBoundary room = null)
    {
        if (!IsMultiplayer || Instance == null) return fallback;
        Transform best = null; float distance = float.PositiveInfinity;
        foreach (var actor in Instance.actors.Values)
        {
            if (actor == null || !actor.isActiveAndEnabled || actor.GetComponent<PlayerHealth>().IsDead) continue;
            if (room != null && !room.Contains(actor.transform.position)) continue;
            float squared = (actor.transform.position - origin).sqrMagnitude;
            if (squared < distance) { distance = squared; best = actor.transform; }
        }
        return best != null ? best : fallback;
    }
    public static Player[] Players()
    {
        if (!IsMultiplayer || Instance == null) return FindObjectsByType<Player>(FindObjectsSortMode.None);
        var result = new List<Player>(); foreach (var actor in Instance.actors.Values) if (actor != null) result.Add(actor.GetComponent<Player>());
        return result.ToArray();
    }
    void Fail(string error)
    {
        bool playing = sceneReady; var callback = onFailure;
        if (mapRequested && GameSceneFlow.IsLoading)
        {
            // Preserve cooperative mode while map components awaken. Switching to single
            // player here could make a failed join initialize the single-player save slot.
            transport?.Dispose(); transport = null; returningToMenu = true;
            StopAllCoroutines(); StartCoroutine(ReturnAfterLoading(error, callback)); return;
        }
        StopSession(); LastError = error ?? "連線失敗"; callback?.Invoke(LastError);
        if (playing) GameSceneFlow.Load(GameSceneFlow.MenuScene);
    }
    IEnumerator ReturnAfterLoading(string error, Action<string> callback)
    {
        while (GameSceneFlow.IsLoading) yield return null;
        StopSession(); LastError = error ?? "連線失敗"; callback?.Invoke(LastError);
        GameSceneFlow.Load(GameSceneFlow.MenuScene);
    }
    public static void Leave() { if (Instance != null) Instance.StopSession(); }
    void StopSession()
    {
        StopAllCoroutines();
        transport?.Dispose(); transport = null;
        if (template != null) Destroy(template.transform.parent.gameObject); template = null;
        if (mirror != null) { Destroy(mirror); mirror = null; }
        if (roomHud != null) Destroy(roomHud); roomHud = null;
        actors.Clear(); joined.Clear(); pendingPeers.Clear(); sentMeshes.Clear(); lastCommand.Clear(); clientRates.Clear(); lastInventory.Clear();
        campPlayers.Clear(); campInputs.Clear(); campInputTimes.Clear(); inCamp = false; nextCampSnapshot = nextCampInput = 0;
        sceneReady = connected = welcomeReceived = false; pendingSnapshot = null; localActor = null; localId = 0;
        mapRequested = returningToMenu = false;
        onConnected = null; onFailure = null; outgoingInput = new CoopInputFrame(); Mode = CoopMode.SinglePlayer;
        IsInternet = false; RoomCode = null;
    }
    void OnApplicationQuit() => StopSession();
    void OnDestroy() { SceneManager.sceneLoaded -= SceneLoaded; StopSession(); if (Instance == this) Instance = null; }
}
