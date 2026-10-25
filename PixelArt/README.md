# 全遊戲 3D 像素風

保留原本第一／第三人稱與 3D 關卡玩法。Map1 的地圖材質已直接儲存為像素資產，在 Scene 與 prefab 編輯模式即可看到；角色與 UI 仍在 Play Mode 自動套用。遊戲相機預設使用視窗完整解析度，讓地圖輪廓更清楚。

- **玩家與第一人稱手臂**：本地程式建立的方塊模型，保留金髮、白襯衫、深藍服裝與青色領結。移動、跑步、跳躍、下蹲、滑行與死亡使用 12 fps 階梯式骨架動畫。
- **敵人與 Boss**：三種士兵配色，方塊頭盔、盔甲、劍盾與弩。Boss 有皇冠、肩刺、三階段配色和階梯形光環，沿用原有攻擊事件與轉階段邏輯。`EnemyCharacterVisual.modelPrefab` 仍可指定個別外觀。
- **槍械與道具**：七種槍械、醫療包、寶箱、掉落物與背包圖示共用方塊裝備幾何。子彈與弩箭也使用同一視覺方向。
- **Map1 場景與地板**：地圖共用的 9 個 `.mat` 已永久使用 `GameTest/PixelSurface`，石牆、地板、石塊、木材使用 `MapTextures` 內的 32×32 PNG、Point 取樣與有限色階。Scene、prefab 與 Play Mode 直接使用同一組資產。保留材質 GUID 與地圖幾何、碰撞、樓層、陷阱開洞及導航資料。已移除執行時場景材質掃描與替換；新增物件也會保留你指定的材質。
- **Unity Terrain**：Map1 使用 Mesh 地板。像素管理器不會自動附加 `PixelTerrainStyle`，也不會更換 TerrainData 或圖層；其他場景的 Terrain 使用編輯器內已儲存的設定。
- **UI**：主選單使用程式繪製的像素地牢插畫；按鈕使用方形底、階梯角框及即時狀態色，物品圖示與勝利皇冠使用像素幾何。繁體中文保留內附字型，以維持可讀性。

## 畫面解析度與像素大小

`PixelArtDirector` 會替遊戲相機建立 `PixelArtCamera`。預設 `nativeResolution = true`，1920×1080 視窗會使用 1920×1080 的世界畫面，保留方塊模型與材質風格。關閉 `nativeResolution` 可恢復較粗的像素呈現：`referenceHeight = 360` 時，同一視窗使用 640×360 的世界畫面與 3 倍點取樣放大；調低參考高度會得到更粗的像素。完整解析度會增加 GPU 渲染負擔。相機關閉時會釋放 RenderTexture，切換視角時由啟用的相機重建；暫停與視窗縮放也會更新輸出。

此管理器只處理相機與 UI，不再建立或替換任何場景材質。像素畫面放大與地圖資產編輯是獨立的；Map1 在 Edit Mode 已經使用完成的像素材質。

HUD 保持在畫面上層，準星瞄準仍使用原本的 `ViewportPointToRay`。像素畫面的 RawImage 不接收點擊。相機擁有的抗鋸齒、HDR 與後處理在像素呈現期間關閉；原有小地圖／預覽 RenderTexture 與 URP Overlay 相機不接管。

每個啟用中的像素相機會建立一個 `Pixel Screen Output` 子相機，直接輸出到對應螢幕，避免 Unity Game View 顯示「No cameras rendering」。此相機的剔除遮罩為 0，僅清除背景；像素世界與 UI 維持原有呈現順序。視角關閉時會同步移除它，管理器不會替這個輸出相機再次套用像素渲染。

遊戲場景有 `CameraSwitch` 時，像素系統僅接管它綁定的 FPS／TPS 相機，並依 `ActiveCamera` 呈現當前視角。Map1 內額外的固定 Main Camera 不會再產生覆蓋玩家視角的像素畫面。呈現選擇每幀更新，保留角色相機原有的移動、轉向與瞄準行為。

多人模式每台電腦只呈現一位本機玩家的 FPS／TPS 相機，使用完整畫面；同機雙人與左右分割畫面已移除。個人 HUD 使用 Screen Space Overlay，透過 `GameUIViewport` 保留完整範圍與縮放，不參與世界深度測試，避免近距離怪物或場景遮住 UI。連線房間中遠端玩家的相機不會建立像素輸出。切換視角時同步保留完整畫面。操作與連線說明見 `../Multiplayer/README.md`。

