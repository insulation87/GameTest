using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One ability per run. Buffs and deployments are transient; cooldown is saved.</summary>
[DisallowMultipleComponent]
public sealed partial class CharacterSkills : MonoBehaviour
{
    public KeyCode skillKey = KeyCode.R;
    CharacterDefinition character;
    public CharacterDefinition Character
    {
        get { if (character == null) character = CharacterRoster.ConsumeSelection(); return character; }
        private set { character = value; }
    }
    public float CooldownRemaining { get; private set; }
    public float EffectRemaining { get; private set; }
    public float FireIntervalMultiplier => Active("gunner") ? .45f / SkillPower : 1f;
    public float DamageMultiplier => Active("assassin") ? 1f + .5f * SkillPower : 1f;
    public float MoveSpeedMultiplier => Active("assassin") ? 1f + .75f * SkillPower : 1f;
    public float IncomingDamageMultiplier => Active("sentinel") ? Mathf.Max(.1f, .25f / SkillPower) :
        Active("assassin") && EffectRemaining > 3f * SkillDuration ? 0f : 1f;
    PlayerHealth health;
    WeaponInventory inventory;
    readonly List<EnemyHealth> targets = new List<EnemyHealth>(128);
    Vector3 deploymentPosition;
    public bool InsideOwnFire => Active("pyromancer") && (transform.position - deploymentPosition).sqrMagnitude <= 25f;
    void HealTeam(float fraction)
    {
        fraction *= SkillPower;
        health.Heal(health.maxHealth * fraction);
        foreach (var teammate in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (teammate == health || teammate.IsDead || (teammate.transform.position - transform.position).sqrMagnitude > 64f ||
                !TacticalField.Clear(AimOrigin, teammate.transform.position + Vector3.up, null, health)) continue;
            float before = teammate.CurrentHealth;
            teammate.Heal(teammate.maxHealth * fraction);
            if (teammate.CurrentHealth > before) GetComponent<CombatMomentum>()?.OnContribution();
        }
    }
    float pulseRemaining, healingRemaining, nextHud;
    GameObject deployment;
    Canvas hud;
    Text title, status;
    Image fill;

