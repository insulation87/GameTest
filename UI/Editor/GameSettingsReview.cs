using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit settings checks using a separate preference namespace and isolated scene.</summary>
[InitializeOnLoad]
public static class GameSettingsReview
{
    const string Pending = "GameSettingsReview.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static string output, prefix;
    static Camera camera;
    static GameSettingsMenu menu;
    static GameObject host;
    static GameSettingsReview() { EditorApplication.playModeStateChanged += StateChanged; }
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        assertions = 0;
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        prefix = "SettingsReview." + Guid.NewGuid().ToString("N") + ".";
        checks = Checks();
        deadline = EditorApplication.timeSinceStartup + .1;
        EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext())
            {
                CleanupPreferences();
                File.WriteAllText(Path.Combine(output, "settings-result.txt"), $"PASS {assertions} settings integration checks");
                Debug.Log($"SETTINGS PASS: {assertions} integration checks");
                EditorApplication.update -= Tick; EditorApplication.Exit(0); return;
            }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .08f);
        }
        catch (Exception ex)
        {
            CleanupPreferences();
            File.WriteAllText(Path.Combine(output, "settings-failure.txt"), ex.ToString());
            Debug.LogException(ex); EditorApplication.update -= Tick; EditorApplication.Exit(1);
        }
    }
    static void CleanupPreferences()
    {
        foreach (string key in new[] { "Width", "Height", "WindowMode", "Fullscreen", "Resolution", "MasterVolume",
            "MusicVolume", "EffectsVolume", "MouseSensitivity", "FieldOfView", "InvertY", "VSync", "FrameRate", "MuteInBackground", "BindingsV1" })
            PlayerPrefs.DeleteKey(prefix + key);
        PlayerPrefs.Save();
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++; Debug.Log("PASS " + message);
    }
    static object Get(object o, string field) => o.GetType().GetField(field, Flags).GetValue(o);
    static void Set(object o, string field, object value) => o.GetType().GetField(field, Flags).SetValue(o, value);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Flags).Invoke(o, args);
    static T Control<T>(string field) => (T)Get(menu, field);
    static IEnumerator Capture(string name, bool dropdown = false)
    {
        if (dropdown) { Control<Dropdown>("frameRate").Show(); yield return .15f; }
        var target = new RenderTexture(1280, 720, 24);
        camera.targetTexture = target;
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        foreach (Canvas canvas in canvases)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        }
        yield return .15f;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture before = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
        RenderTexture.active = before;
        if (dropdown) Control<Dropdown>("frameRate").Hide();
        foreach (Canvas canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
    }
    static IEnumerator Checks()
    {
        Object.FindFirstObjectByType<PixelArtDirector>().enabled = false;
        GameSettings.Load(prefix, false);
        GameSettings.Save();
        Check(GameSettings.Current.sensitivity == 1 && GameSettings.Current.frameRate == 120, "An empty settings profile uses useful defaults");
        var sizes = GameSettings.Resolutions();
        Check(sizes.Count > 0 && sizes.Distinct().Count() == sizes.Count, "Resolution choices are never empty or duplicated by refresh rate");
        Check(sizes.Contains(new Vector2Int(GameSettings.Current.width, GameSettings.Current.height)), "An existing custom window size remains selectable");
        var corrupt = GameSettings.Normalize(new GameSettingsData { sensitivity = float.NaN, fieldOfView = 500,
            masterVolume = -5, musicVolume = float.PositiveInfinity, effectsVolume = 4, frameRate = 7,
            width = -100, height = 99999, windowMode = (FullScreenMode)999 });
        Check(corrupt.sensitivity == 1 && corrupt.fieldOfView == 110 && corrupt.masterVolume == 0 && corrupt.musicVolume == 1,
            "Corrupt floating-point settings normalize to valid values");
        Check(corrupt.width == 640 && corrupt.height == 4320 && corrupt.frameRate == 120 && corrupt.windowMode == FullScreenMode.FullScreenWindow,
            "Corrupt display and frame-rate preferences cannot produce invalid settings");
        camera = new GameObject("Settings Camera", typeof(Camera)).GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = GameUITheme.Ink;
        camera.tag = "MainCamera";
        var fpsNode = new GameObject("FPS Settings Camera", typeof(Camera), typeof(FPSCameraLook));
        var tpsNode = new GameObject("TPS Settings Camera", typeof(Camera), typeof(CameraFollowTPS));
        fpsNode.GetComponent<Camera>().enabled = tpsNode.GetComponent<Camera>().enabled = false;
        var data = GameSettings.Current.Clone(); data.fieldOfView = 85;
        GameSettings.Apply(data, false);
        Check(fpsNode.GetComponent<Camera>().fieldOfView == 85 && tpsNode.GetComponent<Camera>().fieldOfView == 85,
            "Both active camera controllers receive field-of-view settings");
        fpsNode.SetActive(false); data.fieldOfView = 90; GameSettings.Apply(data, false); fpsNode.SetActive(true);
        Check(fpsNode.GetComponent<Camera>().fieldOfView == 90, "A camera enabled after settings changes receives the latest field of view");
        data.sensitivity = 1; data.invertY = false; GameSettings.Apply(data, false);
        Vector2 baseline = GameSettings.ScaleMouseDelta(new Vector2(3, 2), 200);
        data.sensitivity = 2; GameSettings.Apply(data, false);
        Check(GameSettings.ScaleMouseDelta(new Vector2(3, 2), 200) == baseline * 2,
            "Doubling sensitivity doubles horizontal and vertical aim displacement");
        data.invertY = true; GameSettings.Apply(data, false);
        Vector2 inverted = GameSettings.ScaleMouseDelta(new Vector2(3, 2), 200);
        Check(inverted.x == baseline.x * 2 && inverted.y == -baseline.y * 2, "Invert-Y affects only vertical aiming");
        data.vSync = true; data.frameRate = 60; GameSettings.Apply(data, false);
        Check(QualitySettings.vSyncCount == 1 && Application.targetFrameRate == -1, "VSync takes precedence over the manual FPS limit");
        data.vSync = false; data.frameRate = 144; GameSettings.Apply(data, false);
        Check(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == 144, "Turning VSync off restores the selected FPS cap");
        GameSettings.Apply(GameSettings.Defaults(), false); GameSettings.Save();

        var canvas = new GameObject("Settings Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; canvas.GetComponent<Canvas>().sortingOrder = 700;
        GameUITheme.FitCanvas(canvas.GetComponent<CanvasScaler>());
        host = new GameObject("Settings Host", typeof(RectTransform)); host.transform.SetParent(canvas.transform, false); host.SetActive(false);
        var legacy = new GameObject("Legacy Setting", typeof(RectTransform)); legacy.transform.SetParent(host.transform, false);
        int closes = 0;
        menu = GameSettingsMenu.Attach(host, () => { closes++; host.SetActive(false); });
        new GameObject("Settings Event System", typeof(EventSystem), typeof(StandaloneInputModule));
        Check(!legacy.activeSelf, "Existing scene controls are preserved and replaced visually");
        Check(GameSettingsMenu.Attach(host, null) == menu && host.GetComponents<GameSettingsMenu>().Length == 1,
            "A settings host cannot accidentally install duplicate menus");
        host.SetActive(true);
        yield return .15f;
        float savedMaster = GameSettings.Current.masterVolume;
        Control<Slider>("master").value = .35f;
        Check(Mathf.Approximately(AudioListener.volume, .35f), "The master-volume slider immediately previews actual listener volume");
        Check(PlayerPrefs.GetFloat(prefix + "MasterVolume") == savedMaster, "A preview does not silently overwrite saved preferences");
        Control<Slider>("sensitivity").value = 1.6f; Control<Slider>("fov").value = 80;
        Check(Mathf.Approximately(GameSettings.Current.sensitivity, 1.6f) && fpsNode.GetComponent<Camera>().fieldOfView == 80,
            "Sensitivity and FOV controls preview the actual shared settings and camera");
        menu.CancelAndClose();
        Check(!host.activeSelf && closes == 1 && GameSettings.Current.masterVolume == savedMaster && GameSettings.Current.sensitivity == 1,
            "Cancel closes the page and restores all previewed controls");
        host.SetActive(true); Control<Slider>("master").value = .42f;
        menu.ApplyAndClose();
        Check(!menu.AwaitingDisplayConfirmation && !host.activeSelf && PlayerPrefs.GetFloat(prefix + "MasterVolume") == .42f,
            "Non-display changes apply and save without an unnecessary confirmation");
        GameSettings.Load(prefix, false);
        Check(GameSettings.Current.masterVolume == .42f, "Reloading settings restores the saved volume");
        host.SetActive(true);
        Control<Slider>("music").value = .27f; Control<Slider>("effects").value = .63f;
        Control<Slider>("sensitivity").value = 1.8f; Control<Slider>("fov").value = 95;
        Control<Toggle>("invertY").isOn = true; Control<Toggle>("backgroundMute").isOn = false;
        Control<Dropdown>("frameRate").value = Array.IndexOf(GameSettings.FrameRates, 165);
        menu.ApplyAndClose(); GameSettings.Load(prefix, false);
        Check(GameSettings.Current.musicVolume == .27f && GameSettings.Current.effectsVolume == .63f &&
            GameSettings.Current.fieldOfView == 95 && Mathf.Abs(GameSettings.Current.sensitivity - 1.8f) < .001f,
            "Independent audio and aiming values survive a full preference reload");
        Check(GameSettings.Current.invertY && !GameSettings.Current.muteInBackground && GameSettings.Current.frameRate == 165,
            "Boolean controls and FPS selections also persist");

        host.SetActive(true);
        GameSettingsData prior = GameSettings.Current.Clone();
        var mode = Control<Dropdown>("windowMode"); mode.value = (mode.value + 1) % 3;
        int savedMode = PlayerPrefs.GetInt(prefix + "WindowMode");
        menu.ApplyAndClose();
        Check(menu.AwaitingDisplayConfirmation && host.activeSelf, "Changing the window mode presents a confirmation overlay");
        Check(!((CanvasGroup)Get(menu, "controls")).interactable && EventSystem.current.currentSelectedGameObject == ((Button)Get(menu, "confirmButton")).gameObject,
            "Display confirmation captures keyboard focus and disables controls behind it");
        Check(PlayerPrefs.GetInt(prefix + "WindowMode") == savedMode, "Display changes remain unsaved until the player confirms");
        IEnumerator shot = Capture("settings-confirmation"); while (shot.MoveNext()) yield return shot.Current;
        menu.RevertDisplay();
        Check(!menu.AwaitingDisplayConfirmation && GameSettings.Current.windowMode == prior.windowMode,
            "Rejecting a display change restores the prior mode and exits confirmation");
        Check(((CanvasGroup)Get(menu, "controls")).interactable, "Reverting a display change restores normal keyboard navigation");
        mode.value = (mode.value + 1) % 3; menu.ApplyAndClose();
        Set(menu, "confirmationDeadline", 0d); Time.timeScale = 0; Call(menu, "Update");
        Check(!menu.AwaitingDisplayConfirmation && GameSettings.Current.windowMode == prior.windowMode,
            "The display countdown reverts even while gameplay is paused");
        Time.timeScale = 1;
        mode.value = (mode.value + 1) % 3; menu.ApplyAndClose();
        FullScreenMode chosenMode = GameSettings.Current.windowMode; menu.ConfirmDisplay(); GameSettings.Load(prefix, false);
        Check(!host.activeSelf && GameSettings.Current.windowMode == chosenMode,
            "Confirming a display change stores the chosen mode for next startup");
        host.SetActive(true); mode.value = (mode.value + 1) % 3; menu.ApplyAndClose(); host.SetActive(false);
        Check(GameSettings.Current.windowMode == chosenMode && !menu.AwaitingDisplayConfirmation,
            "Closing a pending settings page also reverts its unconfirmed display change");
        host.SetActive(true); menu.RestoreDefaults();
        Check(menu.Draft.sensitivity == 1 && menu.Draft.frameRate == 120 && menu.Draft.masterVolume == 1,
            "Restore-defaults repopulates the controls with a complete default preset");
        menu.CancelAndClose();
        Check(GameSettings.Current.fieldOfView == 95 && GameSettings.Current.musicVolume == .27f,
            "Canceling a default reset preserves the saved user settings");
        host.SetActive(true); Control<Toggle>("vSync").isOn = true;
        Check(!Control<Dropdown>("frameRate").interactable, "The UI disables the ignored FPS limit while VSync is selected");
        Control<Toggle>("vSync").isOn = false;
        Check(Control<Dropdown>("frameRate").interactable, "Disabling VSync re-enables FPS selection");

        var testMusic = new GameObject("Audio Setting Review").AddComponent<GameMusic>();
        AudioClip clip = AudioClip.Create("Settings Test Music", 4410, 1, 44100, false);
        ((IDictionary)Get(testMusic, "clips"))[MusicTheme.Menu] = clip;
        Set(testMusic, "desired", MusicTheme.Menu); Set(testMusic, "nextContextCheck", float.MaxValue);
        Control<Slider>("effects").value = 0; Control<Slider>("music").value = 1;
        GameAudio.UI(GameSound.Impact);
        yield return .15f;
        for (int i = 0; i < 30; i++) testMusic.Tick(null, false);
        var musicSources = (AudioSource[])Get(testMusic, "sources");
        Check(musicSources.Any(s => s.volume > 0), "Muting sound effects leaves the separate music voices audible");
        var audio = Object.FindFirstObjectByType<GameAudio>();
        Array voices = (Array)Get(audio, "voices");
        Check(voices.Cast<object>().All(v => ((AudioSource)Get(v, "source")).volume <= .001f), "The effects slider controls the actual sound-effect pool");
        Control<Slider>("music").value = 0; testMusic.Tick(null, false);
        Check(musicSources.All(s => s.volume == 0), "The music slider independently mutes the actual music voices");
        Object.DestroyImmediate(testMusic.gameObject); Object.DestroyImmediate(clip);
        menu.CancelAndClose();

        // Exercise the actual Esc menu integration without a gameplay save or account connection.
        var playerNode = new GameObject("Settings Player"); playerNode.SetActive(false);
        var progress = playerNode.AddComponent<GameProgressSave>(); progress.enabled = false; progress.StoragePath = Path.Combine(output, "unused-settings-save.json");
        var player = playerNode.AddComponent<Player>(); player.enabled = false;
        playerNode.AddComponent<PlayerHealth>();
        var shooter = playerNode.AddComponent<PlayerShooter>(); shooter.enabled = false;
        playerNode.SetActive(true); playerNode.GetComponent<Rigidbody>().isKinematic = true;
        playerNode.GetComponent<PickupMagnet>().enabled = false;
        playerNode.GetComponent<PlayerCursorController>().enabled = false;
        yield return .15f;
        var inventory = playerNode.GetComponent<WeaponInventory>();
        Call(inventory, "SetPaused", true); Call(inventory, "ShowPauseSettings");
        var pauseMenu = (GameSettingsMenu)Get(inventory, "pauseSettingsMenu");
        Check(pauseMenu != null && pauseMenu.gameObject.activeInHierarchy && Time.timeScale == 0,
            "The real Esc settings page opens the shared controls while keeping gameplay paused");
        Button pauseGuideEntry = Array.Find(pauseMenu.GetComponentsInChildren<Button>(true),
            button => button.GetComponentInChildren<Text>()?.text == "武器組合說明");
        Check(pauseGuideEntry != null, "In-game settings have a visible weapon combination entry");
        pauseGuideEntry.onClick.Invoke();
        var pauseGuide = ((GameObject)Get(inventory, "pauseBuildGuideBox")).GetComponent<WeaponBuildGuideUI>();
        Check(pauseGuide.IsOpen && !pauseMenu.gameObject.activeSelf && Time.timeScale == 0, "Opening the weapon guide from settings preserves paused gameplay");
        pauseGuide.Back();
        Check(pauseMenu.gameObject.activeSelf && !pauseGuide.IsOpen, "Returning from the guide restores the in-game settings page");
        ((Slider)Get(pauseMenu, "sensitivity")).value = 2.2f; pauseMenu.CancelAndClose();
        Check(Time.timeScale == 0 && inventory.IsPaused && GameSettings.Current.sensitivity < 2,
            "Cancel from Esc settings restores sensitivity and returns to the pause menu");
        Call(inventory, "ShowPauseSettings");
        ((Slider)Get(pauseMenu, "sensitivity")).value = 1.4f; pauseMenu.ApplyAndClose();
        Check(inventory.IsPaused && PlayerPrefs.GetFloat(prefix + "MouseSensitivity") == 1.4f,
            "Applying in Esc settings saves through the same persistent store");
        Call(inventory, "SetPaused", false);
        Check(Time.timeScale == 1, "Closing the pause menu restores gameplay time");
        Call(inventory, "SetPaused", true);
        var pauseMain = (GameObject)Get(inventory, "pauseMenuBox");
        var mainButtons = pauseMain.GetComponentsInChildren<Button>(false);
        Button directGuide = Array.Find(mainButtons, button => button.GetComponentInChildren<Text>()?.text == "武器組合說明");
        Check(directGuide != null, "The pause menu exposes the weapon guide directly");
        bool buttonsSeparated = true;
        for (int i = 0; i < mainButtons.Length; i++) for (int j = i + 1; j < mainButtons.Length; j++)
        {
            var a = mainButtons[i].GetComponent<RectTransform>(); var b = mainButtons[j].GetComponent<RectTransform>();
            buttonsSeparated &= Mathf.Abs(a.anchoredPosition.y - b.anchoredPosition.y) >= (a.rect.height + b.rect.height) / 2;
        }
        Check(buttonsSeparated, "Adding the guide keeps all pause buttons separated");
        directGuide.onClick.Invoke(); pauseGuide.Back();
        Check(pauseMain.activeSelf && !pauseMenu.gameObject.activeSelf, "The direct guide returns to the pause menu it was opened from");
        Call(inventory, "SetPaused", false);
        Object.DestroyImmediate(playerNode);

        // Migrate old fullscreen/index preferences in a fresh, isolated key namespace.
        CleanupPreferences(); PlayerPrefs.SetInt(prefix + "Fullscreen", 0); PlayerPrefs.SetFloat(prefix + "MasterVolume", .58f);
        if (Screen.resolutions.Length > 0) PlayerPrefs.SetInt(prefix + "Resolution", 0);
        GameSettings.Load(prefix, false);
        Check(GameSettings.Current.windowMode == FullScreenMode.Windowed && GameSettings.Current.masterVolume == .58f,
            "Old fullscreen and master-volume preferences migrate without losing values");
        if (Screen.resolutions.Length > 0)
            Check(GameSettings.Current.width == Screen.resolutions[0].width && GameSettings.Current.height == Screen.resolutions[0].height,
                "Legacy resolution indices migrate to stable width-and-height preferences");
        GameSettings.Save(); PlayerPrefs.SetInt(prefix + "Resolution", -999); GameSettingsData migrated = GameSettings.Current.Clone();
        GameSettings.Load(prefix, false);
        Check(GameSettings.Current.width == migrated.width && GameSettings.Current.height == migrated.height,
            "New dimension preferences take precedence over old monitor-dependent indices");

        host.SetActive(true); menu.RestoreDefaults();
        Button settingsGuideEntry = Array.Find(menu.GetComponentsInChildren<Button>(true),
            button => button.GetComponentInChildren<Text>()?.text == "武器組合說明");
        Check(settingsGuideEntry != null, "Main-menu settings show the guide without requiring a gameplay callback");
        settingsGuideEntry.onClick.Invoke(); yield return .15f;
        var guide = host.GetComponentInChildren<WeaponBuildGuideUI>(false);
        Check(guide != null && guide.IsOpen, "The default settings entry opens the actual weapon guide");
        var descriptions = guide.GetComponentsInChildren<Text>().Single(text => text.name == "Build Descriptions");
        var entries = System.Text.RegularExpressions.Regex.Matches(descriptions.text, @"(?m)^\d+\. ");
        Check(entries.Count == Enum.GetValues(typeof(WeaponBuild)).Length - 1 && descriptions.text.Contains("25. 彈幕風暴"),
            "The visible guide retains all 25 recipes and descriptions");
        var guideScroll = guide.GetComponentInChildren<ScrollRect>(); Canvas.ForceUpdateCanvases();
        Check(guideScroll.content.rect.height > guideScroll.viewport.rect.height && descriptions.preferredHeight <= descriptions.rectTransform.rect.height,
            "The complete description text fits its scroll content");
        IEnumerator guideShot = Capture("weapon-build-guide"); while (guideShot.MoveNext()) yield return guideShot.Current;
        guideScroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
        Check(guideScroll.verticalNormalizedPosition < .01f, "The last weapon combination is reachable by scrolling");
        IEnumerator bottomShot = Capture("weapon-build-guide-bottom"); while (bottomShot.MoveNext()) yield return bottomShot.Current;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var keyboard = InputSystem.AddDevice<Keyboard>(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return .15f;
        Check(!guide.IsOpen && host.activeSelf && menu.Draft != null && GameSettingsMenu.EscapeConsumedFrame >= 0,
            "Escape returns from the guide to settings instead of closing the whole menu");
        InputSystem.RemoveDevice(keyboard);
        menu.RestoreDefaults();
        foreach (int tab in new[] { 0, 1, 2, 3 })
        {
            menu.SelectTab(tab);
            yield return .15f;
            Canvas.ForceUpdateCanvases();
            Text help = (Text)Get(menu, "help");
            Check(help.preferredHeight <= help.rectTransform.rect.height, "Tab " + tab + " help text fits its actual font and layout");
            IEnumerator capture = Capture("settings-tab-" + tab); while (capture.MoveNext()) yield return capture.Current;
        }
        menu.SelectTab(1); yield return .15f; Canvas.ForceUpdateCanvases();
        Slider mouseSlider = Control<Slider>("sensitivity");
        Check(mouseSlider.handleRect.rect.height <= mouseSlider.GetComponent<RectTransform>().rect.height,
            "Slider handles fit their interactive rows without touching neighbouring controls");
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, mouseSlider.transform.position);
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        Check(hits.Any(h => h.gameObject.GetComponentInParent<Slider>() == mouseSlider),
            "The slider's full row is reachable through the UI raycaster; point=" + point + "; hits=" + string.Join(",", hits.Select(h => h.gameObject.name)));
        menu.SelectTab(0);
        IEnumerator dropdownShot = Capture("settings-fps-dropdown", true); while (dropdownShot.MoveNext()) yield return dropdownShot.Current;
        yield return .2f;
        Control<Dropdown>("frameRate").Show();
        yield return .2f;
        var popup = Control<Dropdown>("frameRate").transform.Find("Dropdown List");
        Check(popup != null, "The FPS dropdown creates a real interactive popup");
        ScrollRect scroll = popup.GetComponent<ScrollRect>();
        Check(scroll.content.rect.height > scroll.viewport.rect.height, "The long FPS list has scrollable overflow");
        scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
        Text unlimited = popup.GetComponentsInChildren<Text>().First(t => t.text == "無上限");
        Vector3 local = scroll.viewport.InverseTransformPoint(unlimited.transform.position);
        Check(scroll.viewport.rect.Contains(new Vector2(local.x, local.y)), "Scrolling reaches the final unlimited-FPS option");
        Control<Dropdown>("frameRate").Hide();
        Check(!File.Exists(Path.Combine(output, "unused-settings-save.json")), "Settings tests do not write a gameplay save");
        menu.CancelAndClose();
    }
}
