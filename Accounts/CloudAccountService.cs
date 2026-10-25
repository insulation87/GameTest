using System;
using System.Collections;
using System.Net.Mail;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CloudAccountService : MonoBehaviour
{
    public static CloudAccountService Instance { get; private set; }
    public static CloudAccountService EnsureInstance()
    {
        if (Instance == null) new GameObject("Cloud Account Service").AddComponent<CloudAccountService>();
        return Instance;
    }
    public bool IsSignedIn => !string.IsNullOrEmpty(UserId);
    public bool IsGuest { get; private set; }
    public bool CanPlay => IsSignedIn || IsGuest;
    internal string AccessToken => accessToken;
    internal CloudAuthConfig Configuration => config;
    public bool IsBusy { get; private set; }
    public bool IsConfigured => config != null && config.IsValid;
    public string UserId { get; private set; }
    public string Email { get; private set; }
    public string Message { get; private set; } = "請登入帳號；遊戲進度儲存在這台電腦";
    public event Action Changed;
    CloudAuthConfig config;
    ICloudAuthTransport transport;
    // Tokens live in memory only. Passwords and tokens are never written to PlayerPrefs or save files.
    string accessToken, refreshToken;
    double refreshAt = double.PositiveInfinity;
    double nextEmailAt;
    bool disposed;

    [Serializable] sealed class Credentials { public string email, password; }
    [Serializable] sealed class EmailBody { public string email, type; }
    [Serializable] sealed class VerifyBody { public string email, token, type; }
    [Serializable] sealed class PasswordBody { public string password; }
    [Serializable] sealed class RefreshBody { public string refresh_token; }
    [Serializable] sealed class Session { public string access_token, refresh_token; public int expires_in; }
    [Serializable] sealed class User { public string id, email; }
    [Serializable] sealed class ErrorBody
    {
        public string error_code, error, msg, message, error_description;
        public bool HasMessage(string expected) =>
            string.Equals(msg, expected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(message, expected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error_description, expected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error, expected, StringComparison.OrdinalIgnoreCase);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        EnsureInstance();
        SceneManager.sceneLoaded -= GuardGameplay;
        SceneManager.sceneLoaded += GuardGameplay;
    }
    static void GuardGameplay(Scene scene, LoadSceneMode mode)
    {
#if !UNITY_EDITOR
        if (FindFirstObjectByType<Player>() != null && !EnsureInstance().CanPlay)
        {
            // Builds cannot bypass login by making the map their initial scene.
            foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None)) p.gameObject.SetActive(false);
            GameSceneFlow.LoadLogin();
        }
#endif
    }
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
        Configure(CloudAuthConfig.Load(), null);
    }
    // A replaceable transport lets tests exercise the complete flow without sending real email.
    internal void Configure(CloudAuthConfig settings, ICloudAuthTransport replacement)
    {
        config = settings; transport = replacement ?? new CloudAuthTransport(settings);
        if (!IsConfigured) Message = "登入服務尚未設定，請聯絡遊戲管理者";
    }
    void Update()
    {
        if (IsSignedIn && !IsBusy && !string.IsNullOrEmpty(refreshToken) && Time.realtimeSinceStartupAsDouble >= refreshAt)
            Run(Refresh(), "");
    }
    bool CanOperate()
    {
        if (IsBusy) return false;
        if (!IsConfigured) { Say("登入服務尚未設定，請聯絡遊戲管理者"); return false; }
        if (FindFirstObjectByType<Player>() != null)
        { Say("請先返回主選單再操作帳號"); return false; }
        return true;
    }
    public bool Login(string email, string password)
    {
        if (!CanOperate() || !ValidateEmail(email)) return false;
        if (string.IsNullOrEmpty(password)) { Say("請輸入密碼"); return false; }
        return Run(LoginRoutine(email.Trim(), password), "正在登入……");
    }
    public bool Register(string email, string password)
    {
        if (!CanOperate() || !ValidateEmail(email) || !ValidateNewPassword(password) || !CanSendEmail()) return false;
        return Run(RegisterRoutine(email.Trim(), password), "正在註冊……");
    }
    public bool VerifyEmail(string email, string code)
    {
        if (!CanOperate() || !ValidateEmail(email) || !ValidateCode(code)) return false;
        return Run(VerifyRoutine(email.Trim(), code.Trim()), "正在驗證信箱……");
    }
    public bool ResendVerification(string email)
    {
        if (!CanOperate() || !ValidateEmail(email) || !CanSendEmail()) return false;
        return Run(EmailRoutine("resend", email.Trim(), "signup"), "正在寄送驗證碼……");
    }
    public bool SendPasswordReset(string email)
    {
        if (!CanOperate() || !ValidateEmail(email) || !CanSendEmail()) return false;
        return Run(EmailRoutine("recover", email.Trim(), null), "正在寄送重設密碼驗證碼……");
    }
    public bool ResetPassword(string email, string code, string password)
    {
        if (!CanOperate() || !ValidateEmail(email) || !ValidateCode(code) || !ValidateNewPassword(password)) return false;
        return Run(ResetRoutine(email.Trim(), code.Trim(), password), "正在重設密碼……");
    }
    public bool Logout()
    {
        if (!CanOperate()) return false;
        return Run(LogoutRoutine(), "正在登出……");
    }
    public bool EnterGuest()
    {
        if (IsBusy || IsSignedIn || FindFirstObjectByType<Player>() != null) return false;
        IsGuest = true;
        LocalAccountProgress.SelectGuest();
        GameProgressSave.StartFresh = false;
        Say("遊客模式：可單人或同網路合作；登入後可使用好友與網際網路房間。");
        return true;
    }
    public bool ExitGuest()
    {
        if (!IsGuest || IsBusy || FindFirstObjectByType<Player>() != null) return false;
        IsGuest = false; LocalAccountProgress.ClearAccount(); Say("請登入帳號，或使用遊客模式。"); return true;
    }
    bool Run(IEnumerator operation, string message)
    {
        if (IsBusy) return false;
        IsBusy = true;
        if (message.Length > 0) Message = message;
        Changed?.Invoke(); StartCoroutine(Execute(operation)); return true;
    }
    IEnumerator Execute(IEnumerator operation)
    {
        try { yield return operation; }
        finally { IsBusy = false; if (!disposed) Changed?.Invoke(); }
    }
    IEnumerator LoginRoutine(string email, string password)
    {
        CloudAuthResponse response = null;
        yield return transport.Send("POST", "token?grant_type=password", JsonUtility.ToJson(new Credentials { email = email, password = password }), null, r => response = r);
        password = null;
        if (!Handle(response)) yield break;
        yield return Accept(Parse<Session>(response.body));
    }
    IEnumerator RegisterRoutine(string email, string password)
    {
        CloudAuthResponse response = null;
        yield return transport.Send("POST", "signup", JsonUtility.ToJson(new Credentials { email = email, password = password }), null, r => response = r);
        password = null;
        if (!Handle(response)) yield break;
        Session session = Parse<Session>(response.body);
        if (session != null && !string.IsNullOrEmpty(session.access_token)) yield return Accept(session);
        else Say("請查看信箱並驗證帳號；若已註冊，請直接登入");
    }
    IEnumerator VerifyRoutine(string email, string code)
    {
        CloudAuthResponse response = null;
        yield return transport.Send("POST", "verify", JsonUtility.ToJson(new VerifyBody { email = email, token = code, type = "email" }), null, r => response = r);
        if (Handle(response)) yield return Accept(Parse<Session>(response.body));
    }
    IEnumerator EmailRoutine(string endpoint, string email, string type)
    {
        CloudAuthResponse response = null;
        yield return transport.Send("POST", endpoint, JsonUtility.ToJson(new EmailBody { email = email, type = type }), null, r => response = r);
        if (Handle(response)) Say("若此信箱符合條件，驗證碼將寄到信箱；請同時檢查垃圾郵件");
    }
    IEnumerator ResetRoutine(string email, string code, string password)
    {
        CloudAuthResponse response = null;
        yield return transport.Send("POST", "verify", JsonUtility.ToJson(new VerifyBody { email = email, token = code, type = "recovery" }), null, r => response = r);
        if (!Handle(response)) yield break;
        Session recovery = Parse<Session>(response.body);
        if (recovery == null || string.IsNullOrEmpty(recovery.access_token)) { Say("驗證未完成，請重新索取驗證碼"); yield break; }
        yield return transport.Send("PUT", "user", JsonUtility.ToJson(new PasswordBody { password = password }), recovery.access_token, r => response = r);
        password = null;
        bool success = Handle(response);
        // Recovery tokens must not accidentally sign the game into a different account.
        yield return transport.Send("POST", "logout?scope=local", "{}", recovery.access_token, r => { });
        if (success) Say("密碼已更新，請使用新密碼登入");
        else Say("密碼未更新，請重新索取驗證碼，並確認新密碼符合帳號規則");
    }
    IEnumerator Accept(Session session)
    {
        if (session == null || string.IsNullOrEmpty(session.access_token) || string.IsNullOrEmpty(session.refresh_token) || session.expires_in <= 0)
        { Say("登入回應不完整，請重試"); yield break; }
        CloudAuthResponse response = null;
        yield return transport.Send("GET", "user", null, session.access_token, r => response = r);
        if (!Handle(response)) yield break;
        User user = Parse<User>(response.body);
        if (user == null || !Guid.TryParse(user.id, out _) || string.IsNullOrEmpty(user.email))
        { Say("無法確認帳號身分，請重新登入"); yield break; }
        IsGuest = false; UserId = user.id; Email = user.email;
        accessToken = session.access_token; refreshToken = session.refresh_token;
        refreshAt = Time.realtimeSinceStartupAsDouble + Math.Max(5, session.expires_in - 60);
        LocalAccountProgress.SelectAccount(config.projectUrl, UserId);
        GameProgressSave.StartFresh = false;
        Say("登入成功，遊戲進度將儲存在這台電腦");
    }
    IEnumerator Refresh()
    {
        CloudAuthResponse response = null;
        yield return transport.Send("POST", "token?grant_type=refresh_token", JsonUtility.ToJson(new RefreshBody { refresh_token = refreshToken }), null, r => response = r);
        if (response == null || response.networkError || response.status >= 500 || response.status == 429)
        { refreshAt = Time.realtimeSinceStartupAsDouble + 30; yield break; }
        Session session = response.Success ? Parse<Session>(response.body) : null;
        if (session == null || string.IsNullOrEmpty(session.access_token) || string.IsNullOrEmpty(session.refresh_token) || session.expires_in <= 0)
        {
            accessToken = refreshToken = null; refreshAt = double.PositiveInfinity;
            // Keep the selected local profile for the running map; require login on return to menu.
            UserId = Email = null; Say("登入已過期；本機進度仍會儲存，返回主選單後請重新登入");
            yield break;
        }
        accessToken = session.access_token; refreshToken = session.refresh_token;
        refreshAt = Time.realtimeSinceStartupAsDouble + Math.Max(5, session.expires_in - 60);
    }
    IEnumerator LogoutRoutine()
    {
        string token = accessToken;
        IsGuest = false; UserId = Email = accessToken = refreshToken = null;
        refreshAt = double.PositiveInfinity;
        LocalAccountProgress.ClearAccount();
        if (!string.IsNullOrEmpty(token)) yield return transport.Send("POST", "logout?scope=local", "{}", token, r => { });
        Say("已登出；這台電腦上的遊戲進度仍保留");
    }
    bool CanSendEmail()
    {
        double remaining = nextEmailAt - Time.realtimeSinceStartupAsDouble;
        if (remaining > 0) { Say("請等候 " + Math.Ceiling(remaining) + " 秒後再寄送郵件"); return false; }
        nextEmailAt = Time.realtimeSinceStartupAsDouble + 60; return true;
    }
    bool ValidateEmail(string email)
    {
        try { var address = new MailAddress(email.Trim()); if (address.Address == email.Trim() && email.Length <= 254) return true; }
        catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is NullReferenceException) { }
        Say("請輸入有效的電子郵件地址"); return false;
    }
    bool ValidateNewPassword(string password)
    {
        if (password != null && password.Length >= 8 && password.Length <= 128) return true;
        Say("密碼需為 8 至 128 個字元"); return false;
    }
    bool ValidateCode(string code)
    {
        if (code != null && code.Trim().Length >= 6 && code.Trim().Length <= 10)
        {
            bool digits = true; foreach (char c in code.Trim()) digits &= c >= '0' && c <= '9';
            if (digits) return true;
        }
        Say("請輸入郵件中的數字驗證碼"); return false;
    }
    bool Handle(CloudAuthResponse response)
    {
        if (response != null && response.Success) return true;
        if (response == null || response.networkError) { Say("無法連線至登入服務，請檢查網路後重試"); return false; }
        ErrorBody error = Parse<ErrorBody>(response.body);
        string code = !string.IsNullOrEmpty(error?.error_code) ? error.error_code : error?.error;
        // Translate known errors only. Never display or log raw responses, which may contain private data.
        if (code == "over_email_send_rate_limit") Say("寄信次數已達上限，請稍後再索取驗證碼");
        else if (code == "email_address_not_authorized") Say("寄信服務尚未開放此收件信箱，請聯絡遊戲管理者");
        else if (response.status == 429) Say("操作過於頻繁，請稍後再試");
        else if (code == "email_not_confirmed") Say("請先完成信箱驗證，再登入遊戲");
        else if (code == "otp_expired") Say("驗證碼錯誤或已過期，請重新索取");
        else if (code == "weak_password") Say("密碼強度不足，請使用更長且不常見的密碼");
        else if (code == "signup_disabled") Say("目前暫停新帳號註冊");
        else if (code == "captcha_failed") Say("登入服務需要額外驗證，請聯絡遊戲管理者");
        else if (response.status >= 500 && error != null &&
            (error.HasMessage("Error sending confirmation email") || error.HasMessage("Error sending recovery email")))
            Say("驗證郵件寄送失敗，請聯絡遊戲管理者檢查寄信設定");
        else if (response.status >= 500 && error != null &&
            (error.HasMessage("Database error saving new user") || error.HasMessage("Database error creating user") ||
             error.HasMessage("Database error updating user")))
            Say("雲端帳號資料儲存失敗，請聯絡遊戲管理者");
        else if (response.status >= 500) Say("登入服務發生錯誤（" + response.status + "），請稍後再試");
        else Say("操作未完成，請確認電子郵件、密碼或驗證碼後重試");
        return false;
    }
    static T Parse<T>(string json) where T : class
    {
        try { return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<T>(json); }
        catch (ArgumentException) { return null; }
    }
    void Say(string message) { Message = message; Changed?.Invoke(); }
    void OnDestroy()
    {
        disposed = true;
        if (Instance == this) { Instance = null; accessToken = refreshToken = null; }
    }
}
