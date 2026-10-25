using System.Collections.Generic;
using UnityEngine;

public static class TacticalNetworkVisuals
{
    static readonly Dictionary<int,TacticalMarker> proxies = new Dictionary<int,TacticalMarker>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => proxies.Clear();
    public static CoopTacticalVisual[] Capture()
    {
        var list = new List<CoopTacticalVisual>();
        foreach (var marker in Object.FindObjectsByType<TacticalMarker>(FindObjectsSortMode.None))
        {
            if (!marker.Replicate || list.Count >= 64) continue;
            list.Add(new CoopTacticalVisual { id = marker.GetInstanceID(), position = marker.transform.position,
                direction = marker.transform.forward, color = marker.Color, radius = marker.Radius, text = marker.Text });
        }
        return list.ToArray();
    }
    public static void Apply(CoopTacticalVisual[] states)
    {
        if (states == null || states.Length > 64) return;
        var seen = new HashSet<int>();
        foreach (var state in states)
        {
            if (state == null || !Finite(state.position) || !Finite(state.direction) || float.IsNaN(state.radius) || state.radius < .1f || state.radius > 40) continue;
            seen.Add(state.id);
            if (!proxies.TryGetValue(state.id,out var marker) || marker == null)
                proxies[state.id] = marker = TacticalMarker.Create("隊伍戰術標記", state.position,state.direction,state.color,state.radius);
            marker.transform.SetPositionAndRotation(state.position,Quaternion.LookRotation(state.direction.sqrMagnitude > .01f ? state.direction : Vector3.forward));
            marker.SetText(state.text == null ? "" : state.text.Substring(0,Mathf.Min(256,state.text.Length)));
        }
        foreach (var id in new List<int>(proxies.Keys))
            if (!seen.Contains(id)) { if (proxies[id] != null) Object.Destroy(proxies[id].gameObject); proxies.Remove(id); }
    }
    static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
        !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
}
