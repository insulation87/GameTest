# 公開中繼接線與成品驗證

日期：2026-10-09。公開服務 `https://gametest-relay.onrender.com`，遊戲設定 `wss://gametest-relay.onrender.com/coop`。

- 公開健康檢查：HTTP 200，`{"status":"ok","protocol":1}`。
- 獨立 Unity Play Mode：18 項通過，包含實際 Resources 設定、真實 HTTPS/WSS、伺服器拒絕無效令牌、預設網際網路分頁、房主操作失敗提示／清理／重新啟用、房間代碼檢查及未登入限制。登入 UI 情境使用本機假 Auth；沒有登入真實帳號或建立公開房間。另記錄 1 次 Unity Editor Search 索引啟動例外，與遊戲錯誤分開計數。
- Windows Unity 原生測試版本：6 項通過，確認打包後設定、真實 HTTPS/WSS 及無效令牌拒絕。使用獨立測試產品資料目錄；沒有實際登入、房間、地圖或存檔操作。
- 實際交付的非 Development 成品：初始化 Unity 並保持執行 10 秒，未見啟動 exception／missing script；一般成品不含 Editor 測試類別或原生測試 probe。讀取日誌前先結束自己的程序，因 Unity 執行時會鎖住日誌。
- 使用既有完整隔離專案的 Assets、Packages、ProjectSettings 同步副本建置，未使用原使用者編輯器、未改動使用者場景／存檔，舊成品保留。

## 成品

`D:/GameTest3/GameTest/Builds/Windows64-20261009-Internet/GameTest.exe`

分享壓縮檔：`D:/GameTest3/GameTest/Builds/GameTest-Windows64-20261009-Internet.zip`，190 個檔案、98,388,259 bytes。使用標準 `/` ZIP 路徑，包含執行檔及所有必要的遊戲資料；省略標記為 DoNotShip 的 Burst 偵錯資料。

正式建置結果 **Succeeded**，StandaloneWindows64、BuildOptions.None，三個正式場景 Login → MainMenu → Map1。建置仍記錄 1 個既有 HDRP AutodeskInteractiveTransparent ShaderGraph 匯入類型錯誤診斷及 1 個 Unity Cloud 原生符號上傳權杖為空警告。建置啟動也出現隔離 PackageCache 的 Collections 測試用 DLL 路徑例外；它沒有阻止建置，且原生測試／正式啟動未見此例外。詳見原始建置結果。

## 尚未驗證

兩個真實已驗證帳號的建立／加入房間、實際遊戲同步、不同 ISP／家用網路與手機熱點連線、四人完整試玩、長時間連線及圖形人工操作。現有測試證明公開連線與拒絕流程，不能視為有效帳號的合作試玩已通過。

免費 Render 休眠後，先開啟 `/health` 等回應成功再進遊戲，避免冷啟動超過遊戲目前 10 秒的 WebSocket 連線逾時。

原始結果：

- [Play Mode](public-relay-play-20261009.txt)
- [Play Mode 檢查細項](public-relay-play-details-20261009.txt)
- [Windows 原生連線](public-relay-native-20261009.txt)
- [正式建置](public-relay-release-build-20261009.txt)
- [正式啟動](public-relay-release-startup-20261009.txt)
