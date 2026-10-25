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
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit play-mode review; run in an isolated project, never automatically in a user scene.</summary>
[InitializeOnLoad]
public static class CombatMomentumReview
{
    const string Pending = "CombatMomentumReview.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static string output;
    static PlayerHealth health;
    static CombatMomentum momentum;
    static GameObject playerNode;
    static readonly List<string> runtimeErrors = new List<string>();
    static CombatMomentumReview() { EditorApplication.playModeStateChanged += StateChanged; }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        assertions = 0; checks = Checks(); deadline = EditorApplication.timeSinceStartup + .2;
        runtimeErrors.Clear(); Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext())
            {
                File.WriteAllText(Path.Combine(output, "combat-result.txt"), $"PASS {assertions} combat integration checks");
                Application.logMessageReceived -= OnLog;
                EditorApplication.update -= Tick; EditorApplication.Exit(0); return;
            }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .08f);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "combat-failure.txt"), ex.ToString());
            Application.logMessageReceived -= OnLog;
            Debug.LogException(ex); EditorApplication.update -= Tick; EditorApplication.Exit(1);
        }
    }
    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        // These are known editor-only issues in the isolated project's copied package DLLs.
        if (message.Contains("burst JIT compiler") || stack.Contains("UnityEditor.Search")) return;
        runtimeErrors.Add(message + "\n" + stack);
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++; Debug.Log("PASS " + message);
    }
    static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static EnemyHealth Enemy(string name, Vector3 position, float hp = 1f)
    {
        var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
        node.name = name; node.transform.position = position;
        var material = new Material(Resources.Load<Shader>("PixelSurface"));
        material.color = new Color(.2f, .4f, .6f);
        node.GetComponent<Renderer>().sharedMaterial = material;
        var enemy = node.AddComponent<EnemyHealth>(); enemy.SetMaxHealth(hp);
        enemy.showDamageNumbers = false;
        enemy.healthDropChance = enemy.weakNpcWeaponDropChance = enemy.strongNpcWeaponDropChance =
            enemy.progressionDropChance = 0;
        return enemy;
    }
    static void Kill() => Enemy("Chain Target", new Vector3(30, 1, 30)).TakeDamage(100, health);
    static IEnumerator Checks()
    {
        Object.FindFirstObjectByType<PixelArtDirector>().enabled = false;
        var camera = new GameObject("Test Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 1, -6);
        playerNode = new GameObject("Combat Test Player"); playerNode.SetActive(false);
        var save = playerNode.AddComponent<GameProgressSave>(); save.enabled = false;
        save.StoragePath = Path.Combine(output, "unused-save.json");
        var player = playerNode.AddComponent<Player>(); player.enabled = false; player.fpsCamera = camera;
        health = playerNode.AddComponent<PlayerHealth>(); health.enabled = false;
        health.criticalChance = 0; health.attackMultiplier = 1;
        var shooter = playerNode.AddComponent<PlayerShooter>(); shooter.fpsCamera = camera; shooter.enabled = false;
        playerNode.SetActive(true); playerNode.GetComponent<Rigidbody>().isKinematic = true;
        Call(player, "Start"); // Initialize movement references while keeping input disabled.
        playerNode.GetComponent<PickupMagnet>().enabled = false;
        playerNode.GetComponent<PlayerCursorController>().enabled = false;
        var inventory = playerNode.GetComponent<WeaponInventory>();
        momentum = playerNode.GetComponent<CombatMomentum>();
        var crosshair = playerNode.GetComponent<CrosshairUI>(); crosshair.onlyWhenCursorLocked = false;
        playerNode.AddComponent<PlayerHealthBarUI>();
        var prefab = new GameObject("Bullet Template", typeof(SphereCollider), typeof(Rigidbody), typeof(Projectile)).GetComponent<Projectile>();
        prefab.transform.position = new Vector3(1000, 0, 0); prefab.GetComponent<Rigidbody>().useGravity = false;
        prefab.GetComponent<SphereCollider>().radius = .04f; shooter.projectilePrefab = prefab;
        yield return .2f;
        Check(momentum != null && playerNode.GetComponents<CombatMomentum>().Length == 1, "Shooter automatically installs one momentum component");
        Check(playerNode.GetComponents<CrosshairUI>().Length == 1, "Feedback reuses one crosshair");
        crosshair.NotifyHit(true); crosshair.enabled = false;
        Check(((CanvasGroup)Get(crosshair, "canvasGroup")).alpha == 0 &&
            ((Image[])Get(crosshair, "hitMarks")).All(mark => mark.color.a == 0),
            "Disabling crosshair feedback immediately hides its separate canvas and stale hit marks");
        crosshair.enabled = true;
        Check(momentum.DamageMultiplier == 1 && momentum.FireIntervalMultiplier == 1 && momentum.MoveSpeedMultiplier == 1,
            "Ordinary combat has no passive stat inflation");
        var target = Enemy("Durable Target", new Vector3(0, 1, 5), 1000);
        target.TakeDamage(10, health);
        Check(momentum.KillChain == 0 && (float)Get(crosshair, "hitRemaining") > 0, "A real nonlethal hit gives feedback without awarding a kill");
        Check(target.GetComponent<EnemyHitFlash>() != null, "Accepted hits install render feedback on demand");
        var renderer = target.GetComponent<Renderer>(); var block = new MaterialPropertyBlock();
        block.SetColor("_Color", Color.magenta); block.SetFloat("_WorldPattern", .37f); renderer.SetPropertyBlock(block, 0);
        var flash = target.GetComponent<EnemyHitFlash>();
        Call(flash, "LateUpdate"); renderer.GetPropertyBlock(block, 0);
        Check(block.GetColor("_Color") != Color.magenta && Mathf.Approximately(block.GetFloat("_WorldPattern"), .37f),
            "Flash changes tint and preserves the material's other properties");
        flash.StopFlash(); renderer.GetPropertyBlock(block, 0);
        Check(block.GetColor("_Color") == Color.magenta && renderer.sharedMaterial.color == new Color(.2f, .4f, .6f),
            "Ending a flash restores per-slot tint without modifying shared materials");
        Check(!flash.enabled, "Idle hit feedback stops its per-frame updates");
        target.TakeDamage(1, health);
        Check(flash.enabled, "A subsequent actual hit reuses and wakes the cached flash component");
        flash.StopFlash();
        crosshair.ResetFeedback();
        var bossHealth = Enemy("Phase Test Boss", new Vector3(40, 1, 40), 10);
        var boss = bossHealth.gameObject.AddComponent<BossController>(); boss.Initialize(player, bossHealth, null);
        bossHealth.TakeDamage(100, health);
        Check(boss.Phase == 2 && !bossHealth.IsDead && momentum.KillChain == 0 && (float)Get(crosshair, "killRemaining") == 0,
            "A real boss transformation gives hit feedback without a false kill reward");
        crosshair.ResetFeedback(); float bossHp = bossHealth.CurrentHealth;
        bossHealth.TakeDamage(100, health);
        Check(bossHealth.CurrentHealth == bossHp && momentum.KillChain == 0 && (float)Get(crosshair, "hitRemaining") == 0,
            "Invulnerable boss transformations ignore both damage and hit feedback");
        Object.DestroyImmediate(bossHealth.gameObject);
        var dead = Enemy("Lethal Target", new Vector3(30, 1, 30)); dead.TakeDamage(100, health);
        Check(momentum.KillChain == 1 && (float)Get(crosshair, "killRemaining") > 0, "A real kill gives a gold marker and one chain credit");
        dead.TakeDamage(100, health);
        Check(momentum.KillChain == 1, "Repeated damage on a dead enemy cannot duplicate kill rewards");
        Kill(); Check(!momentum.IsSurging && momentum.KillChain == 2, "Two kills cannot start a three-kill surge");
        Kill(); Check(momentum.IsSurging && momentum.SurgeRemaining <= 6 && momentum.KillChain == 3, "Three real kills activate the six-second surge");
        Check(Mathf.Approximately(momentum.DamageMultiplier, 1.15f) && Mathf.Approximately(momentum.FireIntervalMultiplier, .8f) &&
            Mathf.Approximately(momentum.MoveSpeedMultiplier, 1.12f), "Surge bonuses are bounded, nonstacking multipliers");
        for (int i = 0; i < 30; i++) Kill();
        Check(momentum.SurgeRemaining <= 8 && momentum.DamageMultiplier == 1.15f, "Large multikills cap stored surge time and never stack damage bonuses");
        shooter.EquipWeapon(new WeaponItem { weaponType = WeaponType.Rifle, damage = 20, fireInterval = .2f, projectileSpeed = 20, projectilesPerShot = 1 });
        shooter.Fire();
        var bullet = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).First(p => p != prefab);
        Check(Mathf.Approximately((float)Get(bullet, "damage"), 23f), "Actual fired projectiles capture the surge damage bonus once");
        Check(Mathf.Abs((float)Get(shooter, "nextFireTime") - Time.time - .16f) < .01f, "Actual shots use twenty-five percent faster firing");
        Check((float)Get(crosshair, "bloom") > 0 && (float)Get(shooter, "weaponKick") > 0, "A fired salvo triggers crosshair bloom and model recoil");
        Call(shooter, "LateUpdate");
        var rig = (GameObject)Get(shooter, "activeWeaponModel");
        Check(rig.transform.localPosition.z < 0 && camera.transform.localRotation == Quaternion.identity, "Recoil moves the weapon while leaving aim and the camera untouched");
        Call(player, "HandleRun");
        Check(Mathf.Approximately((float)Get(player, "currentSpeed"), player.walkSpeed * 1.12f), "Movement actually applies the surge multiplier");
        Object.DestroyImmediate(bullet.gameObject);
        Call(inventory, "SetPaused", true);
        float surgeBefore = momentum.SurgeRemaining; int chainBefore = momentum.KillChain;
        yield return .3f;
        Kill(); shooter.Fire();
        Check(momentum.SurgeRemaining == surgeBefore && momentum.KillChain == chainBefore, "The real pause menu freezes timers and suppresses kill rewards");
        Check(Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length == 1, "Paused shooting does not create a projectile");
        Check(!((Canvas)Get(momentum, "hud")).enabled && ((CanvasGroup)Get(crosshair, "canvasGroup")).alpha == 0,
            "Combat feedback is hidden while menu controls are in use");
        Call(inventory, "SetPaused", false);
        Call(inventory, "SetOpen", true);
        surgeBefore = momentum.SurgeRemaining; chainBefore = momentum.KillChain;
        yield return .2f; Kill();
        Check(momentum.SurgeRemaining == surgeBefore && momentum.KillChain == chainBefore,
            "An inventory menu freezes momentum even while the rest of the world runs");
        Call(inventory, "SetOpen", false);
        Call(momentum, "Advance", 9f);
        Check(!momentum.IsSurging && momentum.KillChain == 0 && momentum.DamageMultiplier == 1, "Expired chains and surge bonuses return to baseline");
        Kill(); Call(momentum, "Advance", 5.1f); Kill();
        Check(momentum.KillChain == 1, "A kill after the chain window starts a fresh chain");
        Kill(); Kill();
        Set(save, "player", player); Set(save, "health", health); Set(save, "inventory", inventory);
        Call(save, "Restore", new GameProgressState { scene = "CombatReview", health = health.CaptureProgress(),
            inventory = inventory.CaptureProgress(), floorKeys = new int[4], tactics = playerNode.GetComponent<RogueTactics>().CaptureProgress() });
        Check(momentum.KillChain == 0 && !momentum.IsSurging, "Actual save restoration discards transient combat bonuses");
        Kill(); Kill(); Kill();
        player.Teleport(Vector3.zero, Quaternion.identity);
        Check(momentum.KillChain == 0 && !momentum.IsSurging && (float)Get(crosshair, "killRemaining") == 0,
            "Actual teleport clears combat bonuses and stale markers");
        Kill(); Kill(); Kill();
        health.respawnDelay = 20f; health.KillInstantly();
        Check(momentum.KillChain == 0 && !momentum.IsSurging, "Actual player death clears combat momentum");
        momentum.OnEnemyKilled(); Check(momentum.KillChain == 0, "A dead player cannot receive a surge");
        Time.timeScale = 1; health.ResetHealth(); if (health.respawnCanvas != null) health.respawnCanvas.SetActive(false);
        health.StopAllCoroutines();

        // Exercise the real spawner on an isolated, generated navigation mesh.
        var navSources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(40, .2f, 40), transform = Matrix4x4.Translate(new Vector3(0, -.1f, 0)), area = 0 } };
        NavMeshData navData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), navSources,
            new Bounds(Vector3.zero, new Vector3(50, 10, 50)), Vector3.zero, Quaternion.identity);
        Check(navData != null, "An isolated navigation surface is available for real spawn checks");
        NavMeshDataInstance navInstance = NavMesh.AddNavMeshData(navData);
        var roomNode = new GameObject("L1_CombatReviewRoom");
        var boundary = roomNode.AddComponent<DungeonRoomBoundary>(); boundary.halfSize = Vector2.one * 12;
        roomNode.AddComponent<DungeonMarker>().kind = "Room";
        var npc = Enemy("NPC Template", new Vector3(1000, 0, 1000), 100);
        var spawnerNode = new GameObject("Test Spawner"); spawnerNode.SetActive(false);
        var spawner = spawnerNode.AddComponent<NPCSpawner>(); spawner.npcPrefab = npc.gameObject;
        spawner.playerTarget = playerNode.transform; spawner.roomSpawnDelay = 0; spawner.spawnInterval = 2;
        spawner.maxNpcCount = 3; spawner.enemiesPerRoom = 3; spawner.continuousSpawningWhilePlayerInRoom = false;
        Time.timeScale = 0; spawnerNode.SetActive(true); yield return .2f;
        var spawned = (IList)Get(spawner, "spawnedNpcs");
        Check(spawned.Count == 0, "Even initial room activation cannot spawn enemies during pause");
        Time.timeScale = 1; yield return 1.6f;
        Check(spawned.Count == 3, "The room's three opening enemies arrive within the short opening wave");
        foreach (GameObject node in spawned)
            Check(boundary.Contains(node.transform.position) && Vector3.Distance(node.transform.position, playerNode.transform.position) >= 5,
                "Opening enemies remain in their room and respect the player safe distance");
        yield return .6f;
        Check(spawned.Count == 3, "The opening wave respects the alive cap and finite room quota");
        spawner.enabled = false; navInstance.Remove();
        Kill(); Kill(); Kill(); yield return .1f;

        var rt = new RenderTexture(1280, 720, 24); camera.targetTexture = rt;
        var overlays = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        foreach (Canvas canvas in overlays) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
        yield return .15f; Canvas.ForceUpdateCanvases();
        Text detail = (Text)Get(momentum, "detail"), title = (Text)Get(momentum, "title");
        Check(detail.preferredHeight <= detail.rectTransform.rect.height && title.preferredWidth <= title.rectTransform.rect.width &&
            title.preferredHeight <= title.rectTransform.rect.height,
            $"The full surge label and bonuses fit the scaled HUD ({detail.preferredHeight}/{detail.rectTransform.rect.height}, {title.preferredWidth}/{title.rectTransform.rect.width}, {title.preferredHeight}/{title.rectTransform.rect.height})");
        crosshair.NotifyHit(true); Call(crosshair, "Update");
        camera.Render(); RenderTexture before = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, "combat-hud.png"), image.EncodeToPNG());
        RenderTexture.active = before; camera.targetTexture = null;
        foreach (Canvas canvas in overlays) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        Object.DestroyImmediate(image); Object.DestroyImmediate(rt);
        Check(!File.Exists(Path.Combine(output, "unused-save.json")), "Combat checks never write the player's save");
        Check(runtimeErrors.Count == 0, "No unexpected gameplay errors: " + string.Join("\n", runtimeErrors));
    }
}
