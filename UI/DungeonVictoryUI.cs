using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Scene-local result screen, opened only by the boss's final death.</summary>
public sealed class DungeonVictoryUI : MonoBehaviour
{
    public static DungeonVictoryUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;
    bool open, leaveWithoutSaveConfirmed;
    float previousTimeScale;
    PlayerHealth player;
    CanvasGroup group;
    RectTransform card;
    Text saveStatus;
    Button continueButton, menuButton;
    GameObject previousSelection;

    public static void Show(PlayerHealth winner)
    {
        if (IsOpen) return;
        if (winner == null) winner = FindFirstObjectByType<PlayerHealth>();
        if (winner == null || winner.IsDead) return;
        if (Instance == null)
            Instance = new GameObject("Dungeon Victory", typeof(RectTransform)).AddComponent<DungeonVictoryUI>();
        Instance.Open(winner);
    }

    void Open(PlayerHealth winner)
    {
        player = winner;
        player.GetComponent<WeaponInventory>()?.CloseMenusForGameplay();
        previousTimeScale = Time.timeScale;
        open = true;
        Time.timeScale = 0f;
        leaveWithoutSaveConfirmed = false;
        Build();
        gameObject.SetActive(true);
        previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        EventSystem.current?.SetSelectedGameObject(null);
        player.GetComponent<PlayerCursorController>()?.RefreshCursor();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SaveProgress();
        StartCoroutine(Reveal());
    }

    IEnumerator Reveal()
    {
        // Unscaled time keeps the entrance running while combat is paused.
        group.interactable = false;
        group.alpha = 0f;
        for (float elapsed = 0; elapsed < .45f; elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.SmoothStep(0, 1, elapsed / .45f);
            group.alpha = t;
            card.localScale = Vector3.one * Mathf.Lerp(.96f, 1f, t);
            yield return null;
        }
        group.alpha = 1f;
        card.localScale = Vector3.one;
        group.interactable = true;
        continueButton.Select();
    }

    bool SaveProgress()
    {
        if (CoopSession.IsClient) { saveStatus.text = "合作進度由房主記錄"; return true; }
        GameProgressSave save = CoopSession.IsMultiplayer ? GameProgressSave.Instance : player != null ? player.GetComponent<GameProgressSave>() : null;
        bool saved = save != null && save.SaveNow();
        saveStatus.text = saved ? "通關進度已儲存" : "進度尚未儲存，可繼續探索後再儲存";
        saveStatus.color = saved ? GameUITheme.Teal : GameUITheme.Gold;
        return saved;
    }

    public void ContinueExploring()
    {
        if (!open || !group.interactable || GameSceneFlow.IsLoading) return;
        GameAudio.UI(GameSound.Close);
        Close();
    }

    public void ReturnToMainMenu()
    {
        if (!open || !group.interactable || GameSceneFlow.IsLoading) return;
        if (!Application.CanStreamedLevelBeLoaded("MainMenu"))
        {
            saveStatus.text = "無法載入主選單，請確認 MainMenu 已加入場景清單";
            saveStatus.color = GameUITheme.Gold;
            return;
        }
        bool settled = CoopSession.IsMultiplayer ? SaveProgress() : player.GetComponent<GameProgressSave>()?.EndRun() == true;
        if (!settled && !leaveWithoutSaveConfirmed)
        {
            leaveWithoutSaveConfirmed = true;
            saveStatus.text = "儲存失敗；再次點擊「返回主選單」可不儲存離開";
            GameAudio.UI(GameSound.Denied);
            return;
        }
        GameAudio.UI(GameSound.Close);
        // Leave this modal owning the pause until scene unloading completes. Other
        // menus may also restore their own pause state during OnDisable.
        GameSceneFlow.Load(GameSceneFlow.MenuScene);
    }

