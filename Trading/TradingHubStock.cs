using System;
using System.IO;
using UnityEngine;

public enum HubMerchant { Weapons, Armor, Supplies }
public enum HubGoods { Weapon, Armor, HealthPack, Backpack }

public sealed class HubOffer
{
    public readonly string Name;
    public readonly int Price, Item;
    public readonly HubGoods Goods;
    public HubOffer(string name, int price, HubGoods goods, int item = 0)
    { Name = name; Price = price; Goods = goods; Item = item; }
}

/// <summary>Trades commit the complete map save atomically, retaining position and world progress.</summary>
public sealed class TradingHubStock
{
    public static readonly HubOffer[][] Catalog = {
        Array.ConvertAll(CharacterWeapons.Types, type => new HubOffer(CharacterWeapons.Name(type),
            CharacterWeapons.ShopPrice(type), HubGoods.Weapon, (int)type)),
        new[] {
            new HubOffer("頭盔", 100, HubGoods.Armor, (int)ArmorSlot.Helmet),
            new HubOffer("盔甲", 100, HubGoods.Armor, (int)ArmorSlot.Chest),
            new HubOffer("褲子", 100, HubGoods.Armor, (int)ArmorSlot.Pants),
            new HubOffer("鞋子", 100, HubGoods.Armor, (int)ArmorSlot.Shoes) },
        new[] {
            new HubOffer("血包 ×1（恢復 25 生命）", 30, HubGoods.HealthPack),
            new HubOffer("背包擴充 +5 格", 120, HubGoods.Backpack) }
    };
    readonly string path;
    readonly bool fresh;
    GameProgressState saved;
    InventoryProgress preview;
    public string CharacterId { get; private set; }
    public string Destination { get; private set; }
    public bool IsPreparing => saved != null && saved.preparingRun;
    public bool HasCampCharacter => saved != null && (!IsPreparing || saved.campCharacterSelected);
    public bool HasCampUpgrade => saved != null && (!IsPreparing || saved.campUpgradeSelected);
    public bool RequiresCampPreparation => IsPreparing && (!HasCampCharacter || !HasCampUpgrade);
    public bool CanTrade => saved != null;
    public string Notice { get; private set; }
    public InventoryProgress Inventory => saved != null ? saved.inventory : preview;
    public int ItemLevel => saved != null && saved.preparingRun ? 10 : Mathf.Clamp((1 + (saved?.health.level ?? 1) / 20) * 10, 1, 100);
    public int UsedSlots => CountSlots(Inventory);
    public int WeaponPurchases => saved?.weaponPurchases ?? 0;
    public bool IsUnlocked(HubOffer offer) => offer.Goods != HubGoods.Weapon ||
        WeaponShopProgress.IsUnlocked((WeaponType)offer.Item, WeaponPurchases, CharacterId);

    public TradingHubStock(string storagePath, bool startFresh, string newCharacter, string destination = "Map1")
    {
        path = storagePath; Destination = ExpeditionMaps.ForScene(destination).Scene; destination = Destination;
        bool hasFiles = File.Exists(path) || File.Exists(path + ".bak");
        bool valid = GameProgressSave.TryRead(path, out var data, out _);
        fresh = startFresh || valid && data.runEnded || !hasFiles;
        CharacterId = !fresh && data?.character != null && CharacterRoster.IsKnown(data.character.id) ? data.character.id : CharacterRoster.Find(newCharacter).Id;
        preview = new InventoryProgress {
            weapons = new[] { CharacterWeapons.Create(CharacterWeapons.ForCharacter(CharacterId), 1, 0) },
            slots = new[] { 0, -1, -1 }, keys = new int[4], armorItems = new ArmorItem[0],
            armorSlots = new[] { -1, -1, -1, -1 }, capacity = 20, healAmount = 25,
            gold = valid ? Mathf.Max(0, data.inventory.gold) : 0,
            weaponPurchases = valid ? data.weaponPurchases : 0
        };
        if (fresh)
        {
            // A preparation checkpoint is atomic with the wallet/unlocks. It can be resumed after
            // leaving the camp or quitting, but its previous map/health are never restored.
            var preparation = valid ? data : new GameProgressState {
                health = new PlayerProgress { health = 100, maxHealth = 100, armor = 25, maxArmor = 25,
                    armorRegeneration = 2, level = 1, attack = 1, speed = 1, criticalChance = .1f, criticalDamage = 1.5f },
                floorKeys = new int[4]
            };
            preparation.scene = destination; preparation.inventory = preview;
            preparation.preparingRun = true; preparation.runEnded = false;
            preparation.campCharacterSelected = preparation.campUpgradeSelected = false;
            preparation.character = new CharacterProgress { version = 1, id = CharacterId };
            preparation.savedUtc = DateTime.UtcNow.ToString("O");
            if (GameProgressSave.TryWrite(path, preparation, out _)) saved = preparation;
            Notice = saved != null ? "新一輪裝備已重置；保留金幣與武器解鎖。營地購物會帶入這一輪冒險。" :
                "無法儲存新一輪準備，已暫停交易；請確認磁碟空間與檔案權限。";
        }
        else if (valid)
        {
            if (data.scene == destination) saved = data;
            Notice = saved != null ? "交易成功後會立即儲存。金幣／解鎖跨輪保留，武器僅屬於本輪。" : "存檔屬於其他地圖，營地暫停交易以保留原進度。";
        }
        else Notice = "存檔無法讀取，已保留原檔；請回主選單處理，營地暫停交易。";
    }

