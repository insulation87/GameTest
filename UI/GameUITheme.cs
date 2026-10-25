using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared dungeon UI palette and one-time styling. Never recolors item rarity or map fog.</summary>
public static class GameUITheme
{
    public static readonly Color Ink = new Color(.025f, .045f, .065f, 1f);
    public static readonly Color Panel = new Color(.067f, .10f, .167f, 1f);
    public static readonly Color Recess = new Color(.033f, .067f, .10f, 1f);
    public static readonly Color Raised = new Color(.10f, .16f, .19f, 1f);
    public static readonly Color Gold = new Color(.82f, .66f, .39f, 1f);
    public static readonly Color Teal = new Color(.32f, .78f, .74f, 1f);
    public static readonly Color Ivory = new Color(.94f, .92f, .85f, 1f);
    public static readonly Color Muted = new Color(.65f, .73f, .75f, 1f);
    public static readonly Color Health = new Color(.79f, .29f, .31f, 1f);

    public static void FitCanvas(CanvasScaler scaler)
    {
        if (scaler == null) return;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    }

    public static void Frame(Transform parent, bool accent = false)
    {
        if (parent.Find("Theme Frame") != null) return;
        var node = new GameObject("Theme Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(GameUIFrame));
        node.transform.SetParent(parent, false);
        Stretch(node.GetComponent<RectTransform>());
        var frame = node.GetComponent<GameUIFrame>();
        frame.color = accent ? Gold : new Color(Gold.r, Gold.g, Gold.b, .36f);
        frame.raycastTarget = false;
    }

    public static void Surface(GameObject node, bool inset = false)
    {
        Image image = node.GetComponent<Image>();
        if (image != null) { image.sprite = null; image.type = Image.Type.Simple; image.color = inset ? Recess : Panel; }
        Frame(node.transform);
    }

    public static void StyleButton(Button button, bool primary = false)
    {
        if (button == null) return;
        var image = button.targetGraphic as Image;
        if (image != null) { image.sprite = null; image.type = Image.Type.Simple; image.color = primary ? Gold : Raised; }
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.18f, 1.16f, 1.10f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.70f, .79f, .77f, 1f);
        colors.disabledColor = new Color(.52f, .55f, .56f, .65f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0f;
        button.colors = colors;
        button.transition = Selectable.Transition.ColorTint;
        foreach (Text label in button.GetComponentsInChildren<Text>(true))
        {
            label.color = primary ? Ink : Ivory;
            label.raycastTarget = false;
        }
        Frame(button.transform, primary);
    }

    public static void StyleTree(GameObject root)
    {
        foreach (CanvasScaler scaler in root.GetComponentsInChildren<CanvasScaler>(true)) FitCanvas(scaler);
        foreach (Text text in root.GetComponentsInChildren<Text>(true))
        {
            text.font = GameUIFont.Default;
            text.raycastTarget = false;
            string name = text.name;
            text.color = name.Contains("Title") || name.Contains("Gold") ? Gold :
                name.Contains("Hint") ? Muted : Ivory;
        }
        foreach (Button button in root.GetComponentsInChildren<Button>(true)) StyleButton(button);
        foreach (Slider slider in root.GetComponentsInChildren<Slider>(true))
        {
            if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fill)) fill.color = Teal;
            if (slider.targetGraphic != null) slider.targetGraphic.color = Gold;
        }
        foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true))
        {
            if (toggle.targetGraphic != null) toggle.targetGraphic.color = Raised;
            if (toggle.graphic != null) toggle.graphic.color = Gold;
        }
        foreach (Dropdown dropdown in root.GetComponentsInChildren<Dropdown>(true))
        {
            if (dropdown.targetGraphic != null) dropdown.targetGraphic.color = Raised;
            if (dropdown.template != null && dropdown.template.TryGetComponent(out Image template)) template.color = Panel;
        }
    }

    public static Text Label(Transform parent, string value, int size, Vector2 position, Vector2 dimensions,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var node = new GameObject("Label", typeof(RectTransform), typeof(Text));
        node.transform.SetParent(parent, false);
        Text label = node.GetComponent<Text>();
        label.font = GameUIFont.Default;
        label.text = value;
        label.fontSize = size;
        label.rectTransform.anchoredPosition = position;
        label.rectTransform.sizeDelta = dimensions;
        label.raycastTarget = false;
        label.color = Ivory;
        label.alignment = alignment;
        return label;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
