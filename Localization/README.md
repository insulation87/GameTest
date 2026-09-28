# 繁體中文介面

遊戲介面使用繁體中文；鍵盤提示保留 E、Esc、WASD、Shift 等實際按鍵名稱。
新增 Unity UI `Text` 時，請使用 `GameUIFont.Default`，避免使用缺少中文字元的內建字型。

`Resources/Fonts/NotoSansCJKtc-Regular.otf` 是隨遊戲打包的 Noto Sans CJK TC 字型，
不依賴玩家電腦上的系統字型。主選單場景直接參照同一個字型資產。

- 字型來源：https://github.com/notofonts/noto-cjk/blob/main/Sans/OTF/TraditionalChinese/NotoSansCJKtc-Regular.otf
- 授權：SIL Open Font License 1.1，全文見 `Resources/Fonts/OFL.txt`。
- 字型的原始版權資訊保留於字型檔內。

介面檢查：主選單及解析度選項、玩家與首領等級、背包與快捷欄、商店、
全部 25 個武器組合、升級選項、死亡倒數及小地圖樓層。
武器組合圖鑑的捲動範圍依文字高度計算，確保最後一項可完整顯示。
