# 地城 UI 與封面

視覺方向：動漫地城戰術冒險。深墨藍面板、古金細框、米白文字、青綠護甲／控制元件。
角色與封面沿用既有金髮主角、槍械、中世紀騎士與王冠 Boss 的辨識元素。

## 使用

- 按 Play 預設從獨立 `Assets/Scenes/Login.unity` 開始，登入成功後進入 `MainMenu`，會自動套用封面與選單配置，無須重建場景。
- 開始遊戲／開始新遊戲會先開啟 10 人角色選單；繼續遊戲沿用存檔角色。專屬技能用 R 施放，右下角顯示冷卻，詳見 [角色與技能](../Player/Characters.md)。選單預覽：`Previews/CharacterSelection.png`。
- 正式名稱尚未指定，因此保留場景原本的「遊戲測試」標題；可直接修改 Main Panel 的標題 Text。
- `GameUITheme.cs` 集中管理配色、按鈕互動、框線與縮放。
- `MainMenuPresentation.cs` 管理主選單版面及封面載入。
- 場景切換共用載入動畫：旋轉符文、進度條、載入階段、淡入淡出及操作提示。登入、選角出發、暫停與通關返回均已接上，詳見 [載入畫面](LoadingScreen.md)。
- 主選單及 Esc 的設定共用 `GameSettingsMenu`，可調整視窗、解析度、滑鼠、視野、音量與 FPS；詳見 [遊戲設定](GameSettings.md)。
- `Resources/UI/DungeonCover.png` 是實際使用的橫幅封面原圖，亦可作為宣傳主視覺。
- 主選單、登入／註冊／驗證／重設密碼、設定、暫停、商店、武器圖鑑、背包、升級、重生、快捷欄、玩家與敵人血條、小地圖已接上共同樣式。
- 武器品質保留顏色區別並改用深色底與品質色框線；目前裝備使用金色底。
- 1920 × 1080 為 UI 設計尺寸，Expand 縮放保留整個設計區域，封面依畫面比例等比裁切。

## Boss 通關畫面

- 擊殺 Boss 最終（第三）形態時，自動建立 `DungeonVictoryUI`，不用掛元件或修改場景。一般敵人死亡與 Boss 轉階段不會顯示。
- 使用共用深墨藍底、古金框線、向量王冠、米白繁中文字與不受暫停影響的淡入動畫，顯示討伐結果、角色等級、持有金幣及存檔狀態。
- 顯示時暫停戰鬥、解鎖游標，阻擋背包／暫停快捷鍵、射擊、首領測試快捷鍵及同幀殘留傷害。
- 「繼續探索」恢復原本時間狀態，可撿取 Boss 掉落物；若原本有未選完的升級，會繼續停留在升級選單。
- 通關時及「返回主選單」時會嘗試儲存進度；儲存失敗會提示，再次點擊返回才會不儲存離開。`MainMenu` 需保留在 Build Profiles 的場景清單中。
- 讀取已通關存檔後可繼續探索，不重播通關畫面；使用首領測試快捷鍵重新擊敗 Boss 時可以再次顯示。
- 測試方式：按住 Shift + Ctrl，在 1 秒內按兩次 O 前往 Boss，擊破三條血後確認通關介面，再分別測試繼續探索與返回主選單。
- 驗證：Unity 6000.3.10f1 組件參考編譯通過；隔離 Unity Play Mode 通過 17 項檢查，涵蓋一般敵人／三階段判定、重複觸發、暫停恢復、升級暫停共存、游標、殘留傷害、通關存檔與存檔失敗離場流程。未在原地城進行完整實戰回歸。
- `Previews/Victory.png` 為 Unity 渲染預覽（使用測試等級與金幣）；另已檢查 1440 × 1080 與 2560 × 1080 版面無裁切。

## 驗證與預覽

- Unity 6000.3.10f1 相同組件參考的 C# 編譯通過。
- 獨立 Unity 專案已渲染主選單（有／無存檔、16:9／4:3）、設定、登入、重設密碼、背包、暫停、商店、遊戲內設定與升級面板。
- Play Mode 已測試背包的 E 鍵所呼叫的開關流程、物品圖示、武器詳細數值、裝備第 2 格、出售並增加金幣、右鍵使用血包、再次開啟保留物品與裝備。測試直接呼叫開關方法，沒有模擬實體鍵盤事件。
- 已修正裝飾框線缺少 CanvasRenderer 導致背包 UI 顯示例外；框線本身不攔截滑鼠事件。
- 若更新程式時正在 Play Mode，請停止後重新 Play，讓執行期間建立的介面重新生成。
- `Previews/MainMenu.png`、`Inventory.png`、`Account.png`、`Shop.png` 為 Unity 渲染預覽。帳號頁使用未配置的測試服務，預覽文字不代表正式專案帳號設定。
- 未在原本地城場景中進行完整戰鬥回歸，也未連線操作實際帳號。

## 封面製作

使用內建 image_gen 工具，以 `Player/CharacterArt/Preview.png` 與 `NPC/CharacterArt/Lineup.png` 作為參考。
封面為宣傳插畫，非遊戲實機截圖；標題和互動按鈕由 Unity 繪製，沒有烘焙進圖片。

最終提示詞：

> Use case: stylized-concept. Asset type: finished landscape 16:9 key art for a Unity anime dungeon shooter main menu and cover, 2048x1152. Reference image 1 is the actual protagonist: preserve her straight golden blonde hair with bangs, small crossed hairpin, amber eyes, white short sleeve shirt, navy vest, cyan bow, opaque navy athletic shorts and dark knee socks. Reference image 2 shows actual medieval enemies: closed steel helmets, simple plate armor and colored tabards; use the crowned dark steel red-tabard boss as a looming distant silhouette. Create polished anime game promotional illustration with painterly cinematic medieval stone arches, ruined dungeon, atmospheric teal mist and small warm golden embers. Protagonist at right third in composed adventurous stance holding a modern dark rifle safely angled down, waist-to-knees visible, nonsexual framing. Strong elegant silhouette and facial detail, rich material rendering, warm golden rimlight and teal reflected light. Left 45 percent intentionally dark uncluttered navy stone and fog, suitable for live title and vertical menu controls; preserve generous negative space. Palette midnight ink blue, antique gold, subtle turquoise, warm ivory. Mood mysterious adventurous and dignified. No text, no letters, no logos, no UI, no watermark. This is key art illustration, not a gameplay screenshot.
