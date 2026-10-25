using System;
using System.Collections.Generic;
using MedievalDungeonKit;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-200)]
public sealed class DungeonHazardDirector : MonoBehaviour
{
    [Min(1)] public int roomInterval = 1;
    [Range(1,3)] public int hazardsPerRoom = 2;
    public int SpikeCount { get; private set; }
    public int CollapsingFloorCount { get; private set; }
    public int FlameVentCount { get; private set; }
    public int SweepingBladeCount { get; private set; }
    public int BlastRuneCount { get; private set; }
    public int TotalCount => placed.Count;
    public bool Built { get; private set; }
    readonly List<GameObject> placed = new List<GameObject>();
    DungeonFloorOpening openings;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }
    static void SceneLoaded(Scene scene,LoadSceneMode mode)
    {
        if (FindFirstObjectByType<Player>() == null) return;
        DungeonViewSettings map=FindFirstObjectByType<DungeonViewSettings>();
        if(map!=null && map.GetComponent<DungeonHazardDirector>()==null) map.gameObject.AddComponent<DungeonHazardDirector>();
    }
    void Start() { Build(); }

    public void Build()
    {
        if(Built) return;
        // Indoor floor mechanisms do not belong on the meadow/forest surface.
        if (OverworldDirector.EnsureWorld() != null) { Built = true; return; }
        Built=true;
        Player player=FindFirstObjectByType<Player>();
        if(player==null) return;
        var markers=new List<DungeonMarker>(GetComponentsInChildren<DungeonMarker>());
        var rooms=markers.FindAll(m=>string.Equals(m.kind,"Room",StringComparison.OrdinalIgnoreCase));
        rooms.Sort((a,b)=>string.CompareOrdinal(a.name,b.name));
        openings=gameObject.AddComponent<DungeonFloorOpening>();
        for(int i=0;i<rooms.Count;i++)
        {
            // Keep the initial hall and the central cross clear; mix mechanisms at room edges.
            if(i==0 || i%Mathf.Max(1,roomInterval)!=(roomInterval > 1 ? 1 : 0)) continue;
            DungeonMarker room=rooms[i];
            DungeonRoomBoundary boundary=DungeonRoomBoundary.GetOrCreate(room);
            if(boundary==null || IsBossRoom(boundary,markers)) continue;
            float offset=Mathf.Min(boundary.halfSize.x,boundary.halfSize.y)*.55f;
            if(offset<5.5f) continue;
            var quadrants = new[] { new Vector2(-1,1),new Vector2(1,-1),new Vector2(-1,-1),new Vector2(1,1) };
            int wanted = Mathf.Clamp(hazardsPerRoom + (room.name.StartsWith("L3_",StringComparison.Ordinal) ? 1 : 0),1,3);
            int inRoom = 0;
            for (int q = 0; q < quadrants.Length && inRoom < wanted; q++)
            {
                Vector2 quadrant = quadrants[(q + i) % quadrants.Length];
                DungeonHazardKind kind = (DungeonHazardKind)((i * 2 + inRoom) % 5);
                Vector3 candidate=room.transform.TransformPoint(new Vector3(quadrant.x*offset,0,quadrant.y*offset));
                float half = kind == DungeonHazardKind.BlastRune ? DungeonHazard.BlastRadius : DungeonHazard.Width * .5f;
                Vector3 localScale = room.transform.lossyScale;
                if (offset * Mathf.Min(Mathf.Abs(localScale.x),Mathf.Abs(localScale.z)) < half + 2.4f ||
                    !boundary.Contains(candidate,half + .6f) || IsProtected(candidate,player,markers) || IsUnsafe(candidate,half + .8f)) continue;
                if(!FindFloor(candidate,out BoxCollider floor,out Vector3 surface)) continue;
                if(Physics.OverlapBox(surface+Vector3.up*1.05f,new Vector3(1.65f,1f,1.65f),floor.transform.rotation,
                    Physics.AllLayers,QueryTriggerInteraction.Ignore).Length>0) continue;
                if(kind==DungeonHazardKind.CollapsingFloor && !openings.Cut(floor,surface,DungeonHazard.Width,transform)) continue;
                if (kind == DungeonHazardKind.CollapsingFloor) Physics.SyncTransforms();
                string[] labels = { "警示地刺", "崩落地板", "節奏火焰地板", "掃掠刀刃", "誘敵爆裂符文" };
                var node=new GameObject(labels[(int)kind]);
                // Keep dimensions in world metres even when an imported map root is scaled.
                node.transform.SetPositionAndRotation(surface,floor.transform.rotation);
                node.transform.SetParent(transform,true);
                var hazard=node.AddComponent<DungeonHazard>();hazard.cycleOffset = (i * 1.37f + inRoom * .81f); hazard.Initialize(kind);placed.Add(node); inRoom++;
                switch (kind)
                {
                    case DungeonHazardKind.Spikes: SpikeCount++; break;
                    case DungeonHazardKind.CollapsingFloor: CollapsingFloorCount++; break;
                    case DungeonHazardKind.FlameVent: FlameVentCount++; break;
                    case DungeonHazardKind.SweepingBlade: SweepingBladeCount++; break;
                    case DungeonHazardKind.BlastRune: BlastRuneCount++; break;
                }
            }
        }
        Physics.SyncTransforms();
        Debug.Log($"已配置 {TotalCount} 組機關：地刺 {SpikeCount}、崩落 {CollapsingFloorCount}、火焰 {FlameVentCount}、刀刃 {SweepingBladeCount}、符文 {BlastRuneCount}。",this);
    }
    static bool IsBossRoom(DungeonRoomBoundary room,List<DungeonMarker> markers)
    {
        foreach(var marker in markers)
            if(string.Equals(marker.kind,"Boss",StringComparison.OrdinalIgnoreCase) && room.Contains(marker.transform.position)) return true;
        return false;
    }
    bool IsProtected(Vector3 point,Player player,List<DungeonMarker> markers)
    {
        if(Near(point,player.transform.position,6f)) return true;
        PlayerHealth health=player.GetComponent<PlayerHealth>();
        if(health!=null && health.respawnPoint!=null && Near(point,health.respawnPoint.position,6f)) return true;
        foreach(var marker in markers)
            if(!string.Equals(marker.kind,"Room",StringComparison.OrdinalIgnoreCase) && Near(point,marker.transform.position,6f)) return true;
        foreach(Transform item in GetComponentsInChildren<Transform>())
            if(item.name.StartsWith("Stair_",StringComparison.Ordinal) &&
                (item.name.EndsWith("Top",StringComparison.Ordinal) || item.name.EndsWith("Bottom",StringComparison.Ordinal)) && Near(point,item.position,7f)) return true;
        return false;
    }
    static bool Near(Vector3 a,Vector3 b,float distance)
    {
        Vector3 delta=a-b;
        if(Mathf.Abs(delta.y)>3f) return false;
        delta.y=0;return delta.sqrMagnitude<distance*distance;
    }
    static bool FindFloor(Vector3 candidate,out BoxCollider floor,out Vector3 point)
    {
        floor=null;point=default;
        foreach(RaycastHit hit in Physics.RaycastAll(candidate+Vector3.up,Vector3.down,2f,Physics.AllLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider is BoxCollider box && DungeonFloorOpening.IsFloor(box.transform) &&
                Vector3.Dot(hit.normal,Vector3.up)>.98f)
            { floor=box;point=hit.point;return true; }
        }
        return false;
    }
    public static bool IsUnsafe(Vector3 point,float clearance=0f)
    {
        foreach(DungeonHazard hazard in DungeonHazard.All)
        {
            if (hazard == null) continue;
            Vector3 local=hazard.transform.InverseTransformPoint(point);
            float half=hazard.UnsafeHalfSize+Mathf.Max(0,clearance);
            if(Mathf.Abs(local.x)<half && Mathf.Abs(local.z)<half && local.y>-4.5f && local.y<3f) return true;
        }
        return false;
    }
    void OnDestroy()
    {
        foreach(GameObject hazard in placed) if(hazard!=null) Destroy(hazard);
        if(openings!=null) Destroy(openings);
    }
}
