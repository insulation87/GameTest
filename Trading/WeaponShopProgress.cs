using System;

/// <summary>Purchase count survives new runs. Matching equipment is always available for every role.</summary>
public static class WeaponShopProgress
{
    public const int InitialWeapons = 3;
    public const int PurchasesPerUnlock = 2;
    public static int RequiredPurchases(WeaponType type, string character)
    {
        if (!CharacterWeapons.IsCharacterWeapon(type)) return int.MaxValue;
        if (type == CharacterWeapons.ForCharacter(character)) return 0;
        int index = Array.IndexOf(CharacterWeapons.Types, type);
        return Math.Max(0, index - InitialWeapons + 1) * PurchasesPerUnlock;
    }
    public static bool IsUnlocked(WeaponType type, int purchases, string character) =>
        CharacterWeapons.IsCharacterWeapon(type) && purchases >= RequiredPurchases(type, character);
    public static int AfterPurchase(int purchases) => purchases == int.MaxValue ? int.MaxValue : Math.Max(0, purchases) + 1;
}
