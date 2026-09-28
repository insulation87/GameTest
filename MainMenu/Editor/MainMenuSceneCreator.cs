#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MainMenuSceneCreator
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private static readonly Color Navy = new Color(0.035f, 0.055f, 0.10f, 1f);
    private static readonly Color Blue = new Color(0.10f, 0.42f, 0.82f, 1f);

    [MenuItem("Game Tools/Create Main Menu Scene")]
    public static void CreateScene()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Main Menu already exists",
                "Replace Assets/Scenes/MainMenu.unity?", "Replace", "Cancel"))
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "MainMenu";

        CreateCamera();
        CreateEventSystem();

        Canvas canvas = CreateCanvas();
        GameObject background = CreateUIObject("Background", canvas.transform);
        Stretch(background.GetComponent<RectTransform>());
        background.AddComponent<Image>().color = Navy;

        MainMenuController controller = background.AddComponent<MainMenuController>();
        GameObject mainPanel = CreatePanel(background.transform, "Main Panel");
        GameObject settingsPanel = CreatePanel(background.transform, "Settings Panel");

        CreateText(mainPanel.transform, "遊戲測試", 54, new Vector2(0, 170), new Vector2(650, 90));
        Button join = CreateButton(mainPanel.transform, "加入遊戲", new Vector2(0, 45));
        Button settings = CreateButton(mainPanel.transform, "設定", new Vector2(0, -35));
        Button quit = CreateButton(mainPanel.transform, "退出遊戲", new Vector2(0, -115));

        CreateText(settingsPanel.transform, "設定", 44, new Vector2(0, 190), new Vector2(500, 70));
        Slider volume = CreateSlider(settingsPanel.transform, "主音量", 80);
        Toggle fullscreen = CreateToggle(settingsPanel.transform, "全螢幕", 5);
        Dropdown resolution = CreateDropdown(settingsPanel.transform, "解析度", -70);
        Button save = CreateButton(settingsPanel.transform, "儲存並返回", new Vector2(0, -170));

        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("mainPanel").objectReferenceValue = mainPanel;
        serialized.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        serialized.FindProperty("volumeSlider").objectReferenceValue = volume;
        serialized.FindProperty("fullscreenToggle").objectReferenceValue = fullscreen;
        serialized.FindProperty("resolutionDropdown").objectReferenceValue = resolution;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(join.onClick, controller.JoinGame);
        UnityEventTools.AddPersistentListener(settings.onClick, controller.ShowSettings);
        UnityEventTools.AddPersistentListener(quit.onClick, controller.QuitGame);
        UnityEventTools.AddPersistentListener(save.onClick, controller.SaveSettings);

        settingsPanel.SetActive(false);
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Main Menu created",
            "MainMenu is now the first scene in Build Settings.\nPress Play to test it.", "OK");
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0, 0, -10);
        cameraObject.GetComponent<Camera>().backgroundColor = Navy;
    }

    private static void CreateEventSystem()
    {
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    private static Canvas CreateCanvas()
    {
        GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static GameObject CreatePanel(Transform parent, string name)
    {
        GameObject panel = CreateUIObject(name, parent);
        Stretch(panel.GetComponent<RectTransform>());
        return panel;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 position)
    {
        GameObject buttonObject = CreateUIObject(label + " Button", parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(360, 62);
        rect.anchoredPosition = position;
        Image image = buttonObject.AddComponent<Image>();
        image.color = Blue;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        Text text = CreateText(buttonObject.transform, label, 26, Vector2.zero, rect.sizeDelta);
        text.raycastTarget = false;
        return button;
    }

    private static Slider CreateSlider(Transform parent, string label, float y)
    {
        CreateText(parent, label, 24, new Vector2(-210, y), new Vector2(180, 45));
        GameObject root = CreateUIObject(label + " Slider", parent);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(360, 30);
        rootRect.anchoredPosition = new Vector2(80, y);

        GameObject background = CreateUIObject("Background", root.transform);
        Stretch(background.GetComponent<RectTransform>(), 0, 0, 10, 10);
        background.AddComponent<Image>().color = new Color(1, 1, 1, 0.25f);

        GameObject fillArea = CreateUIObject("Fill Area", root.transform);
        Stretch(fillArea.GetComponent<RectTransform>(), 10, 10, 10, 10);
        GameObject fill = CreateUIObject("Fill", fillArea.transform);
        Stretch(fill.GetComponent<RectTransform>());
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = Blue;

        GameObject handleArea = CreateUIObject("Handle Slide Area", root.transform);
        Stretch(handleArea.GetComponent<RectTransform>(), 10, 10, 0, 0);
        GameObject handle = CreateUIObject("Handle", handleArea.transform);
        handle.GetComponent<RectTransform>().sizeDelta = new Vector2(28, 42);
        Image handleImage = handle.AddComponent<Image>();

        Slider slider = root.AddComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImage;
        slider.value = 1;
        return slider;
    }

    private static Toggle CreateToggle(Transform parent, string label, float y)
    {
        CreateText(parent, label, 24, new Vector2(-210, y), new Vector2(180, 45));
        GameObject root = CreateUIObject(label + " Toggle", parent);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(42, 42);
        rect.anchoredPosition = new Vector2(-80, y);
        Image background = root.AddComponent<Image>();
        background.color = new Color(1, 1, 1, 0.25f);

        GameObject check = CreateUIObject("Checkmark", root.transform);
        Stretch(check.GetComponent<RectTransform>(), 7, 7, 7, 7);
        Image checkImage = check.AddComponent<Image>();
        checkImage.color = Blue;

        Toggle toggle = root.AddComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.graphic = checkImage;
        toggle.isOn = true;
        return toggle;
    }

    private static Dropdown CreateDropdown(Transform parent, string label, float y)
    {
        CreateText(parent, label, 24, new Vector2(-210, y), new Vector2(180, 45));
        GameObject root = CreateUIObject(label + " Dropdown", parent);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(360, 52);
        rect.anchoredPosition = new Vector2(80, y);
        Image image = root.AddComponent<Image>();
        image.color = new Color(1, 1, 1, 0.18f);

        Text caption = CreateText(root.transform, "1920 × 1080", 20, new Vector2(-15, 0), new Vector2(290, 50));
        caption.alignment = TextAnchor.MiddleLeft;
        Text arrow = CreateText(root.transform, "▼", 18, new Vector2(155, 0), new Vector2(40, 50));
        arrow.raycastTarget = false;

        GameObject template = CreateUIObject("Template", root.transform);
        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.pivot = new Vector2(0.5f, 1);
        templateRect.anchoredPosition = new Vector2(0, -2);
        templateRect.sizeDelta = new Vector2(0, 220);
        Image templateImage = template.AddComponent<Image>();
        templateImage.color = new Color(0.07f, 0.09f, 0.14f, 1);
        ScrollRect scrollRect = template.AddComponent<ScrollRect>();

        GameObject viewport = CreateUIObject("Viewport", template.transform);
        Stretch(viewport.GetComponent<RectTransform>(), 4, 4, 4, 4);
        viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 34);

        GameObject dropdownItem = CreateUIObject("Item", content.transform);
        RectTransform itemRect = dropdownItem.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
        itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.sizeDelta = new Vector2(0, 34);
        Toggle itemToggle = dropdownItem.AddComponent<Toggle>();
        Image itemBackground = dropdownItem.AddComponent<Image>();
        itemBackground.color = new Color(1, 1, 1, 0.08f);
        itemToggle.targetGraphic = itemBackground;
        Text itemText = CreateText(dropdownItem.transform, "選項", 18, Vector2.zero, new Vector2(320, 34));
        itemText.alignment = TextAnchor.MiddleLeft;
        itemText.raycastTarget = false;

        scrollRect.content = contentRect;
        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        template.SetActive(false);

        Dropdown dropdown = root.AddComponent<Dropdown>();
        dropdown.targetGraphic = image;
        dropdown.captionText = caption;
        dropdown.template = templateRect;
        dropdown.itemText = itemText;
        return dropdown;
    }

    private static Text CreateText(Transform parent, string content, int fontSize, Vector2 position, Vector2 dimensions)
    {
        GameObject textObject = CreateUIObject(content + " Text", parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = dimensions;
        rect.anchoredPosition = position;
        Text text = textObject.AddComponent<Text>();
        text.text = content;
        text.font = GameUIFont.Default;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        return text;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void UpdateBuildSettings()
    {
        string gameScene = "Assets/Scenes/Map1.unity";
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(gameScene, true)
        };
    }
}
#endif
