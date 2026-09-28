using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    public Transform target;
    public float chaseRange = 100f;
    public float stopRange = 1.5f;
    [Min(0.05f)] public float damageInterval = 1f;
    [Min(0.1f)] public float rangedInterval = 1.8f;
    [Min(2f)] public float rangedRange = 12f;
    [Min(1f)] public float boltSpeed = 20f;
    [Min(0f)] public float attackWindup = 0.28f;
    [HideInInspector] public float contactDamage = 1f;
    private NavMeshAgent agent;
    private EnemyHealth health;
    private float nextAttackTime;
    private Coroutine attackRoutine;
    private bool initialized;
    [Header("Room Movement")]
    [SerializeField] private DungeonRoomBoundary homeRoom;
    private NavMeshPath roomPath;
    private Vector3 lastInsidePosition;
    private bool hasInsidePosition;
    private float nextPathTime;
    public DungeonRoomBoundary HomeRoom => homeRoom;
    public int CombatLevel { get; private set; } = 1;
    public float AttackDamage => EnemyCombatRules.DamageAtLevel(CombatLevel);
    public bool CanUseRanged => EnemyCombatRules.AllowsRanged(CombatLevel);
    public bool IsAttacking => attackRoutine != null;
    public bool CombatPaused { get; private set; }
    public void SetCombatPaused(bool paused)
    {
        if (CombatPaused == paused) return;
        CombatPaused = paused;
        if (!paused) return;
        if (attackRoutine != null) StopCoroutine(attackRoutine);
        attackRoutine = null;
        StopMoving();
    }
    public bool LastAttackWasRanged { get; private set; }
    public event System.Action AttackPerformed;
    public void ConfigureLevel(int level) { CombatLevel = Mathf.Max(1, level); contactDamage = AttackDamage; }
    void Start()
    {
        agent = GetComponent<NavMeshAgent>(); health = GetComponent<EnemyHealth>();
        AssignRoom(homeRoom != null ? homeRoom : DungeonRoomBoundary.FindContaining(transform.position, agent.radius));
        if (homeRoom == null)
            Debug.LogWarning("NPC has no room boundary and will remain stationary. Assign a DungeonRoomBoundary to enable movement.", this);
        if (health != null) ConfigureLevel(health.EnemyLevel);
        EnemyCharacterVisual.Ensure(gameObject); initialized = true;
    }
    void Update()
    {
        if (health != null && health.EnemyLevel != CombatLevel) ConfigureLevel(health.EnemyLevel);
        if (!CanNavigate()) return;
        if (CombatPaused) { StopMoving(); return; }
        if (target == null || homeRoom == null || (health != null && health.IsDead)) { StopMoving(); return; }
        PlayerHealth victim = target.GetComponentInParent<PlayerHealth>();
        float distance = Vector3.Distance(transform.position, target.position);
        if (victim == null || victim.IsDead || distance > chaseRange) { StopMoving(); return; }
        Vector3 facing = target.position - transform.position; facing.y = 0;
        if (IsAttacking || distance <= rangedRange)
            if (facing.sqrMagnitude > 0.001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), 540f * Time.deltaTime);
        if (IsAttacking) { StopMoving(); return; }
        bool melee = distance <= stopRange;
        bool ranged = !melee && CanUseRanged && distance <= rangedRange;
        if ((melee || ranged) && HasLineOfSight(victim, AttackOrigin)) { StopMoving(); TryBeginAttack(victim, ranged); }
        else
        {
            MoveWithinRoom(target.position);
        }
    }

    public void AssignRoom(DungeonRoomBoundary room)
    {
        homeRoom = room;
        if (roomPath == null) roomPath = new NavMeshPath();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        // A later automatic re-path must not bypass our full-path validation.
        agent.autoRepath = false;
        agent.autoTraverseOffMeshLink = false;
        hasInsidePosition = room != null && room.Contains(transform.position, agent.radius);
        if (hasInsidePosition) lastInsidePosition = transform.position;
        nextPathTime = 0f;
        StopMoving();
    }

    void MoveWithinRoom(Vector3 destination)
    {
        if (!homeRoom.Contains(destination)) { StopMoving(); return; }
        if (Time.time < nextPathTime) return;
        nextPathTime = Time.time + .2f;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (!homeRoom.TryGetNavMeshPoint(destination, agent.radius, filter, out Vector3 point) ||
            !agent.CalculatePath(point, roomPath) || !homeRoom.ContainsPath(roomPath, agent.radius))
        {
            StopMoving();
            return;
        }
        agent.stoppingDistance = Mathf.Max(.05f, stopRange * .8f);
        agent.isStopped = false;
        agent.SetPath(roomPath);
    }

    void LateUpdate()
    {
        if (homeRoom == null || agent == null || !agent.enabled) return;
        if (homeRoom.Contains(transform.position, agent.radius))
        {
            if (CanNavigate()) { lastInsidePosition = transform.position; hasInsidePosition = true; }
            return;
        }
        // Keep avoidance or an external push from carrying the NPC through a doorway.
        StopMoving();
        Vector3 safePosition = lastInsidePosition;
        if (!hasInsidePosition)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!homeRoom.TryGetNavMeshPoint(homeRoom.transform.position, agent.radius, filter, out safePosition)) return;
        }
        if (agent.Warp(safePosition))
        {
            agent.velocity = Vector3.zero;
            lastInsidePosition = transform.position;
            hasInsidePosition = true;
        }
    }
    bool CanNavigate() => agent != null && agent.enabled && agent.isOnNavMesh;
    void StopMoving() { if (CanNavigate()) { agent.isStopped = true; if (agent.hasPath) agent.ResetPath(); } }
    public Vector3 AttackOrigin
    {
        get
        {
            EnemyCharacterVisual visual = GetComponent<EnemyCharacterVisual>();
            if (visual != null && visual.ProjectileOrigin != null) return visual.ProjectileOrigin.position;
            if (TryGetComponent(out Collider body)) return body.bounds.center + Vector3.up * body.bounds.extents.y * 0.35f;
            return transform.position + Vector3.up * 0.4f;
        }
    }
    static Vector3 AimPoint(PlayerHealth victim)
    {
        Collider body = victim.GetComponent<Collider>();
        return body != null ? body.bounds.center : victim.transform.position;
    }
    bool HasLineOfSight(PlayerHealth victim, Vector3 origin)
    {
        Vector3 offset = AimPoint(victim) - origin;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<PlayerHealth>() == victim) continue;
            return false;
        }
        return true;
    }
    void TryBeginAttack(PlayerHealth victim, bool ranged)
    {
        if (!initialized || CombatPaused || IsAttacking || Time.time < nextAttackTime || victim == null || victim.IsDead || (health != null && health.IsDead)) return;
        LastAttackWasRanged = ranged;
        GameAudio.At(ranged ? GameSound.Crossbow : GameSound.Sword, transform.position);
        nextAttackTime = Time.time + Mathf.Max(attackWindup + 0.15f, ranged ? rangedInterval : damageInterval);
        AttackPerformed?.Invoke(); attackRoutine = StartCoroutine(ResolveAttack(victim, ranged));
    }
    IEnumerator ResolveAttack(PlayerHealth victim, bool ranged)
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, attackWindup));
        if (victim != null && !victim.IsDead && (health == null || !health.IsDead))
        {
            float distance = Vector3.Distance(transform.position, victim.transform.position);
            Vector3 origin = AttackOrigin;
            if (distance <= (ranged ? rangedRange : stopRange + 0.2f) && HasLineOfSight(victim, origin))
            {
                if (ranged && CanUseRanged) EnemyProjectile.Launch(origin, AimPoint(victim) - origin, boltSpeed, AttackDamage, 4f, gameObject);
                else if (!ranged) victim.TakeDamage(AttackDamage);
            }
        }
        yield return new WaitForSeconds(0.3f); attackRoutine = null;
    }
    void OnCollisionStay(Collision collision) { TryContactAttack(collision.collider); }
    void OnTriggerStay(Collider other) { TryContactAttack(other); }
    void TryContactAttack(Collider other)
    {
        if (!enabled || other == null) return;
        PlayerHealth victim = other.GetComponentInParent<PlayerHealth>();
        if (victim != null && Vector3.Distance(transform.position, victim.transform.position) <= stopRange + 0.2f && HasLineOfSight(victim, AttackOrigin))
            TryBeginAttack(victim, false);
    }
    void OnDisable() { if (attackRoutine != null) StopCoroutine(attackRoutine); attackRoutine = null; }
    void OnValidate()
    {
        chaseRange = Mathf.Max(0f, chaseRange); stopRange = Mathf.Clamp(stopRange, 0.1f, Mathf.Max(0.1f, chaseRange));
        damageInterval = Mathf.Max(0.05f, damageInterval); rangedInterval = Mathf.Max(0.1f, rangedInterval);
        rangedRange = Mathf.Max(stopRange + 1f, rangedRange); boltSpeed = Mathf.Max(1f, boltSpeed); attackWindup = Mathf.Max(0f, attackWindup);
    }
}
