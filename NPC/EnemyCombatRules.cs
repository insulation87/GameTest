using System;

public static class EnemyCombatRules
{
    public const int FirstRangedLevel = 21;
    public static float DamageAtLevel(int level) => 1f + (Math.Max(1, level) - 1) * 0.2f;
    public static bool AllowsRanged(int level) => level >= FirstRangedLevel;
}
