# 第一輪多人連線資安強化

日期：2026-10-21。目標：降低惡意加入者透過異常操作封包拖垮房主、污染遊戲輸入或耗盡接收佇列的風險。

## 檢查結果與修改

| 入口 | 原本行為 | 本次修改 |
| --- | --- | --- |
| 客戶端送往房主的壓縮封包 | 和大型世界快照共用 2 MiB 上限 | TCP 在讀取內文前檢查 8 KiB 壓縮長度；TCP 與雲端接收端均限制操作資料解壓後為 4 KiB。雲端先移除超限的個別玩家。 |
| 房主的遊戲指令入口 | 已有部分數值與遊戲規則驗證，但未完整限制封包階段、種類與速率 | JSON 解析前限制 UTF-8 大小；加入前只接受 hello，加入後只接受 input、command、upgrade；拒絕偽造 state/mesh、重複 hello、未知指令、非法索引、未定義按鍵與無效數值。 |
| 操作頻率與待處理訊息 | 沒有房主共用的每玩家速率限制 | 使用每秒補充 120 次、容量 120 次的 token bucket；正常客戶端每秒送出約 30 次輸入。每位遠端玩家最多累積 64 筆未處理資料訊息。 |
| 接收資料佇列 | 只有訊息筆數上限，大型字串仍可能大量累積 | TCP 與雲端接收佇列均增加 8 MiB 的 UTF-16 字串內容預算。這是佇列字串內容的預算，不是遊戲程序的總記憶體上限。 |
| 雲端異常資料 | 解壓／UTF-8 失敗可能終止房主的整條中繼連線 | 房主只踢出資料來源玩家；保留其他玩家。客戶端仍接受房主發送的大型世界資料。 |

修改檔案：`CoopClientPolicy.cs`、`CoopSession.cs`、`CoopTransport.cs`、`CloudRelayTransport.cs`。新增可重跑的 `Editor/CoopSecurityReview.cs`，及 `Verification~/SecurityRelayFixture` 本機測試中繼站。

## 驗證方式

使用 Unity 6000.3.10f1 的獨立專案 `Builds/SecurityReview/Project`。測試進入 Play Mode，建立簡單地板與真正的玩家元件，透過 loopback TCP 操作房主與遠端角色；雲端傳輸測試連到僅監聽 `127.0.0.1:18465` 的 .NET WebSocket fixture。測試中繼站不驗證正式帳號，僅在具有隔離標記的測試目錄啟動，並且不會被正式 Unity 遊戲納入編譯。

測試專案設定獨立 company/product 與 `coop-fixture-save.json` 路徑。正式場景、正式帳號與玩家存檔不作為測試輸入。正式 Render/Supabase 服務未部署或寫入。

最終結果：**PASS，33 項檢查通過**。遊戲執行期間未收到 runtime error/exception。紀錄：`Builds/SecurityReview/security-playmode-result.txt`、`security-live.txt`、`unity-security.log`。`source-hashes.json` 確認四個正式程式檔與測試入口，均與實測副本 SHA-256 完全相同。

| 實測情境 | 結果 |
| --- | --- |
| 正常客戶端加入，接收 welcome 與世界快照 | 通過 |
| 以每秒 30 次輸入，透過房主物理模擬移動遠端角色 | 通過，實際位置改變 |
| 合法裝備指令與升級請求 | 通過，相容且未被誤踢；未完整覆蓋所有商店交易與升級效果 |
| JSON 損壞、尚未加入即送操作、偽造 state/mesh、重複 hello | 通過，移除異常玩家，正常玩家仍在線 |
| 超範圍移動、非有限／溢位數值、未知按鍵、未知指令、非法升級索引 | 通過，移除異常玩家 |
| 錯誤房間密碼 | 通過，保留拒絕訊息，不產生角色 |
| 超大壓縮長度、不足 8 KiB 壓縮內容展開超過 4 KiB | 通過，TCP 拒絕連線；長度超限不必先傳送內文 |
| 大量輸入後仍由正常玩家送出有效操作 | 通過，異常玩家被移除，正常玩家的新輸入仍套用到角色 |
| TCP 暫停消費接收佇列，送入至少五筆 1 MiB 文字 | 通過，字串內容預算維持在 8 MiB 以內並關閉過載連線 |
| 本機 WebSocket 模擬超大解壓資料、錯誤 UTF-8、同一玩家大量待處理訊息 | 通過，只踢出 peer 1，peer 2 的資料仍送達，房主未發生 fatal |
| TCP 與 WebSocket 正常接受超過 4 KiB 的房主資料 | 通過，未把操作封包限制套用到大型世界資料 |
| WebSocket 暫停消費接收佇列並送入多筆 1 MiB 文字 | 通過，字串內容預算維持在 8 MiB 以內並安全結束過載連線 |

