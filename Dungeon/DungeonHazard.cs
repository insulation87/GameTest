using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum DungeonHazardKind { Spikes, CollapsingFloor }
public enum DungeonHazardState { Ready, Warning, Active, Recovering }

public sealed class DungeonHazard : MonoBehaviour
{
    public DungeonHazardKind kind;
    [Min(.2f)] public float warningSeconds = .9f;
    [Min(1f)] public float resetSeconds = 7f;
    [Min(0f)] public float damage = 35f;
    public DungeonHazardState State { get; private set; }
    public const float Width = 2.8f;
    readonly Collider[] overlaps = new Collider[32];
    readonly HashSet<PlayerHealth> hitThisCycle = new HashSet<PlayerHealth>();
    readonly List<Material> materials = new List<Material>();
    readonly List<Mesh> meshes = new List<Mesh>();
    Material warningMaterial;
    Transform movingPart;
    BoxCollider support;
    float elapsed;
    Color safeColor;

    public void Initialize(DungeonHazardKind type)
    {
        kind = type;
        Material metal = Material(new Color(.17f,.19f,.22f));
        Material stone = Material(new Color(.31f,.27f,.22f));
        safeColor = type == DungeonHazardKind.Spikes ? new Color(.36f,.19f,.035f) : new Color(.5f,.3f,.07f);
        warningMaterial = Material(safeColor);
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side < 2;
            Vector3 p = alongX ? new Vector3(0,.045f,(side==0?-1:1)*1.47f) : new Vector3((side==2?-1:1)*1.47f,.045f,0);
            Box("機關邊框",p,alongX ? new Vector3(3.1f,.09f,.14f) : new Vector3(.14f,.09f,3.1f),metal,false);
            Box("警示刻紋",p+Vector3.up*.051f,alongX ? new Vector3(2.65f,.012f,.045f) : new Vector3(.045f,.012f,2.65f),warningMaterial,false);
        }
        if (type == DungeonHazardKind.Spikes)
        {
            Box("地刺底座",new Vector3(0,.016f,0),new Vector3(Width,.03f,Width),metal,false);
            movingPart = new GameObject("伸縮地刺").transform;
            movingPart.SetParent(transform,false);
            Mesh cone = SpikeMesh(); meshes.Add(cone);
            for (int x=-1;x<=1;x++) for (int z=-1;z<=1;z++)
            {
                var spike = new GameObject("地刺",typeof(MeshFilter),typeof(MeshRenderer));
                spike.transform.SetParent(movingPart,false);
                spike.transform.localPosition = new Vector3(x*.8f,0,z*.8f);
                spike.GetComponent<MeshFilter>().sharedMesh = cone;
                spike.GetComponent<MeshRenderer>().sharedMaterial = stone;
            }
            movingPart.localScale = new Vector3(1,.02f,1);
        }
        else
        {
            movingPart = Box("脆弱石板",new Vector3(0,-.12f,0),new Vector3(Width,.24f,Width),stone,true).transform;
            support = movingPart.GetComponent<BoxCollider>();
            // Jagged amber seams are visible before activation; warning pulses strengthen them.
            for (int i=0;i<4;i++)
            {
                var seam = Box("裂紋",new Vector3((i%2==0?-.22f:.22f),.008f,(i-1.5f)*.65f),new Vector3(.75f,.012f,.035f),warningMaterial,false);
                seam.transform.localRotation=Quaternion.Euler(0,i%2==0?32:-32,0);
                seam.transform.SetParent(movingPart,true);
            }
            Material darkness = Material(new Color(.025f,.018f,.012f));
            Box("坑底",new Vector3(0,-4.05f,0),new Vector3(Width,.1f,Width),darkness,false);
            for(int side=0;side<4;side++)
                Box("坑壁",new Vector3(side<2?(side==0?-1.45f:1.45f):0,-2f,side>=2?(side==2?-1.45f:1.45f):0),
                    side<2?new Vector3(.1f,4f,Width):new Vector3(Width,4f,.1f),stone,false);
        }
        // Permanent navigation exclusion keeps NPCs off the moving mechanism, including its warning period.
        var obstacle=gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape=NavMeshObstacleShape.Box; obstacle.center=new Vector3(0,.25f,0);
        obstacle.size=new Vector3(Width+.5f,1f,Width+.5f); obstacle.carving=true; obstacle.carveOnlyStationary=false;
    }

    void Update()
    {
        if (movingPart == null || Time.timeScale <= 0f) return;
        elapsed += Time.deltaTime;
        if (State == DungeonHazardState.Ready && HasPlayer(.13f,.2f))
        {
            State=DungeonHazardState.Warning; elapsed=0;
            GameAudio.At(kind==DungeonHazardKind.Spikes?GameSound.TrapWarning:GameSound.FloorCrack,transform.position);
        }
        if (State == DungeonHazardState.Warning)
        {
            warningMaterial.color=Color.Lerp(safeColor,new Color(2f,.3f,.025f),.5f+.5f*Mathf.Sin(elapsed*22));
            if (elapsed>=warningSeconds)
            {
                State=DungeonHazardState.Active; elapsed=0; hitThisCycle.Clear();
                if (support!=null) support.enabled=false;
                GameAudio.At(kind==DungeonHazardKind.Spikes?GameSound.SpikeRise:GameSound.FloorCollapse,transform.position);
            }
        }
        if (State == DungeonHazardState.Active)
        {
            if (kind==DungeonHazardKind.Spikes)
            {
                movingPart.localScale=new Vector3(1,Mathf.Lerp(.02f,1f,Mathf.Clamp01(elapsed/.12f)),1);
                DamagePlayers(false);
                if (elapsed>=1.1f) { State=DungeonHazardState.Recovering; elapsed=0; }
            }
            else
            {
                movingPart.localPosition=new Vector3(0,-.12f-Mathf.Min(3.75f,elapsed*elapsed*6),0);
                movingPart.localRotation=Quaternion.Euler(Mathf.Min(40,elapsed*45),0,Mathf.Min(16,elapsed*20));
                DamagePlayers(true);
                if (elapsed>=resetSeconds && !HasPlayer(-1.5f,5f)) Restore();
            }
        }
        if (State == DungeonHazardState.Recovering)
        {
            movingPart.localScale=new Vector3(1,Mathf.Lerp(1,.02f,Mathf.Clamp01(elapsed/.25f)),1);
            if (elapsed>=2.2f) Restore();
        }
    }

    int Query(float centerY,float height)
    {
        return Physics.OverlapBoxNonAlloc(transform.TransformPoint(new Vector3(0,centerY,0)),
            new Vector3(Width*.5f,height*.5f,Width*.5f),overlaps,transform.rotation,Physics.AllLayers,QueryTriggerInteraction.Ignore);
    }
    bool HasPlayer(float centerY,float height)
    {
        int count=Query(centerY,height);
        for(int i=0;i<count;i++) if(overlaps[i].GetComponentInParent<PlayerHealth>()!=null) return true;
        return false;
    }
    void DamagePlayers(bool pit)
    {
        int count=Query(pit?-2.4f:.45f,pit?2.5f:.9f);
        for(int i=0;i<count;i++)
        {
            PlayerHealth health=overlaps[i].GetComponentInParent<PlayerHealth>();
            if(health==null || health.IsDead || hitThisCycle.Contains(health)) continue;
            // Do not consume this cycle's hit while respawn immunity is active.
            if (!pit && health.IsInvincible) continue;
            hitThisCycle.Add(health);
            if(pit) health.KillInstantly(); else health.TakeDamage(damage);
        }
    }
    void Restore()
    {
        State=DungeonHazardState.Ready;elapsed=0;hitThisCycle.Clear();warningMaterial.color=safeColor;
        movingPart.localRotation=Quaternion.identity;
        if(kind==DungeonHazardKind.CollapsingFloor)
        { movingPart.localPosition=new Vector3(0,-.12f,0);support.enabled=true; }
        else movingPart.localScale=new Vector3(1,.02f,1);
    }
    Material Material(Color color)
    {
        var material=new Material(Resources.Load<Shader>("DungeonHazardSurface"));
        material.color=color; materials.Add(material); return material;
    }
    GameObject Box(string label,Vector3 position,Vector3 size,Material material,bool collision)
    {
        var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name=label;
        box.transform.SetParent(transform,false);box.transform.localPosition=position;box.transform.localScale=size;
        box.GetComponent<Renderer>().sharedMaterial=material;
        if(!collision) { box.GetComponent<Collider>().enabled=false;Destroy(box.GetComponent<Collider>()); }
        return box;
    }
    static Mesh SpikeMesh()
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;int index=vertices.Count;
            vertices.Add(new Vector3(Mathf.Cos(a)*.16f,0,Mathf.Sin(a)*.16f));
            vertices.Add(Vector3.up*.85f);
            vertices.Add(new Vector3(Mathf.Cos(b)*.16f,0,Mathf.Sin(b)*.16f));
            triangles.Add(index);triangles.Add(index+1);triangles.Add(index+2);
        }
        var mesh=new Mesh { name="地刺網格" };mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();return mesh;
    }
    void OnDestroy()
    {
        foreach(Material material in materials) if(material!=null) Destroy(material);
        foreach(Mesh mesh in meshes) if(mesh!=null) Destroy(mesh);
    }
}
