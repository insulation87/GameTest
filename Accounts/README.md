# 雲端帳號與本機遊戲進度

雲端採用 Supabase Auth 的電子郵件／密碼帳號，支援註冊、信箱驗證碼、登入、忘記密碼和登出。遊戲進度完全保留於玩家電腦，不建立雲端進度資料表，不上傳存檔。

## 啟用 Supabase

1. 建立或選擇 Supabase 專案，啟用 Email provider、允許註冊並開啟信箱驗證。
2. 從專案的 Connect 或 API Keys 頁面取得 Project URL 與 `sb_publishable_` 開頭的 Publishable key，填入 `Resources/CloudAuthConfig.json`。遊戲只接受 HTTPS URL 與公開金鑰；不要填入 secret 或 service_role 金鑰。
3. 在 Auth 郵件模板中，將 Confirm signup 內容換成 `EmailTemplates/ConfirmSignup.html`，Reset password 內容換成 `EmailTemplates/ResetPassword.html`。兩者皆使用 `{{ .Token }}`，讓玩家直接在遊戲內輸入驗證碼，無需瀏覽器回跳或額外重設密碼網站。
4. 設定自己的 SMTP 寄信服務，測試驗證與重設郵件是否送達。Supabase 內建寄信服務限制收件對象與流量，不能直接當作對一般玩家開放的正式寄信服務。
5. 建置場景依序為 **Login → MainMenu → Map1**。`Assets/Scenes/Login.unity` 是獨立登入場景，登入成功或選擇遊客模式後載入主選單；登出／離開遊客模式後回到 Login。

設定範例（需替換成專案的真實公開設定）：

```json
{
  "projectUrl": "https://YOUR_PROJECT.supabase.co",
  "publishableKey": "sb_publishable_YOUR_PUBLIC_KEY"
}
```

未填入有效設定時，登入介面會明確顯示尚未設定，且不會假裝登入成功。

### 套用驗證碼郵件

目前專案網址與公開金鑰已填妥。郵件模板需透過專案管理者權限設定，公開金鑰無法修改。

可在 Supabase Auth 的 Email Templates 畫面，將 Confirm signup 與 Reset password 分別貼入上方兩份 HTML。也可在 PowerShell 執行本資料夾的 `SetupAuthEmails.ps1`，於隱藏輸入欄貼上 Supabase 個人管理存取權杖（帳號設定的 Access Tokens）。工具只更新這兩種郵件的主旨與內文，再讀回確認；權杖不寫入檔案，也不需要貼到對話中。工具不會建立 SMTP 帳號、修改 SMTP 憑證或寄信。若電腦的 PowerShell 執行原則不允許執行，直接採用後台貼上 HTML 的方式。

```powershell
& 'D:\GameTest\GameTest\Assets\Script\Accounts\SetupAuthEmails.ps1'
```

只有公開連線設定時，可驗證 Auth 是否可用，無法驗證管理端的郵件模板與 SMTP。尚未完成實際寄信測試前，不能將連線成功視為整套註冊流程已通過。

目前使用電子郵件驗證碼流程，尚未整合 CAPTCHA UI、第三方社群登入或 MFA。若專案要求 CAPTCHA，請先整合對應的 challenge 流程，勿停用既有正式專案的安全要求。

## 註冊失敗排查

遊戲會分別顯示驗證郵件寄送失敗、寄信次數上限、收件信箱未授權與雲端帳號資料儲存失敗。其他伺服器錯誤顯示 HTTP 狀態碼；不顯示或記錄原始回應，避免洩露私人資料。公開設定端點回應 200 不代表 SMTP 已正常運作。

在 Supabase 專案的 Logs → Auth 尋找對應時間的 `/signup` 錯誤。若為寄信失敗，確認 Authentication → Emails → SMTP Settings 已儲存正確的寄件人、主機、連接埠與 SMTP 憑證，以及郵件模板可用。若使用 Gmail，SMTP Password 應使用 Google 應用程式密碼。不要將 SMTP 密碼或管理權杖貼到日誌或對話中。資料庫錯誤則需依 Auth／Postgres 紀錄檢查，不應一律歸因於 SMTP。

## 遊玩與存檔

