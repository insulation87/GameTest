# 怪物武器掉落率減半

- 最弱怪物：35% → 17.5%。
- 中等強度怪物：21.5% → 10.75%。
- 最強怪物：8% → 4%。
- 沿用原有強度線性插值，所以中間所有強度的機率皆為原本的一半。
- 遊戲程式只修改 `NPC/EnemyHealth.cs` 的兩個預設機率。正式 `Assets/NPC/Enemy.prefab` 未序列化覆寫這兩個欄位；實測確認會採用新值。

## Play Mode 實測

2026-10-15，Unity 6000.3.10f1，獨立暫存專案、空白測試場景、batchmode/nographics。複製目前正式程式與真正的怪物 prefab，透過 `TakeDamage(maxHealth)` 觸發死亡與武器生成；其他掉落在抽樣時關閉以隔離武器亂數。

18 項檢查全部通過，包含 3,009 次真實怪物死亡：

| 怪物強度 | 新設定 | 1,000 次死亡的武器數量 |
| --- | --- | --- |
| 0，最弱 | 17.5% | 176 |
| 0.5，中等 | 10.75% | 109 |
| 1，最強 | 4% | 36 |

各組均使用相同的 1,000 個固定種子；每次實際生成結果皆符合該次亂數的預期。另各測試三種強度在新門檻略下方會掉落、略上方不掉落，以及舊門檻上半段不再掉落，且沒有重複武器。

確認正式 prefab 的生命、防具、進度道具預設機率仍為 10%、25%、1%。測試期間沒有未預期的遊戲執行錯誤。Unity 搜尋索引的 Editor 啟動錯誤不納入遊戲錯誤判定。

使用者編輯中的場景與遊戲存檔未用於測試。尚未驗證完整地圖遊玩、多人連線及打包執行檔；本次未重新匯出遊戲。

## 重現與證據

- 測試執行器：`WeaponDropChanceReview.cs.txt`，複製至獨立暫存專案的 `Assets/Editor/WeaponDropChanceReview.cs`。
- Unity 參數：`-batchmode -nographics -projectPath <獨立暫存專案> -executeMethod WeaponDropChanceReview.Run -logFile <紀錄路徑>`。
- 檢查結果：`weapon-drop-checks-20261015.txt`。
- Unity 完整紀錄：`weapon-drop-unity-20261015.log`。
- 已比對正式與測試副本的 `EnemyHealth.cs` SHA256，內容相同。
