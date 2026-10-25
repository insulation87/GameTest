# 草原與森林半開放世界

`Assets/Scenes/Map1.unity` 已直接改用儲存好的草原／森林地圖。原本的 `MedievalDungeon.prefab` 保留資產 GUID，內容已替換：不需要進入遊戲，Scene 視窗與 Prefab 模式就能看到新地圖。舊迷宮牆壁、樓層、屋頂與樓梯幾何已移除。

| 區域 | 地形 | 一般魔物 | 收集目標 |
| --- | --- | --- | --- |
| 1 風語草原 | 開闊草地、土路、稀疏樹木、花叢 | 哥布林 | 10 個本區鑰匙，解鎖森林傳送門 |
| 2 翠葉森林 | 較密樹林、林間道路與岩石 | 獸人 | 20 個本區鑰匙，解鎖古林傳送門 |
| 3 暮光古林 | 深色森林、魔法花叢 | 森林魔物 | 9 個本區鑰匙，出現原三階段首領 |

每區約 320 × 320 公尺，位於同一地面高度，以可往返的傳送門連接。區域內可自由探索草地與森林，外圍丘陵構成邊界。玩家、NPC 生成器與原有相機引用保留；原本高樓層的已放置角色已移至對應地面區域。

地形 Mesh、材質、預先烘焙的 NavMesh 存於 `Dungeon/AuthoredWorld`。`OverworldDirector` 在 Play Mode 只登錄已儲存的導航與區域資訊，不生成地形。Map1 的舊迷宮 NavMeshSurface 已停用。

蒐集完本區掉落物後，HUD 顯示下一區傳送門方向與距離，小地圖顯示目標。未解鎖、暫停或死亡時不能前往下一區；返回前區保留進度。魔物由原 NPCSpawner 生成，使用哥布林／獸人／森林魔物外觀並掉落本區鑰匙。

第一區魔物不再掉落背包中的「第1層道具」，由草原試煉保證通往第二區的鑰石進度。第二、三區一般魔物的本區鑰匙掉落率為 1%，生成器不再覆寫成必掉。既有共用鑰石進度保留，傳送門依目前的 10／20 門檻判定。

## 怪物難度與生成

一般生成器開場每批 3 隻，玩家進入房間 0.15 秒後開始生成，前 6 隻分兩批、間隔 0.15 秒。後續起始間隔為 0.35 秒；怪物出現在房間標記周圍 12 公尺內，仍保持與玩家至少 5 公尺、彼此至少 1.5 公尺的出生距離。

| 有效遊玩時間 | 每批數量 | 生成間隔 | 一般生成器存活上限 |
| --- | --- | --- | --- |
| 開始 | 3 | 0.35 秒 | 150 |
| 90 秒 | 4 | 約 0.152 秒 | 180 |
| 270 秒後 | 6 | 最快 0.1 秒 | 240 |

間隔持續縮短；每 45 秒上限增加 15 隻，每 90 秒每批增加 1 隻。暫停、單人選單與升級選擇不累積成長時間。實際生成仍受安全出生點與空間限制；每批會截斷至存活上限，非持續生成模式也會遵守房間總量。關閉時間成長後仍使用起始每批數量。

一般怪物隨機生命由 50～200 提高為 60～240。草原側襲試煉每波 5 隻、包圍試煉每波 6 隻，森林與古林事件每波 5 隻守衛；試煉敵人與試煉首領生命提高 20%，清場後下一波間隔由 3 秒縮為 1.5 秒。

調整入口為 `NPCSpawner` 的 `Initial Spawn Batch Size`、`Spawn Interval`、`Max NPC Count`、時間成長欄位與生命範圍；正式 Map1 使用 `Assets/Player/NPCSpawner.prefab` 的已儲存設定。

戰術冒險版增加草原側襲、包圍試煉與試煉首領，完成後保底補足本區鑰石。森林商人與古樹救援同樣保底提供進度；普通魔物的 1% 掉落作為額外加速。商人補給、古樹庇護與摧毀祭壇會影響後續區域或最終戰，詳見 [戰術冒險第一版](../Player/TacticalAdventure.md)。

`FloorProgressionManager.floorOneRequired`、`floorTwoRequired`、`floorThreeRequired` 可調整收集數量。`EnemyCharacterVisual.useFantasyMonsters` 控制一般敵人的魔物外觀；首領保留既有流程。

存檔保留原收集進度，使用 `worldLayoutVersion` 遷移舊第二、三樓位置到對應戶外區域。

隔離專案自動驗證入口：`OverworldBaker.Run`（儲存 prefab／Map1 並執行流程測試）、`AuthoredMapSceneReview.Run`（實際 Map1 Play Mode 測試）。批次入口要求隔離專案標記，避免誤改正在編輯的場景。測試結果見 [OverworldReviewReport.md](Editor/OverworldReviewReport.md)。
雙人連線與 Windows 64 位元成品驗證結果見 [OverworldNetworkBuildReport.md](Editor/OverworldNetworkBuildReport.md)。
