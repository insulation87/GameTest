using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>A peaceful scene with its own walker; combat and map save components never spawn here.</summary>
public sealed partial class TradingHubController : MonoBehaviour
{
    [SerializeField] string gameSceneName = "Map1";
    static readonly string[] MerchantNames = { "鐵匠 · 武器商", "裁甲師 · 防具商", "藥師 · 補給商" };
    static readonly string[] MerchantLines = {
        "選一把趁手的武器吧。相符武器能發揮角色的完整威力。",
        "頭盔、盔甲、褲子與鞋子，為下一場冒險做好準備。",
        "補充血包，或替背包多留一點空間。" };
    static readonly Vector3[] MerchantPositions = { new Vector3(-7, 0, 3), new Vector3(7, 0, 3), new Vector3(-7, 0, -5) };
    public static Vector3 PortalPosition(int index) => new Vector3(-9 + Mathf.Clamp(index, 0, 3) * 6, 0, 11);
    const float InteractionDistance = 3.6f;
    CharacterController walker;
    Animation walkingAnimation;
    Camera view;
    readonly System.Collections.Generic.List<Transform> portalRunes = new System.Collections.Generic.List<Transform>();
    CloudAccountService account;
    TradingHubStock stock;
    string profilePath;
    Text wallet, prompt, tradeStatus, shopWallet, destinationLabel;
    readonly System.Collections.Generic.List<Button> offerButtons = new System.Collections.Generic.List<Button>();
    Button interactButton;
    RectTransform canvasRoot, modal, content;
    HubMerchant openMerchant;
    bool portalOpen;
    bool initialized;
    int openPortal = -1;
    int nearby = -1;
    public TradingHubStock Stock => stock;
    public Transform Visitor => walker != null ? walker.transform : null;

    void Start()
    {
        account = CloudAccountService.EnsureInstance();
        if (CoopSession.InCamp && account.CanPlay) { StartCoopCamp(); return; }
        if (!account.CanPlay || CoopSession.IsMultiplayer) { GameSceneFlow.LoadLogin(); return; }
        profilePath = GameProgressSave.SavePath;
        string character = CharacterRoster.ConsumeSelection().Id;
        string destination = ExpeditionMaps.TakePreparation();
        if (destination == null && GameProgressSave.TryRead(profilePath, out var progress, out _) && ExpeditionMaps.IsMap(progress.scene)) destination = progress.scene;
        stock = new TradingHubStock(profilePath, GameProgressSave.StartFresh, character, destination ?? gameSceneName);
        GameProgressSave.StartFresh = false;
        account.Changed += AccountChanged;
        Time.timeScale = GameSceneFlow.IsLoading ? 0 : 1;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        BuildCourtyard(); BuildVisitor(); BuildUI();
        BuildSoloPreparationUI();
        RefreshWallet();
        initialized = true;
    }

