using UnityEngine;
using UnityEngine.AI;

public class BossSpawner : MonoBehaviour
{
    public GameObject bossPrefab;
    [Header("Boss Presence")]
    [Tooltip("每條血的生命值；首領共有三條血與三個型態。")]
    [Min(1f)] public float bossMaxHealth = 5000f;
    [Min(1f)] public float bossHeightMultiplier = 1.6f;
    [Min(1f)] public float bossWidthMultiplier = 1.3f;
    public bool HasSpawned { get; private set; }
    public BossController CurrentBoss { get; private set; }

    public void SpawnBoss()
    {
        TrySpawnBoss();
    }

    public bool TrySpawnBoss(bool respawnIfDefeated = false)
    {
        if (CurrentBoss != null)
        {
            EnemyHealth currentHealth = CurrentBoss.GetComponent<EnemyHealth>();
            if (currentHealth != null && !currentHealth.IsDead)
                return true;
        }
        if (HasSpawned && !respawnIfDefeated)
            return false;

        if (bossPrefab == null)
        {
            NPCSpawner npcSpawner = FindFirstObjectByType<NPCSpawner>();
            if (npcSpawner != null)
                bossPrefab = npcSpawner.npcPrefab;
        }

        if (bossPrefab == null || bossPrefab.GetComponent<EnemyHealth>() == null)
        {
            Debug.LogError("找不到包含生命值元件的首領或敵人預製物件。", this);
            return false;
        }

        GameObject boss = Instantiate(bossPrefab, transform.position, transform.rotation);
        boss.name = "Dungeon Boss";

        Player player = FindFirstObjectByType<Player>();
        EnemyHealth health = boss.GetComponent<EnemyHealth>();
        EnemyAI ai = boss.GetComponent<EnemyAI>();
        NavMeshAgent agent = boss.GetComponent<NavMeshAgent>();
        float bodyTop = EnlargeBody(boss, agent);

        if (health != null)
        {
            health.SetMaxHealth(bossMaxHealth);
            health.SetDungeonFloor(3);
            health.SetLevelAndExperience(100, 10);
            health.progressionDropChance = 0f;
        }

        EnemyHealthBarUI healthBar = boss.GetComponent<EnemyHealthBarUI>();
        if (healthBar != null)
        {
            healthBar.SetBossPresentation(bodyTop + 0.8f);
            healthBar.SetBossLevel(100);
        }

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
        if (healthBar != null) healthBar.SetBossPhases(controller);
        EnemyCharacterVisual.Ensure(boss);

        CurrentBoss = controller;
        GameAudio.UI(GameSound.BossSpawn);
        HasSpawned = true;
        return true;
    }

    float EnlargeBody(GameObject boss, NavMeshAgent agent)
    {
        float heightScale = Mathf.Max(1f, bossHeightMultiplier);
        float widthScale = Mathf.Max(1f, bossWidthMultiplier);
        float top = 2.2f;
        if (boss.TryGetComponent(out CapsuleCollider capsule) && capsule.direction == 1)
        {
            float feet = capsule.center.y - capsule.height * 0.5f;
            capsule.height *= heightScale;
            capsule.radius *= widthScale;
            Vector3 center = capsule.center;
            center.y = feet + capsule.height * 0.5f;
            capsule.center = center;
            top = feet + capsule.height;
        }
        else if (boss.TryGetComponent(out BoxCollider box))
        {
            float feet = box.center.y - box.size.y * 0.5f;
            box.size = Vector3.Scale(box.size, new Vector3(widthScale, heightScale, widthScale));
            Vector3 center = box.center;
            center.y = feet + box.size.y * 0.5f;
            box.center = center;
            top = feet + box.size.y;
        }
        if (agent != null)
        {
            agent.height *= heightScale;
            agent.radius *= widthScale;
        }
        return top;
    }
}
