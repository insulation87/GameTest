using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class AccountLoginPanel : MonoBehaviour
{
    enum Page { Login, Register, Verify, Reset }
    CloudAccountService service;
    GameObject root;
    Page page;
    Text title, status, note;
    InputField email, password, confirm, code;
    Button primary, secondary, loginTab, registerTab, verifyTab, resetTab;
    readonly List<Selectable> controls = new List<Selectable>();
    bool wasBusy;

    public void Initialize(Transform parent, UnityEngine.Events.UnityAction quitGame)
    {
        service = CloudAccountService.EnsureInstance();
        root = new GameObject("Account Login Panel", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(parent, false);
        var fill = root.GetComponent<RectTransform>(); fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.sizeDelta = Vector2.zero;
        root.GetComponent<Image>().color = new Color(.025f,.04f,.07f,.22f);
        var card = Box(root.transform, "Login Card", new Vector2(-455, 40), new Vector2(730,850), GameUITheme.Panel);
        card.localScale = Vector3.one * .9f;
        GameUITheme.Frame(card, true);
        title = Label(card, "帳號登入", 36, new Vector2(0,350), new Vector2(650,64));
        Label(card, "雲端帳號・本機進度", 20, new Vector2(0,293), new Vector2(650,38)).color = GameUITheme.Muted;
        loginTab = Button(card, "登入", new Vector2(-150,235), new Vector2(280,48), () => ShowPage(Page.Login));
        registerTab = Button(card, "註冊", new Vector2(150,235), new Vector2(280,48), () => ShowPage(Page.Register));
        email = Field(card, "電子郵件", 153, false);
        password = Field(card, "密碼", 69, true);
        confirm = Field(card, "再次輸入密碼", -15, true);
        code = Field(card, "郵件驗證碼", -99, false);
        email.contentType = InputField.ContentType.EmailAddress; email.characterLimit = 254;
        password.characterLimit = confirm.characterLimit = 128;
        code.contentType = InputField.ContentType.IntegerNumber; code.characterLimit = 10;
        primary = Button(card, "登入", new Vector2(0,-177), new Vector2(590,54), Submit);
        secondary = Button(card, "寄送驗證碼", new Vector2(0,-241), new Vector2(590,46), SendCode);
        verifyTab = Button(card, "驗證信箱", new Vector2(-150,-303), new Vector2(280,42), () => ShowPage(Page.Verify));
        resetTab = Button(card, "忘記密碼", new Vector2(150,-303), new Vector2(280,42), () => ShowPage(Page.Reset));
        status = Label(card, "", 18, new Vector2(0,-362), new Vector2(650,64)); status.color = new Color(.98f,.82f,.48f);
        note = Label(root.transform, "登入需連線；遊戲進度不會上傳雲端。\n更換電腦時，本機進度不會自動同步。", 18, new Vector2(-455,-405), new Vector2(710,60));
        note.color = GameUITheme.Muted;
        Button(root.transform, "離開遊戲", new Vector2(-455,-472), new Vector2(250,46), quitGame);
        Button(root.transform, "以遊客身分遊玩", new Vector2(450,120), new Vector2(540,64), () => service.EnterGuest());
        Label(root.transform, "不需登入，可單人與同網路多人遊玩\n登入帳號後可加好友、加入網際網路房間", 22,
            new Vector2(450,32), new Vector2(610,96)).color = GameUITheme.Muted;
        service.Changed += Refresh;
        ShowPage(Page.Login); Refresh();
    }
    public void Show() { if (root != null) { root.SetActive(true); Refresh(); } }
    public void Hide()
    {
        if (root == null) return;
        password.text = confirm.text = code.text = "";
        root.SetActive(false);
    }
    void ShowPage(Page next)
    {
        if (service.IsBusy) return;
        page = next;
        password.text = confirm.text = code.text = "";
        bool registering = page == Page.Register, resetting = page == Page.Reset;
        password.transform.parent.GetComponentInChildren<Text>(true).text = resetting ? "新密碼（8 至 128 個字元）" :
            registering ? "密碼（8 至 128 個字元）" : "密碼";
        title.text = page == Page.Login ? "帳號登入" : registering ? "建立帳號" : resetting ? "重設密碼" : "驗證電子郵件";
        title.color = GameUITheme.Gold;
        GameUITheme.StyleButton(loginTab, page == Page.Login);
        GameUITheme.StyleButton(registerTab, registering);
        GameUITheme.StyleButton(verifyTab, page == Page.Verify);
        GameUITheme.StyleButton(resetTab, resetting);
        GameUITheme.StyleButton(primary, true);
        password.transform.parent.gameObject.SetActive(page != Page.Verify);
        confirm.transform.parent.gameObject.SetActive(registering || resetting);
        code.transform.parent.gameObject.SetActive(page == Page.Verify || resetting);
        primary.GetComponentInChildren<Text>().text = page == Page.Login ? "登入" : registering ? "建立帳號並寄送驗證碼" : resetting ? "驗證並更新密碼" : "驗證信箱並登入";
        secondary.gameObject.SetActive(page == Page.Verify || resetting);
        secondary.GetComponentInChildren<Text>().text = resetting ? "寄送重設密碼驗證碼" : "重新寄送信箱驗證碼";
        Refresh();
    }
    void Submit()
    {
        if ((page == Page.Register || page == Page.Reset) && password.text != confirm.text)
        { status.text = "兩次輸入的密碼不一致"; GameAudio.UI(GameSound.Denied); return; }
        bool started = page == Page.Login ? service.Login(email.text, password.text) :
            page == Page.Register ? service.Register(email.text, password.text) :
            page == Page.Verify ? service.VerifyEmail(email.text, code.text) :
            service.ResetPassword(email.text, code.text, password.text);
        if (started) { password.text = confirm.text = ""; }
    }
    void SendCode()
    {
        if (page == Page.Reset) service.SendPasswordReset(email.text);
        else service.ResendVerification(email.text);
    }
    void Refresh()
    {
        if (root == null) return;
        status.text = service.Message;
        foreach (Selectable control in controls) control.interactable = !service.IsBusy;
        primary.interactable = secondary.interactable = !service.IsBusy && service.IsConfigured;
        if (wasBusy && !service.IsBusy && !service.IsSignedIn && page == Page.Register && service.Message.StartsWith("請查看信箱"))
        { wasBusy = false; ShowPage(Page.Verify); }
        wasBusy = service.IsBusy;
    }
    void Update()
    {
        if (GameSceneFlow.IsLoading || root == null || !root.activeInHierarchy || service.IsBusy) return;
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            var fields = new List<InputField>();
            foreach (var field in new[] { email, password, confirm, code }) if (field.gameObject.activeInHierarchy) fields.Add(field);
            int index = fields.FindIndex(f => f.isFocused);
            int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
            fields[(index + direction + fields.Count) % fields.Count].ActivateInputField();
        }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Submit();
    }
    InputField Field(Transform parent, string label, float y, bool secret)
    {
        var row = new GameObject(label + " Row", typeof(RectTransform)).transform;
        row.SetParent(parent, false);
        var rect = (RectTransform)row; rect.sizeDelta = new Vector2(590,78); rect.anchoredPosition = new Vector2(0,y);
        Label(row, label, 18, new Vector2(0,36), new Vector2(590,38)).alignment = TextAnchor.MiddleLeft;
        var input = Box(row, label, new Vector2(0,-8), new Vector2(590,46), GameUITheme.Recess);
        GameUITheme.Frame(input);
        InputField field = input.gameObject.AddComponent<InputField>();
        field.targetGraphic = input.GetComponent<Image>();
        Text text = Label(input,"",22,Vector2.zero,new Vector2(558,40));text.alignment=TextAnchor.MiddleLeft;
        Text placeholder = Label(input,secret ? "" : label,20,Vector2.zero,new Vector2(558,40));
        placeholder.alignment=TextAnchor.MiddleLeft;placeholder.color=GameUITheme.Muted;
        field.textComponent=text;field.placeholder=placeholder;
        field.contentType = secret ? InputField.ContentType.Password : InputField.ContentType.Standard;
        field.lineType = InputField.LineType.SingleLine; controls.Add(field);
        return field;
    }
    Button Button(Transform parent,string label,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action)
    {
        var rect=Box(parent,label,position,size,new Color(.13f,.29f,.43f));
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=rect.GetComponent<Image>();
        Label(rect,label,21,Vector2.zero,size-Vector2.one*8).raycastTarget=false;
        GameUITheme.StyleButton(button);
        button.onClick.AddListener(action);rect.gameObject.AddComponent<GameUIAudio>();controls.Add(button);return button;
    }
    public static RectTransform Box(Transform parent,string name,Vector2 position,Vector2 size,Color color)
    {
        var node=new GameObject(name,typeof(RectTransform),typeof(Image));node.transform.SetParent(parent,false);
        var rect=node.GetComponent<RectTransform>();rect.anchoredPosition=position;rect.sizeDelta=size;node.GetComponent<Image>().color=color;return rect;
    }
    public static Text Label(Transform parent,string value,int fontSize,Vector2 position,Vector2 size)
    {
        var node=new GameObject("Label",typeof(RectTransform),typeof(Text));node.transform.SetParent(parent,false);
        var text=node.GetComponent<Text>();text.font=GameUIFont.Default;text.text=value;text.fontSize=fontSize;
        text.alignment=TextAnchor.MiddleCenter;text.color=GameUITheme.Ivory;text.raycastTarget=false;
        text.rectTransform.anchoredPosition=position;text.rectTransform.sizeDelta=size;return text;
    }
    void OnDestroy()
    {
        if(service!=null) service.Changed-=Refresh;
        if(root!=null) Destroy(root);
    }
}
