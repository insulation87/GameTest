# 草原森林雙人連線與 Windows 建置實測

完成日期：2026-10-08。Unity：6000.3.10f1。平台：Windows 64 位元、Mono、Direct3D 11。測試使用獨立完整專案副本，沒有操作使用者正在編輯的場景或使用正式帳號存檔。

| 驗證範圍 | 執行方式 | 結果 |
| --- | --- | --- |
| 房主／加入者 | 兩個獨立 Unity 程序，實際 Map1，Play Mode 與本機 TCP | PASS |
| 同機雙人 | 實際 Map1，Play Mode，虛擬手把與獨立相機 | PASS |
| Windows 單人遊戲流程 | 非 Development 驗證成品，實際 Login、MainMenu、Map1 | PASS |
| Windows 同機雙人 | 非 Development 驗證成品，手把控制、分割視圖、共同收集及三區傳送 | PASS |
| Windows 房主／加入者 | 兩個獨立驗證成品程序，本機 TCP | PASS |
| 成品相機渲染 | 真正的遊戲相機、材質與地形，Camera.Render 離屏讀回 | PASS |
| 正式 Windows 64 位元建置 | 原建置場景順序，BuildOptions.None，測試入口排除 | Succeeded，含下述套件診斷 |
| 正式成品啟動 | 未加入驗證指令的正式 exe，獨立隱藏程序 | PASS |

實測包括：登入場景至主選單與 Map1 的場景轉換、兩端角色選擇與操作回傳、加入者不持有權威移動或拾取、未收集完成時不能通過傳送門、兩位玩家共同收集 3／6 個本區掉落物、兩端指引與傳送門解鎖、加入者以真正操作穿過第一與第二道傳送門、另一位玩家位置保持獨立、三區安全落地與導航、原 NPC prefab 生成與加入者收到其模型、房主儲存共用進度，以及房主離線後加入者返回真正主選單並清除角色。單人也實測兩次傳送與戶外存檔讀寫。同機雙人驗證手把只移動第二位玩家、分割相機與共同目標。

首次測試發現並修正：加入者會跳過 GameProgressSave 的存檔初始化，因而沒有建立 FloorProgressionManager 與 ExplorationMinimap。CoopSession 現在在建立本地角色後、接收首份進度快照前初始化它們。兩程序 Play Mode 與 Windows 成品均已重測通過。

另移除了 Assets/Player/Player.prefab 上一個已失效的舊 CameraCollision 元件。現在使用的 FPSCameraLook、CameraFollowTPS 與相機引用保持有效；現有相機碰撞仍由程式中的 CameraCollision.Resolve 處理。清理後連線測試沒有 Missing Script 診斷。

## 成品與正式／驗證版本的區別

正式成品位置：[GameTest.exe](../../../../Builds/Windows64-20261008/GameTest.exe)。三個建置場景依序為 Login、MainMenu、Map1，使用 BuildOptions.None，沒有 Development、Script Debugging 或驗證定義。

OverworldValidationProbe 只在 UNITY_EDITOR 或 OVERWORLD_VALIDATION 下編譯。遊戲流程測試用一份獨立、同樣非 Development 的驗證成品，以假登入 transport 驗證本機流程，沒有寄信或呼叫真實登入服務；測試帳號使用每次產生的新 GUID。正式成品另外建置，並用 Mono.Cecil 讀取 Assembly-CSharp.dll 的型別資料，確認 OverworldValidationProbe 與它的假登入入口沒有編入正式版。正式 exe 已另行啟動檢查，並不以含驗證入口的成品交付。

正式輸出已複製到專案 Builds 目錄；190 個成品檔案逐檔以 SHA-256 比對，全部與原建置輸出一致。Burst 除錯資料目錄未放入交付目錄。CoopSession、Map1、地圖與清理後的玩家 prefab 亦與已實測的來源比對。

## 建置紀錄保留的診斷

BuildReport 回報 Succeeded，但 totalErrors 為 1、totalWarnings 為 1，沒有把這些診斷隱藏或當成零錯誤建置：

- HDRP 17.3.0 內 AutodeskInteractiveTransparent.ShaderGraph 被辨識為 DefaultAsset，資源重載記錄「Host type is not matching any asset type」。隔離專案的同步重新匯入仍無法載入為 Shader。這項套件診斷尚未消除；沒有為了檢查而移除或修改原專案的 HDRP 套件。專案啟用 URP，成品三區地形、材質與相機離屏渲染均成功，遊戲流程沒有因此失敗。
- Unity Cloud 沒有 access token，無法上傳 native symbols。這是建置符號上傳提示，exe 及本機遊玩仍可執行；未替使用者登入或變更雲端設定。

原始數值與訊息見 [正式建置紀錄](OverworldNetworkBuildArtifacts/release-build-result.txt)、[資源匯入檢查](OverworldNetworkBuildArtifacts/hdrp-resource-inspection.txt)。

## 渲染檢查的限制

隱藏 Windows 測試程序的自動相機影像讀回為黑色，最初的自動繪圖斷言失敗。其餘遊戲流程已通過，但不以黑影像宣稱渲染成功。後續明確呼叫真正遊戲相機的 Camera.Render，再讀回 render target，完成 Windows 離屏渲染，並人工檢視以下成品影像。正式遊戲碼沒有增加這個測試呼叫；正式版型別檢查確認測試模組已排除。

[房主森林畫面](OverworldNetworkBuildArtifacts/host-gameplay.png)、[加入者森林畫面](OverworldNetworkBuildArtifacts/client-gameplay.png)、[單人森林畫面](OverworldNetworkBuildArtifacts/standalone-gameplay.png)。圖片來自遊戲相機的 render target，不包含全部 Screen Space Overlay UI。

尚未驗證：跨電腦／外網、防火牆與 NAT、網路延遲或封包故障、長時間遊玩效能、首領完整戰鬥與通關、真實雲端登入，以及人工操作可見視窗／全螢幕顯示與全部 UI。本次網路測試使用同一台 Windows 電腦的兩個獨立程序與 127.0.0.1。

## 實測原始結果

[Editor 房主](OverworldNetworkBuildArtifacts/editor-host-result.txt)、[Editor 加入者](OverworldNetworkBuildArtifacts/editor-client-result.txt)、[Editor 同機雙人](OverworldNetworkBuildArtifacts/split-result.txt)、[Windows 房主](OverworldNetworkBuildArtifacts/player-host-result.txt)、[Windows 加入者](OverworldNetworkBuildArtifacts/player-client-result.txt)、[Windows 單人](OverworldNetworkBuildArtifacts/standalone-result.txt)、[Windows 同機雙人](OverworldNetworkBuildArtifacts/native-split-result.txt)、[正式型別檢查](OverworldNetworkBuildArtifacts/release-assembly-inspection.txt)、[正式啟動](OverworldNetworkBuildArtifacts/release-startup-result.txt)。

重跑入口為 OverworldMultiplayerBuildReview.RunHost／RunClient／RunSplit、BuildValidation／BuildRelease。入口要求 batchmode、獨立完整專案與 authored-world-review.json 標記；不應在使用者的工作專案直接執行。