using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-100), DisallowMultipleComponent]
public sealed class GameSettingsMenu : MonoBehaviour
{
    const float ConfirmationSeconds = 15f;
    public static int EscapeConsumedFrame { get; private set; } = -1;
    public bool AwaitingDisplayConfirmation { get; private set; }
    public GameSettingsData Draft => draft;
    GameSettingsData saved, draft;
    Action close;
    bool built, editing, binding;
    double confirmationDeadline;
    List<Vector2Int> resolutions;
    readonly FullScreenMode[] modes = { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };
    readonly GameObject[] pages = new GameObject[4];
    readonly Button[] tabs = new Button[4];
    readonly Dictionary<GameAction, Text> keyLabels = new Dictionary<GameAction, Text>();
    readonly Button[] profiles = new Button[1];
    GameObject captureOverlay;
    Text captureText;
    GameAction captureAction;
    int bindingProfile, captureStartedFrame;
    public bool Capturing { get; private set; }
    Dropdown windowMode, resolution, frameRate;
    Toggle vSync, invertY, backgroundMute;
    Slider sensitivity, fov, master, music, effects;
    Text help, status, confirmationText;
    GameObject confirmation;
    CanvasGroup controls;
    Button confirmButton, applyButton;
    int selectedTab;
    WeaponBuildGuideUI defaultGuide;
    public static void ConsumeEscape() => EscapeConsumedFrame = Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => EscapeConsumedFrame = -1;

    public static GameSettingsMenu Attach(GameObject host, Action onClose, Action openGuide = null)
    {
        GameSettingsMenu menu = host.GetComponent<GameSettingsMenu>();
        if (menu != null) return menu;
        // Keep existing scene references intact; replace their visible controls at runtime.
        foreach (Transform child in host.transform) child.gameObject.SetActive(false);
        menu = host.AddComponent<GameSettingsMenu>();
        menu.close = onClose;
        menu.Build(openGuide);
        menu.built = true;
        if (host.activeInHierarchy) menu.BeginEditing();
        return menu;
    }

    void OnEnable() { if (built) BeginEditing(); }

    public void BeginEditing()
    {
        if (defaultGuide != null) defaultGuide.Hide();
        if (editing) CancelChanges();
        saved = GameSettings.Current.Clone();
        draft = saved.Clone();
        editing = true;
        AwaitingDisplayConfirmation = false;
        confirmation.SetActive(false);
        controls.interactable = true;
        Bind();
        SelectTab(0);
        status.text = "調整後按「套用並儲存」；取消會還原預覽。";
    }

    void Update()
    {
        if (!editing) return;
        if (Capturing)
        {
            if (Time.frameCount <= captureStartedFrame) return;
            if (UnityEngine.InputSystem.Keyboard.current != null ? UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame : Input.GetKeyDown(KeyCode.Escape))
            { EscapeConsumedFrame = Time.frameCount; EndCapture(); status.text = "已取消按鍵設定。"; }
            else if (GameControls.TryCapture(bindingProfile, out int value)) CaptureBinding(value);
            return;
        }
        if (AwaitingDisplayConfirmation)
        {
            double remaining = confirmationDeadline - Time.realtimeSinceStartupAsDouble;
            if (remaining <= 0) { RevertDisplay(); status.text = "未確認顯示變更，已恢復原設定。"; }
            else confirmationText.text = $"保留新的顯示設定？\n{Mathf.CeilToInt((float)remaining)} 秒後自動恢復原設定";
        }
        if (!GameSceneFlow.IsLoading && Input.GetKeyDown(KeyCode.Escape))
        {
            EscapeConsumedFrame = Time.frameCount;
            if (AwaitingDisplayConfirmation) RevertDisplay();
            else CancelAndClose();
        }
    }

    void ReadControls()
    {
        draft.windowMode = modes[windowMode.value];
        Vector2Int size = resolutions[resolution.value];
        draft.width = size.x; draft.height = size.y;
        draft.vSync = vSync.isOn;
        draft.frameRate = GameSettings.FrameRates[frameRate.value];
        draft.sensitivity = sensitivity.value;
        draft.fieldOfView = fov.value;
        draft.invertY = invertY.isOn;
        draft.masterVolume = master.value; draft.musicVolume = music.value; draft.effectsVolume = effects.value;
        draft.muteInBackground = backgroundMute.isOn;
    }