    void Close()
    {
        StopAllCoroutines();
        open = false;
        Time.timeScale = previousTimeScale;
        EventSystem.current?.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy
            ? previousSelection : null);
        if (player != null) player.GetComponent<PlayerCursorController>()?.RefreshCursor();
        gameObject.SetActive(false);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (!open) return;
        open = false;
        Time.timeScale = previousTimeScale;
        if (player != null) player.GetComponent<PlayerCursorController>()?.RefreshCursor();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Build()
    {
        if (group != null)
        {
            UpdateStats();
            return;
        }
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;
        GameUITheme.FitCanvas(gameObject.AddComponent<CanvasScaler>());
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        RectTransform backdrop = Box(transform, "Victory Backdrop", Vector2.zero, Vector2.zero,
            new Color(.008f, .018f, .028f, .90f));
        GameUITheme.Stretch(backdrop);
        card = Box(transform, "Victory Card", Vector2.zero, new Vector2(1000, 780), GameUITheme.Panel);
        GameUITheme.Frame(card, true);
        Box(card, "Top Accent", new Vector2(0, 389), new Vector2(300, 3), GameUITheme.Gold).GetComponent<Image>().raycastTarget = false;
        Label("D U N G E O N   /   C L E A R", 18, 333, 34, GameUITheme.Gold);

        var emblem = new GameObject("Crown Emblem", typeof(RectTransform), typeof(CanvasRenderer), typeof(VictoryCrownGraphic));
        emblem.transform.SetParent(card, false);
        var emblemRect = emblem.GetComponent<RectTransform>();
        emblemRect.anchoredPosition = new Vector2(0, 235);
        emblemRect.sizeDelta = new Vector2(136, 120);
        emblem.GetComponent<VictoryCrownGraphic>().color = GameUITheme.Gold;
        emblem.GetComponent<VictoryCrownGraphic>().raycastTarget = false;

        Label(ExpeditionMaps.Current.Name + "通關", 50, 122, 90, GameUITheme.Ivory).fontStyle = FontStyle.Bold;
        Label("三重形態已擊破，地城首領已被擊敗。", 24, 46, 44, GameUITheme.Muted);
        Box(card, "Divider", new Vector2(0, 2), new Vector2(820, 1), new Color(.82f, .66f, .39f, .35f));
        Stat("Boss Result", -280, "首領討伐", "完成", GameUITheme.Teal);
        Stat("Player Level", 0, "角色等級", "", GameUITheme.Ivory);
        Stat("Player Gold", 280, "持有金幣", "", GameUITheme.Gold);
        Label("勝利屬於你。繼續探索，收集尚未拾取的戰利品。", 20, -150, 40, GameUITheme.Muted);
        continueButton = Button("Continue Exploring", "繼續探索", -215, true, ContinueExploring);
        menuButton = Button("Return to Main Menu", "返回主選單", 215, false, ReturnToMainMenu);
        // Restrict keyboard/controller navigation to this modal.
        continueButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = menuButton };
        menuButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = continueButton };
        saveStatus = Label("", 18, -310, 56, GameUITheme.Teal);
        UpdateStats();
    }

    void UpdateStats()
    {
        card.Find("Player Level/Value").GetComponent<Text>().text = "Lv. " + player.CurrentLevel;
        WeaponInventory inventory = player.GetComponent<WeaponInventory>();
        card.Find("Player Gold/Value").GetComponent<Text>().text = inventory != null ? inventory.Gold.ToString("N0") : "0";
    }

    void Stat(string name, float x, string title, string value, Color color)
    {
        RectTransform box = Box(card, name, new Vector2(x, -65), new Vector2(260, 104), GameUITheme.Recess);
        GameUITheme.Label(box, title, 18, new Vector2(0, 27), new Vector2(236, 28), TextAnchor.MiddleCenter).color = GameUITheme.Muted;
        Text text = GameUITheme.Label(box, value, 32, new Vector2(0, -15), new Vector2(236, 48), TextAnchor.MiddleCenter);
        text.name = "Value";
        text.color = color;
    }

    Text Label(string value, int size, float y, float height, Color color)
    {
        Text text = GameUITheme.Label(card, value, size, new Vector2(0, y), new Vector2(900, height), TextAnchor.MiddleCenter);
        text.color = color;
        return text;
    }

    Button Button(string name, string title, float x, bool primary, UnityEngine.Events.UnityAction action)
    {
        RectTransform rect = Box(card, name, new Vector2(x, -228), new Vector2(400, 68), GameUITheme.Raised);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        GameUITheme.Label(rect, title, 25, Vector2.zero, new Vector2(380, 58), TextAnchor.MiddleCenter);
        GameUITheme.StyleButton(button, primary);
        button.onClick.AddListener(action);
        return button;
    }

    static RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(Image));
        node.transform.SetParent(parent, false);
        var rect = node.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        node.GetComponent<Image>().color = color;
        return rect;
    }
}
