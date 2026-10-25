using System;
using UnityEngine;
using UnityEngine.InputSystem;

[Flags]
public enum CoopButton
{
    None = 0, Jump = 1, Run = 2, Crouch = 4, Fire = 8, Skill = 16, Echo = 32,
    Accept = 64, Decline = 128, View = 256, Inventory = 512, Pause = 1024,
    Slot1 = 2048, Slot2 = 4096, Slot3 = 8192, Heal = 32768
}
[Serializable]
public sealed class CoopInputFrame
{
    public Vector2 move, look;
    public int held, pressed, released;
    public float yaw, pitch;
    public bool firstPerson = true;
    public void ClearEdges() { pressed = released = 0; look = Vector2.zero; }
}

public static class CoopInput
{
    static readonly CoopButton[] Buttons = Array.FindAll((CoopButton[])Enum.GetValues(typeof(CoopButton)), button => button != CoopButton.None);
    public static CoopActor Actor(Component owner)
    {
        if (!CoopSession.IsMultiplayer || owner == null) return null;
        var actor = owner.GetComponentInParent<CoopActor>();
        if (actor != null) return actor;
        if (owner is FPSCameraLook fps && fps.player != null) return fps.player.GetComponent<CoopActor>();
        if (owner is CameraFollowTPS tps && tps.target != null) return tps.target.GetComponent<CoopActor>();
        return null;
    }
    public static bool Down(Component owner, CoopButton button, KeyCode fallback)
    { var actor = Actor(owner); return !GameControls.Blocked && (actor != null ? (actor.Frame.pressed & (int)button) != 0 : GameControls.Down(0, GameControls.Action(button))); }
    public static bool Held(Component owner, CoopButton button)
    { var actor = Actor(owner); return actor != null ? (actor.Frame.held & (int)button) != 0 : GameControls.Held(0, GameControls.Action(button)); }
    public static bool FireHeld(Component owner) => Actor(owner) != null ? Held(owner, CoopButton.Fire) : GameControls.Held(0, GameAction.Fire);
    public static bool FireDown(Component owner) => Actor(owner) != null ? (Actor(owner).Frame.pressed & (int)CoopButton.Fire) != 0 : GameControls.Down(0, GameAction.Fire);
    public static bool FireUp(Component owner) => Actor(owner) != null ? (Actor(owner).Frame.released & (int)CoopButton.Fire) != 0 : GameControls.Up(0, GameAction.Fire);
    public static Vector2 Look(Component owner, float sensitivity)
    { var actor = Actor(owner); return actor != null ? actor.Frame.look : GameSettings.ReadMouseLook(sensitivity); }

    public static CoopInputFrame Read(int localIndex, int previousHeld)
    {
        var frame = new CoopInputFrame();
        if (localIndex != 0 || GameControls.Blocked) return frame;
        frame.move = GameControls.Movement(0);
        frame.look = GameSettings.ReadMouseLook(200);
        foreach (CoopButton button in Buttons)
            if (GameControls.Held(0, GameControls.Action(button))) frame.held |= (int)button;
        if (GameControls.EscapeDown) frame.held |= (int)CoopButton.Pause;
        frame.move = Vector2.ClampMagnitude(frame.move, 1);
        frame.pressed = frame.held & ~previousHeld;
        frame.released = previousHeld & ~frame.held;
        return frame;
    }
}

