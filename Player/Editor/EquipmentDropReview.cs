using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit entry points for isolated projects. Host and client projects share a parent folder.
[InitializeOnLoad]
public static class EquipmentDropReview
{
    const string RoleKey = "EquipmentDropReview.Role";
    static string role;
    static IEnumerator checks;
    static int assertions, minimumFrame;
    static double deadline, timeout;
    static readonly List<string> errors = new List<string>();
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../"));
    static string Marker(string name) => Path.Combine(Output, "equipment-pair-" + name + ".txt");
    static EquipmentDropReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run() => Start("local");
    public static void RunHost() => Start("host");
    public static void RunClient() => Start("client");
    static void Start(string value)
    {
        if (Application.productName != "UIOverlayReview") throw new InvalidOperationException("Use the isolated review project.");
        if (value == "host")
            foreach (string name in new[] { "port", "offer", "seen", "drop", "replay", "weapon-return", "armor-ready", "armor-equipped", "armor-drop", "done" })
                File.Delete(Marker(name));
        LoginSceneSetup.PlayFromCurrentScene(); CoopModeReview.BuildFixtures();
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        SessionState.SetString(RoleKey, value); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        role = SessionState.GetString(RoleKey, ""); if (role.Length == 0) return;
        SessionState.SetString(RoleKey, ""); assertions = 0; errors.Clear();
        checks = role == "host" ? Host() : role == "client" ? Client() : Local();
        minimumFrame = Time.frameCount + 3; deadline = 0; timeout = EditorApplication.timeSinceStartup + 240;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    { if ((type == LogType.Error || type == LogType.Exception) && !stack.Contains("UnityEditor.Search") && !message.Contains("burst JIT compiler")) errors.Add(message); }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("Equipment drop " + role);
            if (Time.frameCount < minimumFrame || EditorApplication.timeSinceStartup < deadline) return;
            if (!checks.MoveNext()) { Finish(0, "PASS " + assertions + " equipment transfer Play Mode checks (" + role + ")"); return; }
            minimumFrame = Time.frameCount + 3;
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .1f);
        }
        catch (Exception ex) { Debug.LogException(ex); Finish(1, "FAIL " + ex); }
    }
    static void Finish(int code, string message)
    {
        File.WriteAllText(Path.Combine(Output, "equipment-drop-" + role + "-result.txt"), message);
        Debug.Log(message); Application.logMessageReceived -= Log; EditorApplication.update -= Tick;
        Time.timeScale = 1; CoopSession.Leave(); EditorApplication.Exit(code);
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); assertions++; Debug.Log("PASS " + message); }
    static IEnumerator Wait(Func<bool> predicate)
    { while (!predicate()) yield return .1f; yield return .15f; }
    static void Invoke(object owner, string method, params object[] args) => owner.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, args);
    static void Select(WeaponInventory inventory, object item)
    {
        inventory.GetType().GetField(item is ArmorItem ? "selectedArmor" : "selectedWeapon", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(inventory, item);
        inventory.GetType().GetField(item is ArmorItem ? "selectedWeapon" : "selectedArmor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(inventory, null);
        Invoke(inventory, "RefreshDetails");
    }
    static CoopActor LocalActor => Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).First(a => a.Local);
    static WeaponPickup WeaponDrop => Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None).FirstOrDefault(p => p.GetComponent<DroppedEquipmentPickup>() != null);
    static ArmorPickup ArmorDrop => Object.FindObjectsByType<ArmorPickup>(FindObjectsSortMode.None).FirstOrDefault(p => p.GetComponent<DroppedEquipmentPickup>() != null);
    static WeaponItem Weapon()
    {
        var item = CharacterWeapons.Create(WeaponType.WarHammer, 37, .83f);
        item.weaponName = "隊友測試戰鎚"; item.damage = 173.25f; item.fireInterval = .47f;
        item.projectileSpeed = 62.5f; item.projectilesPerShot = 3; item.spreadAngle = 4.2f; item.explosionRadius = 2.3f;
        return item;
    }
    static ArmorItem Armor()
    { var item = ArmorLoot.Create(ArmorSlot.Chest, 51, .92f); item.tier = 4; item.armorName = "隊友測試盔甲"; item.armorBonus = 83.25f; return item; }
    static void Freeze(CoopActor actor, Vector3 position)
    {
        actor.GetComponent<Player>().enabled = false;
        var body = actor.GetComponent<Rigidbody>(); body.isKinematic = true; body.position = position;
        actor.transform.position = position;
        actor.GetComponent<Player>().fpsCamera.GetComponent<FPSCameraLook>().SetLook(0, 0);
        Physics.SyncTransforms();
    }
    static void Move(CoopActor actor, Vector3 position)
    { actor.GetComponent<Rigidbody>().position = position; actor.transform.position = position; Physics.SyncTransforms(); }
    static void Click(WeaponInventory inventory, string name)
    {
        var button = inventory.GetComponentsInChildren<Button>(true).First(b => b.name == name);
        Check(button.isActiveAndEnabled && button.interactable, name + " is available in the inventory");
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) {
            position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position) }, hits);
        Check(hits.Any(h => h.gameObject == button.gameObject || h.gameObject.transform.IsChildOf(button.transform)), name + " receives mouse input");
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    static void Capture(string name)
    {
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        Check(texture != null, "Capture inventory: " + name);
        File.WriteAllBytes(Path.Combine(Output, "equipment-" + name + ".png"), texture.EncodeToPNG()); Object.Destroy(texture);
    }
    static IEnumerator Local()
    {
        Check(CloudAccountService.EnsureInstance().EnterGuest(), "Isolated guest entry");
        Check(CoopSession.StartSplit("Map1", "breaker", "sentinel"), "Local cooperative scene starts");
        var wait = Wait(() => CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading); while (wait.MoveNext()) yield return wait.Current;
        var actors = Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None);
        var first = actors.First(a => a.LocalIndex == 0); var second = actors.First(a => a.LocalIndex == 1);
        Freeze(first, new Vector3(0, 1, 0)); Freeze(second, new Vector3(12, 1, 0));
        var giver = first.GetComponent<WeaponInventory>(); var receiver = second.GetComponent<WeaponInventory>();
        first.GetComponent<PickupMagnet>().enabled = true; second.GetComponent<PickupMagnet>().enabled = false;
        var weapon = Weapon(); Check(giver.AddWeapon(weapon), "Giver owns transferable weapon");
        giver.ApplyCoopCommand("equip", giver.Weapons.Count - 1, 1); giver.ApplyCoopCommand("equip", giver.Weapons.Count - 1, 2);
        string weaponData = JsonUtility.ToJson(weapon); int count = giver.Weapons.Count, gold = giver.Gold;
        Invoke(giver, "SetOpen", true); Select(giver, weapon); yield return .3f; Capture("weapon-button"); Click(giver, "Drop Weapon");
        Check(giver.Weapons.Count == count - 1 && giver.Gold == gold, "Drop removes one item and awards no sale gold");
        Check(giver.EquippedSlots.All(w => w != weapon) && giver.ActiveWeapon != weapon && giver.ActiveWeapon != null,
            "All old weapon slots clear and another owned weapon becomes active");
        var drop = WeaponDrop; Check(drop != null, "One physical weapon pickup is created");
        yield return .4f; Check(drop != null && !drop.CanCollect(giver), "Owner magnet cannot reclaim its own drop");
        receiver.capacity = receiver.UsedItemSlots;
        Check(!drop.TryCollect(second.GetComponent<Collider>()) && drop != null, "A full teammate inventory cannot consume the drop");
        receiver.capacity += 5;
        second.GetComponent<PlayerHealth>().GetType().GetProperty("IsDead").GetSetMethod(true).Invoke(second.GetComponent<PlayerHealth>(), new object[] { true });
        Check(!drop.TryCollect(second.GetComponent<Collider>()), "Dead teammates cannot collect equipment");
        second.GetComponent<PlayerHealth>().ResetHealth();
        Move(second, drop.transform.position + Vector3.right * 1.2f); second.GetComponent<PickupMagnet>().enabled = true;
        wait = Wait(() => receiver.Weapons.Any(w => w.itemId == weapon.itemId)); while (wait.MoveNext()) yield return wait.Current;
        var received = receiver.Weapons.First(w => w.itemId == weapon.itemId);
        Check(JsonUtility.ToJson(received) == weaponData && receiver.Weapons.Count(w => w.itemId == weapon.itemId) == 1,
            "Physical teammate pickup retains every weapon stat and identity exactly once");
        Check(drop == null, "Collected pickup disappears");
        receiver.ApplyCoopCommand("equip", receiver.Weapons.ToList().IndexOf(received), 1);
        Check(receiver.ActiveWeapon == received && second.GetComponent<PlayerShooter>().projectileDamage == weapon.damage,
            "Teammate can equip and use the transferred weapon");
        giver.ApplyCoopCommand("dropWeapon", 0, 0, weapon.itemId);
        Check(WeaponDrop == null && giver.Weapons.Count == count - 1, "Replayed drop identity cannot remove a different item");
        Move(second, new Vector3(12, 1, 0));
        var armor = Armor(); Check(giver.AddArmor(armor), "Giver owns transferable armor"); giver.EquipArmor(armor);
        var health = first.GetComponent<PlayerHealth>(); float baseArmor = health.MaxArmor - armor.armorBonus;
        string armorData = JsonUtility.ToJson(armor);
        Select(giver, armor); yield return .3f; Capture("armor-button"); Click(giver, "Drop Armor");
        var armorDrop = ArmorDrop;
        Check(armorDrop != null && !giver.Armors.Contains(armor) && giver.EquippedArmorSlots.All(a => a != armor) &&
            Mathf.Approximately(health.MaxArmor, baseArmor), "Dropping equipped armor removes its slot and armor bonus");
        Check(!armorDrop.CanCollect(giver), "Armor owner is protected from immediate re-attraction");
        Move(second, armorDrop.transform.position + Vector3.right * 1.2f);
        wait = Wait(() => receiver.Armors.Any(a => a.itemId == armor.itemId)); while (wait.MoveNext()) yield return wait.Current;
        var receivedArmor = receiver.Armors.First(a => a.itemId == armor.itemId);
        Check(JsonUtility.ToJson(receivedArmor) == armorData, "Physical teammate armor pickup retains all values");
        float teammateBase = second.GetComponent<PlayerHealth>().MaxArmor;
        receiver.EquipArmor(receivedArmor);
        Check(Mathf.Approximately(second.GetComponent<PlayerHealth>().MaxArmor, teammateBase + armor.armorBonus), "Teammate equips the donated armor bonus");
        Move(second, new Vector3(12, 1, 0)); Invoke(giver, "SetOpen", false);
        var last = giver.Weapons[0]; Check(giver.DropWeapon(last), "The final owned weapon can be dropped");
        Check(giver.Weapons.Count == 0 && giver.ActiveWeapon == null && giver.EquippedSlots.All(w => w == null), "An empty inventory has no stale equipped weapon");
        first.GetComponent<PlayerShooter>().Fire();
        Check(Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length == 0, "An unarmed player cannot fire the old weapon");
        var lastDrop = WeaponDrop; Move(first, new Vector3(10, 1, 0)); yield return .3f;
        Check(lastDrop.CanCollect(giver), "Owner can reclaim after leaving pickup range");
        Move(first, lastDrop.transform.position + Vector3.right * 1.2f);
        wait = Wait(() => giver.Weapons.Any(w => w.itemId == last.itemId)); while (wait.MoveNext()) yield return wait.Current;
        Check(giver.ActiveWeapon != null && giver.ActiveWeapon.itemId == last.itemId, "Reclaiming re-equips an unarmed player's weapon");
        var saved = giver.CaptureProgress(); giver.RestoreProgress(saved);
        Check(giver.Weapons[0].itemId == last.itemId, "Save and restore preserve transfer identity");
        Freeze(first, new Vector3(0, 1, 0));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 1, .75f); wall.transform.localScale = new Vector3(4, 3, .1f);
        Physics.SyncTransforms(); count = giver.Weapons.Count;
        Check(!giver.DropWeapon(giver.Weapons[0]) && giver.Weapons.Count == count && WeaponDrop == null, "A blocked drop leaves the equipment owned");
        Object.Destroy(wall);
        Check(errors.Count == 0, "No unexpected local runtime errors: " + string.Join("; ", errors));
    }

    static IEnumerator Host()
    {
        Check(CloudAccountService.EnsureInstance().EnterGuest(), "Host uses an isolated guest session");
        Check(CoopSession.StartHost("Map1", "breaker", 0, "transfer", out var error), "Independent TCP host starts: " + error);
        var wait = Wait(() => CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading); while (wait.MoveNext()) yield return wait.Current;
        var host = LocalActor; Freeze(host, new Vector3(0, 1, 0));
        File.WriteAllText(Marker("port"), CoopSession.Instance.ListeningPort.ToString());
        wait = Wait(() => CoopSession.Instance.PlayerCount == 2); while (wait.MoveNext()) yield return wait.Current;
        var remote = Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None).First(a => !a.Local);
        Freeze(remote, new Vector3(12, 1, 0));
        var giver = host.GetComponent<WeaponInventory>(); var receiver = remote.GetComponent<WeaponInventory>();
        var weapon = Weapon(); Check(giver.AddWeapon(weapon) && giver.DropWeapon(weapon), "Host drops a real owned weapon");
        var drop = WeaponDrop; File.WriteAllText(Marker("offer"), JsonUtility.ToJson(weapon));
        File.WriteAllText(Marker("drop"), drop.gameObject.GetInstanceID().ToString());
        wait = Wait(() => File.Exists(Marker("seen"))); while (wait.MoveNext()) yield return wait.Current;
        Move(remote, drop.transform.position + Vector3.right * 1.2f);
        wait = Wait(() => receiver.Weapons.Any(w => w.itemId == weapon.itemId)); while (wait.MoveNext()) yield return wait.Current;
        Check(JsonUtility.ToJson(receiver.Weapons.First(w => w.itemId == weapon.itemId)) == JsonUtility.ToJson(weapon), "Host physics awards the complete weapon to the connected teammate");
        wait = Wait(() => WeaponDrop != null && !receiver.Weapons.Any(w => w.itemId == weapon.itemId)); while (wait.MoveNext()) yield return wait.Current;
        var returnedDrop = WeaponDrop;
        Check(!returnedDrop.CanCollect(receiver), "Client-requested drop is protected from its owner's host magnet");
        wait = Wait(() => File.Exists(Marker("replay"))); while (wait.MoveNext()) yield return wait.Current; yield return .4f;
        Check(Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None).Count(p => p.GetComponent<DroppedEquipmentPickup>() != null) == 1,
            "Replayed client requests cannot duplicate gear or drop the next inventory item");
        Check(receiver.Weapons.Count == 1, "Replay retains the teammate's starter weapon");
        Move(host, returnedDrop.transform.position + Vector3.right * 1.2f);
        wait = Wait(() => giver.Weapons.Any(w => w.itemId == weapon.itemId)); while (wait.MoveNext()) yield return wait.Current;
        Check(JsonUtility.ToJson(giver.Weapons.First(w => w.itemId == weapon.itemId)) == JsonUtility.ToJson(weapon), "Host reclaims the client's drop with unchanged weapon stats");
        File.WriteAllText(Marker("weapon-return"), "ready");
        Move(host, new Vector3(15, 1, 0)); var armor = Armor(); Check(receiver.AddArmor(armor), "Host supplies armor to the remote actor");
        File.WriteAllText(Marker("armor-ready"), JsonUtility.ToJson(armor));
        float baseArmor = remote.GetComponent<PlayerHealth>().MaxArmor;
        wait = Wait(() => receiver.EquippedArmorSlots.Contains(armor)); while (wait.MoveNext()) yield return wait.Current;
        Check(Mathf.Approximately(remote.GetComponent<PlayerHealth>().MaxArmor, baseArmor + armor.armorBonus), "Client armor equip is authoritative on the host");
        File.WriteAllText(Marker("armor-equipped"), "ready");
        wait = Wait(() => ArmorDrop != null && receiver.Armors.Count == 0); while (wait.MoveNext()) yield return wait.Current;
        Check(receiver.EquippedArmorSlots.All(a => a == null) && Mathf.Approximately(remote.GetComponent<PlayerHealth>().MaxArmor, baseArmor), "Client armor drop authoritatively removes its equipped bonus");
        Move(host, ArmorDrop.transform.position + Vector3.right * 1.2f);
        wait = Wait(() => giver.Armors.Any(a => a.itemId == armor.itemId)); while (wait.MoveNext()) yield return wait.Current;
        Check(JsonUtility.ToJson(giver.Armors.First(a => a.itemId == armor.itemId)) == JsonUtility.ToJson(armor), "Connected teammate's armor reaches the host intact");
        File.WriteAllText(Marker("armor-drop"), "ready");
        wait = Wait(() => File.Exists(Marker("done"))); while (wait.MoveNext()) yield return wait.Current;
        Check(errors.Count == 0, "No unexpected host runtime errors: " + string.Join("; ", errors));
    }

    static IEnumerator Client()
    {
        Check(CloudAccountService.EnsureInstance().EnterGuest(), "Client uses an isolated guest session");
        var wait = Wait(() => File.Exists(Marker("port"))); while (wait.MoveNext()) yield return wait.Current;
        bool joined = false; string failure = null;
        CoopSession.Join("Map1", "sentinel", "127.0.0.1", int.Parse(File.ReadAllText(Marker("port"))), "transfer", () => joined = true, message => failure = message);
        wait = Wait(() => failure != null || joined && CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading); while (wait.MoveNext()) yield return wait.Current;
        Check(failure == null && CoopSession.IsClient, "Independent Unity client joins the real TCP host: " + failure);
        var actor = LocalActor; var inventory = actor.GetComponent<WeaponInventory>();
        wait = Wait(() => File.Exists(Marker("offer")) && File.Exists(Marker("drop"))); while (wait.MoveNext()) yield return wait.Current;
        var weapon = JsonUtility.FromJson<WeaponItem>(File.ReadAllText(Marker("offer")));
        string worldName = "Shared Entity " + File.ReadAllText(Marker("drop"));
        wait = Wait(() => GameObject.Find(worldName) != null); while (wait.MoveNext()) yield return wait.Current;
        Check(Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None).Length == 0, "Client sees mirrored loot without an independent pickup simulation");
        File.WriteAllText(Marker("seen"), "ready");
        wait = Wait(() => inventory.Weapons.Any(w => w.itemId == weapon.itemId)); while (wait.MoveNext()) yield return wait.Current;
        var received = inventory.Weapons.First(w => w.itemId == weapon.itemId);
        Check(JsonUtility.ToJson(received) == JsonUtility.ToJson(weapon), "Client snapshot retains the donated weapon's name, identity and stats");
        inventory.ApplyCoopCommand("equip", inventory.Weapons.ToList().IndexOf(received), 1);
        wait = Wait(() => inventory.ActiveWeapon != null && inventory.ActiveWeapon.itemId == weapon.itemId); while (wait.MoveNext()) yield return wait.Current;
        Check(actor.GetComponent<PlayerShooter>().projectileDamage == weapon.damage, "The receiving client equips the donated weapon");
        Invoke(inventory, "SetOpen", true); Select(inventory, inventory.Weapons.First(w => w.itemId == weapon.itemId)); yield return .3f;
        int before = inventory.Weapons.Count; Click(inventory, "Drop Weapon");
        Check(inventory.Weapons.Count == before, "Client click waits for host confirmation without optimistic item removal");
        wait = Wait(() => !inventory.Weapons.Any(w => w.itemId == weapon.itemId)); while (wait.MoveNext()) yield return wait.Current;
        Check(inventory.ActiveWeapon != null && inventory.ActiveWeapon.itemId != weapon.itemId && inventory.EquippedSlots.All(w => w == null || w.itemId != weapon.itemId),
            "Host snapshot replaces the dropped active weapon and clears its client slots");
        CoopSession.Command(inventory, "dropWeapon", 0, 0, weapon.itemId); File.WriteAllText(Marker("replay"), "ready");
        Invoke(inventory, "SetOpen", false);
        wait = Wait(() => File.Exists(Marker("weapon-return")) && File.Exists(Marker("armor-ready"))); while (wait.MoveNext()) yield return wait.Current;
        var armor = JsonUtility.FromJson<ArmorItem>(File.ReadAllText(Marker("armor-ready")));
        wait = Wait(() => inventory.Armors.Any(a => a.itemId == armor.itemId)); while (wait.MoveNext()) yield return wait.Current;
        inventory.EquipArmor(inventory.Armors.First(a => a.itemId == armor.itemId));
        wait = Wait(() => File.Exists(Marker("armor-equipped")) && inventory.EquippedArmorSlots.Any(a => a != null && a.itemId == armor.itemId)); while (wait.MoveNext()) yield return wait.Current;
        Invoke(inventory, "SetOpen", true); Select(inventory, inventory.Armors.First(a => a.itemId == armor.itemId)); yield return .3f;
        Capture("client-armor-button"); Click(inventory, "Drop Armor");
        wait = Wait(() => inventory.Armors.Count == 0 && File.Exists(Marker("armor-drop"))); while (wait.MoveNext()) yield return wait.Current;
        Check(inventory.EquippedArmorSlots.All(a => a == null), "Client confirms dropped armor is absent from inventory and slots");
        Check(errors.Count == 0, "No unexpected client runtime errors: " + string.Join("; ", errors));
        File.WriteAllText(Marker("done"), "ready");
    }
}
