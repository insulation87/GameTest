using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Shared entry scenes and one outstanding scene transition at a time.</summary>
public static class GameSceneFlow
{
    public const string LoginScene = "Login";
    public const string MenuScene = "MainMenu";
    public const string TradingHubScene = "TradingHub";
    static GameLoadingScreen loadingScreen;
    public static bool IsLoading => loadingScreen != null && loadingScreen.IsLoading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => loadingScreen = null;

    public static bool LoadLogin()
    {
        CharacterRoster.ClearPending();
        ExpeditionMaps.ClearPreparation();
        GameProgressSave.StartFresh = false;
        return Load(LoginScene);
    }

    public static bool Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"Cannot load '{sceneName}'. Add it to Build Settings.");
            return false;
        }
        if (IsLoading)
        {
            if (loadingScreen.Destination == sceneName) return true;
            // Authentication redirects can be queued while the current operation finishes.
            // Never overlap two async operations or hide an unauthorized scene in between.
            if (sceneName == LoginScene || SceneManager.GetActiveScene().name == loadingScreen.Destination)
            { loadingScreen.Redirect(sceneName); return true; }
            return false;
        }
        if (SceneManager.GetActiveScene().name == sceneName) return true;
        if (loadingScreen == null)
            loadingScreen = new GameObject("Scene Loading Screen").AddComponent<GameLoadingScreen>();
        loadingScreen.Begin(sceneName);
        return true;
    }
}