    public static int CountSlots(InventoryProgress inventory)
    {
        int slots = inventory.weapons.Length + (inventory.armorItems?.Length ?? (inventory.armor != null ? 1 : 0)) +
            (inventory.healthPacks > 0 ? 1 : 0);
        for (int i = 2; i <= 3; i++) if (inventory.keys[i] > 0) slots++;
        return slots;
    }

    public bool SelectCampCharacter(string character, out string message)
    {
        message = "只有準備中的新一輪可以更換角色。";
        if (!IsPreparing || !CharacterRoster.IsKnown(character)) return false;
        var candidate = JsonUtility.FromJson<GameProgressState>(JsonUtility.ToJson(saved));
        candidate.character = new CharacterProgress { version = 1, id = character };
        candidate.inventory.weapons[0] = CharacterWeapons.Create(CharacterWeapons.ForCharacter(character), 1, 0);
        candidate.campCharacterSelected = true; candidate.campUpgradeSelected = false;
        candidate.savedUtc = DateTime.UtcNow.ToString("O");
        if (!GameProgressSave.TryWrite(path, candidate, out _)) { message = "角色未變更：無法儲存準備進度。"; return false; }
        saved = candidate; CharacterId = character;
        message = "已選擇角色，請選一項免費技能升級。"; return true;
    }
    public bool SelectCampUpgrade(int index, out string message)
    {
        message = "請先在新一輪準備中選擇角色。";
        if (!IsPreparing || !HasCampCharacter || index < 0 || index > 2) return false;
        var candidate = JsonUtility.FromJson<GameProgressState>(JsonUtility.ToJson(saved));
        candidate.character.skillPower = index == 0 ? 1 : 0;
        candidate.character.skillHaste = index == 1 ? 1 : 0;
        candidate.character.skillMastery = index == 2 ? 1 : 0;
        candidate.character.cooldown = 0;
        candidate.campUpgradeSelected = true; candidate.savedUtc = DateTime.UtcNow.ToString("O");
        if (!GameProgressSave.TryWrite(path, candidate, out _)) { message = "升級未變更：無法儲存準備進度。"; return false; }
        saved = candidate; message = "技能升級已準備完成，可以選擇地圖。"; return true;
    }

    public bool SelectDestination(string scene, out string message)
    {
        message = "目前無法變更目的地。";
        if (!IsPreparing || !ExpeditionMaps.IsMap(scene)) return false;
        var candidate = JsonUtility.FromJson<GameProgressState>(JsonUtility.ToJson(saved));
        candidate.scene = scene;
        candidate.savedUtc = DateTime.UtcNow.ToString("O");
        if (!GameProgressSave.TryWrite(path, candidate, out _)) {
            message = "目的地未變更：無法儲存，原有裝備與目的地皆已保留。";
            return false;
        }
        saved = candidate; Destination = scene;
        message = "已選擇「" + ExpeditionMaps.ForScene(scene).Name + "」，本輪裝備與補給已保留。";
        return true;
    }

