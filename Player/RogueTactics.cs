using System;
using System.Collections.Generic;
using MedievalDungeonKit;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class RogueTacticsProgress
{
    // JsonUtility can materialize an omitted/null inline object with all-zero fields.
    public int version;
    public float resonance;
    public string[] visitedRooms;
}

/// <summary>Short combat memories and optional, room-bound wagers. No scene setup required.</summary>
[DisallowMultipleComponent]
public sealed partial class RogueTactics : MonoBehaviour
{
    const int MemoryCapacity = 64, ReplayLimit = 30;
    const float MemorySeconds = 3f, EchoCost = 100f;
    public KeyCode echoKey = KeyCode.Q;
    public KeyCode acceptKey = KeyCode.Z;
    public KeyCode declineKey = KeyCode.X;
    public float Resonance { get; private set; } = 50f;
    public bool IsReplaying => replayCount > 0 && !EchoArmed;
    public bool EchoArmed { get; private set; }
    public float EchoRemaining => EchoArmed ? Mathf.Max(0, echoExpires - clock) : 0;
    public Vector3 EchoPosition => replayCount > 0 ? replay[0].origin : transform.position;
    public float ActivationRange => GetComponent<TacticalEquipment>()?.EchoRange ?? 36f;
    float echoExpires;
    TacticalMarker echoMarker;
    public bool HasOffer => offer != Pact.None;
    public bool HasActivePact => active != Pact.None;
    public float IncomingDamageMultiplier => active == Pact.None || active == Pact.Untouched ? 1f : 1.25f;

    enum Pact { None, Arsenal, Hunt, Untouched }
    struct Shot
    {
        public float time, speed, damage, spread, radius;
        public int pellets, penetration;
        public Vector3 origin, direction;
        public WeaponType type;
        public Projectile prefab;
        public bool ricochet;
    }

    readonly Shot[] memory = new Shot[MemoryCapacity];
    readonly Shot[] replay = new Shot[ReplayLimit];
    readonly HashSet<string> visitedRooms = new HashSet<string>(StringComparer.Ordinal);
    readonly HashSet<WeaponType> pactWeapons = new HashSet<WeaponType>();
    readonly Collider[] originHits = new Collider[32];
    PlayerHealth health;
    WeaponInventory inventory;
    Canvas hud;
    Text echoText, pactText, noticeText;
    Image chargeFill;
    LineRenderer route;
    Material routeMaterial;
    DungeonRoomBoundary room;
    DungeonRoomBoundary[] rooms;
    Pact offer, active;
    int memoryStart, memoryCount, replayCount, replayIndex, pactKills;
    float clock, replayStarted, replayFirstTime, nextRoomCheck, nextHudRefresh, nextChargeHit;
    float pactRemaining, offerRemaining, noticeRemaining;
    WeaponType lastHitType;
    bool hasHitType;
    string notice;

    void Start()
    {
        health = GetComponent<PlayerHealth>();
        inventory = GetComponent<WeaponInventory>();
        foreach (DungeonMarker marker in FindObjectsByType<DungeonMarker>(FindObjectsSortMode.None))
            DungeonRoomBoundary.GetOrCreate(marker);
        rooms = FindObjectsByType<DungeonRoomBoundary>(FindObjectsSortMode.None);
        BuildHud();
        RefreshHud();
    }

    bool CanAct => isActiveAndEnabled && health != null && !health.IsDead && !health.IsChoosingUpgrade &&
        Time.timeScale > 0f && !DungeonVictoryUI.IsOpen &&
        (inventory == null || (!inventory.IsOpen && !inventory.IsPaused));

