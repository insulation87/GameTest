# 敵人偵測與追擊範圍

2026-10-09：一般敵人的 `chaseRange` 從 100 提高至 **200 Unity 單位**。同時更新 `EnemyAI` 預設值與實際生成用的 `Assets/NPC/Enemy.prefab` 序列化值，讓既有地圖的生成器直接使用新設定。

草原、森林與古林的敵人原本受營地約 38 單位的邊界限制，即使放大偵測距離也無法追到營地外。`OverworldDirector.EnemyBoundaryAt()` 現在提供各自所屬區域的追擊邊界；`EnemyAI.AssignRoom()` 在已保存的戶外地圖中使用該邊界。敵人可沿保存的 NavMesh 在本區域追擊，仍檢查完整路徑及區域邊界。生成位置與營地標記沿用原設定。

近戰距離維持 1.5、弩箭距離維持 12；牆壁仍會阻擋攻擊視線及飛行中的弩箭。可在 Enemy prefab 的 EnemyAI → Chase Range 調整距離。

## 實測

測試在完整的隔離 Unity 專案副本進行，保留使用者原本的 Unity 編輯器、場景與存檔。

| 情境 | 結果 | 紀錄 |
| --- | --- | --- |
| Unity Editor Play Mode：實際 Login → MainMenu → 已保存的 Map1、真實 NPC 生成器及 prefab | 21 項檢查通過 | `Verification~/enemy-range-editor-20261009.txt` |
| Windows 驗證執行檔：相同 Map1 偵測與追擊情境 | 21 項檢查通過 | `Verification~/enemy-range-native-20261009.txt` |
| 隔離場景 Play Mode：實際 NavMesh、近戰、等級 9/10/11 弩箭、傷害、牆壁遮擋及薄牆碰撞 | 17 項檢查通過 | `Verification~/enemy-attacks-playmode-20261009.txt` |
| 兩個獨立 Windows 程序：遊客 LAN 主機／客戶端、移動、三區傳送、共同蒐集、魔物同步、斷線與存檔保護 | 主機與客戶端通過 | `Verification~/lan-host-20261009.txt`、`Verification~/lan-client-20261009.txt` |

偵測情境確認：實際生成的敵人使用 200；距離約 160 的玩家在舊值 100 時不會觸發追擊、在新值 200 時會觸發真實路徑與移動；超過 200 不追擊；另一區域的玩家即使在 200 內仍不會使敵人跨區追擊。

雙人測試第一次的存檔斷言假設測試目錄一定沒有單人存檔，但該隔離目錄已有先前測試的遊客存檔。因此改為比較測試前後的檔案位元組是否一致，重跑主機／客戶端後通過；首次失敗紀錄保留於 `Verification~/lan-first-*-20261009.txt`。此修正只影響驗證程式，不進入正式遊戲。

## 完整遊戲輸出

- 解壓可玩的完整包：`D:\GameTest3\GameTest\Builds\GameTest-Windows64-20261009.zip`。
- 未壓縮遊戲：`D:\GameTest3\GameTest\Builds\Windows64-20261009\GameTest.exe`。
- Windows 64 位元正式建置，`BuildOptions.None`；Login、MainMenu、Map1 三個場景。
- 190 個執行所需檔案，總計 261,531,544 bytes；ZIP 為 98,383,551 bytes。逐檔 SHA-256 核對建置、輸出與 ZIP 解壓內容一致。
- Mono.Cecil 檢查正式組件：包含新追擊區域實作；排除 OverworldValidationProbe、ReviewInternet 與好友 HTTP 測試注入建構子。
- 精確啟動 ZIP 解壓出的正式 GameTest.exe，以 D3D11 執行 72.2 秒，圖形與組件初始化成功，無啟動例外或遺失腳本。啟動檢查使用批次隱藏視窗，沒有宣稱已人工操作完整遊戲流程。
- ZIP SHA-256：`7B06260DC612DB1B1CC4AF995D1281BD512603F92112DBF904A26BE06CE3479C`。

BuildReport 結果為 **Succeeded**，同時保留 1 個既有 HDRP AutodeskInteractiveTransparent ShaderGraph 資源類型診斷及 1 個 Unity Cloud 未登入、原生符號不上傳的警告。詳細原文見 `Verification~/release-build-20261009.txt`；不宣稱零診斷。

尚未驗證不同實體電腦或不同網路的連線、長時間大量敵人的效能。本次雙人測試使用同一部電腦的兩個獨立程序與 loopback TCP。正式雲端好友資料庫及公開 WSS 中繼仍待部署，`CloudRelayConfig.relayUrl` 目前為空；本包已包含相關用戶端，但尚未提供可使用的正式跨網路多人服務。
