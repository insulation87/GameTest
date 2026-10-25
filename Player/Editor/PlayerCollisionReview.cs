using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit isolated-project entry point. Creates fixture scenes and never touches real saves.
[InitializeOnLoad]
public static class PlayerCollisionReview
{
    const string Pending="PlayerCollisionReview.Pending";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static IEnumerator checks;
    static double next;
    static int assertions;
    static float oldStep;
    static SimulationMode oldMode;
    static readonly List<string> errors=new List<string>();
    static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
    static PlayerCollisionReview(){EditorApplication.playModeStateChanged+=Changed;}
    public static void Run()
    {
        LoginSceneSetup.PlayFromCurrentScene();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);errors.Clear();assertions=0;
        oldStep=Time.fixedDeltaTime;oldMode=Physics.simulationMode;
        checks=Checks();next=EditorApplication.timeSinceStartup+.1;
        Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
    }
    static void Log(string message,string stack,LogType type)
    {if((type==LogType.Error||type==LogType.Exception)&&!message.Contains("burst JIT compiler")&&!stack.Contains("UnityEditor.Search"))errors.Add(message);}
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;
        try
        {
            if(!checks.MoveNext()){Finish(0,"PASS "+assertions+" player collision checks");return;}
            next=EditorApplication.timeSinceStartup+(checks.Current is float delay?delay:.1f);
        }
        catch(Exception ex){Debug.LogException(ex);Finish(1,"FAIL "+ex);}
    }
    static void Finish(int code,string result)
    {
        File.WriteAllText(Path.Combine(Output,"player-collision-result.txt"),result);
        Physics.simulationMode=oldMode;Time.fixedDeltaTime=oldStep;Time.timeScale=1;
        Application.logMessageReceived-=Log;EditorApplication.update-=Tick;EditorApplication.Exit(code);
    }
    static void Check(bool result,string label)
    {if(!result)throw new InvalidOperationException(label);assertions++;Debug.Log("PASS "+label);}
    static void Set(Player player,string field,object value)=>typeof(Player).GetField(field,Private).SetValue(player,value);
    static void Invoke(Player player,string method)=>typeof(Player).GetMethod(method,Private).Invoke(player,null);
    static void Pose(Player player,Vector3 point)
    {
        var body=player.GetComponent<Rigidbody>();body.position=point;body.rotation=Quaternion.identity;
        player.transform.SetPositionAndRotation(point,Quaternion.identity);
        body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
        Set(player,"isSliding",false);Set(player,"isCrouching",false);Invoke(player,"RestoreStandingCollider");Physics.SyncTransforms();
    }
    static void Move(Player player,Vector3 direction,float speed,int steps,float step=.02f)
    {
        Time.fixedDeltaTime=step;Set(player,"moveDirection",direction.normalized);Set(player,"currentSpeed",speed);
        for(int i=0;i<steps;i++){Invoke(player,"HandleMove");Physics.Simulate(step);Physics.SyncTransforms();}
    }
    static BoxCollider Box(string name,Vector3 point,Vector3 size)
    {
        var node=GameObject.CreatePrimitive(PrimitiveType.Cube);node.name=name;node.transform.position=point;node.transform.localScale=size;
        return node.GetComponent<BoxCollider>();
    }
    static IEnumerator Checks()
    {
        Object.FindFirstObjectByType<PixelArtDirector>().enabled=false;
        Vector3 home=new Vector3(1000,1.02f,1000);
        Box("Review Ground",new Vector3(1000,-.5f,1000),new Vector3(100,1,100));
        var node=new GameObject("Collision Review Player");node.SetActive(false);node.transform.position=home;
        var save=node.AddComponent<GameProgressSave>();save.enabled=false;save.StoragePath=Path.Combine(Output,"never-write-collision-save.json");
        var player=node.AddComponent<Player>();var health=node.AddComponent<PlayerHealth>();health.enabled=false;health.pauseWorldWhileDead=false;
        var collider=node.GetComponent<BoxCollider>();collider.size=new Vector3(1,2,1);collider.center=Vector3.up*.014f;
        node.SetActive(true);yield return .2f;
        player.enabled=false;node.GetComponent<CharacterSkills>().enabled=false;node.GetComponent<PlayerCursorController>().enabled=false;
        node.GetComponent<CharacterMovementAudio>().enabled=false;node.GetComponent<BossTestShortcut>().enabled=false;
        Physics.simulationMode=SimulationMode.Script;var body=node.GetComponent<Rigidbody>();body.interpolation=RigidbodyInterpolation.None;
        Pose(player,home);Move(player,Vector3.right,5,50);
        Check(Mathf.Abs(body.position.x-home.x-5)<.2f,"Walking preserves its intended speed on open ground: position="+body.position+" velocity="+body.linearVelocity);
        var wall=Box("Thin Wall",new Vector3(1003,3,1000),new Vector3(.04f,6,50));Physics.SyncTransforms();
        foreach(float speed in new[]{5f,10f,24f})
        {
            Pose(player,home);Move(player,Vector3.right,speed,100);
            Check(collider.bounds.max.x<=wall.bounds.min.x+.025f,"Walking/running/stacked speed buffs cannot cross a thin wall at speed "+speed);
        }
        Pose(player,home);Move(player,Vector3.right,45,30,.1f);
        Check(collider.bounds.max.x<=wall.bounds.min.x+.025f,"A 100 ms physics step and boosted speed cannot tunnel through a thin wall");
        Pose(player,home);Time.fixedDeltaTime=.02f;Set(player,"isSliding",true);Set(player,"isGrounded",true);
        Set(player,"slideDirection",Vector3.right);Set(player,"slideTimeRemaining",.7f);player.slideSpeed=50;player.slideEndSpeed=40;
        for(int i=0;i<30;i++){Invoke(player,"HandleMove");Physics.Simulate(.02f);Physics.SyncTransforms();}
        Check(collider.bounds.max.x<=wall.bounds.min.x+.025f,"A fast slide stays on the near side of the wall");
        Pose(player,home);Move(player,new Vector3(1,0,1),10,80);
        Check(collider.bounds.max.x<=wall.bounds.min.x+.025f && body.position.z>home.z+7,"Diagonal input glides along a wall without sticking or crossing it: "+body.position+" max="+collider.bounds.max+" wall="+wall.bounds.min);
        var corner=Box("Corner Wall",new Vector3(1000,3,1003),new Vector3(50,6,.04f));Physics.SyncTransforms();
        Pose(player,home);Move(player,new Vector3(1,0,1),24,100);
        Check(collider.bounds.max.x<=wall.bounds.min.x+.025f && collider.bounds.max.z<=corner.bounds.min.z+.025f,"Repeated input into a two-wall corner cannot escape the room");
        Object.Destroy(corner.gameObject);yield return .1f;
        Pose(player,home);Time.fixedDeltaTime=.02f;Set(player,"jumpRequested",true);Set(player,"isGrounded",true);Invoke(player,"HandleJump");
        Move(player,Vector3.right,24,12);
        Check(body.position.y>home.y+.6f && collider.bounds.max.x<=wall.bounds.min.x+.025f,"Jumping keeps vertical motion while the wall blocks horizontal motion");
        Move(player,Vector3.zero,0,120);
        Check(Mathf.Abs(body.position.y-home.y)<.1f,"The dynamic body lands on the floor after jumping");
        Pose(player,home);Invoke(player,"ApplyCrouchedCollider");Move(player,Vector3.right,12,70);
        Check(collider.bounds.max.x<=wall.bounds.min.x+.025f,"The shortened crouch collider is also blocked by walls");
        Pose(player,home);Move(player,Vector3.right,10,3);Invoke(player,"OnDisable");Physics.Simulate(.02f);
        Check(Mathf.Abs(body.linearVelocity.x)<.01f && Mathf.Abs(body.linearVelocity.z)<.01f,"Disabling movement for a menu or death stops horizontal drift");
        Pose(player,home);wall.isTrigger=true;Move(player,Vector3.right,10,35);
        Check(body.position.x>1005,"Trigger volumes remain passable");wall.isTrigger=false;
        Pose(player,home);Physics.IgnoreCollision(collider,wall,true);Move(player,Vector3.right,10,35);
        Check(body.position.x>1005,"Explicitly ignored collision pairs remain passable");Physics.IgnoreCollision(collider,wall,false);
        Pose(player,home);collider.size=new Vector3(.6f,2,1.6f);body.position=new Vector3(1002.62f,home.y,1000);body.useGravity=false;Physics.SyncTransforms();
        var rotation=Quaternion.Euler(0,90,0);var rotate=typeof(Player).GetMethod("RotateWithCollision");
        if(rotate!=null)rotate.Invoke(player,new object[]{rotation,true});else body.rotation=rotation;
        Physics.SyncTransforms();
        Check(!Physics.ComputePenetration(collider,body.position,body.rotation,wall,wall.transform.position,wall.transform.rotation,out _,out float depth)||depth<.005f,"Turning a rectangular character beside a wall cannot rotate its body into the wall");
        collider.size=new Vector3(1,2,1);body.useGravity=true;Pose(player,home);
        var ceiling=Box("Low Ceiling",new Vector3(1000,1.6f,1000),new Vector3(4,.2f,4));Invoke(player,"ApplyCrouchedCollider");Physics.SyncTransforms();
        Check(!(bool)typeof(Player).GetMethod("CanStandUp",Private).Invoke(player,null),"A crouching character cannot stand through a low ceiling");
        Object.Destroy(ceiling.gameObject);Object.Destroy(wall.gameObject);yield return .1f;
        Pose(player,home);var ramp=Box("Walkable Ramp",new Vector3(1005,.8f,1000),new Vector3(8,.25f,4));ramp.transform.rotation=Quaternion.Euler(0,0,15);Physics.SyncTransforms();
        Move(player,Vector3.right,5,70);
        Check(body.position.x>1005 && body.position.y>home.y+.7f,"Walkable slopes still lift the dynamic character instead of being treated as walls");
        Object.Destroy(ramp.gameObject);yield return .1f;
        var columnRoot=new GameObject("Review Columns");var details=new GameObject("Details").transform;details.SetParent(columnRoot.transform,false);
        var shaftNode=new GameObject("Collider_ReviewShaft",typeof(BoxCollider));shaftNode.transform.SetParent(details,false);shaftNode.transform.position=new Vector3(1004,3.5f,1000);
        var shaft=shaftNode.GetComponent<BoxCollider>();shaft.size=new Vector3(.84f,6.1f,.84f);
        var baseNode=new GameObject("Collider_ReviewBase",typeof(BoxCollider));baseNode.transform.SetParent(details,false);baseNode.transform.position=new Vector3(1004,.225f,1000);
        var plinth=baseNode.GetComponent<BoxCollider>();plinth.size=new Vector3(1.3f,.45f,1.3f);
        var profiles=columnRoot.AddComponent<DungeonPillarCollisions>();profiles.Build();
        Check(profiles.Pillars.Count==1&&!shaft.enabled&&!plinth.enabled,"A paired column gets one smooth collision profile without the foot-catching plinth");
        Vector3 columnPoint=new Vector3(1004,home.y,1000);
        bool glides=true;
        foreach(Vector3 axis in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
        {
            Vector3 tangent=Vector3.Cross(Vector3.up,axis);
            Pose(player,columnPoint-axis*2.8f+tangent*.85f);Move(player,axis,6,50);
            glides &= Vector3.Dot(body.position-columnPoint,axis)>1.8f;
            glides &= !Physics.ComputePenetration(collider,body.position,body.rotation,profiles.Pillars[0],profiles.Pillars[0].transform.position,profiles.Pillars[0].transform.rotation,out _,out float overlap)||overlap<.015f;
        }
        Check(glides,"Glancing approaches glide around a rounded column from all four directions");
        Pose(player,columnPoint-Vector3.right*2.8f);Move(player,Vector3.right,45,20,.1f);
        Check(body.position.x<columnPoint.x-.85f,"The column's solid core still blocks direct high-speed movement");
        Vector3 contact=body.position;Move(player,Vector3.left,6,20);
        Check(body.position.x<contact.x-1.5f,"A character touching a column can back away immediately");
        profiles.Build();Check(profiles.Pillars.Count==1,"Repeated column setup does not duplicate collision profiles");
        Object.Destroy(profiles);yield return .1f;
        Check(shaft.enabled&&plinth.enabled&&columnRoot.GetComponentsInChildren<CapsuleCollider>().Length==0,"Removing column setup restores the original map colliders");
        Object.Destroy(columnRoot);yield return .1f;
        Pose(player,home);body.useGravity=false;
        wall=Box("Camera Wall",new Vector3(1000,3,1001),new Vector3(20,6,.15f));Physics.SyncTransforms();
        var fpsNode=new GameObject("Review FPS");fpsNode.SetActive(false);var fps=fpsNode.AddComponent<Camera>();fps.nearClipPlane=.1f;
        var fpsLook=fpsNode.AddComponent<FPSCameraLook>();fpsLook.player=node.transform;fpsLook.playerRb=body;fpsLook.cameraOffset=Vector3.up*.5f;fpsLook.forwardOffset=2;
        fpsNode.SetActive(true);fpsLook.enabled=false;fpsLook.SetLook(0,0);
        Check(fps.transform.position.z<wall.bounds.min.z-.1f,"The first-person camera stays on the player's side of a wall");
        fpsLook.SetLook(0,90);
        Check(Mathf.Abs(fps.transform.position.x-home.x-2)<.01f,"First-person camera offsets are retained in open space");
        Object.Destroy(wall.gameObject);yield return .1f;
        wall=Box("Third Person Camera Wall",new Vector3(1000,3,998),new Vector3(20,6,.15f));Physics.SyncTransforms();
        var tpsNode=new GameObject("Review TPS");tpsNode.SetActive(false);var tps=tpsNode.AddComponent<Camera>();tps.nearClipPlane=.1f;
        var follow=tpsNode.AddComponent<CameraFollowTPS>();follow.target=node.transform;follow.height=3;follow.distance=7;
        tpsNode.SetActive(true);follow.enabled=false;follow.SetLook(25,0);
        Check(tps.transform.position.z>wall.bounds.max.z+.1f,"Third-person camera snapping cannot go behind a wall");
        tps.transform.position=new Vector3(1000,4,990);typeof(CameraFollowTPS).GetMethod("LateUpdate",Private).Invoke(follow,null);
        Check(tps.transform.position.z>wall.bounds.max.z,"Third-person smoothing also clamps positions that started beyond a wall");
        Object.Destroy(wall.gameObject);Object.Destroy(tpsNode);Object.Destroy(fpsNode);yield return .1f;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HazardReviewMap.prefab");
        Check(prefab!=null,"The existing isolated copy of the actual dungeon is available");
        var map=Object.Instantiate(prefab);
        foreach(var script in map.GetComponentsInChildren<MonoBehaviour>())script.enabled=false;
        var mapColumns=map.AddComponent<DungeonPillarCollisions>();mapColumns.Build();
        Check(mapColumns.Pillars.Count==192,"All 192 paired columns in the actual three-floor dungeon receive smooth collision");
        Physics.SyncTransforms();
        var actualWalls=map.GetComponentsInChildren<BoxCollider>().Where(b=>b.enabled&&!b.isTrigger&&b.bounds.size.y>3&&(b.bounds.size.x>3||b.bounds.size.z>3)).Take(24).ToArray();
        Check(actualWalls.Length==24,"The actual generated dungeon contains the sampled solid wall colliders");
        bool blocked=true;
        foreach(var actual in actualWalls)
        {
            Vector3 axis=actual.bounds.size.x<actual.bounds.size.z?Vector3.right:Vector3.forward;
            float thickness=actual.bounds.size.x<actual.bounds.size.z?actual.bounds.extents.x:actual.bounds.extents.z;
            foreach(int side in new[]{-1,1})
            {
                Pose(player,actual.bounds.center+axis*(thickness+.8f)*side);
                Move(player,-axis*side,45,12,.1f);
                float coordinate=Vector3.Dot(body.position-actual.bounds.center,axis)*side;
                blocked &= coordinate>=thickness+.45f;
            }
        }
        Check(blocked,"Boosted movement stays outside 24 actual dungeon walls from both sides (48 approaches)");
        bool canLeave=true,solid=true;
        foreach(var column in mapColumns.Pillars.Where((c,i)=>i%24==0))
        {
            Vector3 center=column.bounds.center;
            var room=DungeonRoomBoundary.FindContaining(center);
            // Layout boundaries are created on demand because map scripts are disabled in this review.
            if(room==null)
            {
                foreach(var marker in map.GetComponentsInChildren<MedievalDungeonKit.DungeonMarker>())DungeonRoomBoundary.GetOrCreate(marker);
                room=DungeonRoomBoundary.FindContaining(center);
            }
            if(room==null)throw new InvalidOperationException("Column has no containing room");
            Vector3 inward=room.transform.position-center;inward.y=0;inward.Normalize();
            Pose(player,center+inward*2.8f);Move(player,-inward,15,25);
            solid &= !Physics.ComputePenetration(collider,body.position,body.rotation,column,column.transform.position,column.transform.rotation,out _,out float overlap)||overlap<.025f;
            Vector3 from=body.position;Move(player,inward,6,20);
            canLeave &= Vector3.Dot(body.position-from,inward)>1.5f;
        }
        Check(solid&&canLeave,"Sampled actual columns remain solid and players can move away after contact");
        Check(errors.Count==0,"No unexpected Unity gameplay errors: "+string.Join(" | ",errors));
    }
}
