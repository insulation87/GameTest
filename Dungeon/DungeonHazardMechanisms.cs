using System.Collections.Generic;
using UnityEngine;

public sealed partial class DungeonHazard
{
    public const float BlastRadius = 3.2f;
    public float cycleOffset;
    readonly Collider[] targets = new Collider[128];
    readonly RaycastHit[] sightHits = new RaycastHit[32];
    readonly HashSet<EnemyHealth> enemiesHit = new HashSet<EnemyHealth>();
    Material effectMaterial;
    PlayerHealth activator;
    bool detonated;

    void BuildNewMechanism()
    {
        safeColor = kind == DungeonHazardKind.FlameVent ? new Color(.8f,.32f,.06f) :
            kind == DungeonHazardKind.SweepingBlade ? new Color(.12f,.58f,.66f) : new Color(.65f,.22f,.65f);
        var metal = Material(new Color(.18f,.22f,.27f));
        warningMaterial = Material(safeColor);
        effectMaterial = Material(kind == DungeonHazardKind.FlameVent ? new Color(1.35f,.55f,.08f) : safeColor);
        Box("機關底座", new Vector3(0,.025f,0), new Vector3(Width,.05f,Width), metal, false);
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side < 2;
            Box("警示邊線", alongX ? new Vector3(0,.065f,(side == 0 ? -1 : 1) * 1.35f) : new Vector3((side == 2 ? -1 : 1) * 1.35f,.065f,0),
                alongX ? new Vector3(2.7f,.045f,.08f) : new Vector3(.08f,.045f,2.7f), warningMaterial, false);
        }
        movingPart = new GameObject("機關動態效果").transform; movingPart.SetParent(transform, false);
        if (kind == DungeonHazardKind.FlameVent)
        {
            warningSeconds = 1.15f; damage = 30; resetSeconds = 3.8f;
            var mesh = SpikeMesh(); meshes.Add(mesh);
            for (int i = 0; i < 5; i++)
            {
                float x = i == 4 ? 0 : i < 2 ? -.8f : .8f, z = i == 4 ? 0 : i % 2 == 0 ? -.8f : .8f;
                Box("噴火孔", new Vector3(x,.06f,z), new Vector3(.38f,.05f,.38f), warningMaterial, false);
                var plume = new GameObject("火焰柱", typeof(MeshFilter), typeof(MeshRenderer));
                plume.transform.SetParent(movingPart, false); plume.transform.localPosition = new Vector3(x,.06f,z);
                plume.transform.localScale = new Vector3(2.3f,2.6f,2.3f);
                plume.GetComponent<MeshFilter>().sharedMesh = mesh; plume.GetComponent<MeshRenderer>().sharedMaterial = effectMaterial;
            }
            movingPart.localScale = new Vector3(1,.015f,1);
            elapsed = Mathf.Repeat(cycleOffset, resetSeconds);
        }
        else if (kind == DungeonHazardKind.SweepingBlade)
        {
            warningSeconds = 1f; damage = 28; resetSeconds = 3f;
            movingPart.localPosition = Vector3.up * .65f;
            var blade = Box("掃掠刀刃", Vector3.up * .65f, new Vector3(2.6f,.22f,.16f), metal, false);
            blade.transform.SetParent(movingPart, true);
            var edge = Box("刀刃亮邊", new Vector3(0,.8f,0), new Vector3(2.6f,.025f,.08f), effectMaterial, false);
            edge.transform.SetParent(movingPart, true);
            for (int i = -1; i <= 1; i++) Box("跳躍警示紋", new Vector3(i * .7f,.065f,0), new Vector3(.09f,.03f,2.35f), warningMaterial, false);
            movingPart.localRotation = Quaternion.Euler(0,-75,0);
        }
        else
        {
            warningSeconds = 1.25f; damage = 45; resetSeconds = 6f;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f;
                var edge = Box("爆炸範圍警示", new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * BlastRadius,.07f,Mathf.Cos(angle * Mathf.Deg2Rad) * BlastRadius),
                    new Vector3(.85f,.035f,.075f), warningMaterial, false);
                edge.transform.localRotation = Quaternion.Euler(0,angle,0);
                var wave = Box("爆裂波", edge.transform.localPosition, new Vector3(.8f,.1f,.1f), effectMaterial, false);
                wave.transform.localRotation = edge.transform.localRotation; wave.transform.SetParent(movingPart, true);
            }
            var rune = Box("壓力符文", Vector3.up * .07f, new Vector3(1.2f,.05f,1.2f), warningMaterial, false);
            rune.transform.localRotation = Quaternion.Euler(0,45,0);
            movingPart.localScale = Vector3.one * .015f;
        }
        // The new mechanisms can also hurt pursuers, so enemies may walk through them.
        // Spawn points and teleports still exclude their full danger footprint.
    }

    void TickNewMechanism()
    {
        elapsed += Time.deltaTime;
        if (State == DungeonHazardState.Ready)
        {
            bool trigger = kind == DungeonHazardKind.FlameVent && elapsed >= resetSeconds;
            if (kind != DungeonHazardKind.FlameVent && Time.time >= nextProbe)
            {
                nextProbe = Time.time + .12f;
                int count = Query(.13f,.2f);
                for (int i = 0; i < count; i++)
                {
                    var health = overlaps[i].GetComponentInParent<PlayerHealth>();
                    if (health == null || health.IsDead || !CanAffect(health)) continue;
                    activator = health; trigger = true; break;
                }
            }
            if (!trigger) return;
            State = DungeonHazardState.Warning; elapsed = 0;
            GameAudio.At(GameSound.TrapWarning, transform.position);
        }
        if (State == DungeonHazardState.Warning)
        {
            warningMaterial.color = Color.Lerp(safeColor, new Color(1.6f,.5f,.12f), .55f + .45f * Mathf.Sin(elapsed * 15));
            if (elapsed < warningSeconds) return;
            State = DungeonHazardState.Active; elapsed = 0; hitThisCycle.Clear(); enemiesHit.Clear(); detonated = false;
            GameAudio.At(kind == DungeonHazardKind.BlastRune ? GameSound.Explosion : kind == DungeonHazardKind.FlameVent ? GameSound.FlameJet : GameSound.Sword, transform.position);
        }
        if (State == DungeonHazardState.Active)
        {
            if (kind == DungeonHazardKind.FlameVent)
            {
                movingPart.localScale = new Vector3(1, Mathf.Min(1,elapsed / .15f) * (.88f + .12f * Mathf.Sin(elapsed * 18)), 1);
                HurtArea(false);
            }
            else if (kind == DungeonHazardKind.SweepingBlade)
            {
                movingPart.localRotation = Quaternion.Euler(0, Mathf.Lerp(-75,75,Mathf.Clamp01(elapsed / 1.25f)), 0);
                HurtArea(false);
            }
            else
            {
                movingPart.localScale = Vector3.one * Mathf.Lerp(.1f,1,Mathf.Clamp01(elapsed / .32f));
                effectMaterial.color = Color.Lerp(new Color(1.8f,.6f,.15f), safeColor, Mathf.Clamp01(elapsed / .55f));
                if (!detonated) { detonated = true; HurtArea(true); }
            }
            float duration = kind == DungeonHazardKind.FlameVent ? 1.45f : kind == DungeonHazardKind.SweepingBlade ? 1.25f : .55f;
            if (elapsed < duration) return;
            State = DungeonHazardState.Recovering; elapsed = 0;
            movingPart.localScale = kind == DungeonHazardKind.SweepingBlade ? Vector3.one : Vector3.one * .015f;
            warningMaterial.color = safeColor * .45f;
        }
        if (State == DungeonHazardState.Recovering)
        {
            if (kind == DungeonHazardKind.SweepingBlade) movingPart.localRotation = Quaternion.Euler(0,Mathf.Lerp(75,-75,Mathf.Clamp01(elapsed / .5f)),0);
            if (elapsed < (kind == DungeonHazardKind.FlameVent ? .5f : resetSeconds)) return;
            State = DungeonHazardState.Ready; elapsed = 0; warningMaterial.color = safeColor; activator = null;
        }
    }

    bool CanAffect(PlayerHealth health)
    {
        var actor = CoopInput.Actor(health);
        return !CoopSession.IsClient && (actor == null || actor.Local || actor.HasRemoteInput);
    }
    void HurtArea(bool blast)
    {
        int count = blast ? Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * .65f, BlastRadius, targets, Physics.AllLayers, QueryTriggerInteraction.Ignore) :
            kind == DungeonHazardKind.SweepingBlade ? Physics.OverlapBoxNonAlloc(movingPart.position, new Vector3(1.3f,.16f,.14f), targets, movingPart.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore) :
            Physics.OverlapBoxNonAlloc(transform.position + transform.up * 1.1f, new Vector3(Width * .5f,1.1f,Width * .5f), targets, transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider target = targets[i];
            if (target == null || blast && Occluded(target)) continue;
            var health = target.GetComponentInParent<PlayerHealth>();
            if (health != null && !health.IsDead && !health.IsInvincible && CanAffect(health) && hitThisCycle.Add(health)) health.TakeDamage(damage);
            var enemy = target.GetComponentInParent<EnemyHealth>();
            if (enemy != null && !enemy.IsDead && enemiesHit.Add(enemy)) enemy.TakeDamage(blast ? 95f : damage * 1.5f, blast ? activator : null);
        }
    }
    bool Occluded(Collider target)
    {
        Vector3 origin = transform.position + transform.up * .7f;
        Vector3 delta = target.bounds.center - origin; float distance = delta.magnitude;
        if (distance < .01f) return false;
        int count = Physics.RaycastNonAlloc(origin, delta / distance, sightHits, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = sightHits[i].collider;
            if (hit == target || hit.GetComponentInParent<PlayerHealth>() != null || hit.GetComponentInParent<EnemyHealth>() != null || hit.GetComponentInParent<DungeonHazard>() != null) continue;
            return true;
        }
        return false;
    }
}