    public bool Buy(HubMerchant merchant, int index, out string message)
    {
        message = "目前無法交易。";
        if (!CanTrade) { message = Notice; return false; }
        if (!Enum.IsDefined(typeof(HubMerchant), merchant) || index < 0 || index >= Catalog[(int)merchant].Length) return false;
        HubOffer offer = Catalog[(int)merchant][index];
        if (!IsUnlocked(offer)) {
            message = $"武器尚未解鎖：累計購買 {WeaponShopProgress.RequiredPurchases((WeaponType)offer.Item, CharacterId)} 把武器後開放。";
            return false;
        }
        var current = Inventory;
        if (current.gold < offer.Price) { message = $"金幣不足，還需要 {offer.Price - current.gold} 金幣。"; return false; }
        bool needsSlot = offer.Goods == HubGoods.Weapon || offer.Goods == HubGoods.Armor ||
            offer.Goods == HubGoods.HealthPack && current.healthPacks == 0;
        if (needsSlot && UsedSlots >= current.capacity) { message = "背包已滿，請先向補給商擴充背包，或在遊戲中整理物品。"; return false; }
        if (offer.Goods == HubGoods.Backpack && current.capacity > int.MaxValue - 5 ||
            offer.Goods == HubGoods.HealthPack && current.healthPacks == int.MaxValue)
        { message = "已達持有上限。"; return false; }

        // Work on a detached candidate. A failed disk write cannot debit gold or grant items.
        var candidate = JsonUtility.FromJson<GameProgressState>(JsonUtility.ToJson(saved));
        var inventory = candidate.inventory;
        switch (offer.Goods)
        {
            case HubGoods.Weapon:
                var weapon = CharacterWeapons.Create((WeaponType)offer.Item, ItemLevel, .55f);
                EquipmentIdentity.Ensure(weapon);
                Array.Resize(ref inventory.weapons, inventory.weapons.Length + 1);
                inventory.weapons[inventory.weapons.Length - 1] = weapon;
                candidate.weaponPurchases = WeaponShopProgress.AfterPurchase(candidate.weaponPurchases);
                break;
            case HubGoods.Armor:
                // Normalize legacy single-armor saves before appending a new piece.
                if (inventory.armorItems == null)
                {
                    inventory.armorItems = inventory.armor == null ? new ArmorItem[0] : new[] { inventory.armor };
                    inventory.armorSlots = new[] { -1, inventory.armor == null ? -1 : 0, -1, -1 };
                    if (inventory.armor != null) {
                        inventory.armor.slot = ArmorSlot.Chest;
                        inventory.armor.level = Mathf.Clamp(Mathf.Max(1, inventory.armor.tier * 10), 1, 100);
                    }
                    inventory.armor = null;
                }
                var armor = ArmorLoot.Create((ArmorSlot)offer.Item, ItemLevel, .55f);
                EquipmentIdentity.Ensure(armor);
                Array.Resize(ref inventory.armorItems, inventory.armorItems.Length + 1);
                inventory.armorItems[inventory.armorItems.Length - 1] = armor;
                break;
            case HubGoods.HealthPack:
                inventory.healthPacks++;
                // Packs share one healing amount in existing saves; keep stronger owned packs.
                inventory.healAmount = Mathf.Max(25, inventory.healAmount);
                break;
            case HubGoods.Backpack: inventory.capacity += 5; break;
        }
        inventory.gold -= offer.Price;
        inventory.weaponPurchases = candidate.weaponPurchases;
        candidate.savedUtc = DateTime.UtcNow.ToString("O");
        if (!GameProgressSave.TryWrite(path, candidate, out _))
        { message = "交易未完成：無法儲存進度，金幣與物品都未變動。"; return false; }
        saved = candidate;
        message = $"已購買 {offer.Name}，花費 {offer.Price} 金幣，交易已儲存。";
        return true;
    }

    public void PrepareDeparture()
    {
        GameProgressSave.StartFresh = saved == null && fresh;
        CharacterRoster.SelectForNewRun(CharacterId);
    }
}