    void Awake()
    {
        _ = Character;
        health = GetComponent<PlayerHealth>();
        inventory = GetComponent<WeaponInventory>();
    }
    void Start() { health = GetComponent<PlayerHealth>(); inventory = GetComponent<WeaponInventory>(); BuildHud(); RefreshHud(); }
    bool Active(string id) => isActiveAndEnabled && health != null && !health.IsDead &&
        Character.Id == id && EffectRemaining > 0f;
    bool CanAct => isActiveAndEnabled && health != null && !health.IsDead && !health.IsChoosingUpgrade &&
        Time.timeScale > 0f && !DungeonVictoryUI.IsOpen &&
        (inventory == null || (!inventory.IsOpen && !inventory.IsPaused));
    void Update()
    {
        if (CoopSession.IsClient) { RefreshHud(); return; }
        if (hud != null) hud.enabled = CanAct;
        if (!CanAct) return;
        Advance(Time.deltaTime);
        if (CoopInput.Down(this, CoopButton.Skill, skillKey)) TryUseSkill();
        if (Time.unscaledTime >= nextHud) { nextHud = Time.unscaledTime + .1f; RefreshHud(); }
    }
    void Advance(float delta)
    {
        CooldownRemaining = Mathf.Max(0f, CooldownRemaining - delta);
        float activeDelta = Mathf.Min(delta, EffectRemaining);
        if (activeDelta > 0f)
        {
            if (Character.Id == "medic")
            {
                // Batch small heals so the healing sound does not play every frame.
                healingRemaining += activeDelta;
                if (healingRemaining >= .5f || delta >= EffectRemaining)
                { HealTeam(.03f * healingRemaining); healingRemaining = 0f; }
            }
            if (Character.Id == "pyromancer" || Character.Id == "engineer")
            {
                pulseRemaining -= activeDelta;
                if (pulseRemaining <= 0f)
                {
                    // At most one pulse per frame: no catch-up burst after a hitch.
                    pulseRemaining = Character.Id == "engineer" ? .4f : .5f;
                    PulseDeployment();
                }
            }
        }
        EffectRemaining = Mathf.Max(0f, EffectRemaining - delta);
        if (EffectRemaining <= 0f) DestroyDeployment();
    }
    public bool TryUseSkill()
    {
        if (!CoopSession.Simulates(this)) return false;
        if (!CanAct || CooldownRemaining > 0f) return false;
        CooldownRemaining = SkillCooldown;
        float power = health.AttackMultiplier * SkillPower;
        Vector3 origin = AimOrigin;
        switch (Character.Id)
        {
            case "breaker": AreaDamage(origin, 7f * SkillRange, 80f * power, false, false); break;
            case "sentinel": health.RestoreSkillArmor(health.MaxArmor * .2f * SkillPower); EffectRemaining = 5f * SkillDuration;
                TacticalField.Create(TacticalField.Kind.Ward, transform.position, health, 4, EffectRemaining, 0); break;
            case "gunner": EffectRemaining = 5f * SkillDuration; break;
            case "medic": HealTeam(.35f); healingRemaining = 0f; EffectRemaining = 5f * SkillDuration; break;
            case "assassin": EffectRemaining = 4f * SkillDuration; break;
            case "pyromancer": Deploy(8f * SkillDuration, false); break;
            case "engineer": Deploy(10f * SkillDuration, true); break;
            case "frost": AreaDamage(origin, 9f * SkillRange, 35f * power, true, false); break;
            case "reaper": AreaDamage(origin, 6f * SkillRange, 50f * power, false, true); break;
            case "ranger": FireBeam(power); break;
        }
        GameAudio.UI(GameSound.LevelUp);
        SkillTrace.Ring(transform.position + Vector3.up * .15f, Character.Color,
            (Character.Id == "breaker" ? 7f : Character.Id == "frost" ? 9f : Character.Id == "reaper" ? 6f : 1.3f) * SkillRange, .45f);
        RefreshHud();
        return true;
    }
    Camera AimCamera
    {
        get
        {
            var shooter = GetComponent<PlayerShooter>();
            if (shooter != null)
            {
                if (shooter.fpsCamera != null && shooter.fpsCamera.gameObject.activeInHierarchy) return shooter.fpsCamera;
                if (shooter.tpsCamera != null && shooter.tpsCamera.gameObject.activeInHierarchy) return shooter.tpsCamera;
            }
            return Camera.main;
        }
    }
    Vector3 AimOrigin => GetComponent<Collider>() is Collider body ? body.bounds.center : transform.position + Vector3.up;
    static Vector3 TargetPoint(EnemyHealth enemy) => enemy.TryGetComponent(out Collider body) ? body.bounds.center : enemy.transform.position + Vector3.up * .5f;
    bool Visible(Vector3 origin, EnemyHealth enemy)
    {
        Vector3 offset = TargetPoint(enemy) - origin;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(enemy.transform) ||
                hit.collider.GetComponentInParent<EnemyHealth>() != null ||
                hit.collider.GetComponentInParent<SkillTrace>() != null) continue;
            return false;
        }
        return true;
    }
    void Collect(Vector3 origin, float radius)
    {
        targets.Clear();
        // Snapshot before damage: deaths remove themselves from the registry.
        foreach (var enemy in EnemyHealth.ActiveEnemies)
            if (enemy != null && !enemy.IsDead && (TargetPoint(enemy) - origin).sqrMagnitude <= radius * radius && Visible(origin, enemy))
                targets.Add(enemy);
    }
    void AreaDamage(Vector3 origin, float radius, float damage, bool freeze, bool reap)
    {
        Collect(origin, radius);
        int kills = 0;
        foreach (var enemy in targets)
        {
            if (enemy == null || enemy.IsDead) continue;
            var boss = enemy.GetComponent<BossController>();
            if (boss != null && boss.IsTransforming) continue;
            bool execute = reap && boss == null && enemy.HealthNormalized <= .3f;
            enemy.TakeDamage(execute ? enemy.CurrentHealth : damage, health, origin);
            if (enemy.IsDead) kills++;
            else if (freeze && (boss == null || !boss.IsTransforming))
            {
                float duration = boss != null ? .5f : 3f;
                enemy.GetComponent<EnemyAI>()?.FreezeFor(duration);
                var setup = enemy.GetComponent<WeaponComboStatus>() ?? enemy.gameObject.AddComponent<WeaponComboStatus>();
                setup.Hit(health,WeaponType.FrostStaff);
                health.GetComponent<CombatMomentum>()?.OnContribution();
                SkillTrace.Ring(enemy.transform.position + Vector3.up * .1f,Character.Color,.65f,duration);
            }
        }
        if (reap && kills > 0) health.Heal(health.maxHealth * .1f * SkillPower * Mathf.Min(3, kills));
    }
    void Deploy(float duration, bool turret)
    {
        DestroyDeployment(); EffectRemaining = duration; pulseRemaining = 0f;
        deploymentPosition = AimOrigin;
        deployment = SkillTrace.Ring(transform.position + Vector3.up * .15f, Character.Color, turret ? .6f : 5f, duration);
        if (turret)
        {
            var model = ItemAppearance.Create((int)WeaponType.ArcaneFocus, deployment.transform);
            model.transform.localPosition = Vector3.up * .7f;
            model.transform.localScale = Vector3.one * .6f;
        }
    }
    void PulseDeployment()
    {
        if (Character.Id == "pyromancer") { AreaDamage(deploymentPosition, 5f, 18f * health.AttackMultiplier * SkillPower, false, false); return; }
        Collect(deploymentPosition, 16f);
        EnemyHealth nearest = null; float distance = float.PositiveInfinity;
        foreach (var enemy in targets)
        {
            var boss = enemy.GetComponent<BossController>();
            if (boss != null && boss.IsTransforming) continue;
            float square = (TargetPoint(enemy) - deploymentPosition).sqrMagnitude;
            if (square < distance) { nearest = enemy; distance = square; }
        }
        if (nearest == null) return;
        SkillTrace.Line(deploymentPosition, TargetPoint(nearest), Character.Color, .12f);
        nearest.TakeDamage(35f * health.AttackMultiplier * SkillPower, health, deploymentPosition);
    }
    void FireBeam(float power)
    {
        Camera camera = AimCamera;
        Vector3 origin = camera != null ? camera.transform.position : AimOrigin;
        Vector3 forward = camera != null ? camera.transform.forward : transform.forward;
        float length = 25f * SkillRange;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, forward, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<EnemyHealth>() == null)
                length = Mathf.Min(length, hit.distance);
        Collect(origin, 27f * SkillRange);
        foreach (var enemy in targets)
        {
            Vector3 offset = TargetPoint(enemy) - origin;
            float projection = Vector3.Dot(offset, forward);
            if (projection >= 0f && projection <= length && (offset - forward * projection).sqrMagnitude <= 1.44f * SkillRange * SkillRange)
                enemy.TakeDamage(180f * power, health, origin);
        }
        SkillTrace.Line(origin + forward * .4f, origin + forward * length, Character.Color, .35f);
    }
    public CharacterProgress CaptureProgress() => new CharacterProgress { version = 1, id = Character.Id, cooldown = CooldownRemaining,
        skillPower = skillPower, skillHaste = skillHaste, skillMastery = skillMastery };
    public void RestoreProgress(CharacterProgress data)
    {
        CancelEffects();
        Character = CharacterRoster.Find(data != null && data.version == 1 ? data.id : null);
        RestoreSkillUpgrades(data != null && data.version == 1 ? data : null);
        GetComponent<AnimeCharacterVisual>()?.RefreshCharacterStyle(Character);
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == "Pixel First Person Arms") PixelCharacterFactory.ApplyArmStyle(child,Character);
        float value = data != null && data.version == 1 ? data.cooldown : 0f;
        CooldownRemaining = float.IsNaN(value) || float.IsInfinity(value) ? SkillCooldown : Mathf.Clamp(value, 0f, SkillCooldown);
        RefreshHud();
    }
    public void CancelEffects()
    { EffectRemaining = healingRemaining = pulseRemaining = 0f; DestroyDeployment(); RefreshHud(); }
    void DestroyDeployment() { if (deployment != null) Destroy(deployment); deployment = null; }
    void BuildHud()
    {
        var node = new GameObject("Character Skill HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        node.transform.SetParent(transform, false); hud = node.GetComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay; hud.sortingOrder = 63;
        GameUITheme.FitCanvas(node.GetComponent<CanvasScaler>());
        var card = AccountLoginPanel.Box(node.transform, "Skill", Vector2.zero, new Vector2(400,116), GameUITheme.Panel);
        card.anchorMin = card.anchorMax = card.pivot = Vector2.right;
        card.anchoredPosition = new Vector2(-20,145); card.GetComponent<Image>().raycastTarget = false;
        GameUITheme.Frame(card);
        title = GameUITheme.Label(card, "", 22, new Vector2(0,30), new Vector2(368,38));
        status = GameUITheme.Label(card, "", 18, new Vector2(0,-10), new Vector2(368,36));
        var bar = AccountLoginPanel.Box(card,"Cooldown",new Vector2(0,-44), new Vector2(368,6),GameUITheme.Recess);
        bar.GetComponent<Image>().raycastTarget = false;
        fill = AccountLoginPanel.Box(bar,"Fill",Vector2.zero,Vector2.zero,Character.Color).GetComponent<Image>();
        fill.raycastTarget = false; GameUITheme.Stretch(fill.rectTransform);
    }
    void RefreshHud()
    {
        if (title == null) return;
        title.text = Character.Name + " · " + Character.Skill; title.color = Character.Color;
        status.text = EffectRemaining > 0f ? $"效果剩餘 {EffectRemaining:F1}s · 冷卻 {CooldownRemaining:F1}s" :
            CooldownRemaining > 0f ? $"[{GameControls.Label(this, GameAction.Skill)}] 冷卻中 {CooldownRemaining:F1}s" : $"[{GameControls.Label(this, GameAction.Skill)}] 施放技能 · 已就緒";
        fill.color = Character.Color;
        fill.rectTransform.anchorMax = new Vector2(1f - Mathf.Clamp01(CooldownRemaining / SkillCooldown),1f);
    }
    void OnDisable() { CancelEffects(); if (hud != null) hud.enabled = false; }
}

