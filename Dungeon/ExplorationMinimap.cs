using UnityEngine;
using UnityEngine.UI;
using MedievalDungeonKit;

public partial class ExplorationMinimap : MonoBehaviour
{
    [Header("Display")]
    [Min(128)] public int textureSize = 512;
    [Min(100f)] public float uiSize = 260f;
    [Min(8)] public int fogGridSize = 30;
    [Min(1f)] public float revealRadius = 18f;
    public Color unexploredColor = new Color(0.025f, 0.035f, 0.045f, 0.98f);

    private Player player;
    private DungeonViewSettings dungeon;
    private Camera mapCamera;
    private RenderTexture mapTexture;
    private RectTransform mapRect;
    private RectTransform playerMarker;
    private Image[][] fogTiles = new Image[3][];
    private Bounds[] floorBounds = new Bounds[3];
    private float[] floorHeights = new float[3];
    private int currentFloor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<Player>() == null ||
            FindFirstObjectByType<DungeonViewSettings>() == null ||
            FindFirstObjectByType<ExplorationMinimap>() != null)
            return;

        new GameObject("Exploration Minimap").AddComponent<ExplorationMinimap>();
    }

    void Awake()
    {
        OverworldDirector.EnsureWorld();
        player = FindFirstObjectByType<Player>();
        dungeon = FindFirstObjectByType<DungeonViewSettings>();
        if (player == null || dungeon == null)
        {
            enabled = false;
            return;
        }

        CalculateFloorBounds();
        BuildCamera();
        BuildUI();
        SetFloor(GetClosestFloor());
    }

    void LateUpdate()
    {
        if (player == null)
            return;

        int playerFloor = GetClosestFloor();
        if (playerFloor != currentFloor)
            SetFloor(playerFloor);

        RevealAroundPlayer();
        UpdatePlayerMarker();
        UpdateObjectiveMarker();
    }

    void CalculateFloorBounds()
    {
        var world = FindFirstObjectByType<OverworldDirector>();
        if (world != null)
        {
            for (int i = 0; i < 3; i++) { floorBounds[i] = world.RegionBounds[i]; floorHeights[i] = floorBounds[i].center.y; }
            return;
        }
        GameObject[] levels = { dungeon.level1, dungeon.level2, dungeon.level3 };
        for (int floorIndex = 0; floorIndex < levels.Length; floorIndex++)
        {
            if (levels[floorIndex] != null && TryGetBounds(levels[floorIndex], out Bounds bounds))
            {
                floorBounds[floorIndex] = bounds;
                floorHeights[floorIndex] = bounds.min.y;
            }
            else
            {
                floorBounds[floorIndex] = new Bounds(
                    dungeon.transform.position + new Vector3(150f, floorIndex * 12f, 150f),
                    new Vector3(300f, 8f, 300f));
                floorHeights[floorIndex] = dungeon.transform.position.y + floorIndex * 12f;
            }

            Vector3 floorSize = floorBounds[floorIndex].size;
            float squareSize = Mathf.Max(floorSize.x, floorSize.z);
            floorBounds[floorIndex].size = new Vector3(squareSize, floorSize.y, squareSize);
        }
    }

    static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            bounds.Encapsulate(renderers[rendererIndex].bounds);
        return true;
    }

    void BuildCamera()
    {
        GameObject cameraObject = new GameObject("Minimap Camera");
        cameraObject.transform.SetParent(transform, false);
        mapCamera = cameraObject.AddComponent<Camera>();
        mapCamera.orthographic = true;
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = new Color(0.035f, 0.055f, 0.065f, 1f);
        mapCamera.nearClipPlane = 0.1f;
        mapCamera.farClipPlane = 35f;
        mapCamera.depth = -10f;

        mapTexture = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.ARGB32);
        mapTexture.name = "Exploration Minimap Texture";
        mapTexture.Create();
        mapCamera.targetTexture = mapTexture;
    }

    void BuildUI()
    {
        GameObject canvasObject = new GameObject("Minimap Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;
        canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);

        GameObject frame = CreateImage("Minimap Frame", canvasObject.transform, new Color(0.05f, 0.07f, 0.08f, 0.95f));
        RectTransform frameRect = frame.GetComponent<RectTransform>();
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(1f, 1f);
        frameRect.pivot = new Vector2(1f, 1f);
        frameRect.anchoredPosition = new Vector2(-20f, -20f);
        frameRect.sizeDelta = new Vector2(uiSize + 12f, uiSize + 38f);
        GameUITheme.Surface(frame);
        GameUITheme.FitCanvas(canvasObject.GetComponent<CanvasScaler>());

        GameObject rawObject = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        mapRect = rawObject.GetComponent<RectTransform>();
        mapRect.SetParent(frameRect, false);
        mapRect.anchorMin = new Vector2(0.5f, 0f);
        mapRect.anchorMax = new Vector2(0.5f, 0f);
        mapRect.pivot = new Vector2(0.5f, 0f);
        mapRect.anchoredPosition = new Vector2(0f, 6f);
        mapRect.sizeDelta = Vector2.one * uiSize;
        rawObject.GetComponent<RawImage>().texture = mapTexture;

        BuildFogTiles();
        BuildPlayerMarker();
        BuildFloorLabel(frameRect);
    }

    void BuildFogTiles()
    {
        float cellSize = uiSize / fogGridSize;
        for (int floor = 0; floor < 3; floor++)
        {
            fogTiles[floor] = new Image[fogGridSize * fogGridSize];
            for (int y = 0; y < fogGridSize; y++)
            for (int x = 0; x < fogGridSize; x++)
            {
                GameObject tile = CreateImage($"Fog F{floor + 1} {x},{y}", mapRect, unexploredColor);
                RectTransform tileRect = tile.GetComponent<RectTransform>();
                tileRect.anchorMin = tileRect.anchorMax = Vector2.zero;
                tileRect.pivot = Vector2.zero;
                tileRect.anchoredPosition = new Vector2(x * cellSize, y * cellSize);
                tileRect.sizeDelta = new Vector2(cellSize + 0.5f, cellSize + 0.5f);
                fogTiles[floor][y * fogGridSize + x] = tile.GetComponent<Image>();
            }
        }
    }

    void BuildPlayerMarker()
    {
        GameObject marker = CreateImage("Player Position", mapRect, new Color(0.1f, 1f, 0.95f, 1f));
        playerMarker = marker.GetComponent<RectTransform>();
        playerMarker.anchorMin = playerMarker.anchorMax = Vector2.zero;
        playerMarker.pivot = new Vector2(0.5f, 0.5f);
        playerMarker.sizeDelta = new Vector2(12f, 16f);
    }

    void BuildFloorLabel(Transform parent)
    {
        GameObject labelObject = new GameObject("Floor Label", typeof(RectTransform), typeof(Text));
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(parent, false);
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -2f);
        labelRect.sizeDelta = new Vector2(0f, 28f);
        Text floorLabel = labelObject.GetComponent<Text>();
        floorLabel.name = "Current Floor Text";
        floorLabel.font = GameUIFont.Default;
        floorLabel.fontSize = 18;
        floorLabel.fontStyle = FontStyle.Bold;
        floorLabel.alignment = TextAnchor.MiddleCenter;
        floorLabel.color = GameUITheme.Gold;
        floorLabel.raycastTarget = false;
    }

    void SetFloor(int floor)
    {
        currentFloor = Mathf.Clamp(floor, 0, 2);
        Bounds bounds = floorBounds[currentFloor];
        float halfSize = Mathf.Max(bounds.extents.x, bounds.extents.z);
        mapCamera.orthographicSize = halfSize;
        mapCamera.transform.SetPositionAndRotation(
            new Vector3(bounds.center.x, floorHeights[currentFloor] + 20f, bounds.center.z),
            Quaternion.Euler(90f, 0f, 0f));

        for (int floorIndex = 0; floorIndex < fogTiles.Length; floorIndex++)
            foreach (Image tile in fogTiles[floorIndex])
                tile.gameObject.SetActive(floorIndex == currentFloor && tile.color.a > 0.01f);

        Text label = GetComponentInChildren<Text>();
        if (label != null)
            label.text = FindFirstObjectByType<OverworldDirector>() != null ? $"第 {currentFloor + 1}/3 關 · " + OverworldDirector.RegionNames[currentFloor] : $"第 {currentFloor + 1} 層";
    }

    int GetClosestFloor()
    {
        var world = FindFirstObjectByType<OverworldDirector>();
        if (world != null) return world.RegionAt(player.transform.position) - 1;
        int closestFloorIndex = 0;
        float closestDistance = Mathf.Abs(player.transform.position.y - floorHeights[0]);
        for (int floorIndex = 1; floorIndex < floorHeights.Length; floorIndex++)
        {
            float heightDifference = Mathf.Abs(player.transform.position.y - floorHeights[floorIndex]);
            if (heightDifference < closestDistance)
            {
                closestFloorIndex = floorIndex;
                closestDistance = heightDifference;
            }
        }
        return closestFloorIndex;
    }

    void RevealAroundPlayer()
    {
        Bounds bounds = floorBounds[currentFloor];
        for (int y = 0; y < fogGridSize; y++)
        for (int x = 0; x < fogGridSize; x++)
        {
            Image tile = fogTiles[currentFloor][y * fogGridSize + x];
            if (tile.color.a <= 0.01f)
                continue;

            Vector3 worldPosition = new Vector3(
                Mathf.Lerp(bounds.min.x, bounds.max.x, (x + 0.5f) / fogGridSize),
                player.transform.position.y,
                Mathf.Lerp(bounds.min.z, bounds.max.z, (y + 0.5f) / fogGridSize));

            Vector3 difference = worldPosition - player.transform.position;
            difference.y = 0f;
            if (difference.sqrMagnitude <= revealRadius * revealRadius)
            {
                tile.color = Color.clear;
                tile.gameObject.SetActive(false);
            }
        }
    }

    void UpdatePlayerMarker()
    {
        Bounds bounds = floorBounds[currentFloor];
        float normalizedX = Mathf.InverseLerp(bounds.min.x, bounds.max.x, player.transform.position.x);
        float normalizedY = Mathf.InverseLerp(bounds.min.z, bounds.max.z, player.transform.position.z);
        playerMarker.anchoredPosition = new Vector2(normalizedX * uiSize, normalizedY * uiSize);
        playerMarker.localRotation = Quaternion.Euler(0f, 0f, -player.transform.eulerAngles.y);
    }

    static GameObject CreateImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return imageObject;
    }

    void OnDestroy()
    {
        if (mapTexture != null)
        {
            mapTexture.Release();
            Destroy(mapTexture);
        }
    }
}