    bool CanOperate => initialized && stock != null && account != null && account.CanPlay && !account.IsBusy &&
        GameProgressSave.SavePath == profilePath && !GameSceneFlow.IsLoading;
    void AccountChanged()
    {
        if (account == null || !account.CanPlay || GameProgressSave.SavePath != profilePath) GameSceneFlow.LoadLogin();
    }
    void Update()
    {
        if (CoopSession.InCamp) { UpdateCoopCamp(); return; }
        if (!CanOperate) return;
        if (HandleSoloPreparationInput()) return;
        Keyboard keys = Keyboard.current;
        if (keys != null && keys.escapeKey.wasPressedThisFrame)
        {
            if (modal.gameObject.activeSelf) CloseDialog(); else ReturnToMenu();
            return;
        }
        if (!modal.gameObject.activeSelf)
        {
            Vector3 move = Vector3.zero;
            if (keys != null) {
                move.x = (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1 : 0) - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1 : 0);
                move.z = (keys.wKey.isPressed || keys.upArrowKey.isPressed ? 1 : 0) - (keys.sKey.isPressed || keys.downArrowKey.isPressed ? 1 : 0);
            }
            move = Vector3.ClampMagnitude(move, 1);
            walker.Move((move * 5.5f + Vector3.down * 12) * Time.deltaTime);
            if (move.sqrMagnitude > .01f) walker.transform.rotation = Quaternion.Slerp(walker.transform.rotation, Quaternion.LookRotation(move), Time.deltaTime * 14);
            walkingAnimation.CrossFade(move.sqrMagnitude > .01f ? "Walk" : "Idle", .12f);
            RefreshNearby();
            if (keys != null && keys.fKey.wasPressedThisFrame) Interact();
        }
        foreach (var rune in portalRunes) if (rune != null) rune.Rotate(Vector3.forward, Time.deltaTime * 22, Space.Self);
    }
    void LateUpdate()
    {
        if (walker == null || view == null) return;
        Vector3 focus = new Vector3(walker.transform.position.x * .15f, 0, 2 + Mathf.Clamp(walker.transform.position.z * .25f, -1, 2));
        view.transform.position = focus + new Vector3(0, 20, -19);
        view.transform.LookAt(focus);
    }
    void RefreshNearby()
    {
        nearby = -1; float distance = InteractionDistance;
        for (int i = 0; i < MerchantPositions.Length + ExpeditionMaps.All.Length; i++) {
            Vector3 position = i < MerchantPositions.Length ? MerchantPositions[i] : PortalPosition(i - MerchantPositions.Length);
            float candidate = Vector2.Distance(new Vector2(Visitor.position.x, Visitor.position.z), new Vector2(position.x, position.z));
            if (candidate <= distance) { distance = candidate; nearby = i; }
        }
        interactButton.interactable = nearby >= 0;
        prompt.text = nearby < 0 ? "購物後走向北側四座傳送門，選擇想去的地圖" : nearby >= MerchantPositions.Length ?
            "F · 前往「" + ExpeditionMaps.All[nearby - MerchantPositions.Length].Name + "」 · 3 關" : "F · 與" + MerchantNames[nearby] + "交易";
    }
    public void Interact()
    {
        if (!CanOperate || modal.gameObject.activeSelf) return;
        RefreshNearby();
        if (nearby < 0) return;
        portalOpen = nearby >= MerchantPositions.Length;
        openPortal = portalOpen ? nearby - MerchantPositions.Length : -1;
        if (portalOpen && stock.RequiresCampPreparation) { OpenSoloPreparation(); return; }
        modal.gameObject.SetActive(true);
        walkingAnimation.CrossFade("Idle", .1f);
        if (portalOpen) ShowPortal(); else { openMerchant = (HubMerchant)nearby; ShowShop(); }
        GameAudio.UI(GameSound.Open);
    }
    public void CloseDialog()
    {
        if (modal == null) return;
        modal.gameObject.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        GameAudio.UI(GameSound.Close);
    }
    public void Buy(int index)
    {
        if (!CanOperate || portalOpen || !modal.gameObject.activeSelf) return;
        bool success = stock.Buy(openMerchant, index, out string message);
        tradeStatus.text = message;
        tradeStatus.color = success ? GameUITheme.Teal : GameUITheme.Gold;
        RefreshWallet();
        RefreshOffers();
        GameAudio.UI(success ? GameSound.Coins : GameSound.Denied);
    }
    public void EnterPortal()
    {
        if (!CanOperate || !portalOpen || !modal.gameObject.activeSelf) return;
        if (stock.RequiresCampPreparation) { CloseDialog(); OpenSoloPreparation(); return; }
        string destination = ExpeditionMaps.All[openPortal].Scene;
        if (!Application.CanStreamedLevelBeLoaded(destination)) {
            tradeStatus.text = "目的地尚未加入 Build Settings，請先返回營地。"; return;
        }
        if (stock.IsPreparing) {
            if (!stock.SelectDestination(destination, out string message)) { tradeStatus.text = message; return; }
        } else if (stock.Destination != destination) {
            var next = new TradingHubStock(profilePath, true, stock.CharacterId, destination);
            if (!next.CanTrade) { tradeStatus.text = next.Notice; return; }
            stock = next;
        }
        RefreshWallet();
        if (stock.RequiresCampPreparation) { CloseDialog(); OpenSoloPreparation(); return; }
        stock.PrepareDeparture();
        if (GameSceneFlow.Load(destination)) GameAudio.UI(GameSound.StartGame);
    }
    public void ReturnToMenu()
    {
        if (!CanOperate) return;
        GameProgressSave.StartFresh = false; CharacterRoster.ClearPending();
        GameSceneFlow.Load(GameSceneFlow.MenuScene);
    }
    void RefreshWallet()
    {
        wallet.text = $"金幣  {stock.Inventory.gold}　｜　背包 {stock.UsedSlots}/{stock.Inventory.capacity}\n血包 {stock.Inventory.healthPacks}　·　{CharacterRoster.Find(stock.CharacterId).Name}";
        if (destinationLabel != null) destinationLabel.text = stock.IsPreparing ? "準備中 · 北側四座門可自由選擇地圖" :
            "冒險進度：" + ExpeditionMaps.ForScene(stock.Destination).Name + " · 3 關";
        if (shopWallet != null) shopWallet.text = $"金幣 {stock.Inventory.gold}　｜　背包 {stock.UsedSlots}/{stock.Inventory.capacity}　｜　累計購買武器 {stock.WeaponPurchases} 把";
    }
    void RefreshOffers()
    {
        if (portalOpen || offerButtons.Count == 0) return;
        var offers = TradingHubStock.Catalog[(int)openMerchant];
        for (int i = 0; i < offerButtons.Count; i++) {
            var offer = offers[i]; bool unlocked = stock.IsUnlocked(offer);
            string match = offer.Goods == HubGoods.Weapon && offer.Item == (int)CharacterWeapons.ForCharacter(stock.CharacterId) ? " ★ 相符" : "";
            string suffix = unlocked ? offer.Price + " 金幣" : $"未解鎖 · 再購買 {WeaponShopProgress.RequiredPurchases((WeaponType)offer.Item, stock.CharacterId) - stock.WeaponPurchases} 把";
            offerButtons[i].GetComponentInChildren<Text>().text = offer.Name + match + "\n" + suffix;
            offerButtons[i].interactable = unlocked;
        }
    }

    void BuildUI()
    {
        if (EventSystem.current == null) {
            var events = new GameObject("Camp Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var node = new GameObject("Camp Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        node.transform.SetParent(transform, false);
        node.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        GameUITheme.FitCanvas(node.GetComponent<CanvasScaler>()); canvasRoot = node.GetComponent<RectTransform>();
        var heading = AccountLoginPanel.Box(canvasRoot, "Camp Heading", Vector2.zero, new Vector2(530, 200), GameUITheme.Panel);
        heading.anchorMin = heading.anchorMax = new Vector2(0, 1); heading.pivot = new Vector2(0, 1); heading.anchoredPosition = new Vector2(24, -24);
        GameUITheme.Frame(heading);
        GameUITheme.Label(heading, "旅人營地", 34, new Vector2(0, 65), new Vector2(480, 56)).color = GameUITheme.Gold;
        wallet = GameUITheme.Label(heading, "", 22, new Vector2(0, 0), new Vector2(480, 76));
        destinationLabel = GameUITheme.Label(heading, "", 21, new Vector2(0, -66), new Vector2(480, 44));
        destinationLabel.color = GameUITheme.Teal;
        var back = MultiplayerMenu.Button(canvasRoot, "返回主選單", Vector2.zero, new Vector2(240, 58), ReturnToMenu).GetComponent<RectTransform>();
        back.anchorMin = back.anchorMax = Vector2.one; back.anchoredPosition = new Vector2(-150, -57);
        var bar = AccountLoginPanel.Box(canvasRoot, "Interaction Bar", Vector2.zero, new Vector2(1280, 138), GameUITheme.Panel);
        bar.anchorMin = bar.anchorMax = new Vector2(.5f, 0); bar.anchoredPosition = new Vector2(0, 95); GameUITheme.Frame(bar);
        prompt = GameUITheme.Label(bar, "", 25, new Vector2(-150, 26), new Vector2(900, 48));
        GameUITheme.Label(bar, "WASD／方向鍵移動　·　F 互動　·　Esc 關閉／返回主選單", 19, new Vector2(-150, -27), new Vector2(900, 40)).color = GameUITheme.Muted;
        interactButton = MultiplayerMenu.Button(bar, "互動  [F]", new Vector2(475, 0), new Vector2(260, 70), Interact);
        modal = AccountLoginPanel.Box(canvasRoot, "Camp Dialog", Vector2.zero, Vector2.zero, new Color(0, 0, 0, .72f));
        GameUITheme.Stretch(modal);
        content = AccountLoginPanel.Box(modal, "Shop Card", Vector2.zero, new Vector2(1500, 900), GameUITheme.Panel);
        GameUITheme.Frame(content, true); modal.gameObject.SetActive(false); RefreshNearby();
    }
    void ClearContent()
    {
        shopWallet = null; offerButtons.Clear();
        for (int i = content.childCount - 1; i >= 0; i--) {
            var child = content.GetChild(i).gameObject;
            if (child.name == "Theme Frame") continue;
            child.SetActive(false); Destroy(child);
        }
    }
    void ShowShop()
    {
        ClearContent();
        GameUITheme.Label(content, MerchantNames[(int)openMerchant], 38, new Vector2(0, 370), new Vector2(1400, 70)).color = GameUITheme.Gold;
        GameUITheme.Label(content, MerchantLines[(int)openMerchant], 22, new Vector2(0, 310), new Vector2(1400, 50));
        string details = openMerchant == HubMerchant.Supplies ? "血包可堆疊佔 1 格；擴充背包不需空格。" : $"商品等級 {stock.ItemLevel} · 購入後請在遊戲內按 E 開啟背包裝備。";
        shopWallet = GameUITheme.Label(content, "", 23, new Vector2(0, 260), new Vector2(1400, 44));
        shopWallet.color = GameUITheme.Teal;
        GameUITheme.Label(content, details + (openMerchant == HubMerchant.Weapons ? " 初始三款與相符武器開放，每購買兩把解鎖一款。" : ""), 18,
            new Vector2(0, -238), new Vector2(1400, 40)).color = GameUITheme.Muted;
        var offers = TradingHubStock.Catalog[(int)openMerchant];
        for (int i = 0; i < offers.Length; i++) {
            int selection = i; var offer = offers[i];
            offerButtons.Add(MultiplayerMenu.Button(content, offer.Name, new Vector2(i % 2 == 0 ? -345 : 345, 180 - i / 2 * 87),
                new Vector2(650, 76), () => Buy(selection)));
        }
        RefreshOffers(); RefreshWallet();
        tradeStatus = GameUITheme.Label(content, stock.Notice, 21, new Vector2(0, -300), new Vector2(1380, 90));
        tradeStatus.color = GameUITheme.Gold;
        MultiplayerMenu.Button(content, "結束交易", new Vector2(0, -395), new Vector2(420, 62), CloseDialog);
    }
    void ShowPortal()
    {
        ClearContent();
        var map = ExpeditionMaps.All[openPortal];
        bool newRun = !stock.IsPreparing && stock.Destination != map.Scene;
        GameUITheme.Label(content, "冒險傳送門 · " + (openPortal + 1), 45, new Vector2(0, 300), new Vector2(1300, 80)).color = GameUITheme.Gold;
        GameUITheme.Label(content, map.Name + " · " + new[]{"入門", "進階", "困難", "極難"}[openPortal] + " · 全 3 關", 32, new Vector2(0, 205), new Vector2(1300, 64));
        GameUITheme.Label(content, "第 1 關 " + map.Stages[0] + "　→　第 2 關 " + map.Stages[1] + "　→　第 3 關 " + map.Stages[2], 23,
            new Vector2(0, 130), new Vector2(1370, 55));
        GameUITheme.Label(content, $"敵人生命 ×{map.Health:0.##}　傷害 ×{map.Damage:0.##}　生成頻率 ×{map.SpawnRate:0.##}", 22,
            new Vector2(0, 65), new Vector2(1300, 50)).color = GameUITheme.Teal;
        GameUITheme.Label(content, newRun ? "你已有另一張地圖的冒險進度。\n進入此門會開始新一輪，重置裝備與關卡進度。\n保留金幣與武器解鎖；取消則不會改動任何進度。" :
            "所有成功交易都已儲存。\n進入後會帶著本輪金幣、裝備與補給" + (stock.IsPreparing ? "開始冒險。" : "接續原地圖進度。") + "\n想去其他地圖，可返回營地走向另一座門。", 25,
            new Vector2(0, -60), new Vector2(1300, 180));
        tradeStatus = GameUITheme.Label(content, "", 23, new Vector2(0, -185), new Vector2(1300, 80));
        MultiplayerMenu.Button(content, newRun ? "開始新一輪並進入" : "進入遊戲", new Vector2(-290, -280), new Vector2(500, 74), EnterPortal);
        MultiplayerMenu.Button(content, "繼續留在營地", new Vector2(290, -280), new Vector2(500, 74), CloseDialog);
    }
    void OnDestroy()
    {
        if (account != null) account.Changed -= AccountChanged;
        foreach (var material in sceneMaterials.Values) if (material != null) Destroy(material);
    }
}
