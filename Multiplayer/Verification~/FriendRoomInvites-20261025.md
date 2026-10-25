# 好友直接邀請進房驗證（2026-10-25）

## 操作方式

1. 已登入的房主建立「網際網路」房間，在旅人營地右下按「邀請好友」。
2. 在已確認的好友旁按「邀請進房」。
3. 好友在主選單「好友」或多人模式「好友與房間邀請」按「接受進房」，直接加入營地，無須手動輸入代碼或密碼；亦可拒絕。

正式使用前，管理員須在既有 Supabase 專案執行 `Accounts/Backend~/room-invites.sql`（先安裝既有 `friends.sql`），並使用已設定的網際網路中繼。本次沒有操作正式資料庫、正式帳號或公開中繼。

## 實測

在 `Builds/FriendInviteReview/Project` 與 `ClientProject` 進入 Unity 6000.3.10f1 Play Mode。測試使用獨立產品／存檔身分、獨立場景，以及同一個 `TradingHubController` 生成營地；沒有切換使用者正在編輯的場景。Auth 是本機測試替身，好友及邀請 RPC 實際執行正式 SQL，使用獨立 PostgreSQL 17.11（127.0.0.1:15473）。HTTP fixture 在 18474，原始中繼程式在 18475，透過真實 WebSocket 傳輸。

| 情境 | 結果 |
| --- | --- |
| 房主在營地從好友視窗送出邀請；收件者看到接受／拒絕，不會自動進房 | 通過 |
| 拒絕後移除邀請，保留目前畫面 | 通過 |
| 房主關房後，以已取得的邀請 ID 接受 | 通過：後台拒絕，顯示失效訊息，不建立連線 |
| 房主重新開密碼房並再次邀請，好友按接受 | 通過：不用輸入代碼／密碼，雙端營地人數均為 2 |
| 房主離線、登出、RPC 尚未部署後重試 | 通過 |
| 滿員營地禁止新邀請、目前已在房間時禁止接受另一邀請 | 通過（滿員使用 Play Mode 營地名單模擬） |
| 好友視窗開啟時按住 W，關閉後保持同一按鍵 | 通過：開啟時不移動，關閉後恢復移動 |
| 既有加好友、接受、移除、失敗重試、登出，以及多人選單邀請入口 | 通過：23 項 Play Mode 檢查 |
| SQL：匿名／直接資料表存取、自己與非好友邀請、去重、收件者限定密碼、重複接受、有效期限、心跳、拒絕、移除好友、關房 | 通過 |
| SQL 重複安裝 | 通過 |

房主／收件者整合各 16／14 項，營地輸入 10 項，好友回歸 23 項，共 63 項 Play Mode 檢查。執行期間沒有未預期的遊戲執行期錯誤。Unity Search 啟動時出現既有編輯器例外，另列於日誌，未計為遊戲通過項目。

已檢視雙端 1600×900 好友邀請介面的 PNG：按鈕、邀請列與狀態文字均在可視區域。這是測試程式以 Camera／RenderTexture 渲染實際 UI；不是獨立執行版的螢幕擷取。

## 證據與重跑入口

`Builds/FriendInviteReview` 保留 `host-result.txt`、`client-result.txt`、`input-result.txt`、`friends-play-result.txt`、`sql-result.txt`、雙端 UI 圖片與日誌。`source-hashes.json` 確認 8 個遊戲程式與實測專案完全一致。

- `Multiplayer/Editor/FriendRoomInviteReview.cs`：`RunHost`、`RunClient`、`RunInput`，只允許有標記的隔離 batch 專案。
- `MainMenu/Editor/FriendsServiceReview.cs`：既有好友回歸與多人邀請入口，所有網路回應均為測試替身。
- `Accounts/Backend~/Review/room-invites-review.sql`：在空白隔離資料庫先執行 `bootstrap.sql`、`friends.sql`、`room-invites.sql`，再執行此檢查；測試本身 rollback。
- `Multiplayer/Verification~/FriendInviteFixture`：loopback Auth／RPC fixture，RPC 執行同一份資料庫函式。配合原始 `RelayServer~/GameRelay.csproj` 的 `--isolated-review`，僅連本機測試 Auth。

尚未驗證：正式 Supabase 部署、正式登入身分、公開 WSS、不同外部網路、正式執行版。本次沒有產生新的發行包。
