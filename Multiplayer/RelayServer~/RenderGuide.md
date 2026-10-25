# 用 Render 啟用網際網路房間

已部署的正式服務可直接雙擊專案根目錄 `D:\GameTest3\GameTest\開啟遊戲伺服器.url`，或開啟 [快速啟動連結](https://gametest-relay.onrender.com/health)。等頁面顯示 `{"status":"ok","protocol":1}` 後再進遊戲建立房間。捷徑可複製到桌面供管理者使用；操作說明見根目錄的 `伺服器快速開啟說明.md`。

這份套件包含現有遊戲中繼服務和 Render 免費 Web Service 設定。使用 Render 提供的網址，不需購買網域。它使用既有 Supabase 帳號驗證；好友 SQL 是另外的好友功能，不是中繼房間的前置條件。

## 1. 建立 GitHub 儲存庫

1. 登入 GitHub，開啟 <https://github.com/new>。
2. Repository name 填 `gametest-relay`，選 Private，按 Create repository。
3. 解壓縮 `GameTest-RenderRelay-20261009.zip`。
4. 在新儲存庫按 uploading an existing file；若儲存庫已有檔案，按 Add file → Upload files。
5. 把解壓後資料夾內的全部檔案拖入頁面，按 Commit changes。
6. 確認儲存庫首頁直接看得到 `Dockerfile`、`GameRelay.csproj`、`Program.cs` 與 `render.yaml`。

## 2. 在 Render 部署

1. 開啟 <https://dashboard.render.com/>，以 GitHub 帳號註冊／登入。
2. 按 New → Blueprint，連接 `gametest-relay` 儲存庫。若看不到它，授權 Render 存取該儲存庫。
3. Blueprint name 可填 `gametest-relay`，選剛才上傳檔案的分支，Blueprint Path 保留 `render.yaml`。
4. 在提示的環境變數欄位填入：
   - `SUPABASE_URL`：遊戲 `Accounts/Resources/CloudAuthConfig.json` 的 `projectUrl`。
   - `SUPABASE_PUBLISHABLE_KEY`：同一檔案的 `publishableKey`，以 `sb_publishable_` 開頭的公開金鑰。
5. 確認預覽是 **一個 Free Web Service、Singapore、Docker**，再按 Deploy Blueprint。
6. 等服務狀態為 Live，開啟 Render 顯示的服務網址，結尾加 `/health`。應顯示：

```json
{"status":"ok","protocol":1}
```

套件內另附 `RenderValues.txt`，可直接複製這兩個既有公開連線設定至 Render。無需管理權杖或 service_role 金鑰。

`render.yaml` 設定 `PORT=8080`，與 Dockerfile 的監聽埠相同；平台負責 HTTPS/WSS，無需部署 Caddy 或另外建立 Render 資料庫。房間在單一服務的記憶體中，重啟會結束現有房間。

## 3. 接回 Unity

將成功部署的 Render 服務網址提供給協作代理，例如 `https://gametest-relay-xxxx.onrender.com`，再設定遊戲 `Assets/Script/Multiplayer/Resources/CloudRelayConfig.json`：

```json
{"relayUrl":"wss://gametest-relay-xxxx.onrender.com/coop"}
```

這裡的網址必須換成你實際取得的網址。設定後需重新建置並分發更新後的遊戲；已有的執行檔不會自行讀取 Unity 專案中的修改。

用兩個已驗證信箱的遊戲帳號，分別從家用網路與手機熱點登入。房主選「多人模式 → 網際網路 → 建立房間」，另一位輸入 10 碼房間代碼及相同密碼，按「加入房間」。確認雙方移動、遊戲同步和房主離線清理。

## 免費方案的測試方式

免費服務連續 15 分鐘沒有流量會休眠，喚醒約需一分鐘。遊戲目前 WebSocket 連線逾時是 10 秒，所以試玩前先在瀏覽器開啟 `/health`，等看到 `status=ok` 再建立／加入房間。這是試玩前的一次喚醒，不是持續保活。

免費方案可能被平台重啟，並有流量／建置額度限制。部署與健康檢查成功仍不等於跨網路試玩通過。Docker 建置、正式 WSS、真實帳號及不同網路需在 Render 部署後確認。

官方參考：

- [Blueprint 部署流程](https://render.com/docs/infrastructure-as-code)
- [Blueprint 設定欄位](https://render.com/docs/blueprint-spec)
- [Docker 部署](https://render.com/docs/docker)
- [免費方案限制](https://render.com/docs/free)
