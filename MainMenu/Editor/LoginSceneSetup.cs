#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Default Editor Play starts from login without replacing the currently edited scene.</summary>
[InitializeOnLoad]
public static class LoginSceneSetup
{
    public const string ScenePath = "Assets/Scenes/Login.unity";
    const string StartKey = "GameTest.StartPlayFromLogin";

    static LoginSceneSetup() { EditorApplication.delayCall += ConfigurePlayStart; }

    public static void ConfigurePlayStart()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!SessionState.GetBool(StartKey, true)) return;
        SceneAsset login = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (login != null) EditorSceneManager.playModeStartScene = login;
    }

    [MenuItem("Game Tools/Start Play From Login")]
    public static void PlayFromLogin()
    {
        SessionState.SetBool(StartKey, true);
        ConfigurePlayStart();
    }

    [MenuItem("Game Tools/Start Play From Current Scene")]
    public static void PlayFromCurrentScene()
    {
        SessionState.SetBool(StartKey, false);
        EditorSceneManager.playModeStartScene = null;
    }

    public static void UpdateBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>();
        if (File.Exists(ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        const string menuPath = "Assets/Scenes/MainMenu.unity";
        const string gamePath = "Assets/Scenes/Map1.unity";
        if (File.Exists(menuPath)) scenes.Add(new EditorBuildSettingsScene(menuPath, true));
        bool hasGame = false;
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.path == ScenePath || scene.path == menuPath) continue;
            scenes.Add(scene);
            if (scene.path == gamePath) hasGame = true;
        }
        if (!hasGame && File.Exists(gamePath)) scenes.Add(new EditorBuildSettingsScene(gamePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
