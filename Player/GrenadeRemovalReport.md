# 移除手榴彈與實測報告

2026-10-09，Unity 6000.3.10f1。

已移除手榴彈的投擲、拾取、敵人掉落、背包數量、裝備模式、鍵盤與手把操作、音效事件及多人同步資料。正式遊戲組件不再包含 GrenadeThrower、GrenadePickup、ThrownGrenade、GrenadeExplosion；場景與預製物查無這些腳本的序列化引用。

舊存檔仍可讀取，舊 `grenades` 欄位會被忽略，不再佔用背包容量；新存檔不會寫入該欄位。武器、護甲、醫療包、經驗與進度道具仍保留。原本 G／End／手把 Y 的手榴彈設定會清除，其他自訂設定及醫療包 H／Insert／手把方向鍵下保持正常。保留既有動作編號，避免舊鍵位對應到其他操作。火球仍使用爆炸音效。

前次設定保持：進度道具掉落率 1%，第二層需要 10 個、第三層需要 20 個；非對應角色武器的傷害及武器效果為 10%。

## 實際執行結果

Unity Editor 測試皆在獨立暫存專案進入 Play Mode 執行，未更動原專案正在編輯的場景或玩家存檔。兩份測試專案的 95 個遊戲執行階段 C# 原始檔皆與原專案 SHA-256 相符。

| 情境 | 結果 |
| --- | --- |
| Editor Play Mode，實際 Login → MainMenu → 已保存的草原／森林 Map1 | 23 項通過。確認無手榴彈類別／HUD、實際 NPC 死亡仍掉落其他物品、醫療包拾取與使用、G 不切換武器、存讀檔不含手榴彈。 |
| Windows 驗證執行檔，實際 Map1 | 同上 23 項通過。 |
| Editor Play Mode，鍵位與設定介面 | 37 項通過。包含舊鍵位清理、自訂鍵位、鍵盤／滑鼠／手把、介面操作及實際雙人分割畫面移動與技能。 |
| Windows 兩個獨立程序，實際 TCP LAN 流程 | 主機 95 項、客戶端 34 項通過。包含共同收集 10／20 個道具、傳送門、玩家移動、魔物同步、斷線返回與各自存檔隔離；無手榴彈元件及 HUD。 |
| Editor Play Mode，完整角色裝備／背包測試 | 159 項通過。結果見 `Verification~/grenade-removal-equipment-20261009.txt`，逐項紀錄見 `Verification~/grenade-removal-equipment-checks-20261009.txt`。 |

完整裝備測試包含帶有有效校驗碼的舊手榴彈存檔讀取、容量不受影響、各角色武器傷害／效果及背包與商店畫面。背包文字驗證使用實際渲染網格的邊界及完整字元數，另輸出 1920×1080 畫面人工檢查。初次測試遇到測試場景未切換遊客、舊存檔測試資料缺少校驗碼、批次編輯器文字度量及測試 API 使用問題；已修正測試流程，保留首次失敗紀錄，不以編譯替代 Play Mode。

原始結果、畫面、執行檔啟動紀錄與檔案雜湊存於 `Player/Verification~`，Unity 不會匯入此資料夾。

## 完整 Windows 遊戲

- ZIP：`D:\GameTest3\GameTest\Builds\GameTest-Windows64-20261009-NoGrenades.zip`，解壓後執行 `GameTest/GameTest.exe`。
- 資料夾：`D:\GameTest3\GameTest\Builds\Windows64-20261009-NoGrenades\GameTest.exe`。
- 正式 Windows 64 位元建置，`BuildOptions.None`，場景為 Login、MainMenu、Map1。
- 190 個執行階段檔案，261,523,704 bytes；ZIP 為 98,381,508 bytes。正式建置、輸出資料夾及 ZIP 解壓內容已逐檔比對 SHA-256。
- ZIP SHA-256：`97260A256323D35D03FA7A655CA94B35F2F51ED846076A2AA88AE7027B8D38E2`。
- 正式組件確認四個手榴彈類別不存在，保留角色武器 0.1 及 NPC 進度道具 0.01 的執行邏輯；不含地圖驗證探針、測試帳號／HTTP 注入功能。
- 從此 ZIP 實際解壓啟動 GameTest.exe，以 D3D11 執行 165.6 秒；未記錄例外、遺失腳本或啟動錯誤。確認後已結束這個測試程序。

BuildReport 為 **Succeeded**，但仍有 1 個既有 HDRP AutodeskInteractiveTransparent ShaderGraph 資產類型診斷，以及 1 個未登入 Unity Cloud、原生符號未上傳警告；原始報告見 `Verification~/grenade-removal-release-build-20261009.txt`，不宣稱零診斷。

尚未驗證兩台實體電腦及跨網路連線；本次雙人連線使用同一台電腦的獨立程序與 loopback TCP。正式公開 WSS 中繼與好友後端尚未部署，因此本次結果不能代表正式跨網路多人或好友服務已啟用。