    void Update()
    {
        if (CoopSession.IsClient) return;
        bool playable = CanAct;
        if (hud != null) hud.enabled = playable;
        if (route != null) route.enabled = playable && (IsReplaying || EchoArmed);
        if (echoMarker != null) echoMarker.gameObject.SetActive(playable);
        if (!playable) return;
        clock += Time.deltaTime;
        noticeRemaining = Mathf.Max(0f, noticeRemaining - Time.deltaTime);
        if (clock >= nextRoomCheck)
        {
            nextRoomCheck = clock + .35f;
            CheckRoom();
        }
        if (HasActivePact)
        {
            pactRemaining -= Time.deltaTime;
            if (pactRemaining <= 0f) EndPact(false, "誓約逾時 · 沒有扣除資源");
        }
        if (HasOffer)
        {
            offerRemaining -= Time.deltaTime;
            if (offerRemaining <= 0f) offer = Pact.None;
        }
        var actor = CoopInput.Actor(this);
        if (Application.isFocused || actor != null && !actor.Local)
        {
            if (CoopInput.Down(this, CoopButton.Echo, echoKey)) TryReleaseEcho();
            if (CoopInput.Down(this, CoopButton.Accept, acceptKey)) TryAcceptPact();
            if (CoopInput.Down(this, CoopButton.Decline, declineKey) && HasOffer) offer = Pact.None;
        }
        AdvanceReplay();
        if (clock >= nextHudRefresh)
        {
            nextHudRefresh = clock + .1f;
            RefreshHud();
        }
    }

    public void RecordShot(Projectile prefab, Vector3 origin, Vector3 direction, float speed,
        float damage, int pellets, float spread, float radius, int penetration, WeaponType type)
    {
        if (!CanAct || prefab == null || damage <= 0f) return;
        int index = (memoryStart + memoryCount) % MemoryCapacity;
        memory[index] = new Shot { time = clock, origin = origin, direction = direction.normalized,
            prefab = prefab, speed = speed, damage = damage, pellets = Mathf.Clamp(pellets, 1, 8),
            spread = spread, radius = radius, penetration = penetration, type = type,
            ricochet = GetComponent<PlayerHealth>()?.ShotStyle == TacticalShotStyle.Ricochet };
        if (memoryCount < MemoryCapacity) memoryCount++;
        else memoryStart = (memoryStart + 1) % MemoryCapacity;
    }

    public bool TryReleaseEcho()
    {
        if (CoopSession.IsClient) return false;
        if (!CanAct || IsReplaying) return false;
        if (EchoArmed)
        {
            if (clock >= echoExpires) { ResetCombatMemory(); Notify("預設回聲已消散"); return false; }
            if ((EchoPosition - transform.position).sqrMagnitude > ActivationRange * ActivationRange)
            { Notify($"回聲太遠 · 請回到 {ActivationRange:0} 公尺內啟動"); return false; }
            EchoArmed = false;
            replayIndex = 0; replayStarted = clock; replayFirstTime = replay[0].time;
            if (echoMarker != null) Destroy(echoMarker.gameObject);
            echoMarker = null;
            Notify("回聲啟動 · 與原位置形成交叉火力");
            return true;
        }
        CheckRoom();
        if (Resonance < EchoCost) { Notify("交替武器命中與擊殺，累積回聲能量"); return false; }
        // The most recent thirty salvos retain their original spacing and firing locations.
        int first = Mathf.Max(0, memoryCount - ReplayLimit);
        replayCount = 0;
        for (int i = first; i < memoryCount; i++)
        {
            Shot shot = memory[(memoryStart + i) % MemoryCapacity];
            if (clock - shot.time <= MemorySeconds && shot.prefab != null)
                replay[replayCount++] = shot;
        }
        if (replayCount == 0) { Notify($"先射擊，再按 {echoKey} 留下戰術回聲"); return false; }
        Resonance -= EchoCost;
        replayIndex = 0;
        EchoArmed = true;
        echoExpires = clock + (GetComponent<TacticalEquipment>()?.EchoDuration ?? 18f);
        echoMarker = TacticalMarker.Create("預設回聲", replay[0].origin, replay[0].direction, GameUITheme.Teal);
        BuildRoute();
        GameAudio.UI(GameSound.BuildReady);
        Notify("回聲已留下 · 移動、滑行繞背，再按回聲鍵啟動");
        return true;
    }

