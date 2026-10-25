using System;
using UnityEngine;

// Value 1 belonged to the removed local split-screen mode. Keep network IDs stable.
public enum CoopMode { SinglePlayer = 0, Host = 2, Client = 3 }
[Serializable] public sealed class CoopPacket
{
    public const int Version = 2;
    public int version = Version, id, value, extra;
    public string kind, scene, password, character, message, action, itemId;
    public CoopInputFrame input;
    public CoopSnapshot snapshot;
    public CoopMesh mesh;
    public CoopCampPlayer[] camp;
}
[Serializable] public sealed class CoopSnapshot
{
    public CoopPlayerState[] players;
    public CoopWorldState[] world;
    public int[] keys;
    public bool victory;
    public ExpeditionProgress expedition;
    public int expeditionActive = -1, expeditionWave, expeditionEnemies;
    public CoopTacticalVisual[] tacticalVisuals;
}
[Serializable] public sealed class CoopPlayerState
{
    public int id;
    public Vector3 position;
    public Quaternion rotation;
    public PlayerProgress health;
    public InventoryProgress inventory;
    public CharacterProgress character;
    public float effect;
    public CoopTacticsState tactics;
    public bool grounded, running, crouching, sliding;
    public float movement, verticalSpeed, cameraHeight;
    public string[] upgradeLabels;
}
[Serializable] public sealed class CoopWorldState
{
    public int id;
    public Vector3 position;
    public Quaternion rotation;
    public CoopPart[] parts;
}
[Serializable] public sealed class CoopPart
{
    public int mesh;
    public Vector3 position, scale;
    public Quaternion rotation;
    public Color color;
}
[Serializable] public sealed class CoopMesh
{
    public int id;
    public Vector3[] vertices;
    public Vector3[] normals;
    public Vector2[] uv;
    public int[] triangles;
}
[Serializable] public sealed class CoopTacticsState
{
    public float resonance;
    public string echo, pact, notice;
    public bool armed;
    public Vector3 position, direction;
    public float remaining, range;
}
[Serializable] public sealed class CoopTacticalVisual
{
    public int id;
    public Vector3 position, direction;
    public Color color;
    public float radius;
    public string text;
}
