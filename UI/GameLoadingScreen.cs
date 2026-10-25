using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Persistent, animated overlay. All timings use unscaled time; progress follows Unity's operation.</summary>
[DisallowMultipleComponent]
public sealed class GameLoadingScreen : MonoBehaviour
{
    public bool IsLoading { get; private set; }
    public string Destination { get; private set; }
    public float VisibleProgress => shownProgress;
    const float FadeDuration = .15f;
    const float MinimumVisibleDuration = .7f;
    CanvasGroup group;
    Image fill;
    Text title, stage, percent, tip;
    LoadingRune rune;
    float targetProgress, shownProgress, openedAt, priorTimeScale;
    PlayerHealth destinationHealth;
    WeaponInventory destinationInventory;

    void Awake() { DontDestroyOnLoad(gameObject); Build(); gameObject.SetActive(false); }

    public void Begin(string scene)
    {
        if (IsLoading) return;
        Destination = scene; IsLoading = true;
        priorTimeScale = Time.timeScale; openedAt = Time.unscaledTime;
        targetProgress = shownProgress = 0f; group.alpha = 0f;
        stage.text = "正在準備……"; RefreshProgress();
        destinationHealth = null; destinationInventory = null;
        gameObject.SetActive(true); RefreshDestination();
        Time.timeScale = 0f; UnlockCursor();
        EventSystem.current?.SetSelectedGameObject(null);
        StartCoroutine(Transition());
    }

    public void Redirect(string scene)
    {
        Destination = scene;
        RefreshDestination();
    }

    IEnumerator Transition()
    {
        // Render the overlay before any synchronous scene deserialization or Awake work.
        yield return Fade(0f,1f);
        yield return null;
        while (IsLoading)
        {
            string loading = Destination;
            if (SceneManager.GetActiveScene().name != loading)
            {
                AsyncOperation operation = null;
                try { operation = SceneManager.LoadSceneAsync(loading,LoadSceneMode.Single); }
                catch (Exception ex) { Debug.LogError("無法載入場景 " + loading + "：" + ex.Message); }
                if (operation == null) { Finish(false); yield break; }
                operation.allowSceneActivation = false;
                stage.text = "正在載入場景資源……";
                while (operation.progress < .9f || Time.unscaledTime - openedAt < MinimumVisibleDuration)
                {
                    targetProgress = Mathf.Clamp01(operation.progress / .9f) * .9f;
                    yield return null;
                }
                targetProgress = .9f; stage.text = "正在準備場景……";
                operation.allowSceneActivation = true;
                while (!operation.isDone) yield return null;
            }
            // sceneLoaded guards may have requested another scene before isDone became true.
            if (Destination != loading) continue;
            targetProgress = .95f;
            // Let Start methods, map generation and save restoration run before revealing gameplay.
            yield return null;
            yield return null;
            var saves = FindObjectsByType<GameProgressSave>(FindObjectsSortMode.None);
            float readyDeadline = Time.unscaledTime + 10f;
            while (Destination == loading && !SavesReady(saves) && Time.unscaledTime < readyDeadline)
                yield return null;
            if (Destination != loading) continue;
            if (!SavesReady(saves)) Debug.LogWarning("場景已載入，進度初始化尚未完成。請檢查玩家元件設定。");
            destinationHealth = FindFirstObjectByType<PlayerHealth>();
            destinationInventory = destinationHealth != null ? destinationHealth.GetComponent<WeaponInventory>() : null;
            targetProgress = shownProgress = 1f;
            stage.text = "準備完成"; RefreshProgress();
            yield return null;
            if (Destination != loading) continue;
            yield return Fade(1f,0f);
            // A late account redirect must still be handled by this same loading overlay.
            if (Destination != loading) { group.alpha = 1f; continue; }
            Finish(true);
        }
    }

    static bool SavesReady(GameProgressSave[] saves)
    {
        foreach (var save in saves)
            if (save != null && save.isActiveAndEnabled && !save.Ready) return false;
        return true;
    }

    IEnumerator Fade(float from,float to)
    {
        float elapsed = 0f;
        string fadeDestination = Destination;
        while (elapsed < FadeDuration)
        {
            if (to < from && Destination != fadeDestination) { group.alpha = 1f; yield break; }
            elapsed += Time.unscaledDeltaTime; group.alpha = Mathf.Lerp(from,to,Mathf.Clamp01(elapsed/FadeDuration)); yield return null;
        }
        group.alpha = to;
    }

    void Finish(bool success)
    {
        IsLoading = false;
        bool paused = !CoopSession.IsOnline && (DungeonVictoryUI.IsOpen || (destinationHealth != null && destinationHealth.IsChoosingUpgrade) ||
            (destinationInventory != null && (destinationInventory.IsOpen || destinationInventory.IsPaused)));
        Time.timeScale = success ? (paused ? 0f : 1f) : priorTimeScale;
        gameObject.SetActive(false);
        var cursor = destinationHealth != null ? destinationHealth.GetComponent<PlayerCursorController>() : null;
        if (cursor != null) cursor.RefreshCursor(); else UnlockCursor();
    }

    void Update()
    {
        if (!IsLoading) return;
        rune.Phase += Time.unscaledDeltaTime * 150f;
        rune.SetVerticesDirty();
        shownProgress = Mathf.MoveTowards(shownProgress,targetProgress,Time.unscaledDeltaTime * 2f);
        RefreshProgress();
    }

