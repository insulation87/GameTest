# 永久草原／森林地圖實測紀錄

日期：2026-10-07。Unity：6000.3.10f1。Edit Mode 與兩組 Play Mode 測試結果：PASS。

在獨立完整 Unity 專案副本中執行，使用原 Assets、Packages 與 ProjectSettings，保留原資產及腳本 GUID。Map1 玩家存檔功能只在測試記憶體內停用；流程測試使用獨立 isolated-save.json，未接觸原專案帳號存檔或操作使用者正在編輯的場景。

Edit Mode 已驗證：新地形、材質與預先烘焙導航均存成正式資產，prefab 重新載入後沒有暫存 mesh/material 引用。Map1 儲存、重新開啟後三個戶外區域已存在並啟用；舊迷宮外殼、樓梯與屋頂已移除。原地圖 GUID、Map1 GUID、玩家、相機及生成器引用保留。原高樓層角色移至對應戶外區域。

Play Mode 第一組使用儲存好的地圖 prefab 與真正的遊戲元件，實測收集、引導、傳送、魔物追擊、掉落與存檔。第二組直接開啟原 Map1 副本，驗證原玩家落地、三區導航與 prefab 位移、相機引用、原 NPC prefab 生成，以及進入 Play Mode 後沒有重建地形。兩組均無未預期的遊戲錯誤。

已檢視 Unity 實際渲染預覽：[Map1 草原](OverworldReviewArtifacts/meadow-preview.png)、[Map1 森林](OverworldReviewArtifacts/forest-preview.png)、[森林魔物](OverworldReviewArtifacts/monster-preview.png)。使用原專案設定執行，沒有以簡化 Built-in 專案取代。

已部署至原專案：Map1.unity、既有 MedievalDungeon.prefab，以及 Dungeon/AuthoredWorld 中 137 個檔案。部署後與通過測試的來源逐檔比對 SHA-256，全部一致；原場景及 prefab 在覆寫前亦與隔離備份比對，確認測試期間沒有其他磁碟修改。

雙人連線與 Windows 建置已完成補測，見 [OverworldNetworkBuildReport.md](OverworldNetworkBuildReport.md)。尚未驗證外網連線、長時間遊玩效能、真實雲端登入與完整人工操作流程。

自動測試入口：OverworldBaker.Run、AuthoredMapSceneReview.Run；皆須在有 authored-world-review.json 標記的隔離專案以 batchmode 執行。

## 流程 Play Mode 檢查

```text
PASS
Saved outdoor map loads in Play Mode without runtime geometry conversion
Both region links have outward and return portals
Meadow and forest geometry is generated with hundreds of trees
Indoor dungeon mechanisms are absent from the outdoor world
Dungeon shell and stair geometry are removed from the saved map
Region 1 has live outdoor navigation
Region 1 has physical ground
Region 2 has live outdoor navigation
Region 2 has physical ground
Region 3 has live outdoor navigation
Region 3 has physical ground
Safe fixture placement at (150.00, 0.00, 154.00)
Player Rigidbody traverses open grass without the former maze walls
Safe fixture placement at (150.00, 0.05, 48.00)
Entering a locked portal does not skip region progression
Safe fixture placement at (150.00, 0.05, 48.00)
Portal guidance stays hidden before the collection objective is complete
Floor-one drop is collected through the real pickup component
The same progression pickup cannot count twice
Floor-one drop is collected through the real pickup component
The same progression pickup cannot count twice
Floor-one drop is collected through the real pickup component
The same progression pickup cannot count twice
Completing meadow collection unlocks and guides to the forest portal
Paused gameplay cannot activate a portal
Forest arrival is outside the return portal trigger
Waiting after arrival does not bounce the player between portals
Return travel preserves collection progress
Safe fixture placement at (570.00, 0.05, 200.00)
A meadow key cannot complete the forest objective
Forest completion guides to the ancient woodland portal
The player arrives alive on safe ancient forest ground
Cleared progression locks forward portals
Restored save progress immediately restores portal unlocks
Old floor-two and floor-three save coordinates migrate to the correct outdoor regions
Real NPC spawner creates a monster in region 1
Region 1 uses Goblin and the correct drop region
Monster appearance and live NavMeshAgent initialize
Safe fixture placement at (36.00, 0.05, 36.00)
Monster chase start has walkable terrain
Region 1 monster follows a real path toward the player on outdoor terrain
Defeating region 1 monster drops a progression key
Real NPC spawner creates a monster in region 2
Region 2 uses Orc and the correct drop region
Monster appearance and live NavMeshAgent initialize
Safe fixture placement at (456.00, 0.05, 36.00)
Monster chase start has walkable terrain
Region 2 monster follows a real path toward the player on outdoor terrain
Defeating region 2 monster drops a progression key
Real NPC spawner creates a monster in region 3
Region 3 uses ForestImp and the correct drop region
Monster appearance and live NavMeshAgent initialize
Safe fixture placement at (876.00, 0.05, 36.00)
Monster chase start has walkable terrain
Region 3 monster follows a real path toward the player on outdoor terrain
Defeating region 3 monster drops a progression key
Completing ancient forest collection spawns and guides to the boss
Minimap switches by outdoor region rather than floor height
Actual game save writes successfully to the isolated test save path: 進度已儲存 20:21:05
Actual game save can be read with checksum verification: 
Saved world layout and portal collection progress round-trip through JSON
Actual save restore moves an old floor-two save into the new forest
Terrain and monster surface shader compiles
No unexpected Unity gameplay errors: 

``` 

## 原 Map1 Play Mode 檢查

```text
PASS
Original Map1 player loads the saved outdoor prefab
Entering Play Mode does not create or replace terrain renderers
Former maze shell and stairs are absent from Map1
Original player saves remain disabled throughout this isolated test
Original player lands safely at the preserved meadow start
Original third-person and first-person camera references survive map replacement
Obsolete dungeon navigation remains disabled in Map1
Map1 region 1 bounds respect the preserved prefab offset
Map1 region 1 has navigation at the scene offset
Original player finds safe ground in region 1
Original player remains grounded in region 1
Map1 region 2 bounds respect the preserved prefab offset
Map1 region 2 has navigation at the scene offset
Original player finds safe ground in region 2
Original player remains grounded in region 2
Map1 region 3 bounds respect the preserved prefab offset
Map1 region 3 has navigation at the scene offset
Original player finds safe ground in region 3
Original player remains grounded in region 3
Original NPC spawner retains its prefab and player references
Original Map1 NPC prefab spawns on the authored navigation
Original Map1 monsters initialize their visual and live navigation
Previously placed upper-floor monsters relocate to the outdoor ground
No unexpected Map1 gameplay errors: 

``` 
