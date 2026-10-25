using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(30)]
public sealed class TacticalBossMechanics : MonoBehaviour
{
    public enum Move { Pursue, SlamWarning, Recovery, ChargeWarning, Charge, Stunned, Ritual }
    public Move CurrentMove { get; private set; }
    public bool ControlsAI => CurrentMove != Move.Pursue;
    public bool IsExposed => CurrentMove == Move.Recovery || CurrentMove == Move.Stunned;
    public string Hint => CurrentMove == Move.SlamWarning ? "重擊預警：離開前方，繞背反擊" :
        CurrentMove == Move.ChargeWarning ? "衝刺鎖定：側移，將衝刺引向岩石" :
        CurrentMove == Move.Charge ? "避開衝刺與火路" : CurrentMove == Move.Ritual ? "破壞三個紫色節點打斷施法，或撤出 14m 紅圈" :
        IsExposed ? "破綻窗口：集中火力！" : boss != null && boss.Phase == 1 ? "正面重甲：引出重擊 → 繞背破甲" : "保持移動，觀察下一次預警";
    BossController boss;
    EnemyAI ai;
    EnemyHealth health;
    NavMeshAgent agent;
    float until, nextMove, nextFlame;
    int lastPhase;
    Vector3 lockedPoint, chargeDirection;
    TacticalMarker marker;
    readonly List<EnemyHealth> nodes = new List<EnemyHealth>();
    readonly HashSet<PlayerHealth> chargedPlayers = new HashSet<PlayerHealth>();
    public IReadOnlyList<EnemyHealth> RitualNodes => nodes;
    void Awake() { boss = GetComponent<BossController>(); ai = GetComponent<EnemyAI>(); health = GetComponent<EnemyHealth>(); agent = GetComponent<NavMeshAgent>(); nextMove = Time.time + 3; }
    public float ModifyIncoming(float damage, Vector3 origin)
    {
        if (boss == null) return damage;
        if (IsExposed) return damage * 1.5f;
        Vector3 offset = origin - transform.position; offset.y = 0;
        if (boss.Phase == 1 && offset.sqrMagnitude > .01f && Vector3.Dot(transform.forward, offset.normalized) > .25f)
            return damage * .3f;
        return damage;
    }
    PlayerHealth Closest()
    {
        PlayerHealth best = null; float distance = 1600;
        foreach (var player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (player.IsDead) continue;
            float square = (player.transform.position - transform.position).sqrMagnitude;
            if (square < distance) { best = player; distance = square; }
        }
        return best;
    }
    void Update()
    {
        if (CoopSession.IsClient || boss == null || health == null || health.IsDead || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen) return;
        if (lastPhase != boss.Phase) { ResetMove(); lastPhase = boss.Phase; nextMove = Time.time + 3f; }
        if (boss.IsTransforming) return;
        var victim = Closest();
        if (victim == null) { if (ControlsAI) ResetMove(); return; }
        if (CurrentMove == Move.Pursue)
        {
            if(marker==null)
            {
                marker=TacticalMarker.Create("首領破解提示",transform.position,transform.forward,GameUITheme.Gold,1.5f);
                marker.transform.SetParent(transform,true); marker.Replicate=true;
            }
            marker.SetText(boss.FormName+"\n"+Hint);
            if (Time.time < nextMove) return;
            if (boss.Phase == 1)
            {
                if (Vector3.Distance(victim.transform.position, transform.position) > 8) return;
                Face(victim.transform.position); Begin(Move.SlamWarning, 1.3f, 7, new Color(1,.5f,.1f));
            }
            else if (boss.Phase == 2)
            { lockedPoint = victim.transform.position; Face(lockedPoint); chargeDirection = transform.forward; Begin(Move.ChargeWarning, 1.4f, 2, Color.red); }
            else BeginRitual();
            return;
        }
        ai?.SetCombatPaused(true);
        if (marker != null) marker.SetText($"{boss.FormName}\n{Hint}\n{Mathf.Max(0, until - Time.time):F1}s");
        if (CurrentMove == Move.Ritual && NodesDestroyed())
        { ClearNodes(); Begin(Move.Stunned, 5, 2, GameUITheme.Teal); return; }
        if (CurrentMove == Move.Charge)
        {
            float step = 14 * Time.deltaTime;
            Vector3 from = transform.position + Vector3.up;
            bool blocked = false;
            foreach (var hit in Physics.SphereCastAll(from, .7f, chargeDirection, step + .8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<PlayerHealth>() == null &&
                    hit.collider.GetComponentInParent<EnemyHealth>() == null && !PickupCollisionUtility.IsPickup(hit.collider)) { blocked = true; break; }
            Vector3 point = transform.position + chargeDirection * step;
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                if (agent.Raycast(point, out _)) blocked = true;
                if (!blocked) agent.Move(chargeDirection * step);
            }
            else blocked = true; // Never teleport an un-navigable boss through scenery.
            if (blocked) { Begin(Move.Stunned, 4, 2, GameUITheme.Teal); return; }
            if (Time.time >= nextFlame)
            { nextFlame = Time.time + .45f; BossFlameTrail.Create(transform.position, health, 5f); }
            foreach (var player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                if (!player.IsDead && (player.transform.position - transform.position).sqrMagnitude < 4 && chargedPlayers.Add(player))
                    player.TakeDamage(EnemyCombatRules.DamageAtLevel(health.EnemyLevel));
        }
        if (Time.time < until) return;
        if (CurrentMove == Move.SlamWarning)
        {
            foreach (var player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            {
                Vector3 delta = player.transform.position - transform.position;
                if (!player.IsDead && delta.magnitude <= 7 && Vector3.Dot(transform.forward, delta.normalized) > .35f &&
                    TacticalField.Clear(transform.position + Vector3.up, player.transform.position + Vector3.up, null, player))
                    player.TakeDamage(EnemyCombatRules.DamageAtLevel(health.EnemyLevel) * 1.5f);
            }
            Begin(Move.Recovery, 3, 2, GameUITheme.Teal);
        }
        else if (CurrentMove == Move.ChargeWarning) Begin(Move.Charge, 1.5f, 1, Color.red);
        else if (CurrentMove == Move.Ritual)
        {
            foreach (var player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
                if (!player.IsDead && (player.transform.position - transform.position).sqrMagnitude <= 196 &&
                    TacticalField.Clear(transform.position + Vector3.up, player.transform.position + Vector3.up, null, player))
                    player.TakeDamage(EnemyCombatRules.DamageAtLevel(health.EnemyLevel) * 2);
            ClearNodes(); Begin(Move.Recovery, 2, 2, GameUITheme.Teal);
        }
        else { ResetMove(); nextMove = Time.time + (boss.Phase == 1 ? 3 : 4); }
    }
    void Face(Vector3 target) { Vector3 delta = target - transform.position; delta.y = 0; if (delta.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(delta); }
    void Begin(Move move, float seconds, float radius, Color color)
    {
        if (move == Move.Charge) chargedPlayers.Clear();
        CurrentMove = move; until = Time.time + seconds; ai?.SetCombatPaused(true);
        if (marker != null) Destroy(marker.gameObject);
        marker = TacticalMarker.Create("首領戰術預警", transform.position, transform.forward, color, radius);
        marker.Replicate = true;
        marker.SetText(Hint);
    }
    void BeginRitual()
    {
        ClearNodes();
        for (int i = 0; i < 3; i++)
        {
            Vector3 point = transform.position + Quaternion.Euler(0, i * 120, 0) * Vector3.forward * 5;
            if (NavMesh.SamplePosition(point, out var hit, 3, NavMesh.AllAreas)) point = hit.position;
            var node = GameObject.CreatePrimitive(PrimitiveType.Cube); node.name = "施法節點 · 攻擊破壞";
            node.transform.position = point + Vector3.up * .8f; node.transform.localScale = new Vector3(.8f,1.6f,.8f);
            var enemy = node.AddComponent<EnemyHealth>(); enemy.SetMaxHealth(35); enemy.SetLevelAndExperience(1,0);
            enemy.healthDropChance = enemy.weakNpcWeaponDropChance = enemy.strongNpcWeaponDropChance = enemy.armorEquipmentDropChance = enemy.progressionDropChance = 0;
            node.AddComponent<TacticalRitualNode>();
            var info = TacticalMarker.Create("紫色施法節點", point, Vector3.forward, Color.magenta, .6f);
            info.transform.SetParent(node.transform, true); info.SetText("施法節點 · 攻擊破壞"); nodes.Add(enemy);
            info.Replicate = true;
        }
        Begin(Move.Ritual, 8, 14, Color.magenta);
    }
    bool NodesDestroyed() { foreach (var node in nodes) if (node != null && !node.IsDead) return false; return nodes.Count > 0; }
    void ClearNodes() { foreach (var node in nodes) if (node != null) Destroy(node.gameObject); nodes.Clear(); }
    void ResetMove() { CurrentMove = Move.Pursue; ClearNodes(); if (marker != null) Destroy(marker.gameObject); marker = null; ai?.SetCombatPaused(false); }
    void OnDisable() => ResetMove();
}

public sealed class TacticalRitualNode : MonoBehaviour { }

public sealed class BossFlameTrail : MonoBehaviour
{
    float expires, nextDamage;
    EnemyHealth boss;
    TacticalMarker marker;
    public static void Create(Vector3 point, EnemyHealth boss, float duration)
    {
        var trail = new GameObject("Boss Flame Trail").AddComponent<BossFlameTrail>();
        trail.transform.position = point; trail.boss = boss; trail.expires = Time.time + duration;
        trail.nextDamage = Time.time + .9f;
        trail.marker = TacticalMarker.Create("火路 · 避開或進入庇護區", point, Vector3.forward, new Color(1,.3f,.1f), 1.8f);
        trail.marker.transform.SetParent(trail.transform, true);
        trail.marker.Replicate = true;
    }
    void Update()
    {
        if (CoopSession.IsClient || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen) return;
        if (Time.time >= expires || boss == null || boss.IsDead) { Destroy(gameObject); return; }
        if (Time.time < nextDamage) return; nextDamage = Time.time + 1f;
        foreach (var player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            if (!player.IsDead && !TacticalField.InShelter(player) && (player.transform.position - transform.position).sqrMagnitude < 3.24f &&
                TacticalField.Clear(transform.position + Vector3.up * .5f, player.transform.position + Vector3.up * .5f, null, player))
                player.TakeDamage(EnemyCombatRules.DamageAtLevel(boss.EnemyLevel) * .3f);
    }
}