第一次無圖形模式已進入 Play Mode，但在 Unity 原生 `GfxDevice::DrawSharedGeometryJobs` 相機渲染路徑崩潰，該次不計為通過；最終以 `-batchmode -force-d3d11` 完成實測。初次正常握手亦揭露 Unity `JsonUtility` 會將 null 欄位展開為預設物件；已修正封包種類檢查，並由最終正常加入測試確認相容性。

隔離編輯器啟動仍出現 `UnityEditor.Search.SearchDatabase` 的 `ArgumentOutOfRangeException`。測試程式將此編輯器搜尋索引診斷獨立寫入 `security-editor-diagnostics.txt`，不納入遊戲 runtime error 計數；**不宣稱整份 Unity log 零錯誤**。這次沒有修改 Unity 搜尋索引系統。

重跑時先啟動 fixture，再執行 Unity 的 `-batchmode -force-d3d11 -projectPath .../Builds/SecurityReview/Project -executeMethod CoopSecurityReview.Run -logFile .../Builds/SecurityReview/unity-security.log`。fixture 啟動命令為 `dotnet run --project Assets/Script/Multiplayer/Verification~/SecurityRelayFixture/SecurityRelayFixture.csproj -- <專案根目錄>/Builds/SecurityReview`。測試入口會自動刪除本次會使用的舊結果與 `relay-kick-*.txt` 證據檔。測試專案的套件使用本機已存在的快取；正式專案的套件設定未更動。最終已關閉本機測試 fixture 與隔離 Unity。

## 本次邊界與後續項目

- 這是多人連線入口的第一輪補強，不是完整滲透測試或零漏洞保證。
- 正式網際網路服務、跨機延遲／丟包、四名玩家長時間遊玩及發布執行檔需另行驗證。本機 WebSocket fixture 未驗證正式 TLS、正式登入或 Supabase 資料庫權限。
- 120 次／秒與 64 筆待處理操作的門檻已驗證正常 30 Hz 操作，但仍需在高延遲與長時間卡頓情況校準，避免把嚴重延遲的正常玩家視為過載來源。
- 目前仍採房主權威；惡意房主發送的世界快照／網格內容需要另一輪更完整的資料驗證。這次保留既有大型快照格式。
- LAN 仍使用未加密 TCP；房間密碼不等於傳輸加密。若需防範同網段竊聽，應另行設計具身分驗證的加密傳輸。
- 本機存檔的 SHA-256 checksum 可檢查毀損，無法阻止有本機寫入權限的玩家重算內容。若未來加入交易或排行榜，可信進度與經濟數值需在服務端驗證。
- 帳號傳輸現有 HTTPS、禁止重新導向及記憶體內憑證保護，本次未修改帳號流程。Supabase 正式設定、寄信速率限制、MFA、實際權限與管理者帳號仍需透過部署環境另行稽核。

## 參考

- [Unity：客戶端 RPC 需驗證與服務端權威原則](https://docs-multiplayer.unity3d.com/netcode/2.0.0/learn/dealing-with-latency/)
- [Unity CVE-2025-59489 官方公告](https://unity.com/security/sept-2025-01)：公告列出的 6000.3 修補起點為 6000.3.0b4，本專案版本為 6000.3.10f1。這僅確認該公告的版本門檻，不能代表所有引擎或套件安全問題均已排除；本次沒有更新 Unity 或套件。
