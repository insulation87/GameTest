using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class TradingHubController
{
    readonly Dictionary<Color, Material> sceneMaterials = new Dictionary<Color, Material>();
    static readonly Color Stone = new Color(.23f, .29f, .31f), Wood = new Color(.32f, .21f, .14f);
    GameObject Block(string label, Vector3 position, Vector3 scale, Color tint, bool solid = true, PrimitiveType shape = PrimitiveType.Cube)
    {
        var node = GameObject.CreatePrimitive(shape); node.name = label; node.transform.SetParent(transform, false);
        node.transform.localPosition = position; node.transform.localScale = scale;
        node.GetComponent<Collider>().enabled = solid;
        if (!sceneMaterials.TryGetValue(tint, out var material)) {
            // A Resources material keeps its shader and variants in player builds.
            // Shader.Find alone does not create a build dependency.
            material = new Material(Resources.Load<Material>("TradingHubSurface")) { color = tint };
            sceneMaterials.Add(tint, material);
        }
        node.GetComponent<Renderer>().sharedMaterial = material; return node;
    }
    void BuildCourtyard()
    {
        var cameraNode = new GameObject("Camp Camera", typeof(Camera));
        cameraNode.transform.SetParent(transform, false); cameraNode.tag = "MainCamera";
        view = cameraNode.GetComponent<Camera>(); view.orthographic = true; view.orthographicSize = 15.2f;
        view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(.08f, .14f, .17f);
        view.farClipPlane = 100;
        Block("Meadow", new Vector3(0, -.65f, 2), new Vector3(80, .6f, 80), new Color(.14f, .25f, .19f));
        Block("Courtyard Floor", new Vector3(0, -.25f, 1), new Vector3(25, .5f, 29), Stone);
        for (int x = -11; x <= 11; x += 2)
            for (int z = -12; z <= 14; z += 2)
                Block("Paving", new Vector3(x, .013f, z), new Vector3(1.92f, .025f, 1.92f),
                    (x + z) % 4 == 0 ? new Color(.29f, .35f, .36f) : new Color(.26f, .32f, .33f), false);
        Block("West Boundary", new Vector3(-12.5f, .8f, 1), new Vector3(.6f, 1.6f, 29), Stone);
        Block("East Boundary", new Vector3(12.5f, .8f, 1), new Vector3(.6f, 1.6f, 29), Stone);
        Block("North Boundary", new Vector3(0, .8f, 15.5f), new Vector3(25, 1.6f, .6f), Stone);
        Block("South Boundary", new Vector3(0, .8f, -13.5f), new Vector3(25, 1.6f, .6f), Stone);
        for (int i = 0; i < MerchantPositions.Length; i++) BuildStall(i);
        // A lit campfire and benches leave the central walking path open.
        var fire = new Vector3(7, 0, -5);
        Block("Fire Pit", fire + Vector3.up * .18f, new Vector3(2, .3f, 2), Stone, true, PrimitiveType.Cylinder);
        Block("Fire", fire + Vector3.up * .65f, new Vector3(.75f, 1, .75f), new Color(1, .55f, .16f), false, PrimitiveType.Sphere);
        Block("Bench", new Vector3(7, .45f, -8), new Vector3(3, .8f, .75f), Wood);
        for (int i = 0; i < 12; i++) {
            float angle = i * Mathf.PI / 6;
            var at = new Vector3(Mathf.Sin(angle) * 18, 0, 1 + Mathf.Cos(angle) * 22);
            Block("Pine Trunk", at + Vector3.up * 1.6f, new Vector3(.6f, 3.2f, .6f), Wood);
            Block("Pine Crown", at + Vector3.up * 3.6f, new Vector3(3, 4, 3), new Color(.11f, .30f, .22f), false, PrimitiveType.Sphere);
        }
        for (int i = 0; i < ExpeditionMaps.All.Length; i++) BuildPortal(i);
    }
    void BuildStall(int index)
    {
        var at = MerchantPositions[index];
        Color tint = index == 0 ? new Color(.68f, .36f, .22f) : index == 1 ? new Color(.31f, .46f, .69f) : new Color(.28f, .60f, .47f);
        Block("Merchant Platform", at + new Vector3(0, .08f, .4f), new Vector3(4.7f, .16f, 3.6f), Stone);
        foreach (float side in new[] { -1f, 1f }) Block("Canopy Post", at + new Vector3(side * 2, 1.8f, .8f), new Vector3(.16f, 3.6f, .16f), Wood);
        Block("Cloth Canopy", at + new Vector3(0, 3.6f, .6f), new Vector3(4.6f, .18f, 3), tint, false);
        Block("Counter", at + new Vector3(0, .65f, -1.2f), new Vector3(3.4f, 1.1f, .7f), Wood);
        Block("Counter Top", at + new Vector3(0, 1.24f, -1.2f), new Vector3(3.7f, .12f, .9f), new Color(.50f, .36f, .23f));
        var npc = new GameObject(MerchantNames[index]); npc.transform.SetParent(transform, false); npc.transform.localPosition = at + Vector3.up * .16f;
        npc.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var model = PixelCharacterFactory.Create(npc.transform, "Npc" + (index + 1));
        foreach (Transform part in model.GetComponentsInChildren<Transform>())
            if (part.name == "Sword" || part.name == "Shield" || part.name == "Crossbow") part.gameObject.SetActive(false);
        model.GetComponent<Animation>().Play("Idle");
        var collision = npc.AddComponent<CapsuleCollider>(); collision.height = 1.9f; collision.radius = .45f; collision.center = Vector3.up;
        Sign(MerchantNames[index], at + new Vector3(0, 3.05f, -1.15f), tint);
        for (int i = -1; i <= 1; i++) {
            var prop = at + new Vector3(i * .9f, 1.48f, -1.2f);
            Block("Merchant Goods", prop, index == 0 ? new Vector3(.12f, .55f, .15f) : new Vector3(.36f, .36f, .36f),
                index == 2 ? new Color(.40f, .88f, .71f) : GameUITheme.Gold, false, index == 2 ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        }
    }
    void BuildPortal(int index)
    {
        var map = ExpeditionMaps.All[index]; var at = PortalPosition(index);
        var root = new GameObject("Adventure Portal " + map.Scene, typeof(TradingHubPortal));
        root.transform.SetParent(transform, false); root.transform.localPosition = at;
        root.GetComponent<TradingHubPortal>().Initialize(index);
        Color glow = index == 0 ? new Color(.35f,.90f,.55f) : index == 1 ? new Color(1,.74f,.30f) :
            index == 2 ? new Color(.48f,.85f,1) : new Color(1,.35f,.16f);
        GameObject Piece(string label, Vector3 offset, Vector3 scale, Color tint)
        {
            var piece = Block(label, at + offset, scale, tint, false);
            piece.transform.SetParent(root.transform, true); return piece;
        }
        Piece("Portal Dais", Vector3.zero, new Vector3(4.5f, .12f, 3.4f), Color.Lerp(Stone,map.Ground,.5f));
        for (int i = 0; i < 16; i++) {
            float angle = i * Mathf.PI * 2 / 16;
            var stone = Piece("Portal Arch", new Vector3(Mathf.Cos(angle) * 2.1f, 2.5f + Mathf.Sin(angle) * 2.1f, .1f),
                new Vector3(.65f, .6f, .7f), i % 2 == 0 ? Stone : Color.Lerp(map.Rock,glow,.35f));
            stone.transform.localRotation = Quaternion.Euler(0, 0, i * 22.5f);
        }
        var portalRune = new GameObject("Turning Portal Rune").transform;
        portalRune.SetParent(root.transform, false); portalRune.localPosition = Vector3.up * 2.5f; portalRunes.Add(portalRune);
        for (int i = 0; i < 12; i++) {
            float angle = i * Mathf.PI / 6;
            var glyph = Piece("Portal Light", Vector3.up * 2.5f + new Vector3(Mathf.Cos(angle) * 1.55f, Mathf.Sin(angle) * 1.55f, -.1f),
                new Vector3(.17f, .42f, .13f), glow);
            glyph.transform.SetParent(portalRune, true); glyph.transform.localRotation = Quaternion.Euler(0, 0, i * 30);
        }
        var sign = Sign((index + 1) + " · " + map.Name + "\n" + new[]{"入門", "進階", "困難", "極難"}[index] + " · 3 關 · F", at + new Vector3(0, 5.4f, 0), glow, true);
        sign.transform.SetParent(root.transform, true);
    }
    GameObject Sign(string text, Vector3 position, Color tint, bool portal = false)
    {
        var node = new GameObject(text + " Sign", typeof(RectTransform), typeof(Canvas)); node.transform.SetParent(transform, false);
        node.transform.localPosition = position; node.transform.localRotation = Quaternion.Euler(46, 0, 0); node.transform.localScale = Vector3.one * .006f;
        node.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var size = new Vector2(780, portal ? 260 : 110);
        var rect = node.GetComponent<RectTransform>(); rect.sizeDelta = size;
        var panel = AccountLoginPanel.Box(rect, "Sign Back", Vector2.zero, size, GameUITheme.Ink);
        GameUITheme.Label(panel, text, portal ? 54 : 47, Vector2.zero, size - new Vector2(30,10),
            portal ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft).color = tint;
        return node;
    }
    void BuildVisitor()
    {
        var actor = new GameObject("Camp Visitor"); actor.transform.SetParent(transform, false); actor.transform.localPosition = new Vector3(0, .15f, -8);
        walker = actor.AddComponent<CharacterController>(); walker.height = 1.8f; walker.radius = .35f; walker.center = new Vector3(0, .9f, 0); walker.stepOffset = .3f;
        var model = PixelCharacterFactory.Create(actor.transform, "Player");
        PixelCharacterFactory.ApplyCharacterStyle(model, CharacterRoster.Find(CoopSession.InCamp ? CoopSession.Instance.LocalCampPlayer?.character : stock.CharacterId));
        walkingAnimation = model.GetComponent<Animation>(); walkingAnimation.Play("Idle");
        // This visitor is deliberately independent of combat and the map's automatic saving.
    }
}
