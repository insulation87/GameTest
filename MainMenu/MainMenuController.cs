using UnityEngine;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string gameSceneName = "Map1";

    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;

    [Header("Settings UI")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Dropdown resolutionDropdown;

    private GameSettingsMenu settingsMenu;
    private Button newGameButton;
    private bool confirmingNewGame;
    private CloudAccountService account;
    private Text accountLabel;
    private Button logoutButton, importButton;
    private MultiplayerMenu multiplayerMenu;
    private FriendsMenu friendsMenu;
    private Button friendsButton;
    private bool loadingGame;
    private string destinationScene;

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Start()
    {
        account = CloudAccountService.EnsureInstance();
        if (GameProgressSave.TryRead(GameProgressSave.SavePath, out var progress, out _) && ExpeditionMaps.IsMap(progress.scene)) gameSceneName = progress.scene;
        account.Changed += RefreshAccount;
        if (!account.CanPlay)
        {
            mainPanel.SetActive(false);
            settingsPanel.SetActive(false);
            GameSceneFlow.LoadLogin();
            return;
        }
        MainMenuPresentation.Build(mainPanel.transform.parent, mainPanel, settingsPanel);
        settingsMenu = GameSettingsMenu.Attach(settingsPanel, ShowMainMenu);
        BuildAccountControls();
        MainMenuPresentation.PlaceAccountControls(accountLabel, logoutButton, importButton);
        BuildMultiplayerEntry();
        ShowMainMenu();
    }

    public void JoinGame()
    {
        StartSinglePlayer();
    }

    public void EnterTradingHub()
    {
        StartSinglePlayer();
    }

    void StartSinglePlayer()
    {
        if (loadingGame || GameSceneFlow.IsLoading) return;
        destinationScene = GameSceneFlow.TradingHubScene;
        CoopSession.SinglePlayer();
        if (account == null) account = CloudAccountService.EnsureInstance();
        if (!account.CanPlay || account.IsBusy)
        { GameProgressSave.StartFresh = false; RefreshAccount(); return; }
        if (!Application.CanStreamedLevelBeLoaded(destinationScene))
        {
            GameAudio.UI(GameSound.Denied);
            Debug.LogError($"Cannot load '{destinationScene}'. Add it to Build Settings.");
            return;
        }

        LoadGame();
    }
    public void BeginNewGame()
    {
        if (loadingGame || account == null || !account.CanPlay || account.IsBusy) return;
        GameProgressSave.StartFresh = true;
        StartSinglePlayer();
    }
    void LoadGame()
    {
        if (loadingGame) return;
        loadingGame = GameSceneFlow.Load(destinationScene ?? gameSceneName);
        if (loadingGame) GameAudio.UI(GameSound.StartGame);
    }

    void BuildProgressButtons(bool hasSave, bool hasSaveFile)
    {
        Button start = null;
        foreach (Button button in mainPanel.GetComponentsInChildren<Button>(true))
        {
            Text label = button.GetComponentInChildren<Text>();
            if (label != null && (label.text == "加入遊戲" || label.text == "開始遊戲" || label.text == "繼續遊戲"))
            { start = button; break; }
        }
        if (start == null) return;
        start.GetComponentInChildren<Text>().text = hasSave ? "繼續遊戲" : "開始遊戲";
        if (newGameButton != null) { newGameButton.gameObject.SetActive(hasSaveFile); return; }
        if (!hasSaveFile) return;
        newGameButton = Instantiate(start, mainPanel.transform);
        newGameButton.name = "New Game";
        newGameButton.onClick = new Button.ButtonClickedEvent();
        newGameButton.GetComponent<RectTransform>().anchoredPosition += Vector2.down * 240f;
        GameUITheme.StyleButton(newGameButton);
        newGameButton.GetComponentInChildren<Text>().text = "開始新遊戲";
        newGameButton.onClick.AddListener(() =>
        {
            if (!confirmingNewGame)
            {
                confirmingNewGame = true;
                newGameButton.GetComponentInChildren<Text>().text = "重設本輪，保留金幣";
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(gameSceneName)) return;
            BeginNewGame();
        });
    }

    public void ShowSettings()
    {
        GameAudio.UI(GameSound.Open);
        mainPanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    public void ShowMainMenu()
    {
        if (GameProgressSave.TryRead(GameProgressSave.SavePath, out var progress, out _) && ExpeditionMaps.IsMap(progress.scene)) gameSceneName = progress.scene;
        if (multiplayerMenu != null) multiplayerMenu.gameObject.SetActive(false);
        if (friendsMenu != null) friendsMenu.gameObject.SetActive(false);
        destinationScene = gameSceneName;
        GameProgressSave.StartFresh = false;
        CharacterRoster.ClearPending();
        ExpeditionMaps.ClearPreparation();
        confirmingNewGame = false;
        if (newGameButton != null) newGameButton.GetComponentInChildren<Text>().text = "開始新遊戲";
        if (settingsPanel.activeSelf) GameAudio.UI(GameSound.Close);
        settingsPanel.SetActive(false);
        mainPanel.SetActive(true);
        if (account != null) RefreshAccount();
    }

    void BuildAccountControls()
    {
        accountLabel = AccountLoginPanel.Label(mainPanel.transform, "", 18, new Vector2(0,-263), new Vector2(850,36));
        logoutButton = AccountButton("登出帳號", -321, () => { if (account.IsGuest) account.ExitGuest(); else account.Logout(); });
        importButton = AccountButton("匯入原本機進度", -383, () =>
        {
            LocalAccountProgress.ImportLegacy(out string message);
            RefreshAccount(); accountLabel.text = message;
        });
    }
    Button AccountButton(string label, float y, UnityEngine.Events.UnityAction action)
    {
        var rect = AccountLoginPanel.Box(mainPanel.transform, label, new Vector2(0,y), new Vector2(360,48), new Color(.15f,.25f,.35f));
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        AccountLoginPanel.Label(rect,label,22,Vector2.zero,new Vector2(344,44));
        GameUITheme.StyleButton(button);
        button.onClick.AddListener(action); rect.gameObject.AddComponent<GameUIAudio>(); return button;
    }
    void RefreshAccount()
    {
        if (account == null) return;
        if (!account.CanPlay)
        {
            CharacterRoster.ClearPending();
            mainPanel.SetActive(false); settingsPanel.SetActive(false); GameSceneFlow.LoadLogin(); return;
        }
        if (accountLabel == null) return;
        if (!settingsPanel.activeSelf &&
            (multiplayerMenu == null || !multiplayerMenu.gameObject.activeSelf) &&
            (friendsMenu == null || !friendsMenu.gameObject.activeSelf)) mainPanel.SetActive(true);
        accountLabel.text = account.IsGuest ? "遊客模式　｜同網路合作　｜進度儲存在這台電腦" : "已登入：" + account.Email + "　｜進度儲存在這台電腦";
        logoutButton.GetComponentInChildren<Text>().text = account.IsGuest ? "返回登入帳號" : "登出帳號";
        BuildProgressButtons(GameProgressSave.HasActiveRun,
            System.IO.File.Exists(GameProgressSave.SavePath) || System.IO.File.Exists(GameProgressSave.SavePath + ".bak"));
        foreach (Button button in mainPanel.GetComponentsInChildren<Button>(true)) button.interactable = !account.IsBusy;
        if (friendsButton != null) friendsButton.interactable = account.IsSignedIn && !account.IsBusy;
        importButton.gameObject.SetActive(LocalAccountProgress.CanImportLegacy);
    }

    public void SaveSettings()
    {
        settingsMenu?.ApplyAndClose();
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDestroy()
    {
        if (account != null) account.Changed -= RefreshAccount;
    }

    void BuildMultiplayerEntry()
    {
        CloudRoomInviteService.Ensure();
        var card = AccountLoginPanel.Box(mainPanel.transform, "Mode Selection", new Vector2(480, -20), new Vector2(650, 590), GameUITheme.Panel);
        GameUITheme.Frame(card);
        AccountLoginPanel.Label(card, "選擇旅程／整裝出發", 29, new Vector2(0, 243), new Vector2(620, 52)).color = GameUITheme.Gold;
        AccountLoginPanel.Label(card, "在營地交易、選擇地圖，再由傳送門出發", 21, new Vector2(0, 188), new Vector2(620, 50));
        MultiplayerMenu.Button(card, "旅人營地 · 交易與選圖", new Vector2(0, 100), new Vector2(570, 76), EnterTradingHub);
        var rect = AccountLoginPanel.Box(card, "Multiplayer", new Vector2(0, -12), new Vector2(570, 60), GameUITheme.Raised);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        AccountLoginPanel.Label(rect, "多人模式", 25, Vector2.zero, new Vector2(540, 54)); GameUITheme.StyleButton(button, true);
        button.onClick.AddListener(() =>
        {
            if (!CanStartMultiplayer()) return;
            if (multiplayerMenu == null) multiplayerMenu = MultiplayerMenu.Create(mainPanel.transform.parent, gameSceneName, CanStartMultiplayer, ShowMainMenu);
            mainPanel.SetActive(false); settingsPanel.SetActive(false); multiplayerMenu.Open();
        });
        friendsButton = MultiplayerMenu.Button(card, "好友", new Vector2(0, -90), new Vector2(570, 48), () =>
        {
            if (!account.IsSignedIn || account.IsBusy) return;
            if (friendsMenu == null) friendsMenu = FriendsMenu.Create(mainPanel.transform.parent, ShowMainMenu);
            mainPanel.SetActive(false); friendsMenu.Open();
        });
        AccountLoginPanel.Label(card, "營地使用單人進度；合作模式另存進度\n遊客可使用營地與同網路合作", 18, new Vector2(0, -172), new Vector2(610, 60)).color = GameUITheme.Muted;
    }
    bool CanStartMultiplayer() => !GameSceneFlow.IsLoading && account != null && account.CanPlay && !account.IsBusy;
    void Update()
    {
        if (friendsButton == null || CloudRoomInviteService.Instance == null) return;
        int count = CloudRoomInviteService.Instance.Invites.Length;
        friendsButton.GetComponentInChildren<Text>().text = count > 0 ? "好友 · 房間邀請（" + count + "）" : "好友";
    }
}