- 每次啟動可選登入或遊客模式。登入權杖只保留於本次執行記憶體，切換主選單與地圖時沿用登入狀態。密碼與權杖不寫入 PlayerPrefs、存檔或日誌。
- 遊客不需網路驗證，可使用單人、同機雙人及同網路合作；不開放好友或網際網路房間。遊客單人進度在 `Guest/progress-v1.json`，合作進度在 `Guest/coop-v1.json`，不寫入帳號或舊版本機檔案。主選單可按「返回登入帳號」。
- 登入需要網路。登入後暫時斷線不妨礙目前遊戲的本機儲存；權杖更新會稍後重試。若伺服器撤銷登入，本局進度仍存入原帳號的檔案，返回主選單後重新登入。
- 本機路徑：`Application.persistentDataPath/Profiles/<專案網址與帳號 UUID 的 SHA-256>/progress-v1.json`。不同帳號與不同 Supabase 專案分開，不以電子郵件當檔名。
- 每張地圖進入時固定存檔目的地，避免稍後登入狀態改變時把進度存進別人的帳號。
- 登出保留本機進度；同一帳號換電腦登入，不會自動取得另一台電腦的遊戲進度。
- 舊版 `Application.persistentDataPath/progress-v1.json` 不會自動分配給第一個登入者。有舊檔且目前帳號沒有進度時，可按「匯入原本機進度」，複製進度至目前帳號並保留舊檔。
- Unity 編輯器預設按 Play 從 Login 開始，停止後仍回到原本編輯的場景。開發時可選 **Game Tools → Start Play From Current Scene**，直接測試 Map1 與開發用本機存檔；以 **Start Play From Login** 恢復登入入口。正式建置必須先登入或明確選擇遊客模式。

## 好友服務

新增 `CloudFriendsService` 使用同一個 Supabase 帳號的存取權杖，呼叫 `game_social` RPC。支援 12 碼好友代碼、暱稱、好友邀請、接受／拒絕／取消、移除及清單刷新；好友名稱與代碼保留雲端，但遊戲存檔仍在本機。換帳號或登出會清除記憶體中的好友清單，避免看到其他帳號的資料。

管理員需在既有 Supabase SQL Editor 執行 [好友 SQL](Backend~/friends.sql)，再按 [中繼部署說明](../Multiplayer/RelayServer~/README.md) 啟用網際網路房間。公開金鑰不能建立資料表或部署主機；目前未操作正式後台。SQL 與遊戲 UI 已在本機的真實 PostgreSQL、Auth 測試替身與 Unity Play Mode 中驗證；正式帳號、寄信及不同網路連線仍需部署後驗證。

### 好友列表與邀請都無法使用

2026-10-09 以公開金鑰及無效操作參數探測正式專案 `elntwxoivnsescxvjtlo`，回應 HTTP 404 / `PGRST202`：PostgREST 找不到 `public.game_social(nickname, operation, target)`。這次探測未登入、建立資料或送出邀請。此錯誤表示好友 RPC 尚未部署，或部署後的 schema cache 尚未更新；不是好友列表真的空白。

