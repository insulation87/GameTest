# 選圖與三關地圖實測紀錄

日期：2026-10-23。Unity 6000.3.10f1，獨立專案 `Builds/MapSelectionReview/Project`，產品名稱 `MapSelectionReview20261023`。實際進入 Play Mode，以獨立遊客存檔操作 Login、MainMenu、TradingHub 與 Map1–Map4，沒有操作原專案的編輯中場景或正式遊戲存檔。

- `map-play-results.txt`：166 項檢查通過。實際 UI 選圖及取消、合作選圖 UI、營地目的地、四張地圖十二關的地面碰撞與導航、指定關卡敵人生成與等級、實際受傷與難度遞增、鎖定／解鎖傳送門、第三關存檔續玩、三階段首領與四張地圖通關、跨圖保留金幣與重置裝備、營地更換地圖。另驗證雪地首領第二階段讀檔後生命倍率只套用一次。
- `trading-hub-result.txt`：117 項交易回歸檢查通過。包含實際移動互動、三種商人、購買與解鎖、金幣持久化、同輪背包恢復、新遊戲裝備重設、背包容量、檔案鎖定失敗回復、備份復原與損毀存檔保護。
- 兩組測試皆未捕捉到執行階段 Error／Exception／Assert。既有 Map1 的雙 Audio Listener 警告仍會出現；匯入時的舊 Visual Scripting 型別警告與執行階段檢查分開記錄。
- `map-authoring-result.txt`：三張新地圖各有三個已保存的區域，網格、材質與導航資產已保存。地圖載入時不重新建圖。
- `map-selection.png`、`world-Map1.png` 至 `world-Map4.png`：本次 Play Mode 擷取並已檢視的選圖介面與四種地景。
- `unity.log`、`trading-regression.log`：本次完整 Unity 紀錄。`tested-files-sha256.csv` 核對遊戲腳本、場景與新地圖資產和獨立受測專案的內容相同。

驗證使用真實執行階段系統；為可重複檢查十二關，測試透過 Player.Teleport 移動到各關、指定房間生成敵人、呼叫 Collect 完成鑰石需求，以及對首領施加致命傷。這些是測試操作，不代表已以一般玩家操作完整打過十二關。未驗證正式打包執行檔、兩台客戶端的多人連線、長時間平衡與效能，以及所有角色／武器組合。
