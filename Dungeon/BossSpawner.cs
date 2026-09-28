using UnityEngine;
using UnityEngine.AI;

public class BossSpawner : MonoBehaviour
{
    public GameObject bossPrefab;
    public bool HasSpawned { get; private set; }

    public void SpawnBoss()
    {
        if (HasSpawned)
            return;

        if (bossPrefab == null)
        {
            NPCSpawner npcSpawner = FindFirstObjectByType<NPCSpawner>();
            if (npcSpawner != null)
                bossPrefab = npcSpawner.npcPrefab;
        }

        if (bossPrefab == null)
        {
            Debug.LogError("BossSpawner could not find a boss or NPC prefab.", this);
            return;
        }

        GameObject boss = Instantiate(bossPrefab, transform.position, transform.rotation);
        boss.name = "Dungeon Boss";

        Player player = FindFirstObjectByType<Player>();
        EnemyHealth health = boss.GetComponent<EnemyHealth>();
        EnemyAI ai = boss.GetComponent<EnemyAI>();
        NavMeshAgent agent = boss.GetComponent<NavMeshAgent>();

        if (health != null)
        {
            health.SetMaxHealth(1000f);
            health.SetDungeonFloor(3);
            health.SetLevelAndExperience(100, 10);
            health.progressionDropChance = 0f;
        }

        EnemyHealthBarUI healthBar = boss.GetComponent<EnemyHealthBarUI>();
        if (healthBar != null)
            healthBar.SetBossLevel(100);

        if (ai != null)
        {
            ai.target = player != null ? player.transform : null;
            ai.ConfigureLevel(100);
        }

        if (agent != null && player != null)
            agent.speed = player.RunSpeed * 0.9f;

        BossController controller = boss.GetComponent<BossController>();
        if (controller == null)
            controller = boss.AddComponent<BossController>();
        controller.Initialize(player, health, ai);
        EnemyCharacterVisual.Ensure(boss);

        HasSpawned = true;
    }
}
