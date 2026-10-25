using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit batch review in a temporary project; never touches an interactive editor or real saves.
[InitializeOnLoad]
public static class CharacterEquipmentReview
{
    const string Pending = "CharacterEquipmentReview.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static string output;
    static GameObject actor;
    static PlayerHealth health;
    static WeaponInventory inventory;
    static PlayerShooter shooter;
    static Camera camera;
    static Projectile prefab;
    static readonly List<string> errors = new List<string>();
    static CharacterEquipmentReview() { EditorApplication.playModeStateChanged += StateChanged; }
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!Application.isBatchMode || !root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run this review in an isolated temporary batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        File.Delete(Path.Combine(output, "equipment-failure.txt")); File.Delete(Path.Combine(output, "equipment-result.txt"));
        assertions = 0; errors.Clear(); checks = Checks(); deadline = EditorApplication.timeSinceStartup + .2;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext())
            { File.WriteAllText(Path.Combine(output, "equipment-result.txt"), $"PASS {assertions} Play Mode integration checks"); Finish(0); return; }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .08f);
        }
        catch (Exception ex)
        { File.WriteAllText(Path.Combine(output, "equipment-failure.txt"), ex.ToString()); Debug.LogException(ex); Finish(1); }
    }
    static void Finish(int code)
    { Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code); }
    static void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
            !message.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(message + "\n" + stack);
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); assertions++; Debug.Log("PASS " + message); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    static void CreateActor(string id, bool shooterFirst = false)
    {
        if (actor != null) Object.DestroyImmediate(actor);
        CharacterRoster.SelectForNewRun(id);
        actor = new GameObject("Equipment Review Player"); actor.SetActive(false);
        actor.AddComponent<Rigidbody>().isKinematic = true; actor.AddComponent<BoxCollider>();
        var save = actor.AddComponent<GameProgressSave>(); save.enabled = false; save.StoragePath = Path.Combine(output, "unused-save.json");
        health = actor.AddComponent<PlayerHealth>(); health.enabled = false; health.criticalChance = 0;
        if (!shooterFirst) actor.AddComponent<CharacterSkills>();
        shooter = actor.AddComponent<PlayerShooter>(); shooter.enabled = false; shooter.fpsCamera = camera; shooter.projectilePrefab = prefab;
        var mount = new GameObject("Test Held Weapon Mount").transform; mount.SetParent(actor.transform, false);
        mount.position = camera.transform.position; Set(shooter, "weaponModelMount", mount);
        if (shooterFirst) actor.AddComponent<Player>();
        if (actor.GetComponent<Player>() != null) actor.GetComponent<Player>().enabled = false;
        actor.SetActive(true); actor.GetComponent<PickupMagnet>().enabled = false;
        inventory = actor.GetComponent<WeaponInventory>(); inventory.capacity = 100;
        Set(health, "invincibleUntil", 0f);
    }
    static EnemyHealth Enemy(Vector3 position, float hp = 1000)
    {
        var node = GameObject.CreatePrimitive(PrimitiveType.Cube); node.transform.position = position;
        var enemy = node.AddComponent<EnemyHealth>(); enemy.SetMaxHealth(hp); enemy.showDamageNumbers = false;
        enemy.SetLevelAndExperience(1, 0);
        enemy.healthDropChance = enemy.weakNpcWeaponDropChance = enemy.strongNpcWeaponDropChance =
            enemy.armorEquipmentDropChance = enemy.progressionDropChance = 0;
        Physics.SyncTransforms(); return enemy;
    }
    static void ClearCombat()
    {
        foreach (var enemy in EnemyHealth.ActiveEnemies.ToArray()) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
        foreach (var item in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) if (item != prefab) Object.DestroyImmediate(item.gameObject);
    }
    static Button FindButton(string text) => actor.GetComponentsInChildren<Button>(true)
        .First(b => b.GetComponentInChildren<Text>().text == text);
    static IEnumerator Checks()
    {
        var director = Object.FindFirstObjectByType<PixelArtDirector>(); if (director != null) director.enabled = false;
        camera = new GameObject("Review Camera", typeof(Camera)).GetComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 1.5f, 0); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f, .05f, .07f);
        new GameObject("Review Light", typeof(Light)).GetComponent<Light>().type = LightType.Directional;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.transform.position = new Vector3(0, -.5f, 0);
        ground.transform.localScale = new Vector3(100, 1, 100);
        var bolt = GameObject.CreatePrimitive(PrimitiveType.Sphere); bolt.name = "Review Projectile Template";
        bolt.transform.position = new Vector3(1000, 1, 0); bolt.transform.localScale = Vector3.one * .12f;
        prefab = bolt.AddComponent<Projectile>(); bolt.GetComponent<Rigidbody>().useGravity = false;
        Check(CharacterWeapons.Types.Length == 10 && CharacterWeapons.Types.Distinct().Count() == 10, "Ten unique character weapons");
        for (int i = 0; i < CharacterRoster.All.Length; i++)
        {
            var role = CharacterRoster.All[i]; CreateActor(role.Id, i % 2 == 0); yield return .1f;
            Check(inventory.Weapons.Count == 1 && inventory.Weapons[0].weaponType == CharacterWeapons.ForCharacter(role.Id) &&
                inventory.Weapons[0].level == 1 && actor.GetComponent<CharacterSkills>().Character.Id == role.Id,
                "Correct initial weapon with either Awake order: " + role.Id);
            Check(actor.GetComponentsInChildren<MeshFilter>().Any(m => m.sharedMesh != null && m.sharedMesh.name == CharacterWeapons.ForCharacter(role.Id).ToString()),
                "Held model exists: " + role.Id);
            Check(CharacterWeapons.Create(CharacterWeapons.Types[i], 50, .2f).damage > CharacterWeapons.Create(CharacterWeapons.Types[i], 1, .2f).damage,
                "Levels increase actual weapon damage: " + role.Id);
            Check(CharacterWeapons.Create(CharacterWeapons.Types[i], 20, .9f).damage > CharacterWeapons.Create(CharacterWeapons.Types[i], 20, 0).damage,
                "Quality increases actual weapon damage: " + role.Id);
        }
        var rolls = new HashSet<WeaponType>();
        for (int i = 0; i < 1000; i++) rolls.Add(WeaponLoot.RollType());
        Check(rolls.SetEquals(CharacterWeapons.Types), "Random loot covers exactly ten character weapons");
        foreach (var type in CharacterWeapons.Types)
        {
            ClearCombat(); CreateActor(CharacterRoster.All[(int)type - 10].Id); yield return .1f;
            var near = Enemy(new Vector3(0, 1.5f, CharacterWeapons.IsMelee(type) ? 2 : 6));
            var behind = Enemy(new Vector3(0, 1.5f, -3));
            var splash = type == WeaponType.FireStaff ? Enemy(new Vector3(1.25f, 1.5f, 6)) : null;
            shooter.Fire(); yield return .45f;
            Check(near.CurrentHealth < 1000, "Real attack damages an NPC: " + type);
            Check(behind.CurrentHealth == 1000, "Attack excludes targets behind the camera: " + type);
            float matchedDamage = 1000 - near.CurrentHealth;
            if (splash != null) Check(splash.CurrentHealth < 1000, "Matching fire staff hits a secondary target inside its full explosion radius");
            if (CharacterWeapons.IsMelee(type))
                Check(!Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Any(p => p != prefab), "Melee creates no bullets: " + type);

            var skills = actor.GetComponent<CharacterSkills>();
            skills.RestoreProgress(new CharacterProgress { version = 1, id = CharacterRoster.All[((int)type - 9) % 10].Id });
            ClearCombat();
            near = Enemy(new Vector3(0, 1.5f, CharacterWeapons.IsMelee(type) ? 2 : 6));
            splash = type == WeaponType.FireStaff ? Enemy(new Vector3(1.25f, 1.5f, 6)) : null;
            Set(shooter, "nextFireTime", 0f); shooter.Fire(); yield return .45f;
            Check(Mathf.Abs((1000 - near.CurrentHealth) - matchedDamage * .1f) < .001f,
                "Wrong character deals exactly ten percent actual hit damage: " + type);
            if (splash != null) Check(splash.CurrentHealth == 1000, "Wrong character's reduced explosion radius excludes the secondary target");
            var savedInventory = inventory.CaptureProgress();
            inventory.RestoreProgress(savedInventory);
            Check(Mathf.Approximately(shooter.WeaponEfficiency, .1f) &&
                Mathf.Approximately(inventory.Weapons[0].damage, shooter.projectileDamage),
                "Restored inventory applies the penalty without changing the weapon's saved base stats: " + type);
            var details = (Text)inventory.GetType().GetField("detailText", Flags).GetValue(inventory);
            Check(details.text.Contains("角色不相符") && details.text.Contains("10%"), "Backpack explains the current role penalty: " + type);
            skills.RestoreProgress(new CharacterProgress { version = 1, id = CharacterRoster.All[(int)type - 10].Id });
            ClearCombat(); near = Enemy(new Vector3(0, 1.5f, CharacterWeapons.IsMelee(type) ? 2 : 6));
            Set(shooter, "nextFireTime", 0f); shooter.Fire(); yield return .45f;
            Check(Mathf.Abs((1000 - near.CurrentHealth) - matchedDamage) < .001f,
                "Returning to the matching character restores full hit damage without cumulative penalties: " + type);
        }
        ClearCombat(); CreateActor("breaker"); yield return .1f;
        var far = Enemy(new Vector3(0, 1.5f, 8)); shooter.Fire();
        Check(far.CurrentHealth == 1000, "Melee cannot hit beyond reach");
        var blocked = Enemy(new Vector3(0, 1.5f, 3));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 1.5f, 1.5f);
        wall.transform.localScale = new Vector3(4, 4, .3f); Physics.SyncTransforms(); shooter.Fire();
        Check(blocked.CurrentHealth == 1000, "Melee cannot attack through a wall"); Object.DestroyImmediate(wall); ClearCombat();

        var enemyLoot = Enemy(new Vector3(15, 1.5f, 0), 1);
        enemyLoot.weakNpcWeaponDropChance = enemyLoot.strongNpcWeaponDropChance = enemyLoot.armorEquipmentDropChance = 1;
        enemyLoot.SetDungeonFloor(3); enemyLoot.TakeDamage(100); yield return .1f;
        var droppedWeapon = Object.FindFirstObjectByType<WeaponPickup>(); var droppedArmor = Object.FindFirstObjectByType<ArmorPickup>();
        Check(droppedWeapon != null && droppedArmor != null, "NPC death drops both weapon and armor through the real death path");
        var collector = actor.GetComponent<Collider>();
        Check(droppedWeapon.TryCollect(collector) && droppedArmor.TryCollect(collector), "Dropped items enter the backpack");
        var collectedWeapon = inventory.Weapons.Last();
        Check(CharacterWeapons.IsCharacterWeapon(collectedWeapon.weaponType) && collectedWeapon.level >= 81 && collectedWeapon.level <= 100,
            "Third-floor weapon has level 81–100");
        Check(inventory.Armors.Last().level >= 81, "Armor follows the floor level range");
        yield return .1f;
        Call(inventory, "SetOpen", true); yield return .1f;
        var list = actor.transform.Find("Weapon Inventory Canvas/Weapon Inventory/Inventory Grid/Inventory Items");
        var weaponButton = list.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>().text.StartsWith(CharacterWeapons.Name(collectedWeapon.weaponType) + "\nLv." + collectedWeapon.level));
        weaponButton.onClick.Invoke(); FindButton("2").onClick.Invoke();
        Check(inventory.EquippedSlots[1] == collectedWeapon && Mathf.Approximately(shooter.projectileDamage, collectedWeapon.damage), "Backpack buttons equip dropped weapons");
        float baseArmor = health.MaxArmor, originalArmor = health.CurrentArmor;
        var equipped = new List<ArmorItem>();
        foreach (ArmorSlot slot in Enum.GetValues(typeof(ArmorSlot)))
        {
            var item = ArmorLoot.Create(slot, 25, .5f); var pickup = ArmorLoot.Drop(item, new Vector3(12, 1, 0));
            Check(pickup.TryCollect(collector), "Real armor pickup: " + slot); yield return .1f;
            list.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>().text.StartsWith(
                ArmorLoot.SlotName(slot) + "\nLv.25")).onClick.Invoke();
            FindButton("裝備防具").onClick.Invoke();
            Check(inventory.EquippedArmorSlots[(int)slot] == item, "Backpack equips the correct armor slot: " + slot);
            equipped.Add(item);
        }
        Check(Mathf.Approximately(health.MaxArmor, baseArmor + equipped.Sum(a => a.armorBonus)) && health.CurrentArmor == originalArmor,
            "Four armor bonuses add together without restoring damaged armor");
        var upgrade = ArmorLoot.Create(ArmorSlot.Helmet, 90, .9f); inventory.AddArmor(upgrade);
        float beforeSwap = health.MaxArmor; inventory.EquipArmor(upgrade);
        Check(Mathf.Approximately(health.MaxArmor, beforeSwap - equipped[0].armorBonus + upgrade.armorBonus) && inventory.Armors.Contains(equipped[0]),
            "Replacement removes the old bonus and retains the old item");
        for (int i = 0; i < 5; i++) { inventory.UnequipArmor(ArmorSlot.Helmet); inventory.EquipArmor(upgrade); }
        Check(health.CurrentArmor <= originalArmor && Mathf.Approximately(health.MaxArmor, beforeSwap - equipped[0].armorBonus + upgrade.armorBonus),
            "Repeated equip and unequip cannot heal or stack bonuses");
        int count = inventory.Armors.Count; FindButton("出售防具").onClick.Invoke();
        Check(inventory.Armors.Count == count, "Equipped armor cannot be sold");
        var slots = actor.transform.Find("Weapon Inventory Canvas/Weapon Inventory/Weapon Details");
        slots.Find("Armor Slot Helmet").GetComponent<Button>().onClick.Invoke(); FindButton("出售防具").onClick.Invoke();
        Check(inventory.EquippedArmorSlots[0] == null && inventory.Armors.Count == count - 1, "Backpack buttons unequip then sell armor");
        inventory.EquipArmor(equipped[0]);
        inventory.capacity = inventory.UsedItemSlots;
        var fullWeapon = WeaponLoot.Drop(CharacterWeapons.Create(WeaponType.Longbow, 20, .2f), new Vector3(12, 1, 0));
        var fullArmor = ArmorLoot.Drop(ArmorLoot.Create(ArmorSlot.Shoes, 20, .2f), new Vector3(12, 1, 0));
        Check(!fullWeapon.TryCollect(collector) && !fullArmor.TryCollect(collector), "Full backpack rejects weapons and armor without deleting drops");
        Object.DestroyImmediate(fullWeapon.gameObject); Object.DestroyImmediate(fullArmor.gameObject); inventory.IncreaseCapacity(20);

        var state = new GameProgressState { scene = "equipment-review", ground = Vector3.zero, health = health.CaptureProgress(),
            inventory = inventory.CaptureProgress(), floorKeys = new int[4], character = actor.GetComponent<CharacterSkills>().CaptureProgress() };
        string savePath = Path.Combine(output, "equipment-save.json");
        Check(GameProgressSave.TryWrite(savePath, state, out string writeError), "Equipment is written to an isolated save: " + writeError);
        Check(GameProgressSave.TryRead(savePath, out var loaded, out string readError), "Equipment save passes validation: " + readError);
        int slotsBeforeLegacy = inventory.UsedItemSlots;
        string oldSavePath = Path.Combine(output, "equipment-legacy-grenades-save.json");
        var envelopeType = typeof(GameProgressSave).GetNestedType("Envelope", BindingFlags.NonPublic);
        var envelope = JsonUtility.FromJson(File.ReadAllText(savePath), envelopeType);
        string oldPayload = ((string)envelopeType.GetField("payload").GetValue(envelope)).Replace("\"inventory\":{", "\"inventory\":{\"grenades\":99,");
        envelopeType.GetField("payload").SetValue(envelope, oldPayload);
        envelopeType.GetField("checksum").SetValue(envelope, typeof(GameProgressSave).GetMethod("Hash", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { oldPayload }));
        File.WriteAllText(oldSavePath, JsonUtility.ToJson(envelope));
        Check(oldPayload.Contains("\"grenades\":99"), "The legacy save fixture contains a real old grenade inventory field with a valid checksum");
        Check(GameProgressSave.TryRead(oldSavePath, out var oldSave, out _), "A real legacy save containing grenades remains readable");
        inventory.RestoreProgress(oldSave.inventory);
        Check(inventory.UsedItemSlots == slotsBeforeLegacy && !JsonUtility.ToJson(inventory.CaptureProgress()).Contains("grenades"),
            "Legacy grenades consume no backpack space and disappear from newly captured saves");
        Check(!actor.GetComponents<MonoBehaviour>().Any(c => c.GetType().Name.Contains("Grenade")),
            "Real player initialization creates no grenade component");
        Check(!actor.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("手榴彈")),
            "Actual backpack and shop contain no grenade entry");
        float savedMax = health.MaxArmor;
        inventory.RestoreProgress(loaded.inventory); health.RestoreProgress(loaded.health);
        Check(inventory.Armors.Count == state.inventory.armorItems.Length && inventory.EquippedArmorSlots.All(a => a != null) &&
            Mathf.Approximately(health.MaxArmor, savedMax) && inventory.EquippedSlots[1].weaponType == collectedWeapon.weaponType,
            "Reload restores weapons, all armor slots and bonuses exactly once");
        loaded.inventory.armorItems[0].armorBonus = float.NaN;
        Check(!GameProgressSave.TryWrite(Path.Combine(output, "invalid-save.json"), loaded, out _), "Non-finite equipment stats are rejected");
        var legacy = new InventoryProgress { weapons = new[] { new WeaponItem { weaponName = "old gun", weaponType = WeaponType.Rifle, level = 42 } },
            slots = new[] { 0, -1, -1 }, keys = new int[4], armor = new ArmorItem { armorName = "legacy armor", tier = 2, armorBonus = 30 }, capacity = 20, healAmount = 25 };
        inventory.RestoreProgress(legacy);
        Check(inventory.Weapons.All(w => CharacterWeapons.IsCharacterWeapon(w.weaponType)) && inventory.Weapons[0].level == 42 &&
            inventory.EquippedArmorSlots[1]?.armorBonus == 30, "Legacy guns and single armor migrate without losing levels");
        inventory.RestoreProgress(state.inventory); health.RestoreProgress(state.health);
        Call(inventory, "SetOpen", false);
        var playerCollider = actor.GetComponent<BoxCollider>(); playerCollider.center = new Vector3(0, .9f, 0);
        playerCollider.size = new Vector3(.6f, 1.8f, .6f); Physics.SyncTransforms();
        int beforeAttraction = inventory.Armors.Count;
        var attracted = ArmorLoot.Drop(ArmorLoot.Create(ArmorSlot.Shoes, 12, .1f), new Vector3(1.5f, .9f, 0));
        actor.GetComponent<PickupMagnet>().enabled = true; yield return 1.5f;
        actor.GetComponent<PickupMagnet>().enabled = false;
        Check(attracted == null && inventory.Armors.Count == beforeAttraction + 1, "Armor is physically collected by the pickup magnet in Play Mode");
        inventory.AddGold(5000);
        Call(inventory, "SetPaused", true); Call(inventory, "ShowPauseShop");
        foreach (var type in CharacterWeapons.Types)
        {
            int beforePurchase = inventory.Weapons.Count;
            FindButton(CharacterWeapons.Name(type) + "\n" + CharacterWeapons.ShopPrice(type) + " 金幣").onClick.Invoke();
            Check(inventory.Weapons.Count == beforePurchase + 1 && inventory.Weapons.Last().weaponType == type,
                "Shop button buys a character weapon: " + type);
        }
        foreach (ArmorSlot slot in Enum.GetValues(typeof(ArmorSlot)))
        {
            int beforePurchase = inventory.Armors.Count;
            FindButton(ArmorLoot.SlotName(slot) + "\n100 金幣").onClick.Invoke();
            Check(inventory.Armors.Count == beforePurchase + 1 && inventory.Armors.Last().slot == slot,
                "Shop button buys armor for the backpack: " + slot);
        }
        Call(inventory, "SetPaused", false);
        var menu = CharacterSelectionMenu.Create(actor.transform.Find("Weapon Inventory Canvas"), id => { }, () => { });
        menu.Open(false);
        Check(menu.GetComponentsInChildren<Text>().Any(t => t.text == CharacterWeapons.Name(WeaponType.WarHammer)),
            "Character selection displays corresponding weapon names");
        menu.gameObject.SetActive(false);
        inventory.RestoreProgress(state.inventory); health.RestoreProgress(state.health);
        Call(inventory, "SetOpen", true); Canvas.ForceUpdateCanvases(); yield return .2f;
        Check(actor.GetComponentsInChildren<ItemIconGraphic>(true).Length >= 8, "Inventory and hotbar use mesh icons");
        // Export actual rendered UI for visual review, independently of the headless Game view.
        var canvas = actor.transform.Find("Weapon Inventory Canvas").GetComponent<Canvas>();
        var uiCamera = new GameObject("UI Render Camera", typeof(Camera)).GetComponent<Camera>();
        uiCamera.transform.position = new Vector3(0, 0, -10); uiCamera.clearFlags = CameraClearFlags.SolidColor; uiCamera.backgroundColor = camera.backgroundColor;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 2;
        // The batch Editor's tiny Game view must not scale this explicit 1920x1080 render.
        canvas.GetComponent<CanvasScaler>().enabled = false; canvas.scaleFactor = 1f;
        var image = new RenderTexture(1920, 1080, 24); uiCamera.targetTexture = image; yield return .2f; Canvas.ForceUpdateCanvases(); uiCamera.Render(); Canvas.ForceUpdateCanvases(); uiCamera.Render();
        RenderTexture.active = image; var png = new Texture2D(1920, 1080, TextureFormat.RGB24, false); png.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); png.Apply();
        File.WriteAllBytes(Path.Combine(output, "equipment-backpack.png"), png.EncodeToPNG()); RenderTexture.active = null;
        var stats = slots.Find("Stats").GetComponent<Text>();
        // Check rendered glyphs directly: preferredHeight measures an unbounded layout,
        // and fontSizeUsedForBestFit can use scaled native pixels in the batch Editor.
        var renderedMesh = stats.canvasRenderer.GetMesh();
        var panelBounds = ((RectTransform)slots).rect;
        var textBounds = stats.rectTransform.rect;
        bool glyphsInside = renderedMesh.vertexCount > 0 && renderedMesh.vertices.All(v =>
        {
            // Font bearings can extend past the text anchor, within the pane's padding.
            var panelPosition = slots.InverseTransformPoint(stats.transform.TransformPoint(v));
            return panelPosition.x >= panelBounds.xMin && panelPosition.x <= panelBounds.xMax &&
                v.y >= textBounds.yMin - 1 && v.y <= textBounds.yMax + 1;
        });
        int renderedCharacters = stats.cachedTextGenerator.characterCountVisible;
        var generation = stats.GetGenerationSettings(new Vector2(10000, 10000));
        generation.resizeTextForBestFit = false;
        generation.verticalOverflow = VerticalWrapMode.Overflow;
        using (var fullText = new TextGenerator())
        {
            fullText.Populate(stats.text, generation);
            Check(glyphsInside && renderedCharacters >= fullText.characterCountVisible,
                $"Selected weapon details render completely inside the panel ({renderedCharacters}/{fullText.characterCountVisible} visible characters, bounds {renderedMesh.bounds})");
        }
        list.GetComponentsInChildren<Button>().First(b => b.name == "Armor Item Helmet").onClick.Invoke();
        yield return .1f; Canvas.ForceUpdateCanvases(); uiCamera.Render(); RenderTexture.active = image;
        png.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); png.Apply();
        File.WriteAllBytes(Path.Combine(output, "equipment-armor.png"), png.EncodeToPNG()); RenderTexture.active = null;
        Check(!File.Exists(Path.Combine(output, "unused-save.json")), "Review does not write automatic or player saves");
        Check(errors.Count == 0, "No unexpected gameplay errors: " + string.Join("\n", errors));
    }
}
