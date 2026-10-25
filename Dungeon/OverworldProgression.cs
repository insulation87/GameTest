using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class FloorProgressionManager
{
    readonly List<RegionPortal> portals = new List<RegionPortal>();
    readonly Dictionary<Player, float> portalCooldowns = new Dictionary<Player, float>();
    public IReadOnlyList<RegionPortal> Portals => portals;
    public int CollectedFor(int floor) => collected[Mathf.Clamp(floor, 1, 3)];
    public int RequiredItemsFor(int floor) => Mathf.Max(1, RequiredFor(Mathf.Clamp(floor, 1, 3)));
    public bool IsComplete(int floor) => CollectedFor(floor) >= RequiredItemsFor(floor);
    public bool IsRegionUnlocked(int floor) => floor <= 1 || (IsComplete(1) && (floor == 2 || IsComplete(2)));

    IEnumerator Start()
    {
        yield return null;
        if (FindFirstObjectByType<OverworldDirector>() == null) yield break;
        BuildPortalPair(1, FindNamedTransform("Stair_1_Bottom"), FindNamedTransform("Stair_1_Top"));
        BuildPortalPair(2, FindNamedTransform("Stair_2_Bottom"), FindNamedTransform("Stair_2_Top"));
        gameObject.AddComponent<RegionObjectiveUI>();
    }
    void BuildPortalPair(int from, Transform source, Transform destination)
    {
        if (source == null || destination == null) { Debug.LogWarning("區域傳送門缺少出生標記。", this); return; }
        var a = RegionPortal.Create(this, source.position, source.rotation, from, from + 1);
        var b = RegionPortal.Create(this, destination.position, destination.rotation, from + 1, from);
        a.Destination = b; b.Destination = a;
        portals.Add(a); portals.Add(b);
    }
    public Transform GuidanceFor(Player player)
    {
        var world = FindFirstObjectByType<OverworldDirector>();
        if (player == null || world == null) return null;
        int floor = world.RegionAt(player.transform.position);
        if (!IsComplete(floor)) return TacticalExpedition.Instance != null ? TacticalExpedition.Instance.ObjectiveTarget : null;
        if (floor == 3) return bossSpawner != null && bossSpawner.CurrentBoss != null ? bossSpawner.CurrentBoss.transform : null;
        foreach (RegionPortal portal in portals)
            if (portal.SourceRegion == floor && portal.TargetRegion == floor + 1) return portal.transform;
        return null;
    }
    public bool TryUsePortal(RegionPortal portal, Player player)
    {
        if (CoopSession.IsClient || portal == null || !portals.Contains(portal) || portal.Destination == null ||
            player == null || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen || !portal.IsUnlocked) return false;
        var health = player.GetComponent<PlayerHealth>();
        var inventory = player.GetComponent<WeaponInventory>();
        if (health == null || health.IsDead || health.IsChoosingUpgrade || inventory != null && (inventory.IsOpen || inventory.IsPaused)) return false;
        if (portalCooldowns.TryGetValue(player, out float until) && Time.time < until) return false;
        if (Vector3.Distance(player.transform.position, portal.transform.position + Vector3.up) > 3.5f) return false;
        Quaternion facing = portal.Destination.transform.rotation;
        for (int ring = 0; ring < 3; ring++)
        for (int angle = 0; angle < 8; angle++)
        {
            Vector3 direction = Quaternion.Euler(0, angle * 45, 0) * portal.Destination.transform.forward;
            Vector3 candidate = portal.Destination.transform.position + direction * (3.2f + ring * 1.2f);
            if (!Physics.Raycast(candidate + Vector3.up * 2, Vector3.down, out var hit, 4, Physics.AllLayers, QueryTriggerInteraction.Ignore) ||
                Vector3.Dot(hit.normal, Vector3.up) < .95f ||
                !player.TryGetTeleportPosition(hit.point, facing, out Vector3 position)) continue;
            portalCooldowns[player] = Time.time + 1.2f;
            player.Teleport(position, facing);
            health.SetSavedRespawn(position, facing);
            return true;
        }
        portal.ReportBlocked();
        return false;
    }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}

