using System;
using System.Collections.Generic;
using MedievalDungeonKit;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-200)]
public sealed class DungeonHazardDirector : MonoBehaviour
{
    [Min(1)] public int roomInterval = 3;
    public int SpikeCount { get; private set; }
    public int CollapsingFloorCount { get; private set; }
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
        Built=true;
        Player player=FindFirstObjectByType<Player>();
        if(player==null) return;
        var markers=new List<DungeonMarker>(GetComponentsInChildren<DungeonMarker>());
        var rooms=markers.FindAll(m=>string.Equals(m.kind,"Room",StringComparison.OrdinalIgnoreCase));
        rooms.Sort((a,b)=>string.CompareOrdinal(a.name,b.name));
        openings=gameObject.AddComponent<DungeonFloorOpening>();
        for(int i=0;i<rooms.Count;i++)
        {
            // The initial hall stays safe; alternate mechanism types across three floors.
            if(i==0 || i%Mathf.Max(1,roomInterval)!=1) continue;
            DungeonMarker room=rooms[i];
            DungeonRoomBoundary boundary=DungeonRoomBoundary.GetOrCreate(room);
            if(boundary==null || IsBossRoom(boundary,markers)) continue;
            DungeonHazardKind kind=(i/Mathf.Max(1,roomInterval))%2==0 ? DungeonHazardKind.Spikes : DungeonHazardKind.CollapsingFloor;
            float offset=Mathf.Min(boundary.halfSize.x,boundary.halfSize.y)*.5f;
            if(offset<5.5f) continue;
            foreach(Vector2 quadrant in new[] { new Vector2(-1,1),new Vector2(1,-1),new Vector2(-1,-1),new Vector2(1,1) })
            {
                Vector3 candidate=room.transform.TransformPoint(new Vector3(quadrant.x*offset,0,quadrant.y*offset));
                if(!boundary.Contains(candidate,2f) || IsProtected(candidate,player,markers)) continue;
                if(!FindFloor(candidate,out BoxCollider floor,out Vector3 surface)) continue;
                if(Physics.OverlapBox(surface+Vector3.up*1.05f,new Vector3(1.65f,1f,1.65f),floor.transform.rotation,
                    Physics.AllLayers,QueryTriggerInteraction.Ignore).Length>0) continue;
                if(kind==DungeonHazardKind.CollapsingFloor && !openings.Cut(floor,surface,DungeonHazard.Width,transform)) continue;
                var node=new GameObject(kind==DungeonHazardKind.Spikes?"警示地刺":"崩落地板");
                // Keep dimensions in world metres even when an imported map root is scaled.
                node.transform.SetPositionAndRotation(surface,floor.transform.rotation);
                node.transform.SetParent(transform,true);
                var hazard=node.AddComponent<DungeonHazard>();hazard.Initialize(kind);placed.Add(node);
                if(kind==DungeonHazardKind.Spikes) SpikeCount++; else CollapsingFloorCount++;
                break;
            }
        }
        Physics.SyncTransforms();
        Debug.Log($"已配置 {SpikeCount} 組地刺與 {CollapsingFloorCount} 塊崩落地板。",this);
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
        foreach(DungeonHazard hazard in FindObjectsByType<DungeonHazard>(FindObjectsSortMode.None))
        {
            Vector3 local=hazard.transform.InverseTransformPoint(point);
            float half=DungeonHazard.Width*.5f+Mathf.Max(0,clearance);
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
