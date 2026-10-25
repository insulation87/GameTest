using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum ArmorSlot { Helmet, Chest, Pants, Shoes }

public static class ArmorLoot
{
    public static string SlotName(ArmorSlot slot)
    {
        switch (slot) { case ArmorSlot.Helmet: return "頭盔"; case ArmorSlot.Chest: return "盔甲";
            case ArmorSlot.Pants: return "褲子"; default: return "鞋子"; }
    }
    public static ArmorItem Create(ArmorSlot slot, int level, float quality)
    {
        level = Mathf.Clamp(level, 1, 100); quality = Mathf.Clamp01(quality);
        float baseArmor = slot == ArmorSlot.Chest ? 14 : slot == ArmorSlot.Pants ? 10 : 7;
        var item = new ArmorItem { slot = slot, level = level, quality = quality, tier = 1,
            armorName = $"{SlotName(slot)} 等級 {level}",
            armorBonus = baseArmor * (1f + (level - 1) * .04f) * (1f + quality * .35f) };
        return quality >= .75f && UnityEngine.Random.value < .3f ? TacticalEquipment.ApplySpecial(item) : item;
    }
    public static ArmorPickup Drop(ArmorItem item, Vector3 position, float lifetime = 180)
    {
        var node = new GameObject(item.armorName, typeof(SphereCollider), typeof(Rigidbody));
        node.transform.position = position;
        var collider = node.GetComponent<SphereCollider>(); collider.isTrigger = true; collider.radius = .4f;
        var body = node.GetComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        var pickup = node.AddComponent<ArmorPickup>(); pickup.Initialize(item, lifetime); return pickup;
    }
}

public sealed class ArmorPickup : MonoBehaviour, IMagneticPickup
{
    ArmorItem item;
    bool collected;
    Transform visual;
    public void Initialize(ArmorItem value, float lifetime)
    {
        item = value;
        visual = ItemAppearance.Create(ItemAppearance.ArmorStart + (int)item.slot, transform).transform;
        Bounds bounds = visual.GetComponent<MeshFilter>().sharedMesh.bounds;
        float scale = .65f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        visual.localScale = Vector3.one * scale; visual.localPosition = -bounds.center * scale;
        PickupCollisionUtility.IgnoreCollisionsWithProjectiles(gameObject);
        Destroy(gameObject, Mathf.Max(1, lifetime));
    }
    void Update() { if (visual != null) visual.Rotate(Vector3.up, 45 * Time.deltaTime, Space.World); }
    public bool CanCollect(WeaponInventory inventory) => !CoopSession.IsClient && !collected && item != null &&
        inventory != null && inventory.HasItemSpace &&
        (!TryGetComponent(out DroppedEquipmentPickup dropped) || dropped.Allows(inventory));
    public bool TryCollect(Collider collector)
    {
        if (collector == null || collected) return false;
        var inventory = collector.GetComponentInParent<WeaponInventory>();
        var health = collector.GetComponentInParent<PlayerHealth>();
        if (!CanCollect(inventory) || health == null || health.IsDead || !inventory.AddArmor(item)) return false;
        collected = true; GameAudio.UI(GameSound.Pickup); Destroy(gameObject); return true;
    }
    void OnTriggerEnter(Collider collector) => TryCollect(collector);
}

