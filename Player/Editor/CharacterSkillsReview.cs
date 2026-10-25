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

/// <summary>Explicit isolated play-mode checks, with a temporary save path and no authentication.</summary>
[InitializeOnLoad]
public static class CharacterSkillsReview
{
    const string Pending = "CharacterSkillsReview.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static string output;
    static GameObject actor;
    static PlayerHealth health;
    static CharacterSkills skills;
    static readonly List<string> errors = new List<string>();
    static CharacterSkillsReview() { EditorApplication.playModeStateChanged += StateChanged; }
    public static void Run()
    {
        LoginSceneSetup.PlayFromCurrentScene();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false)) return;
        SessionState.SetBool(Pending,false); output = Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        assertions = 0; errors.Clear(); checks = Checks(); deadline = EditorApplication.timeSinceStartup + .2;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext())
            { File.WriteAllText(Path.Combine(output,"character-result.txt"),$"PASS {assertions} character integration checks"); Finish(0); return; }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .08f);
        }
        catch (Exception ex)
        { File.WriteAllText(Path.Combine(output,"character-failure.txt"),ex.ToString()); Debug.LogException(ex); Finish(1); }
    }
    static void Finish(int code)
    { Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code); }
    static void Log(string message,string stack,LogType type)
    { if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !message.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(message+"\n"+stack); }
    static void Check(bool condition,string message)
    { if (!condition) throw new Exception(message); assertions++; Debug.Log("PASS " + message); }
    static void Set(object target,string field,object value) => target.GetType().GetField(field,Flags).SetValue(target,value);
    static object Get(object target,string field) => target.GetType().GetField(field,Flags).GetValue(target);
    static object Call(object target,string method,params object[] args) => target.GetType().GetMethod(method,Flags).Invoke(target,args);
    static void Choose(string id)
    {
        skills.RestoreProgress(new CharacterProgress { version=1,id=id });
        health.ResetHealth(); Set(health,"invincibleUntil",0f); Set(health,"currentArmor",0f);
        actor.GetComponent<CombatMomentum>().ResetCombat();
    }
    static EnemyHealth Enemy(Vector3 position,float hp=1000)
    {
        var node = GameObject.CreatePrimitive(PrimitiveType.Cube); node.transform.position = position;
        var enemy = node.AddComponent<EnemyHealth>(); enemy.SetMaxHealth(hp);
        enemy.showDamageNumbers = false; enemy.healthDropChance = enemy.weakNpcWeaponDropChance = enemy.strongNpcWeaponDropChance =
            enemy.progressionDropChance = 0f;
        enemy.SetLevelAndExperience(1,0); Physics.SyncTransforms(); return enemy;
    }
    static void ClearEnemies()
    { foreach (var enemy in EnemyHealth.ActiveEnemies.ToArray()) if (enemy != null) Object.DestroyImmediate(enemy.gameObject); }
    static IEnumerator Checks()
    {
        Object.FindFirstObjectByType<PixelArtDirector>().enabled = false;
        var camera = new GameObject("Skill Review Camera",typeof(Camera)).GetComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0,1,0);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Safe Ground";
        ground.transform.position = new Vector3(0,-.5f,0); ground.transform.localScale = new Vector3(100,1,100);
        CharacterRoster.SelectForNewRun("engineer");
        actor = new GameObject("Character Test Player"); actor.SetActive(false); actor.transform.position = Vector3.up;
        var save = actor.AddComponent<GameProgressSave>(); save.enabled = false; save.StoragePath = Path.Combine(output,"character-save.json");
        var player = actor.AddComponent<Player>(); player.enabled = false; player.fpsCamera = camera;
        health = actor.AddComponent<PlayerHealth>(); health.enabled = false; health.criticalChance = 0f;
        var shooter = actor.AddComponent<PlayerShooter>(); shooter.enabled = false; shooter.fpsCamera = camera;
        actor.SetActive(true); actor.GetComponent<Rigidbody>().isKinematic = true; Call(player,"Start");
        actor.GetComponent<PlayerCursorController>().enabled = false; actor.GetComponent<PickupMagnet>().enabled = false;
        skills = actor.GetComponent<CharacterSkills>();
        yield return .3f;
        Check(skills.Character.Id == "engineer", "The map consumes the chosen fresh-run character");
        Check(CharacterRoster.ConsumeSelection().Id == "breaker", "Selection is consumed once instead of leaking into another player");
        Check(CharacterRoster.All.Length == 10 && CharacterRoster.All.Select(c=>c.Id).Distinct().Count()==10, "Ten stable and distinct character identities exist");
        foreach (var item in CharacterRoster.All)
        {
            Choose(item.Id);
            Check(skills.Character.Id == item.Id && actor.GetComponent<AnimeCharacterVisual>() != null, "Restoring role and appearance: "+item.Name);
            var model = (GameObject)Get(actor.GetComponent<AnimeCharacterVisual>(),"model");
            var tabard = model.transform.Find("Hips/Chest/Tabard").GetComponent<Renderer>();
            var colorBlock = new MaterialPropertyBlock(); tabard.GetPropertyBlock(colorBlock);
            Check(colorBlock.GetColor("_Color")==item.Color && model.transform.Find("Hips/Chest/Head/Character Headgear") != null,
                "The role has its own rendered palette and headgear: "+item.Name);
        }
        yield return .1f;
        Call(actor.GetComponent<AnimeCharacterVisual>(),"UpdateVisibility",true);
        Check(errors.Count==0, "Refreshing first-person visibility after role restoration has no stale renderer references");

        Choose("breaker"); var enemy = Enemy(new Vector3(0,1,3));
        Check(skills.TryUseSkill() && Mathf.Approximately(enemy.CurrentHealth,920), "Shockwave deals real area damage");
        Check(!skills.TryUseSkill() && Mathf.Approximately(enemy.CurrentHealth,920), "Cooldown prevents repeated casts");
        float cooldown = skills.CooldownRemaining; Time.timeScale = 0f; yield return .2f;
        Check(!skills.TryUseSkill() && skills.CooldownRemaining == cooldown, "Pausing freezes cooldown and blocks skills");
        Time.timeScale = 1f; ClearEnemies();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0,1,1.5f); wall.transform.localScale = new Vector3(8,4,.4f);
        enemy = Enemy(new Vector3(0,1,3)); Choose("breaker"); skills.TryUseSkill();
        Check(enemy.CurrentHealth == 1000, "Area abilities do not damage through walls"); Object.DestroyImmediate(wall); ClearEnemies();

        Choose("sentinel"); Set(health,"currentHealth",50f); skills.TryUseSkill();
        Check(Mathf.Approximately(health.CurrentArmor,health.MaxArmor*.2f), "Shield restores armor without increasing its maximum");
        Set(health,"currentArmor",0f); health.TakeDamage(20);
        Check(Mathf.Approximately(health.CurrentHealth,45), "Shield reduces actual incoming damage by seventy-five percent");

        Choose("gunner"); skills.TryUseSkill(); shooter.EquipWeapon(20,.2f,60);
        var template = new GameObject("Projectile Template",typeof(SphereCollider),typeof(Rigidbody),typeof(Projectile));
        template.transform.position = new Vector3(1000,1000,1000); shooter.projectilePrefab = template.GetComponent<Projectile>();
        shooter.Fire(); Check(Mathf.Abs((float)Get(shooter,"nextFireTime")-Time.time-.09f)<.001f, "Rapid fire changes the real weapon firing interval");
        foreach (var bullet in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) Object.DestroyImmediate(bullet.gameObject);
        Choose("medic"); Set(health,"currentHealth",20f); skills.TryUseSkill();
        Check(Mathf.Approximately(health.CurrentHealth,55), "Medic immediately heals thirty-five percent of max health");
        Call(skills,"Advance",5f); Check(Mathf.Approximately(health.CurrentHealth,70), "Medic's complete follow-up heal restores fifteen percent");

        Choose("assassin"); skills.TryUseSkill(); health.TakeDamage(50);
        Check(health.CurrentHealth==100 && skills.MoveSpeedMultiplier==1.75f, "Assassin avoids ordinary damage and gains movement speed");
        Check(Mathf.Approximately((float)Call(shooter,"GetProjectileDamage"),30), "Assassin increases actual gun damage");
        Call(player,"HandleRun"); Check(Mathf.Approximately((float)Get(player,"currentSpeed"),player.walkSpeed*1.75f), "Assassin changes real movement speed");
        Call(skills,"Advance",1.1f); health.TakeDamage(20);
        Check(health.CurrentHealth==80 && skills.DamageMultiplier==1.5f, "Assassin immunity ends before its damage boost");

        Choose("pyromancer"); enemy = Enemy(new Vector3(0,1,3)); skills.TryUseSkill(); Call(skills,"Advance",.01f);
        Check(Mathf.Approximately(enemy.CurrentHealth,982), "Fire zone applies its real damage pulse");
        actor.transform.position = new Vector3(20,1,0); Call(skills,"Advance",.5f);
        Check(Mathf.Approximately(enemy.CurrentHealth,964), "The fire zone stays at its deployment position");
        skills.CancelEffects(); Call(skills,"Advance",1f);
        Check(Mathf.Approximately(enemy.CurrentHealth,964), "Cancelling removes an active fire zone");
        actor.transform.position = Vector3.up; Physics.SyncTransforms(); ClearEnemies();

        Choose("engineer"); enemy = Enemy(new Vector3(0,1,3)); var far = Enemy(new Vector3(2,1,8));
        skills.TryUseSkill(); Call(skills,"Advance",.01f);
        Check(enemy.CurrentHealth==965 && far.CurrentHealth==1000, "Turret shoots only the nearest visible enemy");
        Call(skills,"Advance",11f); Check(skills.EffectRemaining==0f && Get(skills,"deployment")==null, "Turret expires without leaving a permanent deployment"); ClearEnemies();

        Choose("frost"); enemy = Enemy(new Vector3(0,1,3));
        var ai = enemy.gameObject.AddComponent<EnemyAI>(); ai.enabled = false;
        skills.TryUseSkill(); Check(enemy.CurrentHealth==965 && ai.IsFrozen, "Ice deals damage and applies actual AI freeze");
        Set(ai,"initialized",true); Call(ai,"TryBeginAttack",health,false);
        Check(!ai.IsAttacking, "A frozen enemy cannot begin an attack");
        Set(ai,"frozenUntil",Time.time-.1f); Check(!ai.IsFrozen, "AI freeze naturally expires"); ClearEnemies();
        Choose("frost"); enemy = Enemy(new Vector3(0,1,3));
        ai = enemy.gameObject.AddComponent<EnemyAI>(); ai.enabled = false;
        var boss = enemy.gameObject.AddComponent<BossController>(); boss.enabled = false;
        skills.TryUseSkill();
        Check(ai.IsFrozen && (float)Get(ai,"frozenUntil")-Time.time <= .501f, "Boss freeze is limited to half a second");
        Choose("frost"); Set(boss,"transformationEndsAt",Time.time+5f); Set(ai,"frozenUntil",0f);
        float bossHp=enemy.CurrentHealth; skills.TryUseSkill();
        Check(enemy.CurrentHealth==bossHp && !ai.IsFrozen, "Transforming bosses ignore skill damage and freeze"); ClearEnemies();

        Choose("reaper"); Set(health,"currentHealth",30f); enemy = Enemy(new Vector3(0,1,3)); enemy.RestoreSavedHealth(250);
        skills.TryUseSkill(); Check(enemy.IsDead && health.CurrentHealth==40, "Reaper executes a low-health enemy and restores health");
        ClearEnemies(); Choose("reaper"); enemy = Enemy(new Vector3(0,1,3)); skills.TryUseSkill();
        Check(!enemy.IsDead && enemy.CurrentHealth==950, "Reaper cannot execute a healthy enemy"); ClearEnemies();
        Choose("reaper"); enemy = Enemy(new Vector3(0,1,3)); enemy.RestoreSavedHealth(250);
        boss = enemy.gameObject.AddComponent<BossController>(); boss.enabled = false; skills.TryUseSkill();
        Check(!enemy.IsDead && enemy.CurrentHealth==200, "Reaper cannot instantly execute a boss"); ClearEnemies();

        Choose("ranger"); enemy = Enemy(new Vector3(0,1,3)); far = Enemy(new Vector3(0,1,6)); var sideEnemy = Enemy(new Vector3(4,1,3));
        skills.TryUseSkill(); Check(enemy.CurrentHealth==820 && far.CurrentHealth==820 && sideEnemy.CurrentHealth==1000, "Beam pierces multiple aligned targets without hitting targets beside it");
        Choose("ranger"); wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0,1,4.5f); wall.transform.localScale = new Vector3(8,4,.4f); Physics.SyncTransforms();
        skills.TryUseSkill(); Check(enemy.CurrentHealth==640 && far.CurrentHealth==820, "Beam stops at walls instead of piercing the map");
        Object.DestroyImmediate(wall); ClearEnemies();

        Choose("engineer"); skills.TryUseSkill();
        var snapshot = new GameProgressState { scene="CharacterReview", ground=Vector3.zero, health=health.CaptureProgress(),
            inventory=actor.GetComponent<WeaponInventory>().CaptureProgress(),floorKeys=new int[4],character=skills.CaptureProgress() };
        Check(GameProgressSave.TryWrite(save.StoragePath,snapshot,out string error), "Character progress writes to an isolated checksum-protected save: "+error);
        Check(GameProgressSave.TryRead(save.StoragePath,out var restored,out error) && restored.character.id=="engineer", "Character identity survives disk serialization");
        Choose("medic"); Set(save,"player",player); Set(save,"health",health); Set(save,"inventory",actor.GetComponent<WeaponInventory>());
        Call(save,"Restore",restored);
        Check(skills.Character.Id=="engineer" && skills.CooldownRemaining>23 && skills.EffectRemaining==0,
            "Actual game restoration retains the character and cooldown while discarding deployments");
        Check(!skills.TryUseSkill(), "Loading a save cannot bypass the skill cooldown");
        snapshot.character = null; Call(save,"Restore",snapshot);
        Check(skills.Character.Id=="breaker" && skills.CooldownRemaining==0 && health.maxHealth==snapshot.health.maxHealth, "Legacy saves default safely without applying stat bonuses twice");
        skills.RestoreProgress(new CharacterProgress { version=1,id="unknown",cooldown=float.NaN });
        Check(skills.Character.Id=="breaker" && skills.CooldownRemaining==10, "Unknown identities and invalid cooldowns have safe defaults");
        Choose("gunner"); skills.TryUseSkill(); cooldown=skills.CooldownRemaining;
        player.Teleport(Vector3.up,Quaternion.identity);
        Check(skills.EffectRemaining==0 && skills.CooldownRemaining==cooldown, "Teleport cancels buffs without resetting cooldown");
        Choose("assassin"); skills.TryUseSkill(); health.respawnDelay=100; health.KillInstantly();
        Check(health.IsDead && skills.EffectRemaining==0 && !skills.TryUseSkill(), "Fatal falls bypass assassin immunity and death cancels abilities");
        Time.timeScale=1; health.StopAllCoroutines();
        Check(!File.Exists(Path.Combine(output,"unused-save.json")), "Review uses its explicit isolated save only");
        Check(errors.Count==0, "No unexpected gameplay errors: "+string.Join("\n",errors));
    }
}
