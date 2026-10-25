using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Explicit entry point for an isolated Unity project; never run automatically in a user scene.
[InitializeOnLoad]
public static class EnemyRangedReview
{
    const string Pending = "EnemyRangedReview.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly Stack<IEnumerator> checks = new Stack<IEnumerator>();
    static double nextTick, timeout;
    static PlayerHealth victim;
    static EnemyHealth health;
    static EnemyAI enemy;
    static NavMeshAgent agent;
    static NavMeshDataInstance navMesh;
    static int attacks;
    static bool sawBolt;
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

    static EnemyRangedReview() { EditorApplication.playModeStateChanged += Changed; }

    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run EnemyRangedReview in an isolated batch-mode project.");
        EditorSceneManager.playModeStartScene = null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        results.Clear(); errors.Clear(); checks.Clear(); checks.Push(Checks());
        nextTick = EditorApplication.timeSinceStartup + .1;
        timeout = EditorApplication.timeSinceStartup + 60;
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }

    static void Log(string message, string stack, LogType type)
    {
        // A copied project's search index can fail inside the editor, independently of gameplay.
        if (stack.Contains("UnityEditor.Search")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + stack);
    }

    static void Tick()
    {
        sawBolt |= Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None).Length > 0;
        if (EditorApplication.timeSinceStartup < nextTick) return;
        try
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("NPC review timed out.");
            while (checks.Count > 0)
            {
                var current = checks.Peek();
                if (!current.MoveNext()) { checks.Pop(); continue; }
                if (current.Current is IEnumerator nested) { checks.Push(nested); continue; }
                nextTick = EditorApplication.timeSinceStartup + (current.Current is float delay ? delay : .02f);
                return;
            }
            Finish(0, "PASS");
        }
        catch (Exception ex) { Finish(1, "FAIL: " + ex); }
    }

    static void Finish(int code, string status)
    {
        Application.logMessageReceived -= Log;
        EditorApplication.update -= Tick;
        if (navMesh.valid) navMesh.Remove();
        File.WriteAllText(Path.Combine(Output, "npc-ranged-result.txt"),
            status + "\n" + string.Join("\n", results) + "\n" + string.Join("\n", errors));
        Debug.Log(status + ": " + results.Count + " NPC ranged checks");
        EditorApplication.Exit(code);
    }

    static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        results.Add(label); Debug.Log("PASS " + label);
    }

    static void Set(object component, string field, object value)
    {
        component.GetType().GetField(field, Private).SetValue(component, value);
    }

    static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
        node.name = name; node.transform.position = position; node.transform.localScale = size;
        return node;
    }

    static IEnumerator Reset(int level, float distance)
    {
        enemy.SetCombatPaused(true);
        foreach (var bolt in Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
            Object.Destroy(bolt.gameObject);
        agent.Warp(Vector3.zero);
        health.SetLevelAndExperience(level, 10);
        Set(enemy, "nextAttackTime", 0f);
        // Let the agent apply its base offset before placing the target at the same height.
        yield return .1f;
        victim.transform.position = enemy.transform.position + Vector3.forward * distance;
        victim.Heal(victim.maxHealth);
        Physics.SyncTransforms();
        attacks = 0; sawBolt = false;
        enemy.SetCombatPaused(false);
    }

    static IEnumerator Checks()
    {
        Time.timeScale = 1f;
        var presentation = Object.FindFirstObjectByType<PixelArtDirector>();
        if (presentation != null) presentation.enabled = false;
        Box("Review Ground", new Vector3(0, -.5f, 0), new Vector3(40, 1, 40));
        var source = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.TRS(new Vector3(0, -.5f, 0), Quaternion.identity, Vector3.one),
            size = new Vector3(40, 1, 40), area = 0
        };
        var settings = NavMesh.GetSettingsByIndex(0);
        var data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { source },
            new Bounds(Vector3.zero, new Vector3(42, 8, 42)), Vector3.zero, Quaternion.identity);
        Check(data != null, "Built real NavMesh for the NPC movement and attack loop");
        navMesh = NavMesh.AddNavMeshData(data);
        var room = new GameObject("Review Room").AddComponent<DungeonRoomBoundary>();
        room.halfSize = Vector2.one * 19f;

        var playerNode = Box("Review Player", new Vector3(0, 1, 8), new Vector3(1, 2, 1));
        playerNode.SetActive(false);
        victim = playerNode.AddComponent<PlayerHealth>();
        victim.startingMaxArmor = 0; victim.startingArmorRegeneration = 0;
        victim.pauseWorldWhileDead = false; victim.enabled = false;
        playerNode.SetActive(true);
        Set(victim, "currentArmor", 0f);

        var node = Box("Review NPC", new Vector3(0, 1, 0), new Vector3(1, 2, 1));
        node.SetActive(false);
        agent = node.AddComponent<NavMeshAgent>();
        agent.agentTypeID = settings.agentTypeID; agent.baseOffset = 1f; agent.radius = .4f;
        health = node.AddComponent<EnemyHealth>(); health.showDamageNumbers = false;
        health.SetLevelAndExperience(9, 10);
        enemy = node.AddComponent<EnemyAI>(); enemy.target = victim.transform;
        enemy.attackWindup = .05f; enemy.rangedInterval = enemy.damageInterval = 5f;
        enemy.AssignRoom(room);
        enemy.AttackPerformed += () => attacks++;
        node.SetActive(true);
        yield return .2f;
        Check(agent.isOnNavMesh, "NPC is active on the real NavMesh in Play Mode");
        Check(node.GetComponent<EnemyCharacterVisual>() != null, "Actual NPC appearance initialized");

        yield return Reset(9, 8f);
        yield return .8f;
        Check(!enemy.CanUseRanged && attacks == 0 && !sawBolt && Mathf.Approximately(victim.CurrentHealth, 100f),
            "Level 9 at range does not shoot or damage the player");

        foreach (int level in new[] { 10, 11 })
        {
            yield return Reset(level, 8f);
            yield return .9f;
            Check(enemy.CanUseRanged && enemy.LastAttackWasRanged && attacks == 1 && sawBolt,
                "Level " + level + " fires an actual crossbow projectile at range");
            Check(Mathf.Abs(victim.CurrentHealth - (100f - EnemyCombatRules.DamageAtLevel(level))) < .001f,
                "Level " + level + " projectile hits and deducts the correct health exactly once");
        }

        yield return Reset(10, 1.2f);
        yield return .6f;
        Check(attacks == 1 && !enemy.LastAttackWasRanged && !sawBolt && Mathf.Abs(victim.CurrentHealth - 97.2f) < .001f,
            "Level 10 switches to melee at close range and applies the correct damage" +
            $" (attacks={attacks}, ranged={enemy.LastAttackWasRanged}, bolt={sawBolt}, health={victim.CurrentHealth}, distance={Vector3.Distance(enemy.transform.position, victim.transform.position)})");

        yield return Reset(9, 1.2f);
        yield return .6f;
        Check(attacks == 1 && !enemy.LastAttackWasRanged && !sawBolt && Mathf.Abs(victim.CurrentHealth - 97.4f) < .001f,
            "Level 9 still performs melee attacks");

        var wall = Box("Review Occluder", new Vector3(0, 1.5f, 4), new Vector3(10, 3, .05f));
        yield return Reset(10, 8f);
        yield return .6f;
        Check(attacks == 0 && !sawBolt && Mathf.Approximately(victim.CurrentHealth, 100f),
            "An occluding wall prevents the level 10 NPC from shooting");
        Object.Destroy(wall);
        yield return Reset(10, 8f);
        yield return .9f;
        Check(attacks == 1 && sawBolt && Mathf.Abs(victim.CurrentHealth - 97.2f) < .001f,
            "Removing the wall allows shooting and projectile damage again");

        yield return Reset(10, 16f);
        yield return .2f;
        Check(attacks == 0 && !sawBolt && Mathf.Approximately(victim.CurrentHealth, 100f),
            "A target outside the 12-unit ranged limit is not shot");

        // Introduce a thin wall after the firing windup to exercise projectile collision itself.
        yield return Reset(10, 8f);
        yield return .15f;
        Check(sawBolt, "A bolt exists before the thin-wall collision check");
        enemy.SetCombatPaused(true);
        wall = Box("Projectile Blocker", new Vector3(0, 1.5f, 5), new Vector3(10, 3, .02f));
        Physics.SyncTransforms();
        yield return .6f;
        Check(Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None).Length == 0 &&
            Mathf.Approximately(victim.CurrentHealth, 100f), "An in-flight bolt hits a thin wall without damaging the player");
        Object.Destroy(wall);

        yield return Reset(9, 8f);
        yield return .15f;
        health.SetLevelAndExperience(10, 10);
        yield return .9f;
        Check(enemy.CombatLevel == 10 && attacks == 1 && sawBolt && Mathf.Abs(victim.CurrentHealth - 97.2f) < .001f,
            "Changing the NPC from level 9 to 10 during Play Mode unlocks shooting immediately");
        enemy.SetCombatPaused(true);
        Check(errors.Count == 0, "No gameplay runtime errors during the combat scenarios");
    }
}