透明材質、裁切植被及特殊陷阱／特效 Shader 保留原有功能，仍經過整體像素畫面呈現。Map1 的原始高解析度貼圖保留，像素 PNG 可直接編輯；地圖材質本身已更新，無須先執行遊戲。

## 舒適地圖配色

地圖的 9 個材質已套用暖灰石牆、冷灰地板、淺色拱門與階梯、深灰天花板、棕色木材及柔和金屬色；紅旗與橙色火焰保留辨識度。降低貼圖飽和度、磚縫對比與隨機雜點，並提高暗部可讀性，使牆面和地板較容易區分。角色共用的 Shader 預設值保持原樣，這些調整儲存在地圖材質中。

配色設定位於 `Resources/MapComfortPalette.json`。Unity 選單 **Tools → Pixel Art → Apply Comfortable Map Palette** 可在編輯模式重新套用這組配色；它會覆寫該地圖 9 個材質的配色參數，支援 Undo，保留材質 GUID、貼圖參照與地圖幾何。共享這些材質的地牢 prefab 和示範場景也會顯示新配色。

## 編輯地圖材質

目前地圖的材質位於 `Assets/MedievalDungeon/Generated_20260914_214258/Materials`，可直接在 Inspector 調整 `Palette Color`、`Texture Contrast`、`Texture Saturation`、`Shadow Readability`、`Mortar Contrast`、`Surface Grain` 與 `Palette Steps`。`Texture Grid` 和 `Stone Pattern` 控制原有像素貼圖與磚紋。像素貼圖位於 `Assets/Script/PixelArt/MapTextures`。修改後直接儲存即可。

Unity 選單 **Tools → Pixel Art → Bake Map Materials** 可將 Map1 依賴的新地牢材質永久轉換。它只處理該地圖引用的 MedievalDungeon 材質，保留 GUID，並儲存 32×32 貼圖。重複執行會略過已完成的像素材質，保留後續手動編輯。共享這些材質的地圖 prefab 和示範場景也會同步顯示像素外觀。

## 預覽與驗證

最新配色的實際地圖渲染見 [MapComfort.png](Previews/MapComfort.png)。`MapComfortReview.Run` 供獨立測試專案使用，會建立並儲存測試 Map1 場景、套用配色並重新匯入材質，驗證 Shader、9 個材質的 GUID 與貼圖參照，再渲染修改前後的實際地牢 prefab。`CameraClarityReview.Run` 也只供獨立測試專案使用；已通過 16 項檢查，涵蓋完整解析度、第一／第三人稱切換、暫停時重建、粗像素模式、輸出清理與外部 RenderTexture 保留。

Unity 選單 **Tools → Pixel Art → Validate and Render Previews** 會建立獨立預覽場景，檢查材質、模型幾何、動畫與弩箭發射點，並將實際 Unity 渲染的 `Lineup.png` 和 `Menu.png` 寫入本資料夾的 `Previews`。

`PixelMapReview.Run` 在 Edit Mode 轉換並重新匯入地圖材質，檢查保存結果、GUID、貼圖取樣與重複執行，並從實際地圖 prefab 渲染 `Previews/MapEditMode.png`；不啟動遊戲或執行時材質轉換。它會儲存 Map1 使用的地牢材質。

`PixelArtPlayReview.Run` 是獨立 Play Mode 整合檢查入口，供 Unity batchmode 使用。它建立空白測試場景，驗證視角切換、暫停時輸出、UI 點擊隔離、動態敵人、Boss 轉階段，以及已儲存的 Map1 材質、手動指定的場景材質與 Terrain 資料不被執行時替換。勿在未儲存的工作場景直接執行此入口。

`PixelArtGameplayCameraReview.Run` 使用實際 `Assets/Player/Player.prefab` 的相機與控制器參照，搭配固定 Main Camera 重現多相機情況。它在獨立場景隔離戰鬥與存檔元件，檢查預設第一人稱、移動跟隨、轉向、雙向視角切換、暫停與唯一像素畫面輸出；跟隨測試保留原有平滑與碰撞設定。供 batchmode 使用，勿在未儲存的工作場景直接執行。

實際地牢中的完整戰鬥、裝備手部對位與各解析度 UI 仍需在主專案實機試玩檢查。
