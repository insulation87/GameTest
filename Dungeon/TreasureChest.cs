using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TreasureChest : MonoBehaviour
{
    public const float Clearance = 1.4f;
    public static readonly Vector3 Size = new Vector3(1.65f, 1.2f, 1.3f);
    public DungeonRoomBoundary Room { get; private set; }
    public bool IsBroken { get; private set; }
    public float CurrentHealth { get; private set; }
    WeaponItem reward;
    float maxHealth, lootLifetime, hitTime = -10f;
    Transform model;
    Canvas label;
    Image healthFill;
    BoxCollider body;
    NavMeshObstacle obstacle;

    public void Initialize(DungeonRoomBoundary room, WeaponItem weapon, float health = 120f, float lifetime = 180f)
    {
        Room = room;
        reward = weapon;
        CurrentHealth = maxHealth = Mathf.Max(1, health);
        lootLifetime = Mathf.Max(1, lifetime);
        model = ItemAppearance.Create(ItemAppearance.TreasureChest, transform).transform;
        body = gameObject.AddComponent<BoxCollider>();
        body.size = Size;
        body.center = Vector3.up * Size.y * .5f;
        obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = Size;
        obstacle.center = body.center;
        obstacle.carving = true;
        BuildLabel();
    }

    public void TakeDamage(float damage, PlayerHealth source)
    {
        // Enemy arrows and props cannot farm treasure; one death produces exactly one drop.
        if (IsBroken || reward == null || damage <= 0 || source == null || source.IsDead || Time.timeScale <= 0) return;
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        hitTime = Time.time;
        GameAudio.At(GameSound.Impact, transform.position);
        healthFill.rectTransform.anchorMax = new Vector2(CurrentHealth / maxHealth, 1);
        if (CurrentHealth > 0) return;
        IsBroken = true;
        body.enabled = false;
        obstacle.enabled = false;
        model.gameObject.SetActive(false);
        label.gameObject.SetActive(false);
        WeaponLoot.Drop(reward, transform.position + Vector3.up * .9f, lootLifetime);
        GameAudio.At(GameSound.FloorCrack, transform.position);
        GameAudio.UI(GameSound.Unlock);
        StartCoroutine(BreakApart());
    }

    IEnumerator BreakApart()
    {
        var pieces = new Transform[8];
        var velocities = new Vector3[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            pieces[i] = ItemAppearance.Create(ItemAppearance.ChestFragment, transform).transform;
            pieces[i].localPosition = new Vector3(Random.Range(-.6f, .6f), .65f, Random.Range(-.4f, .4f));
            velocities[i] = new Vector3(Random.Range(-2f, 2f), Random.Range(2f, 4f), Random.Range(-2f, 2f));
        }
        for (float t = 0; t < .7f; t += Time.deltaTime)
        {
            for (int i = 0; i < pieces.Length; i++)
            {
                velocities[i] += Vector3.down * (10f * Time.deltaTime);
                pieces[i].localPosition += velocities[i] * Time.deltaTime;
                pieces[i].Rotate(new Vector3(120, 90, 60) * Time.deltaTime);
                pieces[i].localScale = Vector3.one * (1f - t / .7f);
            }
            yield return null;
        }
        Destroy(gameObject);
    }

    void LateUpdate()
    {
        if (IsBroken || model == null) return;
        float hit = Mathf.Clamp01(1 - (Time.time - hitTime) / .15f);
        model.localRotation = Quaternion.Euler(0, 0, Mathf.Sin((Time.time - hitTime) * 90) * hit * 4f);
        Camera viewer = Camera.main;
        if (viewer == null || !viewer.isActiveAndEnabled)
            foreach (Camera candidate in Camera.allCameras)
                if (candidate.isActiveAndEnabled && candidate.targetTexture == null) { viewer = candidate; break; }
        bool visible = viewer != null && Vector3.SqrMagnitude(viewer.transform.position - transform.position) < 18f * 18f;
        label.gameObject.SetActive(visible);
        if (visible) label.transform.rotation = viewer.transform.rotation;
    }

    void BuildLabel()
    {
        var node = new GameObject("Chest Health", typeof(RectTransform), typeof(Canvas));
        node.transform.SetParent(transform, false);
        node.transform.localPosition = Vector3.up * 1.65f;
        node.transform.localScale = Vector3.one * .005f;
        label = node.GetComponent<Canvas>();
        label.renderMode = RenderMode.WorldSpace;
        node.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 100);
        GameUITheme.Label(node.transform, "秘藏寶箱 · 射擊擊破", 24, new Vector2(0, 20), new Vector2(400, 40),
            TextAnchor.MiddleCenter).color = GameUITheme.Gold;
        var bar = new GameObject("Health", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(node.transform, false);
        bar.GetComponent<RectTransform>().sizeDelta = new Vector2(280, 10);
        bar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -14);
        bar.GetComponent<Image>().color = GameUITheme.Ink;
        bar.GetComponent<Image>().raycastTarget = false;
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bar.transform, false);
        GameUITheme.Stretch(fill.GetComponent<RectTransform>());
        healthFill = fill.GetComponent<Image>();
        healthFill.color = GameUITheme.Gold;
        // Resize the anchored rect so no sprite asset is required for the health bar.
        healthFill.type = Image.Type.Simple;
        healthFill.raycastTarget = false;
    }
}
