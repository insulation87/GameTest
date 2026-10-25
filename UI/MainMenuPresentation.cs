using UnityEngine;
using UnityEngine.UI;

/// <summary>Applies to existing scenes at runtime, retaining serialized button callbacks.</summary>
public static class MainMenuPresentation
{
    public static void Build(Transform background, GameObject main, GameObject settings)
    {
        var canvas = background.GetComponentInParent<Canvas>();
        if (canvas != null) GameUITheme.FitCanvas(canvas.GetComponent<CanvasScaler>());
        if (background.TryGetComponent(out Image backdrop)) backdrop.color = GameUITheme.Ink;
        var art = new GameObject("Pixel Dungeon Cover", typeof(RectTransform), typeof(CanvasRenderer), typeof(PixelMenuBackdrop));
        art.transform.SetParent(background, false);
        art.transform.SetAsFirstSibling();
        art.GetComponent<PixelMenuBackdrop>().raycastTarget = false;
        GameUITheme.Stretch(art.GetComponent<RectTransform>());

        // Stable 1920 x 1080 design area; canvas expands for ultrawide and 4:3 displays.
        foreach (Text text in main.GetComponentsInChildren<Text>(true))
        {
            if (text.transform.parent != main.transform) continue;
            text.rectTransform.anchoredPosition = new Vector2(-540, 270);
            text.rectTransform.sizeDelta = new Vector2(660, 110);
            text.fontSize = 68;
            text.alignment = TextAnchor.MiddleLeft;
        }
        GameUITheme.Label(main.transform, "D U N G E O N   /   T A C T I C A L", 18,
            new Vector2(-540, 360), new Vector2(660, 32)).color = GameUITheme.Gold;
        GameUITheme.Label(main.transform, "地城探索  ／  戰術射擊", 23,
            new Vector2(-540, 184), new Vector2(660, 44)).color = GameUITheme.Muted;
        int index = 0;
        foreach (Button button in main.GetComponentsInChildren<Button>(true))
        {
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(-610, 60 - index * 84);
            rect.sizeDelta = new Vector2(520, 64);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) { label.rectTransform.sizeDelta = new Vector2(484, 56); label.fontSize = 25; }
            GameUITheme.StyleButton(button, index == 0);
            index++;
        }
        GameUITheme.Label(main.transform, "探索深處，準備迎戰。", 18,
            new Vector2(-540, -460), new Vector2(660, 32)).color = GameUITheme.Muted;

        // Settings use the same cover, behind a legible centered card.
        var card = AccountLoginPanel.Box(settings.transform, "Settings Surface", Vector2.zero,
            new Vector2(820, 620), GameUITheme.Panel);
        card.SetAsFirstSibling();
        GameUITheme.Frame(card);
        GameUITheme.StyleTree(settings);
    }

    public static void PlaceAccountControls(Text label, Button logout, Button import)
    {
        label.rectTransform.anchoredPosition = new Vector2(-540, -298);
        label.rectTransform.sizeDelta = new Vector2(660, 68);
        label.alignment = TextAnchor.MiddleLeft;
        label.color = GameUITheme.Muted;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 14;
        label.resizeTextMaxSize = 18;
        logout.GetComponent<RectTransform>().anchoredPosition = new Vector2(-745, -378);
        import.GetComponent<RectTransform>().anchoredPosition = new Vector2(-475, -378);
        foreach (Button button in new[] { logout, import })
        {
            button.GetComponent<RectTransform>().sizeDelta = new Vector2(250, 48);
            Text text = button.GetComponentInChildren<Text>();
            text.rectTransform.sizeDelta = new Vector2(234, 42);
            text.fontSize = 19;
            GameUITheme.StyleButton(button);
        }
    }
}