public partial class WeaponInventory
{
    readonly List<ArmorItem> armors = new List<ArmorItem>();
    readonly ArmorItem[] armorSlots = new ArmorItem[4];
    readonly Button[] armorSlotButtons = new Button[4];
    readonly List<GameObject> weaponActions = new List<GameObject>();
    ArmorItem selectedArmor;
    GameObject armorActions;
    public IReadOnlyList<ArmorItem> Armors => armors;
    public IReadOnlyList<ArmorItem> EquippedArmorSlots => armorSlots;
    public int UsedItemSlots
    {
        get
        {
            int count = weapons.Count + armors.Count + (healthPackCount > 0 ? 1 : 0);
            for (int i = 1; i <= 3; i++) if (floorKeyCounts[i] > 0) count++;
            return count;
        }
    }
    public bool HasItemSpace => UsedItemSlots < capacity;
    public bool AddArmor(ArmorItem item)
    {
        if (item == null || !Enum.IsDefined(typeof(ArmorSlot), item.slot) || !HasItemSpace) return false;
        EquipmentIdentity.Ensure(item);
        armors.Add(item); RefreshWeaponList(); return true;
    }
    public void EquipArmor(ArmorItem item)
    {
        if (item == null || !armors.Contains(item) || !Enum.IsDefined(typeof(ArmorSlot), item.slot)) return;
        if (CoopSession.Command(this, "equipArmor", armors.IndexOf(item))) return;
        var health = GetComponent<PlayerHealth>(); if (health == null || health.IsDead) return;
        int slot = (int)item.slot;
        if (armorSlots[slot] == item) return;
        float old = armorSlots[slot]?.armorBonus ?? 0;
        armorSlots[slot] = item;
        health.ApplyEquipmentArmorBonus(item.armorBonus - old, false);
        selectedArmor = item; selectedWeapon = null;
        GameAudio.UI(GameSound.Equip); RefreshArmorSlots(); RefreshDetails(); RefreshWeaponList();
    }
    public void UnequipArmor(ArmorSlot slot)
    {
        if (!Enum.IsDefined(typeof(ArmorSlot), slot)) return;
        if (CoopSession.Command(this, "unequipArmor", (int)slot)) return;
        var health = GetComponent<PlayerHealth>(); if (health == null || health.IsDead) return;
        var item = armorSlots[(int)slot]; if (item == null) return;
        armorSlots[(int)slot] = null;
        health.ApplyEquipmentArmorBonus(-item.armorBonus, false);
        GameAudio.UI(GameSound.Equip); RefreshArmorSlots(); RefreshDetails(); RefreshWeaponList();
    }
    void SellSelectedArmor()
    {
        if (selectedArmor == null || Array.IndexOf(armorSlots, selectedArmor) >= 0) return;
        if (CoopSession.Command(this, "sellArmor", armors.IndexOf(selectedArmor))) return;
        gold += ArmorSellPrice(selectedArmor); armors.Remove(selectedArmor); selectedArmor = null;
        GameAudio.UI(GameSound.Coins); RefreshDetails(); RefreshWeaponList();
    }
    int ArmorSellPrice(ArmorItem item) => 10 + item.level * 2 + Mathf.RoundToInt(item.quality * 30);
    void BuildArmorUI(Transform details)
    {
        // Move existing weapon controls below the shared item details.
        equippedText.rectTransform.anchoredPosition = new Vector2(0, 278);
        detailText.rectTransform.anchoredPosition = new Vector2(0, 97);
        detailText.rectTransform.sizeDelta = new Vector2(250, 240);
        foreach (Transform child in details)
            if (child.name != "Equipped" && child.name != "Stats" && (child.GetComponent<Button>() != null || child.name == "Equip Hint"))
            { weaponActions.Add(child.gameObject); child.GetComponent<RectTransform>().anchoredPosition += new Vector2(0, 94); }
        armorActions = new GameObject("Armor Actions", typeof(RectTransform)); armorActions.transform.SetParent(details, false);
        var equip = CreateButton(armorActions.transform, "裝備防具", new Vector2(0, -81), new Vector2(220, 50), GameUITheme.Raised);
        equip.onClick.AddListener(() => EquipArmor(selectedArmor));
        var sell = CreateButton(armorActions.transform, "出售防具", new Vector2(-57, -131), new Vector2(106, 40), GameUITheme.Raised);
        sell.onClick.AddListener(SellSelectedArmor);
        dropArmorButton = CreateButton(armorActions.transform, "丟棄防具", new Vector2(57, -131), new Vector2(106, 40), GameUITheme.Raised);
        dropArmorButton.name = "Drop Armor";
        dropArmorButton.onClick.AddListener(() => DropArmor(selectedArmor));
        armorActions.SetActive(false);
        CreateText("Armor Title", details, "防具欄（點擊卸下）", 17, TextAnchor.MiddleCenter, new Vector2(0, -180), new Vector2(250, 30));
        for (int i = 0; i < 4; i++)
        {
            var slot = (ArmorSlot)i;
            var button = CreateButton(details, "", new Vector2((i % 2 == 0 ? -1 : 1) * 67, -230 - (i / 2) * 62),
                new Vector2(125, 55), GameUITheme.Recess);
            button.name = "Armor Slot " + slot; button.GetComponentInChildren<Text>().fontSize = 13;
            button.onClick.AddListener(() => UnequipArmor(slot)); armorSlotButtons[i] = button;
        }
        RefreshArmorSlots();
    }
    void RefreshArmorSlots()
    {
        for (int i = 0; i < 4; i++)
            if (armorSlotButtons[i] != null)
                armorSlotButtons[i].GetComponentInChildren<Text>().text = ArmorLoot.SlotName((ArmorSlot)i) + "\n" +
                    (armorSlots[i] == null ? "空" : $"Lv.{armorSlots[i].level} 護甲 +{armorSlots[i].armorBonus:0}");
    }
    void RefreshArmorDetails()
    {
        foreach (var action in weaponActions) action.SetActive(selectedArmor == null);
        if (armorActions != null) armorActions.SetActive(selectedArmor != null);
        RefreshArmorSlots();
        if (selectedArmor == null) return;
        bool equipped = Array.IndexOf(armorSlots, selectedArmor) >= 0;
        detailText.text = $"{selectedArmor.armorName}\n\n部位：{ArmorLoot.SlotName(selectedArmor.slot)}\n等級：{selectedArmor.level}\n" +
            $"品質：{GetQualityName(selectedArmor.quality)}\n最大護甲：+{selectedArmor.armorBonus:0.0}\n" +
            $"出售：{ArmorSellPrice(selectedArmor)} 金幣\n{TacticalEquipment.Description(selectedArmor.affix)}\n" + (equipped ? "已裝備；先卸下才能出售。" : "點擊裝備，替換同部位的防具。") + "\n裝備不會恢復護甲。";
    }
    void AddArmorButtons()
    {
        foreach (var item in armors)
        {
            var listed = item; bool equipped = Array.IndexOf(armorSlots, item) >= 0;
            var button = CreateButton(weaponListContent,
                $"{ArmorLoot.SlotName(item.slot)}\nLv.{item.level}" + (equipped ? " 已裝備" : ""), Vector2.zero, new Vector2(90, 90), GetQualityColor(item.quality));
            button.name = "Armor Item " + item.slot;
            var label = button.GetComponentInChildren<Text>(); label.fontSize = 12; label.alignment = TextAnchor.LowerCenter;
            label.raycastTarget = false; label.rectTransform.sizeDelta = new Vector2(86, 40); label.rectTransform.anchoredPosition = new Vector2(0, -23);
            ItemIconGraphic.Create(button.transform, ItemAppearance.ArmorStart + (int)item.slot, new Vector2(0, 16), new Vector2(72, 45));
            button.onClick.AddListener(() => { selectedArmor = listed; selectedWeapon = null; RefreshDetails(); });
        }
    }
}
