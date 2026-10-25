# 打包版進入營地後無畫面：2026-10-24

## 原因與修正

使用者的 Windows 執行日誌首先出現 `ArgumentNullException (shader)`，堆疊為 `TradingHubController.Block → BuildCourtyard → Start`，隨後 `Update` 持續拋出 `NullReferenceException`。

營地原本僅以 `Shader.Find("Universal Render Pipeline/Unlit")` 建立材質。這不會建立資產依賴，Shader 在執行版未被保留，導致訪客與 UI 尚未建立就中斷初始化。

新增 `Trading/Resources/TradingHubSurface.mat`，明確引用 URP Unlit，讓 Unity 將 Shader 與所需變體納入建置。營地從這份材質複製各色材質，維持原本外觀。另以初始化完成旗標防止未完成初始化時執行互動更新。

## 實測

- Unity 6000.3.10f1，獨立匯出專案 `Builds/Export-20261024/Project`。
- Play Mode：`TradingHubReview.Run` 的 118 項檢查通過，含營地、移動、商店、交易存檔、傳送門、Map1 與返回流程，沒有執行期錯誤。
- Windows64 執行版：使用正式建置選項 `BuildOptions.None`、獨立測試產品與存檔目錄，16 項檢查通過。確認 Resources 材質及支援的 URP Unlit Shader 在打包後存在，營地相機可看到地板，UI 與訪客建立成功，商店可開啟，能進入 Map1、選擇升級、恢復遊戲，再返回主選單與營地。
- 已直接檢視執行版的營地與 Map1 遊戲截圖，畫面正常。
- 初次隱藏視窗測試無法取得有效畫面，已改為實際顯示遊戲視窗重測。測試亦已處理遊戲原有的升級選擇，避免將該選單的正常暫停誤判為載入失敗。
- 原始結果、測試程式與截圖位於 `Builds/CampDisplayFix-20261024`，包含 `play-mode-result.txt`、`player-result.txt`、`player-camp.png` 與 `player-map1-gameplay.png`。
- 移除執行期測試程式、恢復 `DefaultCompany/GameTest` 產品識別後，正式 Windows64 建置成功（0 errors）；最終包啟動測試沒有執行期例外，未登入或選擇遊客。ZIP 共 199 個項目，必要檔案存在，正式組件確認未包含測試程式。
- 交付：`Builds/CampDisplayFix-20261024/GameTest-Fixed-20261024.zip`。完整解壓至新資料夾後啟動 `GameTest.exe`，使用原本的正式存檔位置。

未驗證範圍：本次未重測 Map2～Map4、多人連線與正式帳號寄信。測試使用遊客及獨立產品存檔，未寫入使用者正式遊戲存檔。