    void Preview()
    {
        if (binding || !editing || AwaitingDisplayConfirmation) return;
        ReadControls();
        var preview = GameSettings.Current.Clone();
        preview.masterVolume = draft.masterVolume; preview.musicVolume = draft.musicVolume; preview.effectsVolume = draft.effectsVolume;
        preview.sensitivity = draft.sensitivity; preview.fieldOfView = draft.fieldOfView;
        preview.invertY = draft.invertY; preview.muteInBackground = draft.muteInBackground;
        GameSettings.Apply(preview, false);
        frameRate.interactable = !draft.vSync;
    }

    public void ApplyAndClose()
    {
        if (!editing || AwaitingDisplayConfirmation || Capturing) return;
        ReadControls();
        draft = GameSettings.Normalize(draft);
        bool changed = GameSettings.DisplayChanged(saved, draft);
        GameSettings.Apply(draft, changed);
        if (changed)
        {
            AwaitingDisplayConfirmation = true;
            confirmationDeadline = Time.realtimeSinceStartupAsDouble + ConfirmationSeconds;
            confirmationText.text = "保留新的顯示設定？\n15 秒後自動恢復原設定";
            confirmation.SetActive(true);
            controls.interactable = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(confirmButton.gameObject);
            return;
        }
        CommitAndClose();
    }

    public void ConfirmDisplay()
    {
        if (!AwaitingDisplayConfirmation) return;
        CommitAndClose();
    }

    void CommitAndClose()
    {
        GameSettings.Save();
        saved = GameSettings.Current.Clone();
        AwaitingDisplayConfirmation = editing = false;
        confirmation.SetActive(false);
        controls.interactable = true;
        GameAudio.UI(GameSound.Confirm);
        close?.Invoke();
    }

    public void RevertDisplay()
    {
        if (!AwaitingDisplayConfirmation) return;
        GameSettings.Apply(saved);
        AwaitingDisplayConfirmation = false;
        confirmation.SetActive(false);
        controls.interactable = true;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(applyButton.gameObject);
        draft = saved.Clone();
        Bind();
        status.text = "已恢復變更前的設定，可以重新調整。";
    }

    public void RestoreDefaults()
    {
        if (!editing || AwaitingDisplayConfirmation) return;
        draft = GameSettings.Defaults();
        Bind(); Preview();
        status.text = "已載入預設值；按「套用並儲存」後生效。";
    }

