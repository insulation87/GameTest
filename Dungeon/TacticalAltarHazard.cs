using UnityEngine;

// The optional ancient-forest altar removes this extra pressure from the final fight.
public sealed class TacticalAltarHazard : MonoBehaviour
{
    EnemyHealth health;
    float next = 5;
    void Awake() { health = GetComponent<EnemyHealth>(); next = Time.time + 5; }
    void Update()
    {
        if (CoopSession.IsClient || Time.timeScale <= 0 || DungeonVictoryUI.IsOpen || health == null || health.IsDead || Time.time < next) return;
        next = Time.time + 12;
        if (TacticalExpedition.Instance != null && TacticalExpedition.Instance.Progress.altarDestroyed) { Destroy(this); return; }
        foreach (var player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
            if (!player.IsDead && (player.transform.position - transform.position).sqrMagnitude < 625)
            { BossFlameTrail.Create(player.transform.position, health, 4); player.GetComponent<RogueTactics>()?.ShowNotice("魔王祭壇仍在運作 · 持續移動避開腳下火焰"); break; }
    }
}