// Portals remain visible when locked and activate through actual player trigger contact.
public sealed class RegionPortal : MonoBehaviour
{
    public int SourceRegion { get; private set; }
    public int TargetRegion { get; private set; }
    public RegionPortal Destination { get; set; }
    public bool IsUnlocked => owner != null && owner.IsRegionUnlocked(Mathf.Max(SourceRegion, TargetRegion));
    FloorProgressionManager owner;
    Material material;
    Text label;
    float blockedUntil;
    static Mesh frameMesh;

    public static RegionPortal Create(FloorProgressionManager manager, Vector3 point, Quaternion rotation, int from, int to)
    {
        var node = new GameObject("Portal " + from + " to " + to, typeof(BoxCollider));
        node.transform.SetParent(manager.transform); node.transform.SetPositionAndRotation(point, rotation);
        var trigger = node.GetComponent<BoxCollider>(); trigger.isTrigger = true;
        trigger.center = Vector3.up; trigger.size = new Vector3(2.1f, 2.5f, 1.4f);
        var portal = node.AddComponent<RegionPortal>(); portal.owner = manager; portal.SourceRegion = from; portal.TargetRegion = to;
        portal.Build(); return portal;
    }
    void Build()
    {
        material = new Material(Resources.Load<Shader>("PixelSurface")) { name = "Region Portal" };
        if (frameMesh == null)
        {
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube); primitive.SetActive(false);
            frameMesh = primitive.GetComponent<MeshFilter>().sharedMesh; Destroy(primitive);
        }
        for (int i = 0; i < 24; i++)
        {
            float angle = i * Mathf.PI * 2 / 24;
            var part = new GameObject("Portal Rune", typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(transform, false);
            part.transform.localPosition = new Vector3(Mathf.Cos(angle) * 1.1f, 1.4f + Mathf.Sin(angle) * 1.25f, 0);
            part.transform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
            part.transform.localScale = new Vector3(.18f, .28f, .20f);
            part.GetComponent<MeshFilter>().sharedMesh = frameMesh; part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        var canvas = new GameObject("Portal Label", typeof(Canvas)); canvas.transform.SetParent(transform, false);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        canvas.transform.localPosition = new Vector3(0, 3.2f, 0); canvas.transform.localScale = Vector3.one * .007f;
        label = GameUITheme.Label(canvas.transform, "", 28, Vector2.zero, new Vector2(620, 120), TextAnchor.MiddleCenter);
    }
    void Update()
    {
        material.color = IsUnlocked ? new Color(.40f, .86f, .75f) * (1 + Mathf.Sin(Time.time * 2) * .12f) : new Color(.42f, .45f, .48f);
        label.text = Time.time < blockedUntil ? "落點暫時被阻擋，請稍後再試" :
            "前往「" + OverworldDirector.RegionNames[TargetRegion - 1] + "」\n" +
            (IsUnlocked ? "走入光環即可傳送" : "蒐集本區鑰石後解鎖");
        var camera = PixelArtDirector.ActiveGameplayCamera != null ? PixelArtDirector.ActiveGameplayCamera : Camera.main;
        if (camera != null) label.transform.parent.rotation = camera.transform.rotation;
    }
    public void ReportBlocked() => blockedUntil = Time.time + 3;
    void OnTriggerEnter(Collider other) => TryContact(other);
    void OnTriggerStay(Collider other) => TryContact(other);
    void TryContact(Collider other)
    {
        var player = other.GetComponentInParent<Player>();
        if (player != null) owner.TryUsePortal(this, player);
    }
    void OnDestroy() { if (material != null) Destroy(material); }
}

public sealed class RegionObjectiveUI : MonoBehaviour
{
    FloorProgressionManager progress;
    OverworldDirector world;
    Player player;
    Text objective, direction;
    RectTransform arrow;
    Canvas canvas;
    float nextRefresh;
    void Start()
    {
        progress = GetComponent<FloorProgressionManager>(); world = FindFirstObjectByType<OverworldDirector>();
        var node = new GameObject("Region Objective", typeof(Canvas), typeof(CanvasScaler)); node.transform.SetParent(transform, false);
        canvas = node.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 75;
        GameUITheme.FitCanvas(node.GetComponent<CanvasScaler>());
        var panel = new GameObject("Objective Panel", typeof(Image)); panel.transform.SetParent(node.transform, false);
        var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(0, -24); rect.sizeDelta = new Vector2(630, 96);
        panel.GetComponent<Image>().raycastTarget = false; GameUITheme.Surface(panel);
        objective = GameUITheme.Label(panel.transform, "", 23, new Vector2(0, 17), new Vector2(590, 42), TextAnchor.MiddleCenter);
        direction = GameUITheme.Label(panel.transform, "", 19, new Vector2(0, -20), new Vector2(520, 38), TextAnchor.MiddleCenter);
        arrow = GameUITheme.Label(panel.transform, "▲", 27, new Vector2(-285, -20), new Vector2(38, 38), TextAnchor.MiddleCenter).rectTransform;
    }
    void LateUpdate()
    {
        if (canvas == null) return;
        if (player == null || !CoopSession.LocalUI(player))
            foreach (var candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
                if (CoopSession.LocalUI(candidate)) { player = candidate; break; }
        if (player == null || world == null) { canvas.enabled = false; return; }
        var health = player.GetComponent<PlayerHealth>();
        canvas.enabled = !DungeonVictoryUI.IsOpen && health != null && !health.IsDead;
        int floor = world.RegionAt(player.transform.position);
        Transform target = progress.GuidanceFor(player);
        arrow.gameObject.SetActive(target != null);
        if (target != null)
        {
            Vector3 delta = target.position - player.transform.position; delta.y = 0;
            var camera = PixelArtDirector.ActiveGameplayCamera;
            Vector3 forward = camera != null ? camera.transform.forward : player.transform.forward; forward.y = 0;
            arrow.localRotation = Quaternion.Euler(0, 0, -Vector3.SignedAngle(forward, delta, Vector3.up));
        }
        if (Time.unscaledTime < nextRefresh) return; nextRefresh = Time.unscaledTime + .1f;
        objective.text = "第 " + floor + "/3 關 · " + OverworldDirector.RegionNames[floor - 1] + " · 鑰石 " +
            Mathf.Min(progress.CollectedFor(floor), progress.RequiredItemsFor(floor)) + " / " + progress.RequiredItemsFor(floor);
        direction.text = !progress.IsComplete(floor) && TacticalExpedition.Instance != null && !string.IsNullOrEmpty(TacticalExpedition.Instance.Objective) ?
            TacticalExpedition.Instance.Objective : target != null ? (floor < 3 ? "蒐集完成 · 前往下一關傳送門" : "蒐集完成 · 挑戰地圖首領") +
            "  " + Mathf.CeilToInt(Vector3.Distance(player.transform.position, target.position)) + " m" :
            progress.IsComplete(floor) ? "區域探索完成" : "自由探索 · 擊敗魔物並拾取掉落鑰石";
    }
}

public partial class ExplorationMinimap
{
    RectTransform objectiveMarker;
    void UpdateObjectiveMarker()
    {
        Transform target = FloorProgressionManager.Instance != null ? FloorProgressionManager.Instance.GuidanceFor(player) : null;
        if (objectiveMarker == null && target != null)
        {
            objectiveMarker = CreateImage("Next Region Portal", mapRect, GameUITheme.Gold).GetComponent<RectTransform>();
            objectiveMarker.anchorMin = objectiveMarker.anchorMax = Vector2.zero; objectiveMarker.sizeDelta = Vector2.one * 15;
            objectiveMarker.localRotation = Quaternion.Euler(0, 0, 45);
        }
        if (objectiveMarker == null) return;
        objectiveMarker.gameObject.SetActive(target != null);
        if (target == null) return;
        Bounds bounds = floorBounds[currentFloor];
        objectiveMarker.anchoredPosition = new Vector2(Mathf.InverseLerp(bounds.min.x, bounds.max.x, target.position.x),
            Mathf.InverseLerp(bounds.min.z, bounds.max.z, target.position.z)) * uiSize;
        objectiveMarker.SetAsLastSibling();
    }
}
