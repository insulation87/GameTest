using UnityEngine;
using MedievalDungeonKit;

[RequireComponent(typeof(PlayerHealth))]
public class FallDeathZone : MonoBehaviour
{
    [Tooltip("How far below the dungeon's lowest collider the player dies.")]
    [Min(0.1f)] public float distanceBelowMap = 10f;
    public float DeathHeight { get; private set; }

    private PlayerHealth playerHealth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachToPlayer()
    {
        Player player = FindFirstObjectByType<Player>();
        if (player != null && player.GetComponent<FallDeathZone>() == null)
            player.gameObject.AddComponent<FallDeathZone>();
    }

    void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        CalculateDeathHeight();
    }

    void Update()
    {
        if (playerHealth != null && !playerHealth.IsDead && transform.position.y <= DeathHeight)
            playerHealth.KillInstantly();
    }

    void CalculateDeathHeight()
    {
        DungeonViewSettings dungeon = FindFirstObjectByType<DungeonViewSettings>();
        if (dungeon == null)
        {
            DeathHeight = transform.position.y - 30f;
            return;
        }

        Collider[] colliders = dungeon.GetComponentsInChildren<Collider>(true);
        bool foundCollider = false;
        float lowestPoint = float.PositiveInfinity;

        foreach (Collider dungeonCollider in colliders)
        {
            if (dungeonCollider == null || dungeonCollider.isTrigger)
                continue;

            lowestPoint = Mathf.Min(lowestPoint, dungeonCollider.bounds.min.y);
            foundCollider = true;
        }

        DeathHeight = foundCollider
            ? lowestPoint - distanceBelowMap
            : dungeon.transform.position.y - 30f;
    }

    void OnValidate()
    {
        distanceBelowMap = Mathf.Max(0.1f, distanceBelowMap);
    }
}
