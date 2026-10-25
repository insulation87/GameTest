# 遊客、好友與網際網路合作實測報告

日期：2026-10-08。Unity 6000.3.10f1；Windows64 Mono；既有 Login → MainMenu → Map1 與草原／森林實體場景。

## 已實作的行為

- 登入頁增加遊客入口；可單人、同機雙人及同網路合作，不能使用好友或網際網路房間。離開遊客模式可返回登入頁。
- 遊客使用 `Guest/progress-v1.json` 和 `Guest/coop-v1.json`，與帳號及舊版存檔分開。遊玩中不能切換身分。
- 登入帳號可使用 12 碼好友代碼、暱稱、邀請、接受／拒絕／取消、移除與好友清單。接受需要由收到邀請的一方操作；切换帳號時清除好友快取。好友資料以 Supabase SQL/RPC 儲存。
- 新增 WSS 中繼服務與 10 碼房間代碼，沿用既有房主判定、角色選擇、房間密碼及最多四人限制。伺服器驗證 Supabase 登入與已驗證信箱，指定真實發送者 ID，防止加入者冒用其他玩家。
- 同網路模式檢查 IPv4 本機介面網段，不接受 DNS 名稱或公網直連。
- 修正載入地圖途中斷線時過早切回單人模式的風險；保留合作狀態到載入完成，再返回主選單，不覆寫單人存檔。

## 實測方式與結果

採用保留原有 Assets、Packages、ProjectSettings 與資產 GUID 的完整隔離專案。原使用者編輯器、當前場景與正式存檔未用於測試。測試使用隨機 UUID 與本機 Auth fixture，未建立正式帳號或寄信。好友 API fixture 執行的是**同一份正式 `friends.sql`，連到本機 PostgreSQL 17 的實際資料庫**，不是假好友清單。

| 情境 | 執行環境 | 結果／檢查數 |
|---|---|---|
| 遊客房主／加入者：無登入、權限封鎖、移動、收集、傳送、怪物、存檔及斷線 | 兩個 Unity 編輯器 Play Mode，TCP loopback | PASS：51／32 |
| 遊客完整冒險 → 返回登入 → 切換帳號 → 原遊客存檔保留 | Unity Play Mode | PASS：50 |
| 登入好友邀請、收件方確認、双方清單、移除＋中繼雙人冒險 | 兩個 Unity Play Mode、實際 WebSocket 中繼及 PostgreSQL API | PASS：57／35 |
| 實際非同步地圖載入中注入連線失敗 → 返回選單、單人檔案逐位元一致 | Unity Play Mode | PASS：7 |
| 登入好友流程及中繼雙人：共同掉落、兩段實際傳送門、怪物外觀、渲染與斷線清理 | 兩個 Windows64 原生測試玩家 | PASS：58／36 |
| 遊客同機雙人：獨立手把輸入、相機、合作收集、兩位玩家傳送及獨立存檔 | Windows64 原生測試玩家 | PASS：48 |
| 遊客單人、切換帳號、存檔保留與好友按鈕解鎖 | Windows64 原生測試玩家 | PASS：51 |
| 載入地圖途中斷線與單人存檔保護 | Windows64 原生測試玩家 | PASS：7 |
| 無效／過期登入、重複帳號、房間 ID、冒用發送者、最多四人、令牌換身分、踢除、房主離開 | 最終 ASP.NET Core 中繼服務＋本機 Auth fixture | PASS：13 |
| 匿名／直接資料表存取拒絕、重複與自己邀請拒絕、只有收件者可接受、局外人隔離、暱稱與移除 | 真實 PostgreSQL，使用 authenticated 角色和逐帳號 JWT claims | PASS |

有一輪原生遊客測試的 UI 擷取選到過場 Canvas 而失敗；已修正測試工具，重跑後 PASS。好友測試替身最初透過 Windows 命令列傳中文 SQL，造成編碼錯誤；已改為 UTF-8 stdin，好友完整流程已重跑通過。這些修正不放入正式玩家。

實際遊戲相機與 UI 均以離屏方式渲染擷取 PNG，已檢視好友清單與遊客主選單，沒有文字／按鈕遮擋。沒有執行人工正常視窗操作。

## 正式建置

成品：`D:\GameTest3\GameTest\Builds\Windows64-20261008-Cloud\GameTest.exe`。

- BuildResult **Succeeded**，StandaloneWindows64、Mono、`BuildOptions.None`，三個正式場景順序正確。
- 成品 190 個檔案，261,530,520 bytes；部署後逐檔 SHA-256 與隔離建置來源相符。先前正式版本保留。
- 以 Cecil 檢查 Assembly-CSharp：新功能存在；`OverworldValidationProbe`、`CoopSession.ReviewInternet` 及好友 HTTP 測試建構子均不在正式執行檔。
- 已從上述部署目錄啟動正式執行檔 65 秒，D3D11／Mono 初始化，未見 exception／missing script；結束自己的測試程序。
- 建置報告仍有 **1 個 error 診斷與 1 個 warning**：既有 HDRP AutodeskInteractiveTransparent ShaderGraph 匯入類型不符，以及 Unity Cloud 符號上傳 access token 為空。BuildResult 仍為 Succeeded；不宣稱零診斷。專案實際使用 URP，原生遊戲渲染測試通過。

## 尚未啟用與尚未驗證

**正式好友後台及公開中繼尚未部署。** 目前僅有既有 Supabase 公開登入設定，未提供資料庫管理權限或公開中繼主機／網址；`CloudRelayConfig.json` 的 `relayUrl` 留空。正式成品可使用遊客／原帳號入口與本機合作；好友／網際網路選單會說明服務尚未啟用，不假裝連線成功。

需在既有 Supabase 後台執行 [friends.sql](../Accounts/Backend~/friends.sql)，並依 [中繼部署文件](RelayServer~/README.md) 啟動服務，填入真實 `wss://.../coop` 後重建玩家。

尚未驗證：兩台實體電腦的 LAN、防火牆設定、不同 ISP／手機熱點的真正跨 NAT 連線、正式 WSS 憑證與反向代理、正式 Supabase 帳號與好友 API、郵件註冊／重設、公開服務延遲及流量、長時間遊玩、四位玩家完整遊戲場景、人工視窗操作。中繼測試在本機使用 WS 和測試 Auth authority；正式程式強制使用 WSS 與 HTTPS Supabase。

原始通過紀錄及畫面保存於 [Verification~](Verification~/)。隔離專案與暫存資料庫保留在本機 Temp，測試服務結束後不持續背景執行。
