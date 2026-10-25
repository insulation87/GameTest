using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Keep these numeric IDs stable: they are persisted with the player's settings.
public enum GameAction
{
    Forward, Backward, Left, Right, Jump, Run, Crouch, Fire, Skill, Echo, Accept, Decline,
    View, Inventory, Pause, Slot1, Slot2, Slot3, Heal = 19, // Retired action 18 remains an unused settings slot.
    LookLeft, LookRight, LookUp, LookDown, MenuPrevious, MenuNext, MenuSubmit
}
public enum PadBinding { A, B, X, Y, LB, RB, LT, RT, LeftStick, RightStick, Back, Start, Up, Down, Left, Right }

[Serializable]
public sealed class GameBindings
{
    // Retain the old arrays for settings deserialization; only playerOne is playable.
    public int[] playerOne, playerTwo, gamepad;
    public GameBindings Clone() => new GameBindings {
        playerOne = playerOne == null ? null : (int[])playerOne.Clone(),
        playerTwo = playerTwo == null ? null : (int[])playerTwo.Clone(),
        gamepad = gamepad == null ? null : (int[])gamepad.Clone() };
    public int[] Profile(int profile) => profile == 0 ? playerOne : profile == 1 ? playerTwo : gamepad;
}

public static class GameControls
{
    public static readonly GameAction[] Actions = (GameAction[])Enum.GetValues(typeof(GameAction));
    static readonly string[] names = { "向前移動", "向後移動", "向左移動", "向右移動", "跳躍", "跑步", "蹲下／滑行", "射擊",
        "角色技能", "戰術回聲", "接受誓約", "略過誓約", "切換視角", "背包", "暫停", "武器欄 1", "武器欄 2", "武器欄 3",
        "", "使用醫療包", "視角向左", "視角向右", "視角向上", "視角向下", "選單上一項", "選單下一項", "選單確認" };
    static readonly Dictionary<KeyCode, Key> keyboardKeys = BuildKeyboardKeys();
    static readonly Dictionary<CoopButton, GameAction> buttonActions = BuildButtonActions();
    static int blockedThroughFrame = -1;
    public static bool Recording { get; private set; }
    public static bool Blocked => Recording || Time.frameCount <= blockedThroughFrame;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Recording = false; blockedThroughFrame = -1; }
    public static void SetRecording(bool value) { Recording = value; if (!value) blockedThroughFrame = Time.frameCount + 1; }
    public static string Name(GameAction action) => names[(int)action];
    public static bool Available(int profile, GameAction action) => profile == 0 && (int)action >= 0 &&
        action < GameAction.LookLeft && (int)action != 18;
    // Pact choices can reuse a gameplay key without conflicting with gameplay actions.
    static int Context(GameAction action) => action >= GameAction.MenuPrevious ? 2 : action == GameAction.Accept || action == GameAction.Decline ? 1 : 0;
    public static GameBindings Defaults()
    {
        var one = new[] { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.Mouse0,
            KeyCode.R, KeyCode.Q, KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.E, KeyCode.Escape, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
            KeyCode.None, KeyCode.H, KeyCode.None, KeyCode.None, KeyCode.None, KeyCode.None, KeyCode.None, KeyCode.None, KeyCode.None };
        var two = new[] { KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Keypad0, KeyCode.RightShift,
            KeyCode.Delete, KeyCode.RightControl, KeyCode.PageUp, KeyCode.PageDown, KeyCode.KeypadPlus, KeyCode.KeypadMinus,
            KeyCode.Home, KeyCode.KeypadEnter, KeyCode.KeypadPeriod, KeyCode.Keypad1, KeyCode.Keypad3, KeyCode.Keypad7, KeyCode.None, KeyCode.Insert,
            KeyCode.Keypad4, KeyCode.Keypad6, KeyCode.Keypad8, KeyCode.Keypad2, KeyCode.Keypad8, KeyCode.Keypad2, KeyCode.Keypad5 };
        var pad = new int[(int)GameAction.MenuSubmit + 1];
        for (int i = 0; i < pad.Length; i++) pad[i] = -1;
        var buttons = new[] { PadBinding.A, PadBinding.LeftStick, PadBinding.B, PadBinding.RT, PadBinding.RB, PadBinding.LB,
            PadBinding.A, PadBinding.X, PadBinding.RightStick, PadBinding.Back, PadBinding.Start, PadBinding.Left, PadBinding.Up, PadBinding.Right,
            PadBinding.Y, PadBinding.Down };
        for (int i = 0; i < buttons.Length; i++) pad[(int)GameAction.Jump + i] = (int)buttons[i];
        pad[18] = -1;
        pad[(int)GameAction.MenuPrevious] = (int)PadBinding.Up; pad[(int)GameAction.MenuNext] = (int)PadBinding.Down;
        pad[(int)GameAction.MenuSubmit] = (int)PadBinding.A;
        return new GameBindings { playerOne = Array.ConvertAll(one, k => (int)k), playerTwo = Array.ConvertAll(two, k => (int)k), gamepad = pad };
    }
    public static GameBindings Normalize(GameBindings source)
    {
        var result = Defaults();
        for (int profile = 0; profile < 3; profile++)
        {
            int[] original = source?.Profile(profile), target = result.Profile(profile);
            if (original == null) continue;
            for (int i = 0; i < Mathf.Min(original.Length, target.Length); i++)
                if (Available(profile, (GameAction)i) && Valid(profile, original[i], (GameAction)i)) target[i] = original[i];
            // Repair corrupt duplicate mappings without discarding other valid settings.
            for (int i = 0; i < target.Length; i++)
                for (int j = 0; j < i; j++)
                    if (Available(profile, (GameAction)i) && Available(profile, (GameAction)j) && Context((GameAction)i) == Context((GameAction)j) && target[i] == target[j])
                    { target = result.Profile(profile); Array.Copy(Defaults().Profile(profile), target, target.Length); i = target.Length; break; }
        }
        return result;
    }
    public static bool Valid(int profile, int value, GameAction action)
    {
        if (profile != 0) return false;
        var code = (KeyCode)value;
        return (keyboardKeys.ContainsKey(code) || profile == 0 && code >= KeyCode.Mouse0 && code <= KeyCode.Mouse4) &&
            (code != KeyCode.Escape || profile == 0 && action == GameAction.Pause);
    }
    public static bool TryBind(GameBindings draft, int profile, GameAction action, int value, out string message)
    {
        if (!Available(profile, action) || !Valid(profile, value, action)) { message = "此按鍵不能用於這項操作；Esc 保留為取消／返回。"; return false; }
        int[] keys = draft.Profile(profile); int old = keys[(int)action];
        foreach (GameAction other in Actions)
            if (other != action && Available(profile, other) && Context(other) == Context(action) && keys[(int)other] == value)
            {
                if (!Valid(profile, old, other)) { message = "此按鍵已用於「" + Name(other) + "」，請選擇其他按鍵。"; return false; }
                keys[(int)other] = old; keys[(int)action] = value; message = "已交換「" + Name(action) + "」與「" + Name(other) + "」的按鍵。"; return true;
            }
        keys[(int)action] = value; message = "已設定「" + Name(action) + "」；按「套用並儲存」後生效。"; return true;
    }
    public static string Label(int profile, GameAction action, GameBindings bindings = null) =>
        LabelValue(profile, (bindings ?? GameSettings.Current.bindings).Profile(profile)[(int)action]);
    public static string Label(Component owner, GameAction action)
    {
        return Label(0, action);
    }
    public static string LabelValue(int profile, int value)
    {
        if (profile == 2) return Enum.IsDefined(typeof(PadBinding), value) ? ((PadBinding)value).ToString() : "—";
        var key = (KeyCode)value;
        switch (key)
        {
            case KeyCode.Mouse0: return "滑鼠左鍵"; case KeyCode.Mouse1: return "滑鼠右鍵"; case KeyCode.Mouse2: return "滑鼠中鍵";
            case KeyCode.Mouse3: return "滑鼠側鍵 1"; case KeyCode.Mouse4: return "滑鼠側鍵 2";
            case KeyCode.Space: return "空白鍵"; case KeyCode.LeftControl: return "左 Ctrl"; case KeyCode.RightControl: return "右 Ctrl";
            case KeyCode.LeftShift: return "左 Shift"; case KeyCode.RightShift: return "右 Shift"; case KeyCode.Escape: return "Esc";
            case KeyCode.UpArrow: return "↑"; case KeyCode.DownArrow: return "↓"; case KeyCode.LeftArrow: return "←"; case KeyCode.RightArrow: return "→";
            case KeyCode.None: return "—";
            default: return key.ToString().Replace("Alpha", "").Replace("Keypad", "Num ");
        }
    }
    public static bool Held(int profile, GameAction action) => !Blocked && Raw(profile, action, 0);
    public static bool Down(int profile, GameAction action) => !Blocked && (Raw(profile, action, 1) || profile == 0 && action == GameAction.Pause && EscapeDown);
    public static bool Up(int profile, GameAction action) => !Blocked && Raw(profile, action, 2);
    public static bool EscapeDown => Keyboard.current != null ? Keyboard.current.escapeKey.wasPressedThisFrame : Input.GetKeyDown(KeyCode.Escape);
    static bool Raw(int profile, GameAction action, int edge)
    {
        if (!Available(profile, action)) return false;
        int value = GameSettings.Current.bindings.Profile(profile)[(int)action];
        if (profile == 2) { var pad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null; return ReadButton(PadControl(pad, value), edge); }
        var code = (KeyCode)value;
        if (code >= KeyCode.Mouse0 && code <= KeyCode.Mouse4 && Mouse.current != null) return ReadButton(MouseControl(Mouse.current, code), edge);
        if (Keyboard.current != null && keyboardKeys.TryGetValue(code, out Key key)) return ReadButton(Keyboard.current[key], edge);
        return edge == 1 ? Input.GetKeyDown(code) : edge == 2 ? Input.GetKeyUp(code) : Input.GetKey(code);
    }
    static bool ReadButton(ButtonControl button, int edge) => button != null && (edge == 1 ? button.wasPressedThisFrame : edge == 2 ? button.wasReleasedThisFrame : button.isPressed);
    static ButtonControl MouseControl(Mouse mouse, KeyCode code) => code == KeyCode.Mouse0 ? mouse.leftButton : code == KeyCode.Mouse1 ? mouse.rightButton :
        code == KeyCode.Mouse2 ? mouse.middleButton : code == KeyCode.Mouse3 ? mouse.backButton : mouse.forwardButton;
    static ButtonControl PadControl(Gamepad pad, int value)
    {
        if (pad == null) return null;
        switch ((PadBinding)value)
        {
            case PadBinding.A: return pad.buttonSouth; case PadBinding.B: return pad.buttonEast; case PadBinding.X: return pad.buttonWest; case PadBinding.Y: return pad.buttonNorth;
            case PadBinding.LB: return pad.leftShoulder; case PadBinding.RB: return pad.rightShoulder; case PadBinding.LT: return pad.leftTrigger; case PadBinding.RT: return pad.rightTrigger;
            case PadBinding.LeftStick: return pad.leftStickButton; case PadBinding.RightStick: return pad.rightStickButton; case PadBinding.Back: return pad.selectButton; case PadBinding.Start: return pad.startButton;
            case PadBinding.Up: return pad.dpad.up; case PadBinding.Down: return pad.dpad.down; case PadBinding.Left: return pad.dpad.left; case PadBinding.Right: return pad.dpad.right;
            default: return null;
        }
    }
    public static bool TryCapture(int profile, out int value)
    {
        value = -1;
        if (profile != 0) return false;
        if (profile == 2)
        { var pad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null; for (int i = 0; i <= (int)PadBinding.Right; i++) if (ReadButton(PadControl(pad, i), 1)) { value = i; return true; } }
        else
        {
            if (Keyboard.current != null) foreach (var entry in keyboardKeys) if (Keyboard.current[entry.Value].wasPressedThisFrame) { value = (int)entry.Key; return true; }
            if (profile == 0 && Mouse.current != null) for (int i = 0; i < 5; i++) if (ReadButton(MouseControl(Mouse.current, KeyCode.Mouse0 + i), 1)) { value = (int)KeyCode.Mouse0 + i; return true; }
        }
        return false;
    }
    public static Vector2 Movement(int profile) => new Vector2((Held(profile, GameAction.Right) ? 1 : 0) - (Held(profile, GameAction.Left) ? 1 : 0),
        (Held(profile, GameAction.Forward) ? 1 : 0) - (Held(profile, GameAction.Backward) ? 1 : 0));
    public static GameAction Action(CoopButton button) => buttonActions[button];
    static Dictionary<CoopButton, GameAction> BuildButtonActions()
    {
        var result = new Dictionary<CoopButton, GameAction>();
        foreach (CoopButton button in Enum.GetValues(typeof(CoopButton)))
            if (button != CoopButton.None) result[button] = (GameAction)Enum.Parse(typeof(GameAction), button.ToString());
        return result;
    }
    static Dictionary<KeyCode, Key> BuildKeyboardKeys()
    {
        var keys = new Dictionary<KeyCode, Key>();
        foreach (Key key in Enum.GetValues(typeof(Key)))
        {
            string name = key.ToString().Replace("Numpad", "Keypad");
            if (name.StartsWith("Digit")) name = name.Replace("Digit", "Alpha");
            name = name.Replace("Ctrl", "Control"); if (name == "Enter") name = "Return";
            if (name == "Backquote") name = "BackQuote";
            if (Enum.TryParse(name, out KeyCode code) && code != KeyCode.None) keys[code] = key;
        }
        return keys;
    }
}
