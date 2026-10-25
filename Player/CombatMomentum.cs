using UnityEngine;
using UnityEngine.UI;

/// <summary>Short, bounded combat rewards. Transient state is never written to a save.</summary>
[DisallowMultipleComponent]
public sealed class CombatMomentum : MonoBehaviour
{
    [Min(1)] public int killsToSurge = 3;
    [Min(.1f)] public float chainWindow = 5f;
    [Min(.1f)] public float surgeDuration = 6f;
    [Min(.1f)] public float maximumSurgeRemaining = 8f;
    public int KillChain { get; private set; }
    public float ChainRemaining { get; private set; }
    public float SurgeRemaining { get; private set; }
    public bool IsSurging => isActiveAndEnabled && SurgeRemaining > 0f;
    public float DamageMultiplier => IsSurging ? 1.15f : 1f;
    public float FireIntervalMultiplier => IsSurging ? .8f : 1f;
    public float MoveSpeedMultiplier => IsSurging ? 1.12f : 1f;

    PlayerHealth health;
    WeaponInventory inventory;
    CrosshairUI crosshair;
    Canvas hud;
    Text title, detail;
    Image fill;
    float nextHudRefresh;
    float nextContribution;
    // Shared rate limit for assists, effective ally healing, protection and crowd control.
    public void OnContribution()
    {
        if (CoopSession.IsClient || !CanAct || Time.time < nextContribution) return;
        nextContribution = Time.time + 2.5f;
        OnEnemyKilled();
    }

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        inventory = GetComponent<WeaponInventory>();
        crosshair = GetComponent<CrosshairUI>();
        if (crosshair == null) crosshair = gameObject.AddComponent<CrosshairUI>();
    }

    void Start() { BuildHud(); RefreshHud(); }

    bool CanAct => isActiveAndEnabled && health != null && !health.IsDead &&
        !health.IsChoosingUpgrade && Time.timeScale > 0f && !DungeonVictoryUI.IsOpen &&
        (inventory == null || (!inventory.IsOpen && !inventory.IsPaused));

    void Update()
    {
        if (hud != null) hud.enabled = CanAct;
        if (!CanAct) return;
        Advance(Time.deltaTime);
        if (Time.unscaledTime >= nextHudRefresh)
        {
            nextHudRefresh = Time.unscaledTime + .1f;
            RefreshHud();
        }
    }

    void Advance(float delta)
    {
        ChainRemaining = Mathf.Max(0f, ChainRemaining - delta);
        SurgeRemaining = Mathf.Max(0f, SurgeRemaining - delta);
        if (ChainRemaining <= 0f) KillChain = 0;
    }

    public void OnHit(bool killed)
    {
        if (!CanAct) return;
        if (crosshair != null) crosshair.NotifyHit(killed);
    }

    public void OnEnemyKilled()
    {
        if (!CanAct) return;
        KillChain = Mathf.Min(999, KillChain + 1);
        ChainRemaining = Mathf.Max(.1f, chainWindow);
        if (IsSurging)
            SurgeRemaining = Mathf.Min(Mathf.Max(.1f, maximumSurgeRemaining), SurgeRemaining + .75f);
        else if (KillChain >= Mathf.Max(1, killsToSurge))
        {
            SurgeRemaining = Mathf.Min(Mathf.Max(.1f, maximumSurgeRemaining), Mathf.Max(.1f, surgeDuration));
            GameAudio.UI(GameSound.LevelUp);
        }
        RefreshHud();
    }

    public void ResetCombat()
    {
        KillChain = 0;
        nextContribution = 0;
        ChainRemaining = SurgeRemaining = 0f;
        if (crosshair != null) crosshair.ResetFeedback();
        RefreshHud();
    }

    void BuildHud()
    {
        var node = new GameObject("Combat Momentum HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        node.transform.SetParent(transform, false);
        hud = node.GetComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 63;
        GameUITheme.FitCanvas(node.GetComponent<CanvasScaler>());
        var card = new GameObject("Momentum Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(node.transform, false);
        RectTransform rect = card.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(20, 145);
        rect.sizeDelta = new Vector2(430, 156);
        card.GetComponent<Image>().raycastTarget = false;
        GameUITheme.Surface(card);
        title = GameUITheme.Label(card.transform, "", 22, new Vector2(0, 50), new Vector2(398, 42));
        detail = GameUITheme.Label(card.transform, "", 17, new Vector2(0, -5), new Vector2(398, 64));
        var bar = new GameObject("Momentum Time", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(card.transform, false);
        bar.GetComponent<RectTransform>().sizeDelta = new Vector2(398, 5);
        bar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -64);
        bar.GetComponent<Image>().color = GameUITheme.Recess;
        bar.GetComponent<Image>().raycastTarget = false;
        var meter = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        meter.transform.SetParent(bar.transform, false);
        fill = meter.GetComponent<Image>();
        fill.raycastTarget = false;
        GameUITheme.Stretch(fill.rectTransform);
    }

    void RefreshHud()
    {
        if (title == null) return;
        title.text = IsSurging ? $"連殺狂潮  ×{KillChain}  ·  {SurgeRemaining:F1}s" :
            KillChain > 0 ? $"連殺  ×{KillChain}  ·  {ChainRemaining:F1}s" : "連殺狂潮 · 越戰越快";
        title.color = IsSurging ? GameUITheme.Gold : GameUITheme.Teal;
        detail.text = IsSurging ? "射速 +25% · 傷害 +15% · 移速 +12%\n擊殺延長狂潮，最多保留 8 秒" :
            $"擊殺、助攻、有效治療／保護／控場\n{chainWindow:0.#} 秒內累積 {killsToSurge} 次貢獻啟動狂潮";
        detail.color = GameUITheme.Muted;
        fill.color = title.color;
        float fraction = IsSurging ? SurgeRemaining / Mathf.Max(.1f, maximumSurgeRemaining) :
            ChainRemaining / Mathf.Max(.1f, chainWindow);
        fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
    }

    void OnDisable() { ResetCombat(); if (hud != null) hud.enabled = false; }
}
