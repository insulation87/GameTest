using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class TacticalField : MonoBehaviour
{
    public enum Kind { Vortex, Fire, Ward, Shelter }
    public Kind Type { get; private set; }
    public PlayerHealth Owner { get; private set; }
    public float Radius { get; private set; }
    float expires, nextPulse, damage;
    TacticalMarker marker;
    EnemyHealth boundBoss;
    bool bossBound;
    public void BindBoss(EnemyHealth value) { boundBoss = value; bossBound = true; }
    public static void ClearOwned(PlayerHealth owner)
    {
        foreach (var field in new List<TacticalField>(fields))
            if (field != null && field.Owner == owner) { field.enabled = false; Destroy(field.gameObject); }
    }
    static readonly HashSet<TacticalField> fields = new HashSet<TacticalField>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => fields.Clear();
    public static TacticalField Create(Kind kind, Vector3 point, PlayerHealth owner, float radius, float duration, float power)
    {
        // At most one field of each type per owner. Repeated hits cannot stack wards or burns.
        foreach (var old in new List<TacticalField>(fields))
            if (old != null && old.Owner == owner && old.Type == kind) { old.enabled = false; Destroy(old.gameObject); }
        var field = new GameObject("Tactical " + kind).AddComponent<TacticalField>();
        field.transform.position = point; field.Type = kind; field.Owner = owner; field.Radius = radius;
        field.expires = Time.time + duration; field.damage = power;
        field.marker = TacticalMarker.Create(kind == Kind.Fire ? "燃燒區" : kind == Kind.Vortex ? "聚怪場" : kind == Kind.Ward ? "隊伍盾牆" : "古樹庇護", point, Vector3.forward,
            kind == Kind.Fire ? new Color(1,.4f,.15f) : kind == Kind.Vortex ? Color.magenta : GameUITheme.Teal, radius);
        field.marker.transform.SetParent(field.transform, true);
        field.marker.Replicate = true;
        return field;
    }
    void OnEnable() => fields.Add(this);
    void OnDisable() => fields.Remove(this);
    public static float Protection(PlayerHealth player)
    {
        foreach (var field in fields)
            if (field != null && (field.Type == Kind.Ward || field.Type == Kind.Shelter) &&
                Time.time < field.expires && (field.transform.position - player.transform.position).sqrMagnitude < field.Radius * field.Radius &&
                Clear(field.transform.position + Vector3.up, player.transform.position + Vector3.up, null, player))
            {
                if (field.Owner != null && field.Owner != player) field.Owner.GetComponent<CombatMomentum>()?.OnContribution();
                return field.Type == Kind.Shelter ? .4f : .7f;
            }
        return 1f;
    }
    public static bool InOwnFire(PlayerHealth owner)
    {
        foreach (var field in fields)
            if (field != null && field.Owner == owner && field.Type == Kind.Fire && Time.time < field.expires &&
                (field.transform.position - owner.transform.position).sqrMagnitude <= field.Radius * field.Radius) return true;
        return false;
    }
    public static bool InShelter(PlayerHealth player)
    {
        foreach (var field in fields)
            if (field != null && field.Type == Kind.Shelter && Time.time < field.expires &&
                (field.transform.position-player.transform.position).sqrMagnitude <= field.Radius*field.Radius &&
                Clear(field.transform.position+Vector3.up,player.transform.position+Vector3.up,null,player)) return true;
        return false;
    }
    public static bool Clear(Vector3 from, Vector3 to, EnemyHealth target, PlayerHealth owner)
    {
        Vector3 delta = to - from;
        foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (hit.collider.GetComponentInParent<EnemyHealth>() == null && hit.collider.GetComponentInParent<PlayerHealth>() == null &&
                !PickupCollisionUtility.IsPickup(hit.collider)) return false;
        return true;
    }
    void Update()
    {
        if (CoopSession.IsClient || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen) return;
        if (Time.time >= expires || (Owner != null && Owner.IsDead) || bossBound && (boundBoss == null || boundBoss.IsDead)) { Destroy(gameObject); return; }
        if (marker != null) marker.SetText((Type == Kind.Fire ? "燃燒區" : Type == Kind.Vortex ? "聚怪場" : Type == Kind.Ward ? "隊伍盾牆" : "古樹庇護") + $" · {expires - Time.time:F1}s");
        if (Time.time < nextPulse || (Type != Kind.Fire && Type != Kind.Vortex)) return;
        nextPulse = Time.time + .5f;
        foreach (var enemy in new List<EnemyHealth>(EnemyHealth.ActiveEnemies))
        {
            if (enemy == null || enemy.IsDead || (enemy.transform.position - transform.position).sqrMagnitude > Radius * Radius ||
                !Clear(transform.position + Vector3.up * .5f, enemy.transform.position + Vector3.up * .5f, enemy, Owner)) continue;
            if (Type == Kind.Fire) enemy.TakeDamage(damage, Owner,transform.position);
            else if (enemy.GetComponent<BossController>() == null)
            {
                enemy.GetComponent<EnemyAI>()?.FreezeFor(.3f);
                if (enemy.TryGetComponent(out NavMeshAgent agent) && agent.enabled && agent.isOnNavMesh)
                {
                    Vector3 delta = transform.position - enemy.transform.position; delta.y = 0;
                    Vector3 destination = enemy.transform.position + Vector3.ClampMagnitude(delta, .7f);
                    if (!agent.Raycast(destination, out _)) agent.Move(destination - enemy.transform.position);
                }
                Owner?.GetComponent<CombatMomentum>()?.OnContribution();
            }
        }
    }
}
