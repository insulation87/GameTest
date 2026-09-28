using UnityEngine;

/// <summary>Shared, bundled Traditional Chinese font for all player-facing UI.</summary>
public static class GameUIFont
{
    private static Font sharedFont;

    public static Font Default
    {
        get
        {
            if (sharedFont == null)
                sharedFont = Resources.Load<Font>("Fonts/NotoSansCJKtc-Regular");
            return sharedFont;
        }
    }
}
