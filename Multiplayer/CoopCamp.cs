using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CoopCampPlayer
{
    public int id, upgrade = -1, portal = -1;
    public string character;
    public bool selected, ready;
    public Vector3 position;
}

public sealed partial class CoopSession
{
    public const float CampPortalRadius = 2.2f;
    bool inCamp;
    float nextCampSnapshot, nextCampInput;
    readonly Dictionary<int, CoopCampPlayer> campPlayers = new Dictionary<int, CoopCampPlayer>();
    readonly Dictionary<int, Vector2> campInputs = new Dictionary<int, Vector2>();
    readonly Dictionary<int, float> campInputTimes = new Dictionary<int, float>();
    public static bool InCamp => IsMultiplayer && Instance != null && Instance.inCamp;
    public IEnumerable<CoopCampPlayer> CampPlayers => campPlayers.Values;
    public CoopCampPlayer LocalCampPlayer => campPlayers.TryGetValue(localId, out var player) ? player : null;
    public int CampLocalId => localId;
    public static int PortalAt(Vector3 position)
    {
        for (int i = 0; i < ExpeditionMaps.All.Length; i++)
        {
            Vector3 offset = position - TradingHubController.PortalPosition(i); offset.y = 0;
            if (offset.sqrMagnitude <= CampPortalRadius * CampPortalRadius) return i;
        }
        return -1;
    }
    void AddCampPlayer(int id, string character)
    {
        campPlayers[id] = new CoopCampPlayer { id = id, character = character,
            position = new Vector3(-3 + campPlayers.Count * 2, .15f, -8) };
        ResetCampReady();
    }
    IEnumerator PrepareCamp()
    {
        sceneReady = false;
        yield return null; yield return null;
        sceneReady = true;
        roomHud = new GameObject("Cooperative Room HUD", typeof(RectTransform), typeof(CoopRoomHud));
        roomHud.transform.SetParent(transform, false);
        if (Mode == CoopMode.Host) foreach (int peer in new List<int>(joined.Keys)) Welcome(peer);
    }
    public bool CampCanConfirm(out string reason)
    {
        reason = "等待至少兩位玩家加入營地";
        if (!InCamp || campPlayers.Count < 2) return false;
        int portal = -1;
        foreach (var player in campPlayers.Values)
        {
            if (!player.selected || player.upgrade < 0) { reason = "等待全員完成角色與技能升級選擇"; return false; }
            int current = PortalAt(player.position);
            if (current < 0) { reason = "全員必須同時站在同一個地圖圓圈內"; return false; }
            if (portal >= 0 && portal != current) { reason = "隊友位於不同地圖圓圈，請先集合"; return false; }
            portal = current;
        }
        reason = "全員已集合，等待房主按下「確認出發」"; return true;
    }
    public void ChooseCampCharacter(string character)
    { if (CharacterRoster.IsKnown(character)) CampCommand(new CoopPacket { kind = "campCharacter", character = character }); }
    public void ChooseCampUpgrade(int index)
    { if (index >= 0 && index < 3) CampCommand(new CoopPacket { kind = "campUpgrade", value = index }); }
    public void ConfirmCamp(bool ready) => CampCommand(new CoopPacket { kind = "campReady", value = ready ? 1 : 0 });
    public void MoveInCamp(Vector2 move)
    {
        if (!InCamp) return;
        move = Vector2.ClampMagnitude(move, 1);
        if (IsClient && Time.unscaledTime < nextCampInput) return;
        nextCampInput = Time.unscaledTime + 1f / 30;
        CampCommand(new CoopPacket { kind = "campMove", input = new CoopInputFrame { move = move } });
    }
    void CampCommand(CoopPacket packet)
    {
        if (!InCamp || !sceneReady || GameSceneFlow.IsLoading) return;
        if (IsClient) Send(0, packet); else HandleCampCommand(localId, packet);
    }
    void HandleCampCommand(int id, CoopPacket packet)
    {
        if (!sceneReady || !campPlayers.TryGetValue(id, out var player)) return;
        switch (packet.kind)
        {
            case "campMove":
                campInputs[id] = Vector2.ClampMagnitude(packet.input.move, 1);
                campInputTimes[id] = Time.unscaledTime; return;
            case "campCharacter":
                if (!CharacterRoster.IsKnown(packet.character)) return;
                player.character = packet.character; player.selected = true; player.upgrade = -1;
                if (id == 0) firstCharacter = player.character; else joined[id] = player.character;
                ResetCampReady(); break;
            case "campUpgrade":
                if (!player.selected || packet.value < 0 || packet.value > 2) return;
                player.upgrade = packet.value; ResetCampReady(); break;
            case "campReady":
                if (id != 0) return;
                if (packet.value != 0 && packet.value != 1) return;
                player.ready = packet.value == 1 && CampCanConfirm(out _); break;
            default: return;
        }
        BroadcastCamp();
    }
    void ResetCampReady() { foreach (var player in campPlayers.Values) player.ready = false; }
    void UpdateCamp()
    {
        if (!sceneReady || GameSceneFlow.IsLoading || IsClient) return;
        var hub = FindFirstObjectByType<TradingHubController>();
        if (hub == null) return;
        bool changedPortal = false;
        foreach (var player in campPlayers.Values)
        {
            Vector2 input = campInputs.TryGetValue(player.id, out var move) && campInputTimes.TryGetValue(player.id, out var time) &&
                Time.unscaledTime - time < .35f ? move : Vector2.zero;
            player.position = hub.MoveCampVisitor(player, input, Mathf.Min(Time.unscaledDeltaTime, .05f));
            int portal = PortalAt(player.position);
            if (portal != player.portal) { player.portal = portal; changedPortal = true; }
        }
        if (changedPortal || !CampCanConfirm(out _)) ResetCampReady();
        bool allReady = CampCanConfirm(out _) && campPlayers.TryGetValue(0, out var host) && host.ready;
        if (allReady) { DepartCamp(); return; }
        if (Time.unscaledTime >= nextCampSnapshot) { nextCampSnapshot = Time.unscaledTime + .05f; BroadcastCamp(); }
    }
    CoopCampPlayer[] CampSnapshot()
    { var result = new CoopCampPlayer[campPlayers.Count]; campPlayers.Values.CopyTo(result, 0); return result; }
    void BroadcastCamp()
    {
        if (!InCamp || Mode != CoopMode.Host) return;
        var packet = new CoopPacket { kind = "campState", camp = CampSnapshot() };
        foreach (int peer in joined.Keys) Send(peer, packet);
    }
    void ReceiveCamp(CoopCampPlayer[] players)
    {
        if (players == null || players.Length < 1 || players.Length > 4) return;
        campPlayers.Clear();
        foreach (var player in players) if (player != null && CharacterRoster.IsKnown(player.character)) campPlayers[player.id] = player;
    }
    void DepartCamp()
    {
        int index = LocalCampPlayer.portal;
        string destination = ExpeditionMaps.All[index].Scene;
        if (!Application.CanStreamedLevelBeLoaded(destination)) { ResetCampReady(); return; }
        var packet = new CoopPacket { kind = "depart", scene = destination, camp = CampSnapshot() };
        foreach (int peer in joined.Keys) Send(peer, packet);
        BeginCampMap(destination);
    }
    void ReceiveDeparture(CoopPacket packet)
    {
        if (!ExpeditionMaps.IsMap(packet.scene) || !Application.CanStreamedLevelBeLoaded(packet.scene)) return;
        ReceiveCamp(packet.camp);
        BeginCampMap(packet.scene);
    }
    void BeginCampMap(string destination)
    {
        if (LocalCampPlayer == null) { Fail("營地角色資料遺失，請重新加入房間。"); return; }
        firstCharacter = LocalCampPlayer.character;
        inCamp = false; sceneReady = false; sceneName = destination;
        campInputs.Clear(); campInputTimes.Clear();
        if (roomHud != null) Destroy(roomHud); roomHud = null;
        Launch();
    }
    IEnumerator ApplyCampUpgradeAfterStart(CoopActor actor)
    { yield return null; yield return null; if (actor != null) ApplyCampUpgrade(actor); }
    void ApplyCampUpgrade(CoopActor actor)
    {
        if (actor == null || !campPlayers.TryGetValue(actor.Id, out var player) || player.upgrade < 0) return;
        var skills = actor.GetComponent<CharacterSkills>();
        skills.ApplyStartingCampUpgrade(player.upgrade);
    }
}