    void AdvanceReplay()
    {
        if (EchoArmed)
        {
            if (clock >= echoExpires) { ResetCombatMemory(); Notify("預設回聲已消散 · 請重新射擊與布局"); }
            else if (echoMarker != null) echoMarker.SetText($"預設回聲  {EchoRemaining:F1}s\n[{GameControls.Label(this, GameAction.Echo)}] 啟動 · {Vector3.Distance(transform.position, EchoPosition):0}/{ActivationRange:0}m");
            return;
        }
        // Bound work per frame even after a hitch. All salvos are eventually replayed.
        int budget = 3;
        while (replayIndex < replayCount && budget-- > 0 &&
            replay[replayIndex].time - replayFirstTime <= clock - replayStarted)
            ReplayShot(replay[replayIndex++]);
        if (replayCount > 0 && replayIndex >= replayCount)
        {
            Array.Clear(replay, 0, replay.Length);
            replayCount = replayIndex = 0;
            if (route != null) route.enabled = false;
        }
    }

    void ReplayShot(Shot shot)
    {
        if (shot.prefab == null || (shot.origin - transform.position).sqrMagnitude > ActivationRange * ActivationRange ||
            Mathf.Abs(shot.origin.y - transform.position.y) > 4f) return;
        // Suppress memories whose muzzle is now inside scenery, an enemy or a closed chest.
        int count = Physics.OverlapSphereNonAlloc(shot.origin, .07f, originHits,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        if (count == originHits.Length) return;
        for (int i = 0; i < count; i++)
            if (!originHits[i].transform.IsChildOf(transform) &&
                originHits[i].GetComponentInParent<Projectile>() == null &&
                !PickupCollisionUtility.IsPickup(originHits[i])) return;
        for (int i = 0; i < shot.pellets; i++)
        {
            Vector3 direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-shot.spread, shot.spread), Vector3.up) *
                Quaternion.AngleAxis(UnityEngine.Random.Range(-shot.spread, shot.spread), Vector3.right) * shot.direction;
            Projectile projectile = Instantiate(shot.prefab, shot.origin, Quaternion.LookRotation(direction));
            projectile.name = "Tactical Echo Projectile";
            if (projectile.TryGetComponent(out Renderer placeholder)) placeholder.enabled = false;
            GameObject visual = PixelCharacterFactory.CreateBolt(projectile.transform, .24f);
            Vector3 scale = projectile.transform.lossyScale;
            visual.transform.localScale = new Vector3(1f / Mathf.Max(.001f, Mathf.Abs(scale.x)),
                1f / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1f / Mathf.Max(.001f, Mathf.Abs(scale.z)));
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_Color", GameUITheme.Teal);
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>()) renderer.SetPropertyBlock(tint);
            // Captured damage is already upgraded. Never reroll crits or trigger weapon-build procs.
            projectile.Launch(direction, shot.speed, shot.damage * .45f, gameObject,
                shot.radius, shot.penetration, shot.type, null);
            projectile.SetTacticalEcho();
            if (shot.ricochet) projectile.SetRicochet();
        }
    }

    public void OnWeaponHit(EnemyHealth target, WeaponType type)
    {
        if (!CanAct || target == null) return;
        bool changed = hasHitType && type != lastHitType;
        if (changed || clock >= nextChargeHit)
        {
            AddResonance(changed ? 10f : 2f);
            nextChargeHit = clock + .25f;
        }
        hasHitType = true;
        lastHitType = type;
        if (active == Pact.Arsenal && room != null && room.Contains(target.transform.position))
        {
            pactWeapons.Add(type);
            if (pactWeapons.Count >= 3) EndPact(true, null);
        }
    }

    public void OnEnemyKilled(EnemyHealth target)
    {
        if (!CanAct || target == null) return;
        AddResonance(12f);
        if (!HasActivePact || room == null || !room.Contains(target.transform.position)) return;
        pactKills++;
        if ((active == Pact.Hunt && pactKills >= 3) || (active == Pact.Untouched && pactKills >= 2))
            EndPact(true, null);
    }

    public void OnDamageTaken()
    {
        if (active == Pact.Untouched) EndPact(false, "無傷誓約中斷 · 護甲受擊也會中斷");
    }

    public void AddResonance(float amount) => Resonance = Mathf.Clamp(Resonance + amount, 0f, EchoCost);

    void CheckRoom()
    {
        DungeonRoomBoundary found = null;
        if (rooms != null)
            foreach (DungeonRoomBoundary candidate in rooms)
                if (candidate != null && candidate.isActiveAndEnabled && candidate.Contains(transform.position))
                { found = candidate; break; }
        if (found != room)
        {
            if (!EchoArmed && !IsReplaying) ResetCombatMemory();
            if (HasActivePact) EndPact(false, "離開房間 · 誓約結束");
            offer = Pact.None;
            room = found;
        }
        if (room == null || HasActivePact || HasOffer || visitedRooms.Contains(RoomId(room))) return;
        int enemies = 0;
        foreach (EnemyHealth enemy in EnemyHealth.ActiveEnemies)
        {
            if (enemy == null || enemy.IsDead || !room.Contains(enemy.transform.position)) continue;
            if (enemy.GetComponent<BossController>() != null) return;
            enemies++;
        }
        if (enemies < 3) return;
        var types = new HashSet<WeaponType>();
        if (inventory != null)
            foreach (WeaponItem weapon in inventory.EquippedSlots)
                if (weapon != null) types.Add(weapon.weaponType);
        int roll = UnityEngine.Random.Range(0, types.Count >= 3 ? 3 : 2);
        offer = roll == 0 ? Pact.Hunt : roll == 1 ? Pact.Untouched : Pact.Arsenal;
        pactKills = 0;
        pactWeapons.Clear();
        visitedRooms.Add(RoomId(room));
        offerRemaining = 20f;
    }

    public bool TryAcceptPact()
    {
        if (!CanAct || !HasOffer || room == null || !room.Contains(transform.position)) return false;
        active = offer;
        offer = Pact.None;
        pactRemaining = 25f;
        pactKills = 0;
        pactWeapons.Clear();
        GameAudio.UI(GameSound.Open);
        RefreshHud();
        return true;
    }

    void EndPact(bool success, string failure)
    {
        if (!HasActivePact) return;
        active = Pact.None; // Remove the drawback before callbacks, healing or UI sounds.
        if (success)
        {
            int gold = Mathf.Clamp(40 + health.CurrentLevel * 2, 40, 140);
            inventory?.AddGold(gold);
            health.Heal(health.maxHealth * .12f);
            AddResonance(35f);
            GameAudio.UI(GameSound.Unlock);
            Notify($"誓約完成 · +{gold} 金幣／12% 生命／35 回聲");
        }
        else Notify(failure);
    }

    public void ResetTransientState()
    {
        ResetCombatMemory();
        if (HasActivePact) EndPact(false, "誓約結束");
        offer = Pact.None;
        room = null;
        hasHitType = false;
        nextChargeHit = clock;
    }

    void ResetCombatMemory()
    {
        EchoArmed = false;
        if (echoMarker != null) Destroy(echoMarker.gameObject);
        echoMarker = null;
        Array.Clear(memory, 0, memory.Length);
        Array.Clear(replay, 0, replay.Length);
        memoryCount = memoryStart = replayCount = replayIndex = 0;
        if (route != null) route.enabled = false;
    }

    static string RoomId(DungeonRoomBoundary boundary)
    {
        string id = boundary.name;
        for (Transform t = boundary.transform.parent; t != null; t = t.parent) id = t.name + "/" + id;
        return id;
    }

    public RogueTacticsProgress CaptureProgress()
    {
        var ids = new string[visitedRooms.Count];
        visitedRooms.CopyTo(ids);
        return new RogueTacticsProgress { version = 1, resonance = Resonance, visitedRooms = ids };
    }

    public void RestoreProgress(RogueTacticsProgress data)
    {
        ResetTransientState();
        visitedRooms.Clear();
        bool supported = data != null && data.version == 1;
        Resonance = !supported || float.IsNaN(data.resonance) || float.IsInfinity(data.resonance)
            ? 50f : Mathf.Clamp(data.resonance, 0f, EchoCost);
        if (supported && data.visitedRooms != null)
            foreach (string id in data.visitedRooms)
                if (!string.IsNullOrEmpty(id)) visitedRooms.Add(id);
    }

    void Notify(string value) { notice = value; noticeRemaining = 4f; }
    public void ShowNotice(string value) => Notify(value);

    void BuildRoute()
    {
        if (route == null)
        {
            var node = new GameObject("Echo Memory Route");
            node.transform.SetParent(transform, false);
            route = node.AddComponent<LineRenderer>();
            routeMaterial = new Material(Resources.Load<Shader>("PixelSurface"));
            routeMaterial.SetColor("_Color", GameUITheme.Teal);
            route.sharedMaterial = routeMaterial;
            route.useWorldSpace = true;
            route.startWidth = route.endWidth = .045f;
            route.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            route.receiveShadows = false;
        }
        route.positionCount = replayCount;
        for (int i = 0; i < replayCount; i++) route.SetPosition(i, replay[i].origin);
        route.enabled = true;
    }

    void BuildHud()
    {
        var node = new GameObject("Rogue Tactics HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        node.transform.SetParent(transform, false);
        hud = node.GetComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 64;
        GameUITheme.FitCanvas(node.GetComponent<CanvasScaler>());
        var card = new GameObject("Tactics Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(node.transform, false);
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(20f, -200f);
        rect.sizeDelta = new Vector2(430f, 220f);
        card.GetComponent<Image>().raycastTarget = false;
        GameUITheme.Surface(card);
        echoText = GameUITheme.Label(card.transform, "", 20, new Vector2(0, 84), new Vector2(398, 34));
        var bar = new GameObject("Charge", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(card.transform, false);
        bar.GetComponent<RectTransform>().sizeDelta = new Vector2(398, 5);
        bar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 61);
        bar.GetComponent<Image>().color = GameUITheme.Recess;
        bar.GetComponent<Image>().raycastTarget = false;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bar.transform, false);
        chargeFill = fill.GetComponent<Image>();
        chargeFill.color = GameUITheme.Teal;
        chargeFill.raycastTarget = false;
        GameUITheme.Stretch(chargeFill.rectTransform);
        pactText = GameUITheme.Label(card.transform, "", 18, new Vector2(0, 6), new Vector2(398, 100));
        noticeText = GameUITheme.Label(card.transform, "", 16, new Vector2(0, -80), new Vector2(398, 48));
        noticeText.color = GameUITheme.Muted;
    }

    void RefreshHud()
    {
        if (echoText == null) return;
        echoText.text = IsReplaying ? "戰術回聲 · 重播中" : EchoArmed ?
            $"[{GameControls.Label(this, GameAction.Echo)}] 啟動回聲 · {EchoRemaining:F1}s" :
            $"[{GameControls.Label(this, GameAction.Echo)}] 留下回聲  {Mathf.FloorToInt(Resonance)}/100";
        echoText.color = Resonance >= EchoCost ? GameUITheme.Gold : GameUITheme.Teal;
        chargeFill.rectTransform.anchorMax = new Vector2(Resonance / EchoCost, 1f);
        Pact shown = HasActivePact ? active : offer;
        string description = shown == Pact.Arsenal ? $"流轉誓約 · 3 種武器命中 {pactWeapons.Count}/3\n代價：承受傷害 +25%" :
            shown == Pact.Hunt ? $"獵場誓約 · 擊殺 {pactKills}/3\n代價：承受傷害 +25%" :
            shown == Pact.Untouched ? $"無傷誓約 · 擊殺 {pactKills}/2\n代價：受到任何傷害即失敗" : "交替武器命中 +10 回聲 · 擊殺 +12\n移動射擊後重播，形成交叉火力";
        pactText.text = HasActivePact ? $"{description}\n剩餘 {Mathf.CeilToInt(pactRemaining)} 秒 · 離房結束" :
            HasOffer ? $"{description}\n[{GameControls.Label(this, GameAction.Accept)}] 接受 25 秒挑戰  [{GameControls.Label(this, GameAction.Decline)}] 略過" : description;
        noticeText.text = noticeRemaining > 0 ? notice : HasOffer ? "獎勵：金幣、12% 生命、35 回聲" :
            EchoArmed ? "已固定位置與方向 · 繞背後再啟動" : "先射擊 → 留下 → 繞背 → 啟動 · 45% 傷害";
    }

    void OnDisable() { ResetTransientState(); if (hud != null) hud.enabled = false; }
    void OnDestroy() { if (routeMaterial != null) Destroy(routeMaterial); }
}
