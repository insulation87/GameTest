using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class OverworldMultiplayerBuildReview
{
    const string Role = "OverworldMultiplayerBuildReview.Role";
    public static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static OverworldMultiplayerBuildReview() { EditorApplication.playModeStateChanged += Changed; }
    static void Guard()
    {
        if (!Application.isBatchMode || !File.Exists(Path.Combine(Output, "authored-world-review.json")))
            throw new InvalidOperationException("Only run in an isolated complete project copy.");
    }
    public static void RunHost() => Play("editor-host");
    public static void RunClient() => Play("editor-client");
    public static void RunSplit() => Play("split");
    public static void RunGuestHost() => Play("guest-host");
    public static void RunGuestClient() => Play("guest-client");
    public static void RunGuestSolo() => Play("guest-solo");
    public static void RunRelayHost() => Play("relay-host");
    public static void RunRelayClient() => Play("relay-client");
    public static void RunRelayLoadingFailure() => Play("relay-load-failure");
    public static void RunEnemyDetection() => Play("enemy-range");
    public static void RunLootBalance() => Play("loot-balance");
    public static void RunGrenadeRemoval() => Play("grenade-removal");
    static void Play(string role)
    {
        Guard(); LoginSceneSetup.PlayFromCurrentScene();
        RepairPlayerPrefab();
        EditorSceneManager.OpenScene("Assets/Scenes/Login.unity", OpenSceneMode.Single);
        SessionState.SetString(Role, role); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        string role = SessionState.GetString(Role, ""); if (role.Length == 0) return;
        SessionState.SetString(Role, "");
        string folder = role == "grenade-removal" ? "GrenadeRemovalReview" : role == "loot-balance" ? "LootBalanceReview" : role == "enemy-range" ? "EnemyDetectionReview" : role == "relay-load-failure" ? "CloudLoadingReview" : role.StartsWith("guest") ? "CloudGuestReview" : role.StartsWith("relay") ? "CloudRelayReview" : "";
        string output = folder.Length == 0 ? Output : Path.Combine(Output, folder);
        Directory.CreateDirectory(output);
        if (folder.Length != 0) File.Copy(Path.Combine(Output, "authored-world-review.json"), Path.Combine(output, "authored-world-review.json"), true);
        OverworldValidationProbe.Begin(role, output);
    }
    public static void RepairPlayerPrefab()
    {
        Guard();
        const string path = "Assets/Player/Player.prefab";
        string backup = Path.Combine(Output, "Player-before-validation.prefab.txt");
        if (!File.Exists(backup)) File.Copy(path, backup);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int removed = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
            var player = root.GetComponent<Player>(); var views = root.GetComponent<CameraSwitch>();
            if (player == null || player.fpsCamera == null || player.tpsCamera == null ||
                views == null || views.fpsController == null || views.tpsController == null)
                throw new InvalidOperationException("Player prefab has incomplete current camera references.");
            if (removed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            File.WriteAllText(Path.Combine(Output, "player-prefab-review.txt"), "Retired missing components removed: " + removed + "\nCurrent FPS/TPS references: valid");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    public static void BuildValidation() => Build(true);
    public static void BuildRelease() => Build(false);
    public static void BuildCloudValidation() => Build(true, true);
    public static void BuildCloudRelease() => Build(false, true);
    public static void InspectRenderPipelinePackage()
    {
        Guard();
        const string path = "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipelineResources/ShaderGraph/AutodeskInteractiveTransparent.ShaderGraph";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        File.WriteAllText(Path.Combine(Output, "hdrp-resource-inspection.txt"),
            "Imported asset type: " + AssetDatabase.GetMainAssetTypeAtPath(path) +
            "\nShader resource loads in Editor: " + (shader != null) +
            "\nActive render pipeline: " + UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline?.GetType().FullName);
        EditorApplication.Exit(shader != null ? 0 : 1);
    }
    static void Build(bool validation, bool cloud = false)
    {
        Guard();
        RepairPlayerPrefab();
        if (validation && cloud) { PlayerSettings.companyName = "CodexReview"; PlayerSettings.productName = "CloudMultiplayerReview"; }
        if (!validation) { PlayerSettings.companyName = "DefaultCompany"; PlayerSettings.productName = "GameTest"; }
        string folder = Path.Combine(Output, cloud ? (validation ? "CloudValidationBuild" : "CloudReleaseBuild") : (validation ? "ValidationBuild" : "ReleaseBuild"));
        Directory.CreateDirectory(folder);
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (!scenes.SequenceEqual(new[] { "Assets/Scenes/Login.unity", "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Map1.unity" }))
            throw new InvalidOperationException("Unexpected formal build scene list.");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = scenes, locationPathName = Path.Combine(folder, "GameTest.exe"),
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
            extraScriptingDefines = validation ? new[] { "OVERWORLD_VALIDATION" } : new string[0]
        });
        var s = report.summary;
        File.WriteAllText(Path.Combine(Output, (cloud ? "cloud-" : "") + (validation ? "validation-build-result.txt" : "release-build-result.txt")),
            s.result + "\nPlatform: " + s.platform + "\nOptions: " + s.options + "\nSize: " + s.totalSize +
            "\nTime: " + s.totalTime + "\nErrors: " + s.totalErrors + "\nWarnings: " + s.totalWarnings +
            "\nScenes: " + string.Join(", ", scenes) + "\nValidation instrumentation: " + validation +
            "\n" + string.Join("\n", report.steps.SelectMany(step => step.messages).Where(m => m.type == LogType.Error || m.type == LogType.Warning).Select(m => m.content)));
        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
