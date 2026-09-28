using System;
using MedievalDungeonKit;
using UnityEngine;
using UnityEngine.AI;

// Local-space room footprint. Markers inherit the dungeon's position, rotation and scale.
[DisallowMultipleComponent]
public class DungeonRoomBoundary : MonoBehaviour
{
    public Vector2 halfSize = new Vector2(14f, 14f);
    [Min(0f)] public float wallInset = .45f;
    public float minimumHeight = -.5f;
    public float maximumHeight = 6f;

    [Serializable] class RoomDefinition { public string id; public float halfSize; public int floor; }
    [Serializable] class Layout { public RoomDefinition[] rooms; }
    static Layout layout;

    public static DungeonRoomBoundary GetOrCreate(DungeonMarker marker)
    {
        if (marker == null || !string.Equals(marker.kind, "Room", StringComparison.OrdinalIgnoreCase))
            return null;
        if (marker.TryGetComponent(out DungeonRoomBoundary existing)) return existing;
        if (layout == null)
        {
            TextAsset data = Resources.Load<TextAsset>("DungeonRoomLayout");
            if (data != null) layout = JsonUtility.FromJson<Layout>(data.text);
        }
        if (layout?.rooms == null) return null;
        foreach (RoomDefinition definition in layout.rooms)
        {
            if (marker.name != definition.id && !marker.name.StartsWith(definition.id + "_", StringComparison.Ordinal))
                continue;
            DungeonRoomBoundary boundary = marker.gameObject.AddComponent<DungeonRoomBoundary>();
            boundary.halfSize = Vector2.one * definition.halfSize;
            return boundary;
        }
        // Unknown layouts need an explicit boundary, rather than guessing into a corridor.
        return null;
    }

    public static DungeonRoomBoundary FindContaining(Vector3 point, float clearance = 0f)
    {
        foreach (DungeonRoomBoundary boundary in FindObjectsByType<DungeonRoomBoundary>(FindObjectsSortMode.None))
            if (boundary.Contains(point, clearance)) return boundary;
        foreach (DungeonMarker marker in FindObjectsByType<DungeonMarker>(FindObjectsSortMode.None))
        {
            DungeonRoomBoundary boundary = GetOrCreate(marker);
            if (boundary != null && boundary.Contains(point, clearance)) return boundary;
        }
        return null;
    }

    Vector2 Interior(float clearance)
    {
        Vector3 scale = transform.lossyScale;
        return new Vector2(halfSize.x - wallInset - clearance / Mathf.Max(.001f, Mathf.Abs(scale.x)),
            halfSize.y - wallInset - clearance / Mathf.Max(.001f, Mathf.Abs(scale.z)));
    }

    public bool Contains(Vector3 point, float clearance = 0f)
    {
        Vector3 local = transform.InverseTransformPoint(point);
        Vector2 interior = Interior(clearance);
        return interior.x > 0f && interior.y > 0f &&
            Mathf.Abs(local.x) <= interior.x && Mathf.Abs(local.z) <= interior.y &&
            local.y >= minimumHeight && local.y <= maximumHeight;
    }

    public bool TryGetNavMeshPoint(Vector3 desired, float clearance, NavMeshQueryFilter filter, out Vector3 point)
    {
        Vector2 interior = Interior(clearance);
        point = default;
        if (interior.x <= 0f || interior.y <= 0f) return false;
        Vector3 local = transform.InverseTransformPoint(desired);
        local.x = Mathf.Clamp(local.x, -interior.x, interior.x);
        local.z = Mathf.Clamp(local.z, -interior.y, interior.y);
        local.y = 0f;
        Vector3 candidate = transform.TransformPoint(local);
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, filter) || !Contains(hit.position, clearance))
            return false;
        point = hit.position;
        return true;
    }

    public bool ContainsPath(NavMeshPath path, float clearance)
    {
        if (path == null || path.status != NavMeshPathStatus.PathComplete) return false;
        Vector3[] corners = path.corners;
        if (corners.Length == 0) return false;
        // The footprint is convex, so every segment between contained corners stays inside.
        foreach (Vector3 corner in corners)
            if (!Contains(corner, clearance)) return false;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, (minimumHeight + maximumHeight) * .5f, 0f),
            new Vector3(Mathf.Max(0f, halfSize.x - wallInset) * 2f, maximumHeight - minimumHeight,
                Mathf.Max(0f, halfSize.y - wallInset) * 2f));
        Gizmos.matrix = Matrix4x4.identity;
    }
}
