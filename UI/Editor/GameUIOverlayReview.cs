using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Run explicitly in an isolated project: creates fixture scenes and enters Play Mode.
[InitializeOnLoad]
public static class GameUIOverlayReview
{
    const string Pending = "GameUIOverlayReview.Pending";
    static IEnumerator checks;
    static int minimumFrame, assertions;
    static double deadline, timeout;
    static readonly List<string> errors = new List<string>();
    static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../"));
    static GameUIOverlayReview() { EditorApplication.playModeStateChanged += StateChanged; }

    public static void Run()
    {
        if (Application.productName != "UIOverlayReview")
            throw new InvalidOperationException("Run this review only in the isolated UIOverlayReview project.");
        LoginSceneSetup.PlayFromCurrentScene();
        CoopModeReview.BuildFixtures();
        var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        gameView.Show(); gameView.Focus();
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        errors.Clear(); assertions = 0; checks = Checks();
        deadline = 0; minimumFrame = Time.frameCount + 3;
        timeout = EditorApplication.timeSinceStartup + 120;
        Application.logMessageReceived += Log;
        EditorApplication.update += Tick;
    }

    static void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception) && !message.Contains("burst JIT compiler") &&
            !stack.Contains("UnityEditor.Search")) errors.Add(message);
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("UI Play Mode review");
            if (Time.frameCount < minimumFrame || EditorApplication.timeSinceStartup < deadline) return;
            if (!checks.MoveNext()) { Finish(0, "PASS " + assertions + " UI overlay Play Mode checks"); return; }
            minimumFrame = Time.frameCount + 3;
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .1f);
        }
        catch (Exception ex) { Debug.LogException(ex); Finish(1, "FAIL " + ex); }
    }

    static void Finish(int code, string result)
    {
        File.WriteAllText(Path.Combine(Output, "ui-overlay-result.txt"), result);
        Debug.Log(result);
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick;
        Time.timeScale = 1; CoopSession.Leave(); EditorApplication.Exit(code);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++; Debug.Log("PASS " + message);
    }

    static void Invoke(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

    static IEnumerator Ready(bool multiplayer)
    {
        while (GameSceneFlow.IsLoading || Object.FindFirstObjectByType<Player>() == null ||
            multiplayer && (CoopSession.Instance == null || !CoopSession.Instance.SceneReady)) yield return .1f;
        yield return 1f;
    }

    static Image Marker(Player player, Color color)
    {
        var canvas = new GameObject("Occlusion Test HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas.transform.SetParent(player.transform, false);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.GetComponent<Canvas>().sortingOrder = 90;
        GameUITheme.FitCanvas(canvas.GetComponent<CanvasScaler>());
        var node = new GameObject("Occlusion Marker", typeof(RectTransform), typeof(Image));
        node.transform.SetParent(canvas.transform, false);
        var image = node.GetComponent<Image>(); image.color = color;
        image.rectTransform.sizeDelta = new Vector2(100, 100);
        return image;
    }

    static GameObject Cover(Camera camera)
    {
        var node = GameObject.CreatePrimitive(PrimitiveType.Cube); node.name = "Near-camera Enemy Occluder";
        Object.Destroy(node.GetComponent<Collider>());
        node.transform.SetParent(camera.transform, false);
        node.transform.localPosition = new Vector3(0, 0, camera.nearClipPlane + .1f);
        node.transform.localScale = new Vector3(10, 10, .01f);
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", Color.blue); node.GetComponent<Renderer>().material = material;
        node.AddComponent<EnemyHealth>();
        return node;
    }

    static void CheckMarker(Image marker, Rect viewport, string message)
    {
        var corners = new Vector3[4]; marker.rectTransform.GetWorldCorners(corners);
        Vector2 center = (corners[0] + corners[2]) * .5f;
        Check(Vector2.Distance(center, new Vector2(Screen.width * viewport.center.x, Screen.height * viewport.center.y)) < 2,
            message + " is centered in its viewport: " + center + " at " + Screen.width + "x" + Screen.height);
        var pixels = ScreenCapture.CaptureScreenshotAsTexture();
        if (pixels == null) throw new InvalidOperationException("Game View capture is unavailable");
        File.WriteAllBytes(Path.Combine(Output, message + ".png"), pixels.EncodeToPNG());
        // Sample away from the crosshair, which correctly sits above this marker.
        Vector2 sample = Vector2.Lerp(center, corners[2], .5f);
        Color actual = pixels.GetPixel(Mathf.Clamp(Mathf.RoundToInt(sample.x), 0, pixels.width - 1),
            Mathf.Clamp(Mathf.RoundToInt(sample.y), 0, pixels.height - 1));
        Check(Vector3.Distance(new Vector3(actual.r, actual.g, actual.b),
            new Vector3(marker.color.r, marker.color.g, marker.color.b)) < .2f,
            message + " remains visible over near-camera enemy geometry: " + actual);
        Object.Destroy(pixels);
    }

    static void CheckHud(Player player, Rect viewport)
    {
        foreach (Canvas canvas in player.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.GetComponentInParent<PixelArtCamera>() != null) continue;
            Check(canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.worldCamera == null,
                canvas.name + " is outside the world depth buffer");
            if (!canvas.isRootCanvas) continue;
            var content = canvas.transform.Find("Player UI Viewport").GetComponent<RectTransform>();
            Check(content.anchorMin == viewport.min && content.anchorMax == viewport.max && content.GetComponent<RectMask2D>() != null,
                canvas.name + " is clipped to its owner's viewport");
            Check(canvas.sortingOrder > short.MinValue, canvas.name + " is above the pixel world output");
        }
    }

    static void Capture(string name)
    {
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        Check(texture != null, name + " captures the actual Game View");
        File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
        Object.Destroy(texture);
    }

    static IEnumerator Checks()
    {
        Check(CloudAccountService.EnsureInstance().EnterGuest(), "Fixture enters guest mode");
        Check(GameSceneFlow.Load("Map1"), "Single-player fixture loads");
        var ready = Ready(false); while (ready.MoveNext()) yield return ready.Current;
        var solo = Object.FindFirstObjectByType<Player>();
        solo.GetComponent<CrosshairUI>().onlyWhenCursorLocked = false;
        var soloMarker = Marker(solo, Color.green); Cover(solo.fpsCamera);
        yield return .4f;
        CheckMarker(soloMarker, new Rect(0, 0, 1, 1), "solo-near-enemy");
        Check(solo.transform.Find("PlayerHealthCanvas").GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay,
            "Single-player health HUD remains an overlay");
        GameSceneFlow.Load("MainMenu"); yield return 1.5f;
        Check(CoopSession.StartSplit("Map1", "breaker", "sentinel"), "Local split-screen fixture starts");
        ready = Ready(true); while (ready.MoveNext()) yield return ready.Current;
        var actors = Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None);
        var first = Array.Find(actors, a => a.Local && a.LocalIndex == 0);
        var second = Array.Find(actors, a => a.Local && a.LocalIndex == 1);
        Check(first != null && second != null, "Both local actors are present");
        var one = first.GetComponent<Player>(); var two = second.GetComponent<Player>();
        var marker1 = Marker(one, Color.green); var marker2 = Marker(two, Color.red);
        one.GetComponent<CrosshairUI>().onlyWhenCursorLocked = false;
        two.GetComponent<CrosshairUI>().onlyWhenCursorLocked = false;
        Cover(one.fpsCamera); Cover(two.fpsCamera);
        yield return .6f;
        CheckHud(one, first.Viewport); CheckHud(two, second.Viewport);
        CheckMarker(marker1, first.Viewport, "split-left-near-enemy");
        CheckMarker(marker2, second.Viewport, "split-right-near-enemy");
        marker2.rectTransform.sizeDelta = new Vector2(6000, 100); yield return .3f;
        CheckMarker(marker1, first.Viewport, "split-clipping");
        marker2.rectTransform.sizeDelta = new Vector2(100, 100); yield return .3f;
        Check(!marker1.canvas.GetComponent<GraphicRaycaster>(), "Informational HUD creates no input blocker");
        var inventory = one.GetComponent<WeaponInventory>(); Invoke(inventory, "SetOpen", true); yield return .4f;
        Check(inventory.IsOpen, "Inventory opens during Play Mode");
        Capture("split-inventory");
        CheckHud(one, first.Viewport);
        var button = Array.Find(one.GetComponentsInChildren<Button>(), b => b.isActiveAndEnabled && b.interactable);
        Check(button != null, "Inventory has an active interactive button");
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
        Check(hits.Exists(hit => hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform)),
            "Mouse raycasts reach inventory buttons above the world");
        Invoke(inventory, "SetOpen", false);
        first.GetComponent<CameraSwitch>().SetCoopView(false); second.GetComponent<CameraSwitch>().SetCoopView(false);
        Cover(one.tpsCamera); Cover(two.tpsCamera); yield return .5f;
        CheckHud(one, first.Viewport); CheckHud(two, second.Viewport);
        CheckMarker(marker1, first.Viewport, "split-tps-left"); CheckMarker(marker2, second.Viewport, "split-tps-right");
        Invoke(inventory, "SetPaused", true); yield return .3f;
        Check(Time.timeScale == 0 && inventory.IsPaused, "Pause menu opens with the shared world paused");
        Capture("split-pause");
        CheckHud(one, first.Viewport);
        Invoke(inventory, "SetPaused", false);
        // Upgrade choices are offered every fifth level, rather than every level.
        two.GetComponent<PlayerHealth>().AddExperience(600); yield return .5f;
        Check(two.GetComponent<PlayerHealth>().IsChoosingUpgrade, "Upgrade menu is dynamically created");
        Capture("split-upgrade");
        CheckHud(two, second.Viewport);
        two.GetComponent<PlayerHealth>().SelectCoopUpgrade(0); yield return .2f;
        Check(!two.GetComponent<PlayerHealth>().IsChoosingUpgrade, "Upgrade choice closes the overlay menu");
        GameSceneFlow.Load("MainMenu"); yield return 1.5f;
        Check(CoopSession.StartHost("Map1", "ranger", 0, "review", out string error), "Online host fixture starts: " + error);
        ready = Ready(true); while (ready.MoveNext()) yield return ready.Current;
        var host = Object.FindFirstObjectByType<CoopActor>(); var hostPlayer = host.GetComponent<Player>();
        var hostMarker = Marker(hostPlayer, Color.green); Cover(hostPlayer.fpsCamera); yield return .5f;
        CheckHud(hostPlayer, new Rect(0, 0, 1, 1));
        CheckMarker(hostMarker, new Rect(0, 0, 1, 1), "host-near-enemy");
        Check(errors.Count == 0, "No unexpected runtime errors: " + string.Join("; ", errors));
    }
}
