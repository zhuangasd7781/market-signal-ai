# 使用者介面繁體中文

使用者可見的導覽、欄位、設定、提示與錯誤採台灣繁體中文。首頁、商品頁、分析歷史、AI 設定、分析排程、Prompt 設定與共用市場標的均已整理。品牌、Model ID、Symbol、API、JSON、Prompt、Token 與使用者保存的 Prompt 原文保留。

`displayText.ts` 提供顯示文字映射。交易動作顯示加碼／持有／減碼／出場；市場參考類型顯示追蹤標的／大盤／產業。內部 enum、TypeScript property、API payload、DB 與 JSON Schema 不翻譯。中文化本身沒有 Backend contract 或 migration 變更；同分支 Provider visibility 功能另見 ai-provider-visibility.md。

`analysisStatus.ts` 集中管理五項分析狀態的欄位、文字與語意色。`AnalysisStatusGrid` 共用於商品卡片與歷史詳細：正向紅色、負向綠色、中性灰色、警示／部分資料橘色、未知值淺灰色。Risk/Reward 顯示偏有利／偏不利／中性／無法判定，代表風險報酬評估，不是交易指令。Badge 使用 span、固定游標與可讀文字，不具有按鈕行為；Action 配色與分析邏輯不變。

Root Event 在商品卡片僅於 ACTIVE／EXPECTED、方向已知且摘要非空時顯示為「關鍵事件」。未確認／未知／失效事件不置於卡片重點；歷史詳細以低強度提示未提供有效事件。原始資料仍保留。

驗證：Frontend production build 通過；Edge Playwright 完整 18 項測試通過，包含所有設定與 Reference CRUD、顯示／隱藏、五項狀態 enum、未知／新增值、文字對比達 4.5:1、Root Event 顯示與 API payload 值不變。實際 DEV／UAT MySQL 頁面於 1280／390 px、深淺色模式檢查：無 layout overflow、page error、console error；檢查期間 API mutation 為 0，沒有觸發付費分析。測試 CRUD 使用隔離 Memory backend，開發網址仍為 5173／5080。
