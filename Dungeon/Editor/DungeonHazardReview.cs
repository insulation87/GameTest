using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MedievalDungeonKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Isolated-project entry point. Copies/remaps the actual map prefab and creates a review scene.
[InitializeOnLoad]
public static class DungeonHazardReview
{
    const string Pending = "DungeonHazardReview.Pending";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static string output;
    static readonly List<string> errors = new List<string>();
    [Serializable] class ScriptIds { public string marker, view, walker; }
    static DungeonHazardReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        output = Path.GetFullPath(Path.Combine(Application.dataPath,"../../"));
        string source = "Assets/MedievalDungeon/Generated_20260914_214258/MedievalDungeon.prefab";
        var ids = JsonUtility.FromJson<ScriptIds>(File.ReadAllText(Path.Combine(output,"hazard-map-scripts.json")));
        string prefab = File.ReadAllText(source);
        Remap(ref prefab, typeof(DungeonMarker), ids.marker); Remap(ref prefab, typeof(DungeonViewSettings), ids.view); Remap(ref prefab, typeof(DungeonTestWalker), ids.walker);
        const string copy = "Assets/HazardReviewMap.prefab"; File.WriteAllText(copy,prefab);
        AssetDatabase.ImportAsset(copy,ImportAssetOptions.ForceSynchronousImport);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(copy),scene);
        EditorSceneManager.SaveScene(scene,"Assets/HazardReviewMap.unity");
        LoginSceneSetup.PlayFromCurrentScene(); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Remap(ref string prefab, Type type, string oldGuid)
    {
        var node = new GameObject("Script Remap"); var script = MonoScript.FromMonoBehaviour((MonoBehaviour)node.AddComponent(type));
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script,out string guid,out long id);
        prefab = prefab.Replace("{fileID: 11500000, guid: " + oldGuid + ", type: 3}","{fileID: " + id + ", guid: " + guid + ", type: 3}")
            .Replace("Assembly-CSharp::"+type.FullName,type.Assembly.GetName().Name+"::"+type.FullName);
        Object.DestroyImmediate(node);
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false)) return;
        SessionState.SetBool(Pending,false); assertions=0; errors.Clear(); output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../"));
        checks=Checks();deadline=EditorApplication.timeSinceStartup+.2;Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
    }
    static void Log(string message,string stack,LogType type)
    { if ((type==LogType.Error||type==LogType.Exception) && !message.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(message); }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<deadline)return;
        try
        {
            if(!checks.MoveNext()){Finish(0,"PASS "+assertions+" hazard integration checks");return;}
            deadline=EditorApplication.timeSinceStartup+(checks.Current is float delay?delay:.1f);
        }
        catch(Exception ex){Debug.LogException(ex);Finish(1,"FAIL "+ex);}
    }
    static void Finish(int code,string result)
    { File.WriteAllText(Path.Combine(output,"hazards-result.txt"),result);Time.timeScale=1;Application.logMessageReceived-=Log;EditorApplication.update-=Tick;EditorApplication.Exit(code); }
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);assertions++;Debug.Log("PASS "+message);}
    static IEnumerator Wait(Func<bool> condition)
    { double until=EditorApplication.timeSinceStartup+12;while(!condition()){if(EditorApplication.timeSinceStartup>until)throw new TimeoutException("Hazard state review");yield return .05f;} }
    static PlayerHealth PlayerAt(Vector3 point)
    {
        var node=new GameObject("Hazard Review Player"); node.SetActive(false);node.transform.position=point;
        var save=node.AddComponent<GameProgressSave>();save.enabled=false;save.StoragePath=Path.Combine(output,"never-write-hazard-save.json");
        var health=node.AddComponent<PlayerHealth>();health.maxHealth=1000;health.startingMaxArmor=0;health.startingArmorRegeneration=0;
        health.pauseWorldWhileDead=false;health.respawnOnDeath=false;
        node.AddComponent<Player>().enabled=false;node.SetActive(true);node.GetComponent<Rigidbody>().isKinematic=true;
        node.GetComponent<BoxCollider>().size=new Vector3(.6f,1.8f,.6f);
        node.GetComponent<CharacterSkills>().enabled=false;node.GetComponent<PlayerCursorController>().enabled=false;
        node.GetComponent<CharacterMovementAudio>().enabled=false;node.GetComponent<BossTestShortcut>().enabled=false;
        Physics.SyncTransforms();return health;
    }
    static EnemyHealth EnemyAt(Vector3 point)
    {
        var node=GameObject.CreatePrimitive(PrimitiveType.Capsule);node.name="Trap Bait Enemy";node.transform.position=point;
        var enemy=node.AddComponent<EnemyHealth>();enemy.SetMaxHealth(1000,true);enemy.showDamageNumbers=false;Physics.SyncTransforms();return enemy;
    }
    static DungeonHazard Trap(DungeonHazardKind kind,Vector3 point)
    { var node=new GameObject("Review "+kind);node.transform.position=point;var hazard=node.AddComponent<DungeonHazard>();hazard.Initialize(kind);hazard.warningSeconds=.35f;hazard.resetSeconds=.4f;return hazard; }
    static IEnumerator Checks()
    {
        Object.FindFirstObjectByType<PixelArtDirector>().enabled=false;
        var map=Object.FindFirstObjectByType<DungeonViewSettings>();
        Check(map!=null && map.GetComponentsInChildren<DungeonMarker>().Length>48,"Actual map marker/view scripts remap correctly into the isolated runtime");
        foreach(var walker in Object.FindObjectsByType<DungeonTestWalker>(FindObjectsSortMode.None))walker.enabled=false;
        var start=map.GetComponentsInChildren<DungeonMarker>().First(marker=>marker.name.StartsWith("L1_R01") && marker.kind=="Room");
        var player=PlayerAt(start.transform.position+Vector3.up*.95f);
        var director=map.gameObject.AddComponent<DungeonHazardDirector>();director.Build();yield return .1f;
        Check(director.TotalCount>=50,"Actual dungeon now has substantially more than the original 15 traps: "+director.TotalCount);
        Check(director.SpikeCount>0 && director.CollapsingFloorCount>0 && director.FlameVentCount>0 && director.SweepingBladeCount>0 && director.BlastRuneCount>0,"All five mechanisms appear in the actual dungeon");
        var hazards=map.GetComponentsInChildren<DungeonHazard>();
        Check(hazards.All(h=>!DungeonRoomBoundary.GetOrCreate(start).Contains(h.transform.position)),"The starting room stays free of traps");
        var markers=map.GetComponentsInChildren<DungeonMarker>();
        Check(markers.Where(m=>m.kind=="Boss").All(b=>!DungeonHazardDirector.IsUnsafe(b.transform.position,4)),"Boss spawn areas stay safe");
        bool routes=true,spacing=true;
        foreach(var hazard in hazards)
        {
            var room=DungeonRoomBoundary.FindContaining(hazard.transform.position);var local=room.transform.InverseTransformPoint(hazard.transform.position);
            routes &= Mathf.Abs(local.x)*Mathf.Abs(room.transform.lossyScale.x)>hazard.UnsafeHalfSize+2 && Mathf.Abs(local.z)*Mathf.Abs(room.transform.lossyScale.z)>hazard.UnsafeHalfSize+2;
            spacing &= Mathf.Abs(hazard.transform.lossyScale.x-1)<.001f && Mathf.Abs(hazard.transform.lossyScale.z-1)<.001f;
        }
        Check(routes && spacing,"Traps retain world dimensions and leave clear central routes");
        var third=hazards.Count(h=>DungeonRoomBoundary.FindContaining(h.transform.position).name.StartsWith("L3_"));
        var first=hazards.Count(h=>DungeonRoomBoundary.FindContaining(h.transform.position).name.StartsWith("L1_"));
        Check(third>first,"Deeper floors contain denser mechanism combinations");
        Check(hazards.All(h=>DungeonHazardDirector.IsUnsafe(h.transform.position,1)),"Spawn and teleport checks exclude every trap footprint");
        var flame=hazards.First(h=>h.kind==DungeonHazardKind.FlameVent);var wait=Wait(()=>flame.State==DungeonHazardState.Active);while(wait.MoveNext())yield return wait.Current;
        var capture=Capture(flame.transform.position,"map-trap-preview.png");while(capture.MoveNext())yield return capture.Current;
        var shader=Resources.Load<Shader>("PixelSurface");
        Check(shader!=null && !ShaderUtil.ShaderHasError(shader),"Floor and shadow opening shader compiles successfully");
        var clipped=map.GetComponentsInChildren<MeshRenderer>().Where(r=>DungeonFloorOpening.IsFloor(r.transform) && !r.GetComponent<MeshFilter>().sharedMesh.isReadable).ToArray();
        var block=new MaterialPropertyBlock();
        Check(clipped.Any(r=>{r.GetPropertyBlock(block);return block.GetInt("_FloorCutoutCount")>0;}),"Actual static batched floors receive runtime visual openings");
        var pit=hazards.First(h=>h.kind==DungeonHazardKind.CollapsingFloor);
        Vector3 pitPosition=pit.transform.position;
        Check(Physics.RaycastAll(pit.transform.position+Vector3.up*.5f,Vector3.down,1f).Any(hit=>hit.collider.GetComponentInParent<DungeonHazard>()==pit),"A ready pit has a solid pressure tile");
        player.transform.position=pit.transform.position+Vector3.up*.95f;Physics.SyncTransforms();
        wait=Wait(()=>pit.State==DungeonHazardState.Warning);while(wait.MoveNext())yield return wait.Current;
        player.transform.position=start.transform.position+Vector3.up*.95f;Physics.SyncTransforms();
        wait=Wait(()=>pit.State==DungeonHazardState.Active);while(wait.MoveNext())yield return wait.Current;yield return .8f;
        Check(!Physics.RaycastAll(pit.transform.position+Vector3.up*.5f,Vector3.down,1.5f).Any(hit=>DungeonFloorOpening.IsFloor(hit.collider.transform)||hit.collider.GetComponentInParent<DungeonHazard>()==pit),"An activated pit opens a real hole in floor collision");
        capture=Capture(pit.transform.position,"map-pit-preview.png");while(capture.MoveNext())yield return capture.Current;
        File.WriteAllLines(Path.Combine(output,"pit-depth-diagnostics.txt"),Physics.RaycastAll(pit.transform.position+Vector3.up*.5f,Vector3.down,5f).Select(hit=>hit.collider.name+" "+hit.point+" "+hit.collider.transform.parent?.name));
        var falling=PlayerAt(pit.transform.position+Vector3.up*.95f);var body=falling.GetComponent<Rigidbody>();body.isKinematic=false;body.useGravity=true;body.constraints=RigidbodyConstraints.FreezeRotation;
        wait=Wait(()=>falling.IsDead);while(wait.MoveNext())yield return wait.Current;
        Check(falling.IsDead,"A real rigidbody falls through the actual map pit and reaches its lethal volume");Object.Destroy(falling.gameObject);yield return .1f;
        // Move fixtures far from the real map so these checks use isolated colliders.
        player.transform.position=new Vector3(1000,.95f,0);Physics.SyncTransforms();
        var jet=Trap(DungeonHazardKind.FlameVent,new Vector3(1000,0,0));var enemy=EnemyAt(new Vector3(1000,1,.3f));
        wait=Wait(()=>jet.State==DungeonHazardState.Warning);while(wait.MoveNext())yield return wait.Current;
        float hp=player.CurrentHealth;Time.timeScale=0;yield return .25f;
        Check(jet.State==DungeonHazardState.Warning && player.CurrentHealth==hp,"Pausing freezes flame warnings and damage");Time.timeScale=1;
        wait=Wait(()=>jet.State==DungeonHazardState.Active);while(wait.MoveNext())yield return wait.Current;yield return .2f;
        Check(player.CurrentHealth==hp-30 && enemy.CurrentHealth==955,"Flame vents damage both players and enemies");yield return .2f;
        Check(player.CurrentHealth==hp-30,"Flames do not deal damage again every physics frame");
        Object.Destroy(jet.gameObject);Object.Destroy(enemy.gameObject);yield return .1f;
        var blade=Trap(DungeonHazardKind.SweepingBlade,new Vector3(1000,0,0));enemy=EnemyAt(new Vector3(1000,1,0));
        wait=Wait(()=>blade.State==DungeonHazardState.Warning);while(wait.MoveNext())yield return wait.Current;
        hp=player.CurrentHealth;player.transform.position=new Vector3(1000,3,0);Physics.SyncTransforms();
        wait=Wait(()=>blade.State==DungeonHazardState.Recovering);while(wait.MoveNext())yield return wait.Current;
        Check(player.CurrentHealth==hp && enemy.CurrentHealth==958,"Jumping above the blade avoids its real sweep while grounded enemies are hit");
        player.transform.position=new Vector3(1000,.95f,0);Physics.SyncTransforms();wait=Wait(()=>player.CurrentHealth<hp);while(wait.MoveNext())yield return wait.Current;
        Check(player.CurrentHealth==hp-28,"Standing in a later blade sweep takes exactly one hit");Object.Destroy(blade.gameObject);Object.Destroy(enemy.gameObject);yield return .1f;
        var rune=Trap(DungeonHazardKind.BlastRune,new Vector3(1000,0,0));enemy=EnemyAt(new Vector3(1000,1,.5f));
        var shielded=EnemyAt(new Vector3(1002,1,0));var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(1001,1,0);wall.transform.localScale=new Vector3(.15f,2,3);
        Physics.SyncTransforms();wait=Wait(()=>rune.State==DungeonHazardState.Warning);while(wait.MoveNext())yield return wait.Current;
        hp=player.CurrentHealth;player.transform.position=new Vector3(995,.95f,0);Physics.SyncTransforms();
        wait=Wait(()=>rune.State==DungeonHazardState.Recovering);while(wait.MoveNext())yield return wait.Current;
        Check(player.CurrentHealth==hp && enemy.CurrentHealth==905,"A player can trigger a rune, escape, and lure an enemy into the blast");
        Check(shielded.CurrentHealth==1000,"Solid walls block rune explosion damage");
        Check(DungeonHazardDirector.IsUnsafe(rune.transform.position+Vector3.right*3),"Safety checks cover the rune's full explosion radius");
        Object.Destroy(wall);Object.Destroy(enemy.gameObject);Object.Destroy(shielded.gameObject);Object.Destroy(rune.gameObject);yield return .1f;
        var peer=PlayerAt(new Vector3(1000,.95f,.3f));player.transform.position=new Vector3(1000,.95f,0);
        var duplicate=new GameObject("Extra Player Collider",typeof(BoxCollider));duplicate.transform.SetParent(player.transform,false);
        rune=Trap(DungeonHazardKind.BlastRune,new Vector3(1000,0,0));Physics.SyncTransforms();hp=player.CurrentHealth;
        wait=Wait(()=>rune.State==DungeonHazardState.Recovering);while(wait.MoveNext())yield return wait.Current;
        Check(player.CurrentHealth==hp-45 && peer.CurrentHealth==955,"A rune damages each teammate once even with multiple colliders");
        var mirror=new GameObject("Hazard World Review").AddComponent<CoopWorldMirror>();var world=mirror.Capture();
        Check(hazards.All(h=>world.Any(s=>s.id==h.gameObject.GetInstanceID() && s.parts.Length>0)),"All new mechanism geometry is included in cooperative world snapshots");
        var mode=typeof(CoopSession).GetField("<Mode>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);
        mode.SetValue(null,CoopMode.Client);
        var clientTrap=Trap(DungeonHazardKind.BlastRune,new Vector3(1000,0,0));hp=player.CurrentHealth;yield return .6f;
        Check(clientTrap.State==DungeonHazardState.Ready && player.CurrentHealth==hp,"Clients do not trigger or simulate duplicate trap damage");
        mode.SetValue(null,CoopMode.SinglePlayer);
        mirror.HideClientSimulation();
        Check(hazards.All(h=>!h.enabled && h.GetComponentsInChildren<Renderer>().All(r=>!r.enabled)),"Client mirroring hides local mechanism simulation and visuals");
        int before=director.TotalCount;director.Build();Check(director.TotalCount==before,"Repeated map initialization does not duplicate traps");
        Object.Destroy(director);yield return .2f;Check(map.GetComponentsInChildren<DungeonHazard>().Length==0,"Destroying the director removes its traps and restores floor openings");
        Check(clipped.All(r=>{r.GetPropertyBlock(block);return block.GetInt("_FloorCutoutCount")==0;}),"Destroying the director restores original floor rendering properties");
        Check(Physics.RaycastAll(pitPosition+Vector3.up*.5f,Vector3.down,1.5f).Any(hit=>DungeonFloorOpening.IsFloor(hit.collider.transform)),"Destroying the director restores floor collision at the pit");
        Check(errors.Count==0,"No unexpected Unity gameplay errors: "+string.Join(" | ",errors));
    }
    static IEnumerator Capture(Vector3 center,string filename)
    {
        var camera=new GameObject("Hazard Preview",typeof(Camera)).GetComponent<Camera>();camera.transform.position=center+new Vector3(4.2f,4.5f,-6.5f);camera.transform.LookAt(center+Vector3.up*.7f);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.09f,.12f);camera.nearClipPlane=.1f;camera.farClipPlane=70;
        var target=new RenderTexture(1280,720,24);camera.targetTexture=target;yield return .05f;camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
        var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(output,filename),texture.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;Object.Destroy(texture);Object.Destroy(target);Object.Destroy(camera.gameObject);
    }
}