public partial class PlayerHealth
{
    public void RestoreSkillArmor(float amount) => currentArmor = Mathf.Clamp(currentArmor + Mathf.Max(0f,amount),0f,maxArmor);
}

/// <summary>Short-lived shared-material skill traces; no colliders and no gameplay callbacks.</summary>
public sealed class SkillTrace : MonoBehaviour
{
    static Material material;
    float remaining;
    static LineRenderer Create(string name, Color color, float duration)
    {
        if (material == null) material = new Material(Shader.Find("Sprites/Default"));
        var node = new GameObject(name,typeof(LineRenderer),typeof(SkillTrace));
        node.GetComponent<SkillTrace>().remaining = duration;
        var line = node.GetComponent<LineRenderer>(); line.sharedMaterial = material;
        line.startColor = line.endColor = color; line.startWidth = line.endWidth = .08f;
        line.useWorldSpace = true; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }
    public static GameObject Ring(Vector3 position, Color color, float radius, float duration)
    {
        var line = Create("Skill Area",color,duration); line.transform.position = position;
        line.positionCount = 48; line.loop = true;
        for (int i = 0; i < 48; i++)
        { float a = i * Mathf.PI * 2f / 48; line.SetPosition(i,position + new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)) * radius); }
        return line.gameObject;
    }
    public static void Line(Vector3 from, Vector3 to, Color color, float duration)
    { var line = Create("Skill Beam",color,duration); line.positionCount = 2; line.SetPosition(0,from); line.SetPosition(1,to); }
    void Update() { if (Time.timeScale <= 0f) return; remaining -= Time.deltaTime; if (remaining <= 0f) Destroy(gameObject); }
}
