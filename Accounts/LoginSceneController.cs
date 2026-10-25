using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>The standalone login scene owns account UI; the service survives scene changes.</summary>
[DisallowMultipleComponent]
public sealed class LoginSceneController : MonoBehaviour
{
    [SerializeField] string mainMenuSceneName = GameSceneFlow.MenuScene;
    CloudAccountService account;
    AccountLoginPanel panel;
    Text transitionLabel;
    bool loading;

    void Awake()
    {
        Time.timeScale = GameSceneFlow.IsLoading ? 0f : 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (Camera.main == null)
        {
            var node = new GameObject("Login Camera", typeof(Camera), typeof(AudioListener));
            node.transform.SetParent(transform, false);
            node.transform.localPosition = Vector3.back * 10;
            node.tag = "MainCamera";
            Camera camera = node.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GameUITheme.Ink;
            camera.orthographic = true;
        }
        if (EventSystem.current == null)
        {
            var events = new GameObject("Login Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var canvasNode = new GameObject("Login Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasNode.transform.SetParent(transform, false);
        canvasNode.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        GameUITheme.FitCanvas(canvasNode.GetComponent<CanvasScaler>());
        var cover = new GameObject("Login Dungeon Cover", typeof(RectTransform), typeof(CanvasRenderer), typeof(PixelMenuBackdrop));
        cover.transform.SetParent(canvasNode.transform, false);
        GameUITheme.Stretch(cover.GetComponent<RectTransform>());
        cover.GetComponent<PixelMenuBackdrop>().raycastTarget = false;
        GameUITheme.Label(canvasNode.transform, "異世界冒險", 48, new Vector2(450, 430), new Vector2(600, 90)).color = GameUITheme.Gold;
        GameUITheme.Label(canvasNode.transform, "登入帳號，或以遊客身分開始冒險", 22, new Vector2(450, 365), new Vector2(600, 55)).color = GameUITheme.Muted;
        transitionLabel = GameUITheme.Label(canvasNode.transform, "", 22, new Vector2(450, -420), new Vector2(600, 70));
        account = CloudAccountService.EnsureInstance();
        panel = gameObject.AddComponent<AccountLoginPanel>();
        panel.Initialize(canvasNode.transform, QuitGame);
        account.Changed += RefreshAccount;
    }

    void Start() => RefreshAccount();

    void RefreshAccount()
    {
        if (loading || account == null || !account.CanPlay || account.IsBusy) return;
        if (GameSceneFlow.Load(mainMenuSceneName))
        {
            loading = true;
            panel.Hide();
            transitionLabel.text = account.IsGuest ? "以遊客身分進入主選單……" : "登入成功，正在進入主選單……";
        }
        else transitionLabel.text = "目前無法進入主選單，請稍後再試。";
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnDestroy() { if (account != null) account.Changed -= RefreshAccount; }
}