    void LateUpdate()
    {
        if (!IsLoading) return;
        Time.timeScale = 0f; UnlockCursor();
        // Raycast blocking also covers mouse clicks; clear keyboard submit/navigation focus.
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    static void UnlockCursor() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

    void RefreshDestination()
    {
        title.text = Destination == GameSceneFlow.LoginScene ? "正在前往登入畫面" :
            Destination == GameSceneFlow.MenuScene ? "正在載入主選單" :
            Destination == GameSceneFlow.TradingHubScene ? "正在前往旅人營地" : "正在載入" + ExpeditionMaps.ForScene(Destination).Name;
        tip.text = ExpeditionMaps.IsMap(Destination) ? "全 3 關 · R 角色技能 · Q 戰術回放 · E 背包" :
            Destination == GameSceneFlow.TradingHubScene ? "WASD 移動 · F 與商人交易 · 傳送門可返回冒險" :
            "每位冒險者都有專屬技能，選擇適合你的戰鬥節奏。";
    }

    void RefreshProgress()
    {
        fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(shownProgress),1f);
        percent.text = Mathf.FloorToInt(Mathf.Clamp01(shownProgress) * 100f) + "%";
    }

    void Build()
    {
        var canvas = new GameObject("Loading Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(CanvasGroup));
        canvas.transform.SetParent(transform,false);
        var layer = canvas.GetComponent<Canvas>(); layer.renderMode = RenderMode.ScreenSpaceOverlay; layer.sortingOrder = 32760;
        GameUITheme.FitCanvas(canvas.GetComponent<CanvasScaler>());
        group = canvas.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = true;
        var background = AccountLoginPanel.Box(canvas.transform,"Loading Background",Vector2.zero,Vector2.zero,GameUITheme.Ink);
        GameUITheme.Stretch(background);
        var cover = new GameObject("Dungeon Backdrop",typeof(RectTransform),typeof(CanvasRenderer),typeof(PixelMenuBackdrop));
        cover.transform.SetParent(background,false); GameUITheme.Stretch(cover.GetComponent<RectTransform>());
        cover.GetComponent<PixelMenuBackdrop>().raycastTarget = false;
        var dim = AccountLoginPanel.Box(background,"Dim",Vector2.zero,Vector2.zero,new Color(.015f,.025f,.045f,.86f));
        GameUITheme.Stretch(dim);
        var card = AccountLoginPanel.Box(background,"Loading Card",Vector2.zero,new Vector2(980,490),GameUITheme.Panel);
        GameUITheme.Frame(card,true);
        var symbol = new GameObject("Animated Rune",typeof(RectTransform),typeof(CanvasRenderer),typeof(LoadingRune));
        symbol.transform.SetParent(card,false);
        var symbolRect = symbol.GetComponent<RectTransform>(); symbolRect.sizeDelta = new Vector2(150,150); symbolRect.anchoredPosition = new Vector2(0,143);
        rune = symbol.GetComponent<LoadingRune>(); rune.raycastTarget = false;
        title = AccountLoginPanel.Label(card,"",38,new Vector2(0,28),new Vector2(900,80)); title.color = GameUITheme.Gold;
        stage = AccountLoginPanel.Label(card,"正在準備……",22,new Vector2(0,-43),new Vector2(900,48)); stage.color = GameUITheme.Muted;
        var bar = AccountLoginPanel.Box(card,"Progress Track",new Vector2(0,-104),new Vector2(800,14),GameUITheme.Recess);
        fill = AccountLoginPanel.Box(bar,"Progress Fill",Vector2.zero,Vector2.zero,GameUITheme.Teal).GetComponent<Image>();
        GameUITheme.Stretch(fill.rectTransform); fill.raycastTarget = false;
        percent = AccountLoginPanel.Label(card,"0%",22,new Vector2(0,-141),new Vector2(900,46));
        tip = AccountLoginPanel.Label(card,"",20,new Vector2(0,-196),new Vector2(900,55)); tip.color = GameUITheme.Muted;
    }
}

/// <summary>A rotating segmented rune drawn in UI geometry, with no image assets or allocations per tick.</summary>
public sealed class LoadingRune : MaskableGraphic
{
    public float Phase;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var center = rectTransform.rect.center;
        float radius = Mathf.Min(rectTransform.rect.width,rectTransform.rect.height) * .38f;
        for (int i=0;i<12;i++)
        {
            float angle = (Phase + i*30f) * Mathf.Deg2Rad;
            var radial = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)); var tangent = new Vector2(-radial.y,radial.x);
            Color tint = Color.Lerp(GameUITheme.Teal,GameUITheme.Gold,i/11f); tint.a = .2f+.8f*i/11f;
            Quad(vh,center+radial*radius,radial*7f,tangent*4f,tint);
        }
        float pulse = .8f + .2f*Mathf.Sin(Phase*Mathf.Deg2Rad*2f);
        Quad(vh,center,new Vector2(18,18)*pulse,new Vector2(-18,18)*pulse,GameUITheme.Gold);
        Quad(vh,center,new Vector2(9,9)*pulse,new Vector2(-9,9)*pulse,GameUITheme.Panel);
    }
    static void Quad(VertexHelper vh,Vector2 center,Vector2 a,Vector2 b,Color color)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center-a-b,color,Vector2.zero); vh.AddVert(center-a+b,color,Vector2.zero);
        vh.AddVert(center+a+b,color,Vector2.zero); vh.AddVert(center+a-b,color,Vector2.zero);
        vh.AddTriangle(start,start+1,start+2); vh.AddTriangle(start,start+2,start+3);
    }
}
