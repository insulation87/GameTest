# 網際網路合作中繼部署

這個 ASP.NET Core 10 服務轉送遊戲既有的壓縮同步封包。玩家登入原有 Supabase 帳號後，建立或加入 10 碼房間。遊客僅能使用同網路合作模式。玩家不需要設定路由器轉送；部署主機需要公開 HTTPS/WSS 網址。

## 必要設定

沒有主機或網域時，可使用 Render 免費 Web Service 提供的 HTTPS/WSS 網址，按照 [Render 部署步驟](RenderGuide.md) 操作；該路線不需要 Docker Compose 或 Caddy。以下為自管主機部署方式。

1. 在現有 Supabase 專案的 SQL Editor 執行 `../../Accounts/Backend~/friends.sql`。該檔案建立兩個遊戲專用資料表與 `game_social` RPC；不改動既有登入或遊戲存檔。資料表啟用 RLS，玩家只能透過檢查登入身分的 RPC 操作。
2. 準備支援 Docker Compose 的公開主機，以及指向主機的網域 DNS。此版本是一個中繼實例；房間存在記憶體，重新啟動服務會結束所有房間。多實例不可直接負載平衡。
3. 複製這個目錄中的 `.env.example` 為 `.env`，填入 `RELAY_DOMAIN`、既有 `SUPABASE_URL` 及 **publishable 公開金鑰**。公開金鑰與專案 URL 可取自 `Accounts/Resources/CloudAuthConfig.json`。不要放入 service_role 或管理金鑰。
4. 在主機執行 `docker compose up -d --build`。開放主機 TCP 80／443；Caddy 自動取得網域憑證並代理 WebSocket。`relay:8080` 僅在容器內網公開。
5. 用 `https://你的網域/health` 確認回應 `status=ok, protocol=1`。
6. 將 `Assets/Script/Multiplayer/Resources/CloudRelayConfig.json` 設為 `{"relayUrl":"wss://你的網域/coop"}`，重新建置遊戲。正式遊戲只接受 WSS，拒絕 URL 中的憑證、query 或 fragment。
7. 使用兩個已驗證信箱的遊戲帳號，分別從家用網路與手機熱點登入。房主選「網際網路」→建立房間，另一台輸入房間代碼與密碼，確認移動、收集、傳送與房主離開的行為。

## 服務行為與限制

- 每次建立、加入與更新令牌都向設定的 Supabase `/auth/v1/user` 驗證；需要已驗證信箱。令牌只放在 WSS 訊息，不放入網址或記錄。更新令牌不能改變房間中的帳號身分。
- 房間為 1 位房主＋3 位玩家，伺服器指定玩家 ID 並覆寫每個封包的發送者 ID。加入者只能傳送给房主；由房主運行遊戲判定與轉送世界狀態。
- 房間密碼由既有遊戲握手檢查。代碼使用密碼學隨機數；房主離開則整個房間結束，不支援轉移房主。中繼讀取／轉送封包，可見遊戲資料；WSS 保護傳輸。
- 服務限制连接、每秒訊息數、封包大小及發送佇列，過慢的接收端會斷線。房主不是專用遊戲伺服器，房主作弊防護與移動預測不包含在此修改。
- 主機流量與租用成本取決於玩家數、世界同步資料量及供應商。部署後需從實際外網測量延遲與流量；目前沒有自動配置主機或購買付費服務。
- 反向代理下，程式內的 IP 限速以代理位址計數；公開大量玩家部署需在入口設定每個用戶的限速。請勿把容器 8080 直接公開到網際網路。

## 隔離實測工具

`Review/` 為本機協定測試工具，部署專案明確排除其 C# 檔。`Accounts/Backend~/Review/` 是本機 Auth/Data API 測試替身，使用真實 PostgreSQL 及同一份 `friends.sql`；不能部署到正式環境。Unity 測試入口 `OverworldMultiplayerBuildReview.RunGuestHost/RunGuestClient/RunGuestSolo/RunRelayHost/RunRelayClient` 只允許有隔離標記的 batch 專案。

正式中繼以 HTTPS Supabase authority 啟動；只有同時使用 `Development`、`--isolated-review` 和 loopback authority 的隔離測試，才允許 HTTP Auth fixture，仍需經過 Auth API 驗證。

參考：[ASP.NET Core WebSocket 文件](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0)、[Supabase Database Functions](https://supabase.com/docs/guides/database/functions)、[Supabase RLS](https://supabase.com/docs/guides/database/postgres/row-level-security)。