1. 登入 [此專案的 SQL Editor](https://supabase.com/dashboard/project/elntwxoivnsescxvjtlo/sql/new)。
2. 貼上 [friends.sql](Backend~/friends.sql) **完整內容**並執行；它建立好友資料表、函式與權限，最後通知 PostgREST 刷新 schema cache。此步需要專案管理權限，遊戲的公開金鑰無法執行。
3. 回遊戲的好友頁按「重新整理」。正常時會取得自己的 12 碼好友代碼；讓另一個已驗證帳號同樣開啟好友頁取得代碼，再測試邀請、接受與雙方列表。

若已執行 SQL 仍看到 `PGRST202`，確認執行的 Supabase 專案與 `Resources/CloudAuthConfig.json` 相同，檢查 SQL 執行結果，再於 SQL Editor 執行 `NOTIFY pgrst, 'reload schema';`。其他 HTTP 狀態應分別排查，不要把所有好友錯誤當成未部署。參考 [PostgREST schema cache 文件](https://docs.postgrest.org/en/v14/references/schema_cache.html)。

好友頁會區分尚未載入與成功載入的空列表，讀取期間停用重複操作；服務不存在時停止自動輪詢，保留「重新整理」供服務啟用後重試。錯誤提示只顯示固定訊息與識別碼，不顯示伺服器原始回應或登入權杖。

### 好友直接邀請進房

在同一個 Supabase 專案執行既有 `friends.sql` 後，再執行 [room-invites.sql](Backend~/room-invites.sql)。此更新是新增資料表與 RPC，可重複執行，不會清除好友。遊戲仍需已啟用網際網路中繼。用戶端無法用公開金鑰代替管理員部署 SQL。

房主登入並建立網際網路房間後，在旅人營地右下按「邀請好友」，再按好友旁的「邀請進房」。好友在主選單「好友」或多人模式「好友與房間邀請」查看邀請，按「接受進房」即可進入營地，不必輸入代碼或密碼。選單約每 8 秒更新房間邀請，亦可手動重新整理；主選單與多人模式入口會顯示待處理數量。已加入其他房間時須先返回主選單。

邀請限已確認的好友，5 分鐘內有效。房主每 15 秒維持邀請房間有效狀態，60 秒未更新就不再列出；離開營地、滿員或關房後會停止邀請並清除已發布的邀請。接受時後台會再次檢查好友關係與有效期限，只對收件者回傳進房憑證；實際房間是否存在、密碼與人數仍由中繼及房主握手確認。好友列表不包含密碼，登出會清除本機邀請狀態。同網路／遊客模式維持使用區域 IP 加入。
- 帳號隔離是本機檔案的分類管理，不是作業系統層級的存取控制。這不是多人競技防作弊機制。

## 實作與驗證

`CloudAccountService` 管理 Auth 流程；`CloudAuthTransport` 僅允許 Auth API 白名單，使用 HTTPS、15 秒逾時與禁止重新導向。`LoginSceneController` 與 `AccountLoginPanel` 在獨立登入場景提供繁體中文介面。`GameSceneFlow` 防止重複載入；主選單未登入或登入已過期時回到 Login，正式建置的未登入地圖也回到 Login。登入與主選單共用原本的選單音樂與地城背景。`LocalAccountProgress` 處理本機帳號目錄與舊檔匯入。

目前已設定 Supabase 專案 `elntwxoivnsescxvjtlo`，並以公開金鑰讀取真實 `/auth/v1/settings`，回應 HTTP 200：電子郵件登入已啟用、允許註冊、需要信箱驗證。此唯讀檢查未建立帳號或寄信，也無法確認管理端的 SMTP 與郵件模板。仍需完成一次註冊／收信驗證／登入／重設密碼／兩個帳號切換的端到端試玩。

官方參考：[API 金鑰](https://supabase.com/docs/guides/getting-started/api-keys)、[密碼登入](https://supabase.com/docs/guides/auth/passwords)、[驗證碼郵件模板](https://supabase.com/docs/guides/auth/auth-email-templates)、[SMTP 設定](https://supabase.com/docs/guides/auth/auth-smtp)、[Auth API](https://github.com/supabase/auth)。

已完成的本機驗證：Unity Play 模式 78 項帳號流程、錯誤分類、主選單整合與介面檢查（模擬 Auth 回應），以及原有 95 項存檔／Boss／陷阱整合測試；Editor 與不含 UNITY_EDITOR 的程式編譯均通過。另已完成真實 Auth 設定端點的唯讀連線檢查；模擬測試與連線檢查均不等同於註冊、寄信與登入的端到端驗證。

獨立登入場景驗證：37 項 Unity Play／Editor 流程檢查與 52 項設定回歸檢查通過。檢查使用真實 Login 場景資產與模擬 Auth，涵蓋建置順序、從其他編輯場景按 Play 仍先進入 Login、登入失敗留在登入頁、註冊／驗證／重設密碼、登入後進主選單與地圖、登出返回 Login、未登入的主選單與正式執行期地圖轉回 Login、服務不重複及不寫入玩家存檔。1280×720 登入與主選單畫面已渲染並確認；執行版、UNITY_EDITOR 版本與全部 Editor 程式編譯通過。這次驗證未呼叫真實登入服務或寄送郵件。

郵件設定工具已通過模擬測試：僅操作指定專案、只更新四個主旨／模板欄位、保留 UTF-8 繁體中文與驗證碼占位符、更新後讀回確認，且不輸出權杖。本機瀏覽器連線目前不可用，因此尚未代為登入 Supabase 後台，也未實際執行管理端郵件更新。
