using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Presentation only: never changes enemy health, navigation or collision settings.
[DisallowMultipleComponent]
public class EnemyCharacterVisual : MonoBehaviour
{
    [Tooltip("Optional appearance override. Leave empty to use the generated NPC/BOSS models.")]
    public GameObject modelPrefab;
    private GameObject model;
    private EnemyAI enemyAI;
    private EnemyHealth health;
    private BossController boss;
    private NavMeshAgent agent;
    private Animation animations;
    private SkinnedMeshRenderer face;
    private int blinkIndex = -1;
    private float blinkClock;
    private float actionUntil;
    private string currentClip;
    private GameObject sword, shield, crossbow;
    private Transform boltMuzzle;
    public Transform ProjectileOrigin => crossbow != null && crossbow.activeInHierarchy ? boltMuzzle : null;
    private readonly List<Renderer> placeholders = new List<Renderer>();

    public static void Ensure(GameObject enemy)
    {
        if (enemy.GetComponent<EnemyCharacterVisual>() == null)
            enemy.AddComponent<EnemyCharacterVisual>();
    }

    void Start()
    {
        enemyAI = GetComponent<EnemyAI>();
        health = GetComponent<EnemyHealth>();
        boss = GetComponent<BossController>();
        agent = GetComponent<NavMeshAgent>();
        int level = health != null ? health.EnemyLevel : 1;
        string appearance = boss != null ? "Boss" : level <= 20 ? "Npc1" : level <= 50 ? "Npc2" : "Npc3";
        GameObject prefab = modelPrefab != null ? modelPrefab :
            Resources.Load<GameObject>("Characters/Enemies/" + appearance);
        if (prefab == null)
        {
            Debug.LogError("Missing enemy appearance: " + appearance, this);
            enabled = false;
            return; // Keep the existing body visible if the asset is unavailable.
        }

        // Only replace the root placeholder. Do not hide attached weapons or UI.
        foreach (Renderer renderer in GetComponents<Renderer>())
            if (renderer.enabled) { placeholders.Add(renderer); renderer.enabled = false; }
        model = Instantiate(prefab, transform, false);
        model.name = boss != null ? "Boss Appearance" : "NPC Appearance";
        float height = 2f;
        float footHeight = agent != null ? -agent.baseOffset : -1f;
        if (TryGetComponent(out CapsuleCollider capsule) && capsule.direction == 1)
        {
            height = capsule.height;
            footHeight = capsule.center.y - height * 0.5f;
        }
        else if (TryGetComponent(out BoxCollider box))
        {
            height = box.size.y;
            footHeight = box.center.y - height * 0.5f;
        }
        model.transform.localPosition = Vector3.up * footHeight;
        model.transform.localScale *= height / 2f;
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = gameObject.layer;
            if (child.name == "Sword") sword = child.gameObject;
            if (child.name == "Shield") shield = child.gameObject;
            if (child.name == "Crossbow") crossbow = child.gameObject;
            if (child.name == "BoltMuzzle") boltMuzzle = child;
        }
        SetRangedWeapon(EnemyCombatRules.AllowsRanged(level) && boss == null);

        animations = model.GetComponentInChildren<Animation>();
        if (animations != null)
        {
            animations.playAutomatically = false;
            animations.cullingType = AnimationCullingType.BasedOnRenderers;
            foreach (AnimationState state in animations)
                state.wrapMode = state.name == "Attack" || state.name == "Cast" || state.name == "Shoot" ? WrapMode.ClampForever : WrapMode.Loop;
            Play("Idle");
        }
        foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            int index = renderer.sharedMesh.GetBlendShapeIndex("Blink");
            if (index < 0) continue;
            face = renderer; blinkIndex = index; break;
        }
        blinkClock = Mathf.Abs(GetInstanceID() % 420) * 0.01f;
        if (enemyAI != null) enemyAI.AttackPerformed += ShowAttack;
        if (boss != null) boss.BurstFired += ShowCast;
    }

    void Update()
    {
        if (model == null || Time.timeScale <= 0 || (health != null && health.IsDead)) return;
        if (Time.time >= actionUntil)
        {
            bool keepRange = enemyAI != null && enemyAI.CanUseRanged && boss == null &&
                (enemyAI.target == null || Vector3.Distance(transform.position, enemyAI.target.position) > enemyAI.stopRange);
            SetRangedWeapon(keepRange);
            float speed = agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0f;
            Play(speed < 0.1f ? "Idle" : speed > 2.5f ? "Run" : "Walk");
            if (animations != null && currentClip != "Idle" && animations[currentClip] != null)
                animations[currentClip].speed = Mathf.Clamp(speed / (currentClip == "Run" ? 4f : 1.8f), 0.7f, 1.6f);
        }
        if (face != null)
        {
            blinkClock = (blinkClock + Time.deltaTime) % 4.2f;
            face.SetBlendShapeWeight(blinkIndex, blinkClock < 0.18f ? Mathf.Sin(blinkClock / 0.18f * Mathf.PI) * 100f : 0f);
        }
    }

    void ShowAttack()
    {
        bool ranged = enemyAI != null && enemyAI.LastAttackWasRanged;
        SetRangedWeapon(ranged);
        ShowAction(ranged ? "Shoot" : "Attack");
    }
    void SetRangedWeapon(bool ranged)
    {
        if (sword != null) sword.SetActive(!ranged);
        if (shield != null) shield.SetActive(!ranged);
        if (crossbow != null) crossbow.SetActive(ranged);
    }
    void LateUpdate()
    {
        if (crossbow == null || !crossbow.activeInHierarchy || enemyAI == null || enemyAI.target == null) return;
        Vector3 direction = enemyAI.target.position - crossbow.transform.position;
        if (direction.sqrMagnitude > 0.01f) crossbow.transform.rotation = Quaternion.LookRotation(direction);
    }
    void ShowCast() { ShowAction("Cast"); }
    void ShowAction(string clip)
    {
        if (animations == null || animations[clip] == null) return;
        actionUntil = Time.time + animations[clip].length;
        currentClip = null;
        Play(clip);
    }
    void Play(string clip)
    {
        if (animations == null || currentClip == clip || animations[clip] == null) return;
        currentClip = clip;
        animations[clip].time = 0f;
        animations[clip].speed = 1f;
        animations.CrossFade(clip, 0.12f);
    }

    void OnDestroy()
    {
        if (enemyAI != null) enemyAI.AttackPerformed -= ShowAttack;
        if (boss != null) boss.BurstFired -= ShowCast;
        if (model != null) Destroy(model);
        foreach (Renderer renderer in placeholders) if (renderer != null) renderer.enabled = true;
    }
}
