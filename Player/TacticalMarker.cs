using UnityEngine;
using UnityEngine.UI;

// A collider-free world marker: circle, firing arrow and readable status.
public sealed class TacticalMarker : MonoBehaviour
{
    Text label;
    Canvas canvas;
    Material material;
    public bool Replicate { get; set; }
    public string Text => label != null ? label.text : "";
    public Color Color { get; private set; }
    public float Radius { get; private set; }
    public static TacticalMarker Create(string name, Vector3 position, Vector3 direction, Color color, float radius = 1f)
    {
        var node = new GameObject(name);
        node.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction.sqrMagnitude > .01f ? direction : Vector3.forward));
        var marker = node.AddComponent<TacticalMarker>();
        marker.Color = color; marker.Radius = radius;
        marker.material = new Material(Shader.Find("Sprites/Default"));
        marker.material.color = color;
        var ring = new Vector3[33];
        for (int i = 0; i < ring.Length; i++)
            ring[i] = position + new Vector3(Mathf.Sin(i * Mathf.PI / 16)*radius, .08f, Mathf.Cos(i * Mathf.PI / 16)*radius);
        marker.Line("Range", ring, color);
        Vector3 forward = node.transform.forward;
        marker.Line("Direction", new[] { position, position + forward * 3f,
            position + forward * 2.3f + node.transform.right * .5f,
            position + forward * 3f, position + forward * 2.3f - node.transform.right * .5f }, color);
        var panel = new GameObject("World Status", typeof(Canvas), typeof(CanvasScaler), typeof(Image));
        panel.transform.SetParent(node.transform, false);
        marker.canvas = panel.GetComponent<Canvas>(); marker.canvas.renderMode = RenderMode.WorldSpace;
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(480, 120);
        panel.transform.localScale = Vector3.one * .012f;
        panel.transform.localPosition = Vector3.up * 2.8f;
        panel.GetComponent<Image>().color = new Color(.025f,.045f,.075f,.88f); panel.GetComponent<Image>().raycastTarget = false;
        marker.label = GameUITheme.Label(panel.transform, name, 32, Vector2.zero, new Vector2(460,110), TextAnchor.MiddleCenter);
        marker.label.resizeTextForBestFit = true; marker.label.resizeTextMinSize = 20; marker.label.resizeTextMaxSize = 32;
        marker.label.color = color; marker.label.raycastTarget = false;
        return marker;
    }
    void Line(string name, Vector3[] points, Color color)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false); line.useWorldSpace = false;
        line.sharedMaterial = material; line.startColor = line.endColor = color;
        for (int i=0;i<points.Length;i++) points[i] = transform.InverseTransformPoint(points[i]);
        line.startWidth = line.endWidth = .075f; line.positionCount = points.Length; line.SetPositions(points);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    public void SetText(string text) { if (label != null) label.text = text; }
    void LateUpdate()
    {
        var camera = PixelArtDirector.ActiveGameplayCamera != null ? PixelArtDirector.ActiveGameplayCamera : Camera.main;
        if (camera != null && canvas != null) canvas.transform.rotation = camera.transform.rotation;
    }
    void OnDestroy() { if (material != null) Destroy(material); }
}
