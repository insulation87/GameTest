# 管理者快速開啟伺服器捷徑實測

日期：2026-10-19（Asia/Taipei）。

## 交付內容

- 專案根目錄 `開啟遊戲伺服器.url`：Windows Internet Shortcut，指向 `https://gametest-relay.onrender.com/health`。
- 根目錄 `伺服器快速開啟說明.md`：操作步驟、成功回應、休眠等待方式及更換網址方式。
- `Multiplayer/RelayServer~/RenderGuide.md`：加入既有服務的快速啟動入口。

## 已實測

1. 讀取實際 `.url` 檔，確認首行為 `[InternetShortcut]`，並解析其中的 `URL=`。
2. 讀取實際遊戲 `Resources/CloudRelayConfig.json`，確認捷徑的 HTTPS 主機與遊戲 WSS 主機一致，健康檢查路徑為 `/health`。
3. 對從捷徑讀出的網址實際發出 HTTPS GET：HTTP **200**，回應 **`{"status":"ok","protocol":1}`**。驗證回應狀態與協定版本均正確。

終端機預設沙箱的首次請求無法連線；經網路權限審查後，實際請求成功。

## 尚未驗證

- Windows 檔案總管雙擊後是否以使用者預設瀏覽器開啟：computer-use 啟動檔案總管回報 `Computer Use app approval timed out`；瀏覽器工具沒有可用瀏覽器，故未完成 UI 實測。
- 未刻意使公開服務休眠，未測量完整冷啟動時間；本次只證明請求後服務回應就緒。
- 未驗證兩個真實帳號的建立／加入房間或遊戲同步。

本次只新增外部網址捷徑與文件，未修改 Unity 遊戲行為，因此未執行 Play Mode，也未操作使用者的場景與遊戲存檔。
