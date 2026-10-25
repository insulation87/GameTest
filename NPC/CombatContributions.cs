using System.Collections.Generic;
using UnityEngine;

public partial class EnemyHealth
{
    readonly Dictionary<PlayerHealth, float> contributors = new Dictionary<PlayerHealth, float>();
    void TrackContributor(PlayerHealth player) => contributors[player] = Time.time;
    void RewardContributors(PlayerHealth killer)
    {
        foreach (var pair in contributors)
            if (pair.Key != null && pair.Key != killer && !pair.Key.IsDead && Time.time - pair.Value <= 8f)
                pair.Key.GetComponent<CombatMomentum>()?.OnContribution();
        contributors.Clear();
    }
}