    public void SelectBindingProfile(int profile)
    {
        if (Capturing) EndCapture();
        bindingProfile = 0;
        RefreshBindings();
    }
    public void BeginCapture(GameAction action)
    {
        if (!editing || AwaitingDisplayConfirmation || !GameControls.Available(bindingProfile, action)) return;
        captureAction = action; captureStartedFrame = Time.frameCount; Capturing = true;
        GameControls.SetRecording(true); controls.interactable = false;
        captureText.text = "設定「" + GameControls.Name(action) + "」\n" +
            (bindingProfile == 2 ? "請按下手把按鈕" : "請按下新的鍵盤按鍵" + (bindingProfile == 0 ? "或滑鼠按鈕" : "")) + "\nEsc 取消";
        captureOverlay.SetActive(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }
    public void CaptureBinding(int value)
    {
        if (!Capturing) return;
        bool accepted = GameControls.TryBind(draft.bindings, bindingProfile, captureAction, value, out string message);
        if (accepted) { EndCapture(); RefreshBindings(); status.text = message; }
        else captureText.text = message + "\n請重新按鍵，或 Esc 取消。";
    }
    void EndCapture()
    {
        if (!Capturing) return;
        Capturing = false; GameControls.SetRecording(false);
        captureOverlay.SetActive(false); controls.interactable = true;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }
    void RefreshBindings()
    {
        if (draft == null) return;
        int row = 0;
        foreach (GameAction action in GameControls.Actions)
        {
            var label = keyLabels[action]; var root = (RectTransform)label.transform.parent.parent;
            bool available = GameControls.Available(bindingProfile, action); root.gameObject.SetActive(available);
            if (!available) continue;
            root.anchoredPosition = new Vector2(row % 2 == 0 ? -240 : 240, -24 - row / 2 * 48);
            label.text = GameControls.Label(bindingProfile, action, draft.bindings); row++;
        }
        bindingContent.sizeDelta = new Vector2(0, Mathf.Ceil(row / 2f) * 48);
        bindingScroll.verticalNormalizedPosition = 1;
        for (int i = 0; i < profiles.Length; i++) GameUITheme.StyleButton(profiles[i], i == bindingProfile);
    }
    RectTransform bindingContent;
    ScrollRect bindingScroll;

    public void CancelAndClose() { CancelChanges(); close?.Invoke(); }
    void CancelChanges()
    {
        if (!editing) return;
        EndCapture();
        GameSettings.Apply(saved, AwaitingDisplayConfirmation);
        AwaitingDisplayConfirmation = editing = false;
        if (confirmation != null) confirmation.SetActive(false);
        if (controls != null) controls.interactable = true;
    }
    void OnDisable() { CancelChanges(); if (defaultGuide != null) defaultGuide.Hide(); }

    void Bind()
    {
        binding = true;
        resolutions = GameSettings.Resolutions();
        var size = new Vector2Int(draft.width, draft.height);
        if (!resolutions.Contains(size)) resolutions.Add(size);
        resolution.ClearOptions();
        var labels = new List<string>();
        foreach (Vector2Int entry in resolutions) labels.Add($"{entry.x} × {entry.y}");
        resolution.AddOptions(labels);
        resolution.SetValueWithoutNotify(resolutions.IndexOf(size));
        windowMode.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(modes, draft.windowMode)));
        frameRate.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(GameSettings.FrameRates, draft.frameRate)));
        vSync.SetIsOnWithoutNotify(draft.vSync);
        invertY.SetIsOnWithoutNotify(draft.invertY);
        backgroundMute.SetIsOnWithoutNotify(draft.muteInBackground);
        sensitivity.SetValueWithoutNotify(draft.sensitivity); fov.SetValueWithoutNotify(draft.fieldOfView);
        master.SetValueWithoutNotify(draft.masterVolume); music.SetValueWithoutNotify(draft.musicVolume); effects.SetValueWithoutNotify(draft.effectsVolume);
        foreach (Slider slider in new[] { sensitivity, fov, master, music, effects }) RefreshSliderValue(slider);
        foreach (Toggle toggle in new[] { vSync, invertY, backgroundMute }) RefreshToggleValue(toggle);
        frameRate.interactable = !draft.vSync;
        windowMode.RefreshShownValue(); resolution.RefreshShownValue(); frameRate.RefreshShownValue();
        binding = false;
        RefreshBindings();
    }

    public void SelectTab(int index)
    {
        if (Capturing) EndCapture();
        selectedTab = Mathf.Clamp(index, 0, 3);
        for (int i = 0; i < pages.Length; i++) { pages[i].SetActive(i == selectedTab); GameUITheme.StyleButton(tabs[i], i == selectedTab); }
        help.text = selectedTab == 0 ? "調整視窗與畫面流暢度；垂直同步開啟時，FPS 由螢幕更新率決定。\n" +
            (Application.isEditor ? "視窗模式與解析度會在遊戲執行檔中生效。" : "顯示變更需在 15 秒內確認，否則會恢復原設定。") :
            selectedTab == 1 ? "靈敏度與視野可即時預覽；第一、第三人稱共用靈敏度。\n反轉垂直滑鼠僅影響第一人稱。" :
            selectedTab == 2 ? "音量可即時預覽；主音量控制所有聲音。\n關閉背景靜音後，切換到其他視窗仍會播放聲音。" :
            "點擊按鍵後按下新的鍵盤或滑鼠按鍵；重複鍵位會交換。\n捲動查看其他操作；Esc 保留為取消與返回。";
    }

    void Build(Action openGuide)
    {
        RectTransform card = Box(transform, "Settings Card", Vector2.zero, new Vector2(1060, 780), GameUITheme.Panel);
        GameUITheme.Frame(card);
        RectTransform body = Box(card, "Settings Controls", Vector2.zero, new Vector2(1060, 780), Color.clear);
        controls = body.gameObject.AddComponent<CanvasGroup>();
        GameUITheme.Label(body, "遊戲設定", 38, new Vector2(0, 326), new Vector2(940, 64), TextAnchor.MiddleCenter).color = GameUITheme.Gold;
        string[] names = { "畫面與視窗", "滑鼠與視野", "音量", "自訂鍵位" };
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            tabs[i] = Button(body, names[i], new Vector2((i - 2f) * 190, 256), new Vector2(180, 52), () => SelectTab(index));
            pages[i] = Box(body, names[i] + " Page", Vector2.zero, new Vector2(960, 320), Color.clear).gameObject;
        }
        help = GameUITheme.Label(body, "", 19, new Vector2(0, 187), new Vector2(930, 66));
        help.color = GameUITheme.Muted;
        windowMode = Dropdown(pages[0].transform, "視窗模式", 111, new List<string> { "視窗", "無邊框全螢幕", "獨佔全螢幕" });
        resolution = Dropdown(pages[0].transform, "解析度", 43, new List<string> { "目前解析度" });
        vSync = Toggle(pages[0].transform, "垂直同步", -25);
        var rates = new List<string>();
        foreach (int rate in GameSettings.FrameRates) rates.Add(rate < 0 ? "無上限" : rate + " FPS");
        frameRate = Dropdown(pages[0].transform, "FPS 上限", -93, rates);
        sensitivity = Slider(pages[1].transform, "滑鼠靈敏度", 111, .2f, 3f);
        invertY = Toggle(pages[1].transform, "反轉垂直滑鼠", 43);
        fov = Slider(pages[1].transform, "視野角度（FOV）", -25, 55f, 110f);
        fov.wholeNumbers = true;
        master = Slider(pages[2].transform, "主音量", 111, 0f, 1f);
        music = Slider(pages[2].transform, "背景音樂", 43, 0f, 1f);
        effects = Slider(pages[2].transform, "音效與環境音", -25, 0f, 1f);
        backgroundMute = Toggle(pages[2].transform, "切換視窗時靜音", -93);
        BuildBindings(pages[3].transform);
        status = GameUITheme.Label(body, "", 19, new Vector2(0, -183), new Vector2(930, 66));
        status.color = GameUITheme.Muted;
        Button(body, "恢復預設", new Vector2(-350, -274), new Vector2(230, 58), RestoreDefaults);
        Button(body, "取消並返回", new Vector2(-70, -274), new Vector2(230, 58), CancelAndClose);
        applyButton = Button(body, "套用並儲存", new Vector2(260, -274), new Vector2(350, 58), ApplyAndClose, true);
        Button(body, "角色武器與防具", new Vector2(380, 256), new Vector2(180, 52), () =>
        {
            CancelChanges();
            if (openGuide != null) openGuide();
            else defaultGuide.Open();
        });
        GameUITheme.Label(body, "Esc 返回", 16, new Vector2(360, -342), new Vector2(210, 38), TextAnchor.MiddleRight).color = GameUITheme.Muted;
        confirmation = Box(card, "Display Confirmation", Vector2.zero, new Vector2(1060, 780), GameUITheme.Panel).gameObject;
        GameUITheme.Frame(confirmation.transform);
        confirmationText = GameUITheme.Label(confirmation.transform, "", 28, new Vector2(0, 45), new Vector2(930, 130), TextAnchor.MiddleCenter);
        confirmButton = Button(confirmation.transform, "保留並儲存", new Vector2(-180, -100), new Vector2(320, 64), ConfirmDisplay, true);
        Button(confirmation.transform, "恢復原設定", new Vector2(180, -100), new Vector2(320, 64), RevertDisplay);
        confirmation.SetActive(false);
        captureOverlay = Box(card, "Key Capture", Vector2.zero, new Vector2(1060, 780), GameUITheme.Panel).gameObject;
        GameUITheme.Frame(captureOverlay.transform);
        captureText = GameUITheme.Label(captureOverlay.transform, "", 27, Vector2.zero, new Vector2(940, 220), TextAnchor.MiddleCenter);
        captureOverlay.SetActive(false);
        if (openGuide == null) defaultGuide = WeaponBuildGuideUI.Create(transform, BeginEditing);
    }

    void BuildBindings(Transform page)
    {
        profiles[0] = Button(page, "鍵盤與滑鼠", new Vector2(0, 118), new Vector2(360, 44), () => SelectBindingProfile(0));
        var viewport = Box(page, "Bindings Viewport", new Vector2(0, -34), new Vector2(960, 244), GameUITheme.Recess);
        viewport.gameObject.AddComponent<RectMask2D>();
        bindingScroll = viewport.gameObject.AddComponent<ScrollRect>(); bindingScroll.horizontal = false; bindingScroll.scrollSensitivity = 36;
        bindingScroll.movementType = ScrollRect.MovementType.Clamped; bindingScroll.viewport = viewport;
        bindingContent = Box(viewport, "Bindings Content", Vector2.zero, Vector2.zero, Color.clear);
        bindingContent.anchorMin = new Vector2(0, 1); bindingContent.anchorMax = Vector2.one; bindingContent.pivot = new Vector2(.5f, 1);
        bindingScroll.content = bindingContent;
        foreach (GameAction action in GameControls.Actions)
        {
            var row = Box(bindingContent, GameControls.Name(action), Vector2.zero, new Vector2(460, 44), Color.clear);
            row.anchorMin = row.anchorMax = new Vector2(.5f, 1);
            GameUITheme.Label(row, GameControls.Name(action), 19, new Vector2(-113, 0), new Vector2(222, 42));
            var captured = action;
            var button = Button(row, "", new Vector2(118, 0), new Vector2(212, 40), () => BeginCapture(captured));
            var text = button.GetComponentInChildren<Text>(); text.fontSize = 18; keyLabels[action] = text;
        }
    }

    static RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(Image));
        node.transform.SetParent(parent, false);
        RectTransform rect = node.GetComponent<RectTransform>();
        rect.anchoredPosition = position; rect.sizeDelta = size;
        node.GetComponent<Image>().color = color;
        node.GetComponent<Image>().raycastTarget = color.a > 0f;
        return rect;
    }

    static Button Button(Transform parent, string label, Vector2 position, Vector2 size, Action action, bool primary = false)
    {
        RectTransform rect = Box(parent, label, position, size, GameUITheme.Raised);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        GameUITheme.Label(rect, label, 22, Vector2.zero, size - new Vector2(20, 4), TextAnchor.MiddleCenter);
        GameUITheme.StyleButton(button, primary);
        button.onClick.AddListener(() => action());
        return button;
    }

    Slider Slider(Transform parent, string label, float y, float minimum, float maximum)
    {
        GameUITheme.Label(parent, label, 22, new Vector2(-300, y), new Vector2(300, 52));
        RectTransform root = Box(parent, label + " Slider", new Vector2(130, y), new Vector2(330, 40), Color.clear);
        root.GetComponent<Image>().raycastTarget = true;
        root.GetComponent<CanvasRenderer>().cullTransparentMesh = false;
        RectTransform track = Box(root, "Track", Vector2.zero, new Vector2(330, 8), GameUITheme.Recess);
        RectTransform fillArea = Box(root, "Fill Area", Vector2.zero, new Vector2(330, 8), Color.clear);
        RectTransform fill = Box(fillArea, "Fill", Vector2.zero, Vector2.zero, GameUITheme.Teal);
        GameUITheme.Stretch(fill);
        RectTransform handleArea = Box(root, "Handle Area", Vector2.zero, new Vector2(310, 32), Color.clear);
        RectTransform handle = Box(handleArea, "Handle", Vector2.zero, new Vector2(20, 0), GameUITheme.Gold);
        var slider = root.gameObject.AddComponent<Slider>();
        slider.targetGraphic = handle.GetComponent<Image>(); slider.fillRect = fill; slider.handleRect = handle;
        slider.minValue = minimum; slider.maxValue = maximum;
        Text value = GameUITheme.Label(parent, "", 22, new Vector2(375, y), new Vector2(120, 52), TextAnchor.MiddleRight);
        value.name = label + " Value";
        slider.onValueChanged.AddListener(v => { value.text = label == "滑鼠靈敏度" ? $"{v:0.00} 倍" :
            label == "視野角度（FOV）" ? $"{v:0}°" : $"{v * 100:0}%"; Preview(); });
        return slider;
    }

    void RefreshSliderValue(Slider slider)
    {
        slider.onValueChanged.Invoke(slider.value); // binding suppresses the preview, but refreshes the numeric label.
    }

    Toggle Toggle(Transform parent, string label, float y)
    {
        GameUITheme.Label(parent, label, 22, new Vector2(-300, y), new Vector2(300, 52));
        RectTransform root = Box(parent, label + " Toggle", new Vector2(170, y), new Vector2(480, 44), GameUITheme.Raised);
        RectTransform check = Box(root, "Box", new Vector2(-210, 0), new Vector2(28, 28), GameUITheme.Recess);
        RectTransform mark = Box(check, "Checkmark", Vector2.zero, new Vector2(18, 18), GameUITheme.Teal);
        Text state = GameUITheme.Label(root, "", 21, new Vector2(20, 0), new Vector2(360, 42));
        var toggle = root.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = root.GetComponent<Image>(); toggle.graphic = mark.GetComponent<Image>();
        toggle.onValueChanged.AddListener(value => { state.text = value ? "開啟" : "關閉"; Preview(); });
        return toggle;
    }
    void RefreshToggleValue(Toggle toggle) => toggle.onValueChanged.Invoke(toggle.isOn);

    Dropdown Dropdown(Transform parent, string label, float y, List<string> options)
    {
        GameUITheme.Label(parent, label, 22, new Vector2(-300, y), new Vector2(300, 52));
        RectTransform root = Box(parent, label + " Dropdown", new Vector2(170, y), new Vector2(480, 44), GameUITheme.Raised);
        Text caption = GameUITheme.Label(root, "", 22, new Vector2(-8, 0), new Vector2(428, 42));
        GameUITheme.Label(root, "▼", 16, new Vector2(215, 0), new Vector2(30, 42), TextAnchor.MiddleCenter);
        RectTransform template = Box(root, "Template", Vector2.zero, new Vector2(0, 240), GameUITheme.Panel);
        template.anchorMin = Vector2.zero; template.anchorMax = new Vector2(1, 0);
        template.pivot = new Vector2(.5f, 1); template.anchoredPosition = new Vector2(0, -2);
        var scroll = template.gameObject.AddComponent<ScrollRect>();
        RectTransform viewport = Box(template, "Viewport", Vector2.zero, Vector2.zero, Color.clear);
        GameUITheme.Stretch(viewport); viewport.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Box(viewport, "Content", Vector2.zero, new Vector2(0, 42), Color.clear);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
        RectTransform item = Box(content, "Item", Vector2.zero, new Vector2(0, 42), GameUITheme.Raised);
        item.anchorMin = new Vector2(0, .5f); item.anchorMax = new Vector2(1, .5f);
        var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item.GetComponent<Image>();
        Text itemText = GameUITheme.Label(item, "選項", 21, new Vector2(0, 0), new Vector2(448, 42));
        scroll.content = content; scroll.viewport = viewport; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
        template.gameObject.SetActive(false);
        var dropdown = root.gameObject.AddComponent<Dropdown>(); dropdown.targetGraphic = root.GetComponent<Image>();
        dropdown.captionText = caption; dropdown.itemText = itemText; dropdown.template = template;
        dropdown.AddOptions(options);
        dropdown.onValueChanged.AddListener(_ => Preview());
        return dropdown;
    }
}
