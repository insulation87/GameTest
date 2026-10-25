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
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class ExpeditionMapReview
{
    const string Pending="ExpeditionMapReview.Pending";
    static readonly Stack<IEnumerator> routines=new Stack<IEnumerator>();
    static readonly List<string> results=new List<string>(),errors=new List<string>();
    static double next,deadline;
    static int frame;
    static Keyboard keyboard;
    static string Output=>ExpeditionMapBaker.Output;
    static ExpeditionMapReview(){EditorApplication.playModeStateChanged+=Changed;}
    public static void Run()
    {
        if(!Application.isBatchMode||Application.productName!="MapSelectionReview20261023") throw new InvalidOperationException("Use the isolated map-review project.");
        string guest=Path.Combine(Application.persistentDataPath,"Guest","progress-v1.json");
        foreach(string file in new[]{guest,guest+".bak"})if(File.Exists(file))File.Delete(file);
        LoginSceneSetup.PlayFromCurrentScene();EditorSceneManager.OpenScene("Assets/Scenes/Login.unity");
        var game=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));game.Show();game.Focus();
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
        SessionState.SetBool(Pending,false);results.Clear();errors.Clear();routines.Clear();routines.Push(Checks());
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>();
        deadline=EditorApplication.timeSinceStartup+480;next=0;frame=Time.frameCount+3;
        Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
    }
    static void Log(string message,string stack,LogType type)
    {if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&!message.Contains("burst JIT compiler")&&!stack.Contains("UnityEditor.Search"))errors.Add(message+"\n"+stack);}
    static void Tick()
    {
        try {
            if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Map Play Mode review timed out.");
            if(EditorApplication.timeSinceStartup<next||Time.frameCount<frame)return;
            if(routines.Count==0){Finish(0,"PASS "+results.Count+" map Play Mode checks");return;}
            var routine=routines.Peek();if(!routine.MoveNext()){routines.Pop();return;}
            if(routine.Current is IEnumerator child)routines.Push(child);
            next=EditorApplication.timeSinceStartup+(routine.Current is float delay?delay:.04f);frame=Time.frameCount+2;
        }catch(Exception ex){Finish(1,"FAIL "+ex+"\n"+string.Join("\n",errors));}
    }
    static void Finish(int code,string summary)
    {File.WriteAllText(Path.Combine(Output,"map-play-results.txt"),summary+"\n"+string.Join("\n",results));Application.logMessageReceived-=Log;EditorApplication.update-=Tick;if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);Time.timeScale=1;EditorApplication.Exit(code);}
    static void Check(bool valid,string text){if(!valid)throw new InvalidOperationException(text);results.Add(text);Debug.Log("PASS "+text);}
    static IEnumerator Until(Func<bool> ready,string text)
    {
        double end=EditorApplication.timeSinceStartup+30;
        while(!ready()) {
            if(EditorApplication.timeSinceStartup>end) {
                var player=Object.FindFirstObjectByType<Player>();
                string debug=player==null?"no player":$"position {player.transform.position}, velocity {player.GetComponent<Rigidbody>().linearVelocity}, enabled {player.enabled}, timeScale {Time.timeScale}, dead {player.GetComponent<PlayerHealth>().IsDead}";
                throw new TimeoutException(text+"; "+debug);
            }
            yield return .1f;
        }
        yield return .2f;
    }
    static IEnumerator Scene(string scene)=>Until(()=>SceneManager.GetActiveScene().name==scene&&!GameSceneFlow.IsLoading,"Loading "+scene);
    static Button Button(string text)
    {
        var buttons=Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>b.GetComponentInChildren<Text>()!=null).ToArray();
        return buttons.FirstOrDefault(b=>b.GetComponentInChildren<Text>().text==text)??buttons.First(b=>b.GetComponentInChildren<Text>().text.Contains(text));
    }
    static void Click(string text)
    {
        var button=Button(text);Canvas.ForceUpdateCanvases();
        var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,button.transform.position),button=PointerEventData.InputButton.Left};
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
        Check(button.interactable&&hits.Count>0&&hits[0].gameObject==button.gameObject,"Actual UI click: "+text+" (hit "+(hits.Count>0?hits[0].gameObject.name:"none")+")");
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    static IEnumerator Upgrades()
    {
        var health=Object.FindFirstObjectByType<PlayerHealth>();
        while(health!=null&&health.IsChoosingUpgrade){var buttons=(List<Button>)typeof(PlayerHealth).GetField("upgradeButtons",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(health);Click(buttons[0].GetComponentInChildren<Text>().text);yield return .1f;}
    }
    static TradingHubController Hub=>Object.FindFirstObjectByType<TradingHubController>();
    static void HubPortal(int index=0)
    {
        var portal=Object.FindObjectsByType<TradingHubPortal>(FindObjectsSortMode.None).Single(p=>p.Map.Index==index);
        var controller=Hub.Visitor.GetComponent<CharacterController>();controller.enabled=false;Hub.Visitor.position=portal.transform.position+new Vector3(0,.2f,-3);controller.enabled=true;Hub.Interact();
    }
    static void Press(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
    static IEnumerator Walk(Vector3 target)
    {
        double end=EditorApplication.timeSinceStartup+20;
        while(Vector2.Distance(new Vector2(Hub.Visitor.position.x,Hub.Visitor.position.z),new Vector2(target.x,target.z))>.25f) {
            if(EditorApplication.timeSinceStartup>end)throw new TimeoutException("Camp walking path is blocked at "+Hub.Visitor.position+" toward "+target);
            Vector3 delta=target-Hub.Visitor.position;var keys=new List<Key>();
            if(Mathf.Abs(delta.x)>.15f)keys.Add(delta.x>0?Key.D:Key.A);
            if(Mathf.Abs(delta.z)>.15f)keys.Add(delta.z>0?Key.W:Key.S);
            Press(keys.ToArray());yield return .04f;
        }
        Press();yield return .1f;
    }
    static GameProgressState Read(){if(!GameProgressSave.TryRead(GameProgressSave.SavePath,out var data,out var error))throw new Exception(error);return data;}
    static Vector3 Safe(Player player,Vector3 near)
    {
        for(int i=0;i<32;i++) {
            Vector3 at=near+Quaternion.Euler(0,i*47,0)*Vector3.forward*(i*.6f);
            if(NavMesh.SamplePosition(at,out var nav,4,NavMesh.AllAreas)&&
                Physics.Raycast(nav.position+Vector3.up*3,Vector3.down,out var floor,6,Physics.AllLayers,QueryTriggerInteraction.Ignore)&&floor.normal.y>.95f&&
                floor.collider.attachedRigidbody==null&&player.TryGetTeleportPosition(floor.point,Quaternion.identity,out var safe))return safe;
        }
        throw new InvalidOperationException("No safe position near "+near);
    }
    static IEnumerator Checks()
    {
        Check(ExpeditionMaps.All.Length==4&&ExpeditionMaps.All.All(m=>m.Stages.Length==3&&Application.CanStreamedLevelBeLoaded(m.Scene)),"All four maps are selectable build scenes, each with exactly three stage definitions");
        var account=CloudAccountService.EnsureInstance();Check(account.EnterGuest(),"Actual Login scene enters a guest session without network calls");yield return Scene("MainMenu");
        string path=GameProgressSave.SavePath;Check(path.Contains("MapSelectionReview20261023"),"Test saves use a separate product directory");
        foreach(string file in new[]{path,path+".bak"})if(File.Exists(file))File.Delete(file);
        Check(Object.FindFirstObjectByType<MapSelectionMenu>()==null,"Main menu has no map picker; a new adventure starts with a camp visit");
        Click("開始遊戲");yield return .15f;Object.FindFirstObjectByType<CharacterSelectionMenu>().Select(9);Click("以此角色出發");yield return Scene("TradingHub");
        string beforeBrowse=File.ReadAllText(path);
        var doors=Object.FindObjectsByType<TradingHubPortal>(FindObjectsSortMode.None).OrderBy(p=>p.Map.Index).ToArray();
        Check(doors.Length==4&&doors.Select(p=>p.Map.Scene).Distinct().Count()==4&&doors.Select(p=>p.transform.position).Distinct().Count()==4,
            "Camp has four separate physical doorways, each with one unique map destination");
        Check(Object.FindFirstObjectByType<MapSelectionMenu>()==null,"Camp map selection uses physical doors without a map-picker overlay");
        Canvas.ForceUpdateCanvases();
        Check(doors.All(d=>d.GetComponentsInChildren<Text>().All(t=>t.text.Contains(d.Map.Name)&&t.text.Contains("3 關")&&t.preferredHeight<=t.rectTransform.rect.height&&t.preferredWidth<=t.rectTransform.rect.width)),
            "All four physical door signs fit their map name, difficulty and three-stage text");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output,"four-portals-camp.png"));yield return .3f;
        yield return Walk(new Vector3(0,0,8));
        foreach(var door in doors) {
            yield return Walk(door.transform.position+Vector3.back*3);
            Check(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains("F · 前往「"+door.Map.Name)),"Walking through the courtyard reaches the "+door.Map.Name+" door's interaction range");
            Press(Key.F);yield return .12f;Press();yield return .12f;
            Check(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains(door.Map.Name+" · ")&&t.text.Contains("全 3 關")),"F opens the fixed destination for "+door.Map.Scene);
            if(door.Map.Index==3){ScreenCapture.CaptureScreenshot(Path.Combine(Output,"four-portals-confirmation.png"));yield return .3f;}
            Click("繼續留在營地");yield return .1f;
            Check(File.ReadAllText(path)==beforeBrowse&&Hub.Stock.Destination=="Map1","Cancelling "+door.Map.Scene+" leaves the preparation, wallet and equipment unchanged");
        }
        Click("返回主選單");yield return Scene("MainMenu");
        Click("多人模式");yield return .15f;
        for(int i=1;i<=4;i++){Click("合作地圖");Check(Button("合作地圖").GetComponentInChildren<Text>().text.Contains(ExpeditionMaps.All[i%4].Name),"Cooperative lobby can select "+ExpeditionMaps.All[i%4].Name+" before starting a room");}
        Click("返回單人主選單");yield return .15f;
        float previousHealth=0,previousDamage=0,previousSpawn=100;
        int bank=0;
        for(int mapIndex=0;mapIndex<ExpeditionMaps.All.Length;mapIndex++) {
            var map=ExpeditionMaps.All[mapIndex];
            Click("開始新遊戲");Click("重設本輪");yield return .15f;
            Object.FindFirstObjectByType<CharacterSelectionMenu>().Select(9);Click("以此角色出發");yield return Scene("TradingHub");
            string boughtWeapon=null;
            if(mapIndex==1) {
                var walker=Hub.Visitor.GetComponent<CharacterController>();walker.enabled=false;Hub.Visitor.position=new Vector3(-7,.2f,.6f);walker.enabled=true;Hub.Interact();yield return .15f;
                Click("震地戰錘");bank-=CharacterWeapons.ShopPrice(WeaponType.WarHammer);boughtWeapon=Hub.Stock.Inventory.weapons.Last().itemId;Click("結束交易");yield return .15f;
                walker.enabled=false;Hub.Visitor.position=new Vector3(7,.2f,.6f);walker.enabled=true;Hub.Interact();yield return .15f;Click("頭盔");bank-=100;Click("結束交易");yield return .15f;
                walker.enabled=false;Hub.Visitor.position=new Vector3(-7,.2f,-7.4f);walker.enabled=true;Hub.Interact();yield return .15f;Click("血包 ×1");Click("背包擴充");bank-=150;Click("結束交易");yield return .15f;
            }
            string equipment=JsonUtility.ToJson(Hub.Stock.Inventory);int purchases=Hub.Stock.WeaponPurchases;
            if(mapIndex==1) {
                Click("返回主選單");yield return Scene("MainMenu");Click("繼續遊戲");yield return Scene("TradingHub");
                Check(JsonUtility.ToJson(Hub.Stock.Inventory)==equipment,"Continue returns an unstarted preparation to camp with all purchased equipment for a doorway choice");
            }
            string beforeDoor=File.ReadAllText(path);HubPortal(mapIndex);yield return .15f;
            Check(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains(map.Name)),map.Name+": portal confirmation names the selected map");
            Check(File.ReadAllText(path)==beforeDoor&&JsonUtility.ToJson(Hub.Stock.Inventory)==equipment&&Hub.Stock.WeaponPurchases==purchases,
                map.Name+": approaching its door preserves all prepared items and does not change the save before confirmation");
            Click("進入遊戲");yield return Scene(map.Scene);
            yield return Until(()=>GameProgressSave.Instance!=null&&GameProgressSave.Instance.Ready&&TacticalExpedition.Instance!=null&&TacticalExpedition.Instance.Ready,"Map systems initialize");yield return .5f;yield return Upgrades();
            yield return Until(()=>Object.FindFirstObjectByType<Player>().IsGrounded,"The saved scene's elevated spawn settles onto the ground");
            var player=Object.FindFirstObjectByType<Player>();var hp=player.GetComponent<PlayerHealth>();var inventory=player.GetComponent<WeaponInventory>();
            var world=Object.FindFirstObjectByType<OverworldDirector>();var settings=world.GetComponent<DungeonViewSettings>();var progression=FloorProgressionManager.Instance;
            var spawner=Object.FindFirstObjectByType<NPCSpawner>();spawner.enabled=false;
            Check(Read().scene==map.Scene&&!Read().preparingRun,map.Name+": the chosen physical doorway persists and loads its own map");
            foreach(var enemy in Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))Object.DestroyImmediate(enemy.gameObject);
            Check(world.IsAuthored&&world.Built&&world.ThemeIndex==mapIndex&&player.IsGrounded&&!hp.IsDead&&Time.timeScale>0,
                map.Name+": saved scene loads its themed authored terrain and a grounded live player (theme "+world.ThemeIndex+", grounded "+player.IsGrounded+", timeScale "+Time.timeScale+")");
            Check(new[]{settings.level1,settings.level2,settings.level3}.Select((level,index)=>level!=null&&level.activeSelf&&level.name.Contains(map.Stages[index])).All(valid=>valid),map.Name+": three distinct saved stage roots are present");
            Check(progression.Portals.Count==4&&!progression.IsRegionUnlocked(2)&&!progression.IsRegionUnlocked(3),map.Name+": stage 2 and 3 begin locked behind real progression portals");
            if(mapIndex==0){inventory.AddGold(700);bank=inventory.Gold;}
            Check(inventory.Gold==bank,map.Name+": cross-map new runs retain gold and reset to a fresh character loadout");
            if(boughtWeapon!=null)Check(inventory.Weapons.Any(w=>w.itemId==boughtWeapon)&&inventory.Armors.Count==1&&inventory.CaptureProgress().healthPacks==1&&inventory.capacity==25,
                "Weapon, armor, pack and backpack space purchased before camp map selection arrive in the selected map through the portal");
            for(int stage=1;stage<=3;stage++) {
                player.Teleport(Safe(player,world.RegionBounds[stage-1].center),Quaternion.identity);
                yield return Until(()=>player.IsGrounded,"Landing in stage "+stage);
                Check(world.RegionAt(player.transform.position)==stage&&player.IsGrounded&&!hp.IsDead,map.Name+": safe navigation and collision in stage "+stage);
                Check((bool)Call(spawner,"TrySpawnNpcInRoom",world.EnemyBoundaryAt(player.transform.position)),map.Name+": a real NPC spawns in stage "+stage);
                var spawned=(List<GameObject>)typeof(NPCSpawner).GetField("spawnedNpcs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(spawner);
                var enemy=spawned.Last().GetComponent<EnemyHealth>();var ai=enemy.GetComponent<EnemyAI>();
                UnityEngine.Random.InitState(4217);Call(spawner,"ApplyRandomStats",enemy.gameObject,ai);ai.enabled=false;
                Check(enemy.DungeonFloor==stage&&enemy.EnemyLevel>=(stage==1?1:stage==2?21:51)&&enemy.EnemyLevel<=(stage==1?20:stage==2?50:99)&&
                    enemy.GetComponent<NavMeshAgent>().isOnNavMesh,map.Name+": NPC uses stage-specific levels and baked navigation in stage "+stage+
                    $" (floor {enemy.DungeonFloor}, level {enemy.EnemyLevel}, onNav {enemy.GetComponent<NavMeshAgent>().isOnNavMesh}, room {world.EnemyBoundaryAt(player.transform.position).transform.parent.name}, npc {enemy.transform.position})");
                if(stage==1) {
                    ai.ConfigureLevel(1);
                    Check(mapIndex==0||enemy.maxHealth>previousHealth&&ai.AttackDamage>previousDamage&&spawner.CurrentSpawnInterval<previousSpawn,
                        map.Name+": actual enemy health/damage/spawn frequency increase over the preceding map");
                    previousHealth=enemy.maxHealth;previousDamage=ai.AttackDamage;previousSpawn=spawner.CurrentSpawnInterval;
                    typeof(PlayerHealth).GetField("invincibleUntil",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(hp,-1f);
                    float before=hp.CurrentHealth+hp.CurrentArmor;hp.TakeDamage(ai.AttackDamage);
                    Check(Mathf.Abs(before-hp.CurrentHealth-hp.CurrentArmor-ai.AttackDamage)<.05f,map.Name+": live damage application uses the selected map's scaled attack strength");
                }
                Object.DestroyImmediate(enemy.gameObject);
                if(stage<3) {
                    var portal=progression.Portals.First(p=>p.SourceRegion==stage&&p.TargetRegion==stage+1);
                    player.Teleport(Safe(player,portal.transform.position+portal.transform.forward*2.5f),portal.transform.rotation);
                    Check(!portal.IsUnlocked&&!progression.TryUsePortal(portal,player),map.Name+": stage "+stage+" portal rejects early travel");
                    while(!progression.IsComplete(stage))progression.Collect(stage);
                    Check(portal.IsUnlocked&&progression.TryUsePortal(portal,player)&&world.RegionAt(player.transform.position)==stage+1,
                        map.Name+": stage "+stage+" completion opens the real portal to stage "+(stage+1));
                    yield return 1.3f;
                }
            }
            Capture(world.RegionBounds[1].center,"world-"+map.Scene+".png");
            Check(GameProgressSave.Instance.SaveNow()&&Read().scene==map.Scene&&Read().floorKeys[1]==progression.RequiredItemsFor(1)&&Read().floorKeys[2]==progression.RequiredItemsFor(2),map.Name+": scene identity, wallet and stage completion save together");
            Check(GameSceneFlow.Load(GameSceneFlow.MenuScene),map.Name+": returning to menu is available");yield return Scene("MainMenu");Click("繼續遊戲");yield return Scene(map.Scene);yield return .3f;yield return Upgrades();
            player=Object.FindFirstObjectByType<Player>();hp=player.GetComponent<PlayerHealth>();progression=FloorProgressionManager.Instance;world=Object.FindFirstObjectByType<OverworldDirector>();
            Object.FindFirstObjectByType<NPCSpawner>().enabled=false;
            foreach(var enemy in Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))Object.DestroyImmediate(enemy.gameObject);
            Check(world.RegionAt(player.transform.position)==3&&progression.IsComplete(1)&&progression.IsComplete(2)&&player.GetComponent<WeaponInventory>().Gold==bank,
                map.Name+": Continue restores the correct map and third-stage position/progress");
            while(!progression.IsComplete(3))progression.Collect(3);
            var boss=Object.FindFirstObjectByType<BossSpawner>();Check(boss!=null&&boss.CurrentBoss!=null,map.Name+": completing stage 3 summons its final boss");
            var bossHealth=boss.CurrentBoss.GetComponent<EnemyHealth>();
            Check(Mathf.Abs(bossHealth.maxHealth-5000*map.Health)<.1f,map.Name+": final boss receives the map health multiplier");
            var savedBoss=boss.CurrentBoss; savedBoss.GetComponent<EnemyAI>().enabled=false;savedBoss.transformationDuration=.1f;
            int firstPhase=1;
            if(mapIndex==2) {
                bossHealth.TakeDamage(bossHealth.maxHealth+1,hp);yield return .25f;
                float maximum=bossHealth.maxHealth;bossHealth.TakeDamage(100,hp);
                Check(GameProgressSave.Instance.SaveNow()&&GameSceneFlow.Load(GameSceneFlow.MenuScene),"Snow-map phase-2 boss health can be saved");yield return Scene("MainMenu");Click("繼續遊戲");yield return Scene(map.Scene);yield return .2f;
                savedBoss=Object.FindFirstObjectByType<BossSpawner>().CurrentBoss;bossHealth=savedBoss.GetComponent<EnemyHealth>();hp=Object.FindFirstObjectByType<PlayerHealth>();
                Check(savedBoss.Phase==2&&Mathf.Abs(bossHealth.maxHealth-maximum)<.1f&&Mathf.Abs(bossHealth.CurrentHealth-(maximum-100))<.1f,"Reloaded boss keeps its difficulty-scaled health exactly once and restores phase 2");
                savedBoss.GetComponent<EnemyAI>().enabled=false;savedBoss.transformationDuration=.1f;yield return 2f;firstPhase=2;
            }
            for(int phase=firstPhase;phase<=3;phase++){bossHealth.TakeDamage(bossHealth.maxHealth+1,hp);yield return .25f;}
            yield return Until(()=>DungeonVictoryUI.IsOpen,"Final map victory");yield return .6f;
            Check(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains(map.Name+"通關")),map.Name+": defeating all three boss phases completes the selected three-stage map");
            Click("返回主選單");yield return Scene("MainMenu");
            Check(Read().runEnded&&Read().scene==map.Scene,map.Name+": map completion ends this run while retaining the profile wallet");
            bank=Read().inventory.gold;
        }
        // Doorway confirmation is transactional, and a different door explicitly starts a new run after departure.
        Click("旅人營地");yield return .15f;Object.FindFirstObjectByType<CharacterSelectionMenu>().Select(9);Click("以此角色出發");yield return Scene("TradingHub");
        string unchanged=File.ReadAllText(path),previousScene=Hub.Stock.Destination;
        HubPortal(1);yield return .15f;
        using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
            Click("進入遊戲");yield return .15f;
            Check(SceneManager.GetActiveScene().name=="TradingHub"&&!GameSceneFlow.IsLoading&&Hub.Stock.Destination==previousScene,
                "A locked preparation save prevents doorway departure and retains the original destination");
            Check(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains("目的地未變更")),"Failed doorway departure displays a save error in its confirmation dialog");
        }
        Check(File.ReadAllText(path)==unchanged&&Hub.Stock.Inventory.gold==bank,"Failed doorway departure preserves the on-disk preparation and wallet");
        Click("進入遊戲");yield return Scene("Map2");yield return Upgrades();
        Check(GameSceneFlow.Load(GameSceneFlow.MenuScene),"An active map can return to camp through the menu");yield return Scene("MainMenu");Click("旅人營地");yield return Scene("TradingHub");
        unchanged=File.ReadAllText(path);HubPortal(2);yield return .15f;
        Check(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains("已有另一張地圖的冒險進度")),"A different doorway after departure clearly warns that confirmation starts a new run");
        Click("繼續留在營地");yield return .15f;
        Check(File.ReadAllText(path)==unchanged&&!Read().preparingRun,"Cancelling a different doorway preserves the active map's world progress");
        HubPortal(1);yield return .15f;
        Check(Button("進入遊戲")!=null&&Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains("接續原地圖進度")),"The matching doorway offers to continue the current map with its existing equipment");
        Click("繼續留在營地");yield return .15f;
        string[] priorWeapons=Hub.Stock.Inventory.weapons.Select(w=>w.itemId).ToArray();int priorGold=Hub.Stock.Inventory.gold,priorPurchases=Hub.Stock.WeaponPurchases;
        HubPortal(2);yield return .15f;Click("開始新一輪並進入");yield return Scene("Map3");yield return Upgrades();
        var freshInventory=Object.FindFirstObjectByType<WeaponInventory>();
        Check(Read().scene=="Map3"&&freshInventory.Gold==priorGold&&freshInventory.WeaponPurchaseCount==priorPurchases&&
            freshInventory.Weapons.All(w=>!priorWeapons.Contains(w.itemId)&&w.level==1)&&freshInventory.Armors.Count==0&&freshInventory.CaptureProgress().healthPacks==0&&
            freshInventory.capacity==20&&!FloorProgressionManager.Instance.IsComplete(1),
            $"Explicitly entering a different doorway after departure loads a fresh map, resetting old gear and retaining gold/unlocks (scene {Read().scene}, gold {freshInventory.Gold}/{priorGold}, purchases {freshInventory.WeaponPurchaseCount}/{priorPurchases}, starter weapons {freshInventory.Weapons.Count})");
        Check(errors.Count==0,"No runtime errors/exceptions occurred during map selection, all 12 stages, portal travel, save/continue and final victories");
    }
    static void Capture(Vector3 center,string filename)
    {
        var node=new GameObject("Map Review Camera",typeof(Camera));var camera=node.GetComponent<Camera>();
        camera.transform.position=center+new Vector3(40,34,-40);camera.transform.LookAt(center);camera.farClipPlane=260;camera.backgroundColor=RenderSettings.fogColor;camera.clearFlags=CameraClearFlags.SolidColor;
        var target=new RenderTexture(1280,720,24);camera.targetTexture=target;camera.Render();var old=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,filename),image.EncodeToPNG());
        RenderTexture.active=old;camera.targetTexture=null;Object.Destroy(image);Object.Destroy(target);Object.Destroy(node);
    }
}
