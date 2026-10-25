using System;
using UnityEngine;

// Clients send controls only. Large world/mesh snapshots travel in the opposite direction.
public static class CoopClientPolicy
{
    public const int MaximumMessageBytes = 4096;
    public const int MaximumWireBytes = 8192; // Includes deflate overhead for incompressible input.
    const int Buttons = (int)(CoopButton.Jump | CoopButton.Run | CoopButton.Crouch | CoopButton.Fire |
        CoopButton.Skill | CoopButton.Echo | CoopButton.Accept | CoopButton.Decline | CoopButton.View |
        CoopButton.Inventory | CoopButton.Pause | CoopButton.Slot1 | CoopButton.Slot2 | CoopButton.Slot3 | CoopButton.Heal);

    public static bool Valid(CoopPacket packet, bool joined)
    {
        // JsonUtility expands null serializable fields into default objects on the wire.
        // Authority comes from the allowed message kind; the host never applies client snapshot/mesh fields.
        if (packet == null || packet.version != CoopPacket.Version) return false;
        if (!joined) return packet.kind == "hello" && packet.scene != null && packet.scene.Length <= 128 &&
            packet.character != null && packet.character.Length <= 32 && (packet.password == null || packet.password.Length <= 64);
        switch (packet.kind)
        {
            case "campCharacter": return CharacterRoster.IsKnown(packet.character);
            case "campUpgrade": return packet.value >= 0 && packet.value < 3;
            case "campReady": return packet.value == 0 || packet.value == 1;
            case "campMove":
            case "input":
                var input = packet.input;
                return input != null && Finite(input.move.x) && Finite(input.move.y) &&
                    Mathf.Abs(input.move.x) <= 1 && Mathf.Abs(input.move.y) <= 1 && input.move.sqrMagnitude <= 1.001f &&
                    Finite(input.look.x) && Finite(input.look.y) && Finite(input.yaw) && Mathf.Abs(input.yaw) < 10000000 &&
                    Finite(input.pitch) && Mathf.Abs(input.pitch) <= 90 &&
                    ((input.held | input.pressed | input.released) & ~Buttons) == 0;
            case "upgrade": return packet.value >= 0 && packet.value < 3;
            case "command": return ValidCommand(packet);
            default: return false;
        }
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool ValidCommand(CoopPacket packet)
    {
        if (packet.itemId != null && packet.itemId.Length > 64) return false;
        switch (packet.action)
        {
            case "slot": return packet.value >= 0 && packet.value < 3;
            case "equip": return Index(packet.value) && packet.extra >= 0 && packet.extra < 3;
            case "equipArmor": case "sellArmor": case "sell": return Index(packet.value);
            case "unequipArmor": case "buyArmor": return packet.value >= 0 && packet.value < 4;
            case "buy": return Enum.IsDefined(typeof(WeaponType), packet.value) && CharacterWeapons.IsCharacterWeapon((WeaponType)packet.value);
            case "armor": return packet.value == 15 || packet.value == 30 || packet.value == 50;
            case "dropWeapon": case "dropArmor": return !string.IsNullOrEmpty(packet.itemId);
            case "heal": return true;
            default: return false;
        }
    }
    static bool Index(int value) => value >= 0 && value < 10000;

    // Token bucket: normal traffic is 30 inputs/second; bounded bursts tolerate jitter.
    public sealed class Rate
    {
        double tokens = 120, last;
        public Rate(double now) { last = now; }
        public bool Accept(double now)
        {
            tokens = Math.Min(120, tokens + Math.Max(0, now - last) * 120); last = now;
            if (tokens < 1) return false;
            tokens--; return true;
        }
    }
}
