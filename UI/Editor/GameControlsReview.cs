using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit isolated-project entry point; creates fixture scenes, never use in the working project.
[InitializeOnLoad]
public static class GameControlsReview
{
    const string Pending = "GameControlsReview.Pending";
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static string output, prefix;
    static readonly List<string> errors = new List<string>();
    static Keyboard keyboard;
    static Mouse mouse;
    static Gamepad pad;
    static GameControlsReview() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        LoginSceneSetup.PlayFromCurrentScene(); CoopModeReview.BuildFixtures();
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); assertions = 0; errors.Clear();
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../")); prefix = "ControlsReview." + Guid.NewGuid().ToString("N") + ".";
        checks = Checks(); deadline = EditorApplication.timeSinceStartup + .2;
        Application.logMessageReceived += Log; EditorApplication.update += Tick;
    }
    static void Log(string message, string stack, LogType type)
    { if ((type == LogType.Error || type == LogType.Exception) && !message.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(message); }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < deadline) return;
        try
        {
            if (!checks.MoveNext()) { Finish(0, "PASS " + assertions + " rebinding integration checks"); return; }
            deadline = EditorApplication.timeSinceStartup + (checks.Current is float delay ? delay : .1f);
        }
        catch (Exception ex) { Debug.LogException(ex); Finish(1, "FAIL " + ex); }
    }
    static void Finish(int code, string result)
    {
        File.WriteAllText(Path.Combine(output, "controls-result.txt"), result);
        foreach (string key in new[] { "BindingsV1", "Width", "Height", "WindowMode", "Fullscreen", "MasterVolume", "MusicVolume", "EffectsVolume", "MouseSensitivity", "FieldOfView", "InvertY", "VSync", "FrameRate", "MuteInBackground" }) PlayerPrefs.DeleteKey(prefix + key);
        PlayerPrefs.Save(); CoopSession.Leave(); GameControls.SetRecording(false); Time.timeScale = 1;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse); if (pad != null) InputSystem.RemoveDevice(pad);
        Application.logMessageReceived -= Log; EditorApplication.update -= Tick; EditorApplication.Exit(code);
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); assertions++; Debug.Log("PASS " + message); }
    static IEnumerator Wait(Func<bool> condition)
    {
        double until = EditorApplication.timeSinceStartup + 20;
        while (!condition()) { if (EditorApplication.timeSinceStartup > until) throw new TimeoutException("Rebinding review"); yield return .1f; }
    }
    static void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); }
    static object Get(object instance, string field) => instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    static IEnumerator Checks()
    {
        Check(CloudAccountService.EnsureInstance().EnterGuest(), "The isolated controls review enters guest mode before starting cooperative gameplay");
        Object.FindFirstObjectByType<PixelArtDirector>().enabled = false;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
        GameSettings.Load(prefix, false);
        Check(GameControls.Label(0, GameAction.Skill) == "R" && GameControls.Label(1, GameAction.Skill) == "PageUp", "Existing settings without bindings receive both keyboard defaults");
        var oldBindings = GameControls.Defaults();
        oldBindings.playerOne[18] = (int)KeyCode.G; oldBindings.playerTwo[18] = (int)KeyCode.End; oldBindings.gamepad[18] = (int)PadBinding.Y;
        oldBindings.playerOne[(int)GameAction.Skill] = (int)KeyCode.F;
        var normalized = GameControls.Normalize(oldBindings);
        Check(normalized.playerOne[18] == (int)KeyCode.None && normalized.playerTwo[18] == (int)KeyCode.None && normalized.gamepad[18] == -1,
            "Old grenade bindings are cleared in both keyboard profiles and gamepad settings");
        Check(normalized.playerOne[(int)GameAction.Skill] == (int)KeyCode.F && normalized.playerOne[(int)GameAction.Heal] == (int)KeyCode.H &&
            normalized.playerTwo[(int)GameAction.Heal] == (int)KeyCode.Insert && normalized.gamepad[(int)GameAction.Heal] == (int)PadBinding.Down,
            "Removing action 18 preserves custom skills and all three medical-pack bindings");
        Check(!GameControls.Actions.Any(a => (int)a == 18) && !GameControls.TryBind(normalized, 0, (GameAction)18, (int)KeyCode.G, out _),
            "Removed action cannot appear in the action list or be rebound");
        Check(GameControls.TryBind(normalized.Clone(), 0, GameAction.Skill, (int)KeyCode.G, out _),
            "The retired grenade key is free for another action");
        Keys(Key.G); Check((CoopInput.Read(0,0).held & 16384) == 0, "Real G key input generates no grenade network bit"); Keys();
        var clone = GameSettings.Current.Clone(); clone.bindings.playerOne[(int)GameAction.Skill] = (int)KeyCode.F;
        Check(GameControls.Label(0, GameAction.Skill) == "R", "Editing a cloned draft cannot mutate live controls");
        var camera = new GameObject("Controls Camera", typeof(Camera)).GetComponent<Camera>(); camera.tag = "MainCamera"; camera.backgroundColor = GameUITheme.Ink;
        new GameObject("Controls Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var canvas = new GameObject("Controls Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; GameUITheme.FitCanvas(canvas.GetComponent<CanvasScaler>());
        var host = new GameObject("Controls Settings", typeof(RectTransform)); host.transform.SetParent(canvas.transform, false); GameUITheme.Stretch(host.GetComponent<RectTransform>());
        var menu = GameSettingsMenu.Attach(host, () => host.SetActive(false)); menu.SelectTab(3); yield return .2f;
        Check(!host.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("手榴彈")), "Actual control settings UI has no grenade action");
        menu.BeginCapture(GameAction.Skill);
        Check(menu.Capturing && GameControls.Blocked, "Recording a binding blocks gameplay input");
        yield return .1f; InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); yield return .15f;
        Check(!menu.Capturing && GameControls.Label(0, GameAction.Skill, menu.Draft.bindings) == "F", "A real Input System key press completes UI capture");
        Check(GameControls.Label(0, GameAction.Skill) == "R", "Captured controls remain a draft until applied");
        Keys(); menu.CancelAndClose();
        Check(GameControls.Label(0, GameAction.Skill) == "R" && !PlayerPrefs.HasKey(prefix + "BindingsV1"), "Cancel discards the binding and leaves saved settings unchanged");
        host.SetActive(true); menu.SelectTab(3); menu.BeginCapture(GameAction.Skill); menu.CaptureBinding((int)KeyCode.F); menu.ApplyAndClose(); yield return .2f;
        GameSettings.Load(prefix, false);
        Check(GameControls.Label(0, GameAction.Skill) == "F", "Applied bindings survive save and reload");
        Keys(Key.R); Check(!CoopInput.Down(null, CoopButton.Skill, KeyCode.R), "The original skill key no longer triggers the action");
        Keys(); Keys(Key.F); Check(CoopInput.Down(null, CoopButton.Skill, KeyCode.R), "The rebound skill key reaches single-player gameplay"); Keys();
        host.SetActive(true); menu.SelectTab(3); menu.BeginCapture(GameAction.Skill); menu.CaptureBinding((int)KeyCode.W);
        Check(GameControls.Label(0, GameAction.Skill, menu.Draft.bindings) == "W" && GameControls.Label(0, GameAction.Forward, menu.Draft.bindings) == "F", "Same-context conflicts exchange both bindings");
        menu.BeginCapture(GameAction.Skill); menu.CaptureBinding((int)KeyCode.UpArrow);
        Check(menu.Capturing && GameControls.Label(0, GameAction.Skill, menu.Draft.bindings) == "W", "Shared-keyboard conflicts keep capture open without changing the draft");
        yield return .1f; InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return .1f;
        Check(!menu.Capturing && host.activeSelf && GameSettingsMenu.EscapeConsumedFrame >= 0, "Escape cancels capture without closing settings"); Keys();
        menu.BeginCapture(GameAction.Fire); menu.CaptureBinding((int)KeyCode.Mouse1); menu.ApplyAndClose(); yield return .2f;
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); InputSystem.Update();
        Check(CoopInput.FireHeld(null) && CoopInput.FireDown(null), "Rebound mouse input drives shooting press edges");
        InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.Update();
        Check(CoopInput.FireUp(null), "Rebound mouse release drives shooting release edges");
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left)); InputSystem.Update();
        Check(!CoopInput.FireHeld(null), "Old mouse fire input does not trigger the rebound action");
        InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.Update();
        host.SetActive(true); menu.SelectTab(3); menu.SelectBindingProfile(1); menu.BeginCapture(GameAction.Skill); menu.CaptureBinding((int)KeyCode.P);
        menu.ApplyAndClose(); yield return .2f; Keys(Key.P);
        Check((CoopInput.Read(1, 0).pressed & (int)CoopButton.Skill) != 0 && (CoopInput.Read(0, 0).pressed & (int)CoopButton.Skill) == 0, "The second keyboard profile changes independently from player one"); Keys();
        pad = InputSystem.AddDevice<Gamepad>(); host.SetActive(true); menu.SelectTab(3); menu.SelectBindingProfile(2); menu.BeginCapture(GameAction.Skill); yield return .1f;
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.West)); yield return .1f;
        Check(!menu.Capturing && GameControls.Label(2, GameAction.Skill, menu.Draft.bindings) == "X", "A real gamepad press completes the selected gamepad binding");
        menu.ApplyAndClose(); yield return .2f;
        InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update();
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.West)); InputSystem.Update();
        Check((CoopInput.Read(1, 0).pressed & (int)CoopButton.Skill) != 0, "The second gamepad profile reaches co-op input frames");
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.RightShoulder)); InputSystem.Update();
        Check((CoopInput.Read(1, 0).held & (int)CoopButton.Skill) == 0, "The old gamepad skill button no longer casts");
        InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update(); InputSystem.RemoveDevice(pad); pad = null;
        var corrupt = GameSettings.Current.Clone(); corrupt.bindings.playerOne[(int)GameAction.Jump] = 999999;
        Check(GameSettings.Normalize(corrupt).bindings.playerOne[(int)GameAction.Jump] == (int)KeyCode.Space, "Invalid stored keys recover to playable defaults");
        PlayerPrefs.SetString(prefix + "BindingsV1", "{bad json"); GameSettings.Load(prefix, false);
        Check(GameControls.Label(0, GameAction.Skill) == "R", "Corrupt stored JSON recovers without breaking startup");
        host.SetActive(true); menu.SelectTab(3); menu.SelectBindingProfile(0);
        menu.BeginCapture(GameAction.Skill); menu.CaptureBinding((int)KeyCode.F); menu.RestoreDefaults();
        Check(GameControls.Label(0, GameAction.Skill, menu.Draft.bindings) == "R", "Restore defaults resets bindings in the settings draft");
        menu.SelectTab(3); yield return .1f; Canvas.ForceUpdateCanvases();
        var scroll = (ScrollRect)Get(menu, "bindingScroll");
        Check(scroll.content.rect.height > scroll.viewport.rect.height, "All binding rows are reachable in a scrollable list");
        var labels = (Dictionary<GameAction, Text>)Get(menu, "keyLabels");
        var point = RectTransformUtility.WorldToScreenPoint(null, labels[GameAction.Forward].transform.position);
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        Check(hits.Exists(hit => hit.gameObject.GetComponentInParent<Button>() == labels[GameAction.Forward].GetComponentInParent<Button>()), "Visible binding buttons are reachable by the UI raycaster");
        var help = (Text)Get(menu, "help"); Check(help.preferredHeight <= help.rectTransform.rect.height, "Binding instructions fit the settings page");
        menu.CancelAndClose(); Object.Destroy(canvas); Object.Destroy(camera.gameObject); yield return .1f;
        // Exercise rebound movement and skill input through the actual split-screen actor update path.
        var data = GameSettings.Current.Clone(); GameControls.TryBind(data.bindings, 0, GameAction.Right, (int)KeyCode.F, out _);
        GameControls.TryBind(data.bindings, 1, GameAction.Skill, (int)KeyCode.P, out _); GameSettings.Apply(data, false);
        Check(CoopSession.StartSplit("Map1", "ranger", "medic"), "A split-screen run starts with custom bindings");
        var wait = Wait(() => CoopSession.Instance.SceneReady && !GameSceneFlow.IsLoading); while (wait.MoveNext()) yield return wait.Current;
        var actors = Object.FindObjectsByType<CoopActor>(FindObjectsSortMode.None); var first = Array.Find(actors, actor => actor.LocalIndex == 0); var second = Array.Find(actors, actor => actor.LocalIndex == 1);
        Vector3 start = first.transform.position; Keys(Key.F); yield return .35f; Keys();
        Check(Vector3.Distance(start, first.transform.position) > .2f, "Rebound movement moves the real split-screen player");
        float firstCooldown = first.GetComponent<CharacterSkills>().CooldownRemaining; Keys(Key.P); yield return .15f; Keys();
        Check(second.GetComponent<CharacterSkills>().CooldownRemaining > 0 && first.GetComponent<CharacterSkills>().CooldownRemaining <= firstCooldown, "Rebound player-two skill casts only that character's skill");
        Check(GameControls.Label(second, GameAction.Skill) == "P", "Character hints resolve the actor's own binding profile");
        Check(errors.Count == 0, "No unexpected Unity gameplay errors: " + string.Join(" | ", errors));
    }
}
