# TASK 1：Taiwan50 歷史資料調查與修正

分支 `fix/tse50-history`，基於 `origin/main` 的 `1651b52`。本次不實作其他 Task，不合併 main、不推送。現有 `.idea/` 不變。

## 根因與實際請求

2026-10-01 Yahoo 官方搜尋確認 `^TSE50` 為 `FTSE TWSE Taiwan 50 Index`、INDEX、TAI；沒有改成 0050、美元版或不同的 capped index。原始公開回應保存於 [tse50-history-evidence.json](tse50-history-evidence.json)。

- `query1.finance.yahoo.com/v8/finance/chart/%5ETSE50`：`interval=1d` 搭配 `range=1mo / 3mo / 1y / max` 全部 HTTP 200，但原始 timestamp 和 OHLCV arrays 都只有 1 筆。`interval=1wk&range=3mo` 同樣 1 筆。
- 使用 `period1 / period2` 的明確日期範圍，也只回 1 筆。追加 `includePrePost=true&events=history`、改為台北午夜邊界仍為 1 筆。
- `query2.finance.yahoo.com` 的同樣月日價查詢也是 HTTP 200 / 1 筆，換 Yahoo host 不能解決。
- Yahoo meta 的 `validRanges` 只列 `1d / 5d`，`firstTradeDate` 缺失；即使接受較長 range 仍未提供對應歷史。對照 `^TWII` 的月日價為 HTTP 200 / 21 筆。
- 現有解析逐一映射 timestamp，Unix seconds 轉 Asia/Taipei 日期；只排除日期範圍外及缺數值的 bar，沒有使用 trading-day calendar 過濾歷史。新增測試確認跨 UTC / Taipei 日期、null bar 和範圍排除行為。這次稀疏資料在解析前就只有 1 筆，並非程式丟掉 20 筆。

因此本次實測根因是 Yahoo 對此特定指數未提供足夠日歷史，不是 ticker、range 語法或 trading-day filtering 問題；這不代表 Yahoo 所有商品都不能取得歷史。

## 最小 fallback 設計

仍先使用 Yahoo。只有 market adapter 處理 `^TSE50` 多日查詢時，Yahoo 少於 2 根有效日價或發生 upstream error，才嘗試 TWSE 官方 `https://www.twse.com.tw/indicesReport/TAI50I?date=yyyyMMdd&response=json`。這是來源辨識，沒有將 00631L 寫死在 Runner / Analyst，也不更改 Product → Reference mapping。

該官方頁名稱為「臺灣50指數歷史資料」，欄位為「日期 / 臺灣50指數 / 臺灣50報酬指數」。只讀取第 2 欄的**價格指數**，不能誤用第 3 欄報酬指數。逐月取得、檢查 report identity / fields / ROC 日期 / 月份 / 正值 / 重複日期，依原請求區間過濾並排序。9 月回 20 筆，10 月回 1 筆；各交易日由來源提供，沒有補週末、節假日或插值。

整份歷史選擇 TWSE 的同一序列，不混接 Yahoo bar 或 ETF proxy。只有 TWSE 至少 2 筆且不比已取得 Yahoo 更少時才採用；若來源錯誤或不足，保留真正取得的 Yahoo sparse history，明確標記 INSUFFICIENT，保留 source attempts 的 HTTP status / safe error。單一 Reference 失敗仍不阻斷其他 Provider。

TWSE 只提供 daily close；`HistoricalPrice` 的 Open / High / Low / Volume 現在允許 null，既有 Yahoo Target 仍提供原來的 OHLCV，沒有 DB schema 改動。TWSE 欄位保持 null，不能用 close 填造 open/high/low，也不能用 0 假裝官方成交量。

新增 `MarketReferenceContext.historyMetadata`，包含 source、sourceSymbol、isFallback、dataQuality、reason、from/through、fetchedAt、attempts（各來源 count / error）。本次是 TWSE / TAI50I / CLOSE_ONLY；Reference 整體 AVAILABLE 表示有 quote 和可用收盤歷史，不表示有完整 OHLCV。若最新日線尚未發布或 quote 日期不符，仍標為 PARTIAL。

沿用 request / batch cache，包含 official 月報與失敗 outcome 的 reuse；不同 Force request 重新取得資料。新 metadata 和實際 close/null 值自然包含在既有 InputSnapshotJson 的 marketReferences / analysisInput，不另建平行分析流程。Skill 說明來源差異、CLOSE_ONLY 限制與 null 語義，schema 不變。

## 真實 End-to-End 驗證

使用既有 User Secrets，啟動本機 MySQL / LiveAI Backend :5080，實際 POST `/api/products/00631L/analysis/force`，Snapshot ID **18**、Target price **39.62**。

UNDERLYING mapping 仍為 `^TSE50` / Taiwan50；最新 quote 仍來自 Yahoo，歷史採用 TWSE TAI50I **21 筆**，2026-09-01 close=43491.42 至 2026-10-01 close=45041.66，quality=CLOSE_ONLY。BROAD_MARKET `^TWII` 仍使用 Yahoo。

| Provider | Actual model | Analysis ID | Action / Confidence | Input / Output / Cached tokens |
| --- | --- | ---: | --- | --- |
| GPT | gpt-6.1-sol | 69 | HOLD / 60 | 9802 / 1119 / 0 |
| DeepSeek | deepseek-v4-pro | 68 | HOLD / 55 | 10941 / 4998 / 0 |
| Claude | mock-v1 | 70 | HOLD / 60（Mock） | 無 |

DB 直接查詢確認：兩個真實 Provider 的 `analysisInput` JSON 完全相同，各有 21 筆 Taiwan50 close 值與 source / fallback / quality metadata；AIAnalysisResults 和 AIAnalysisUsage 已 INSERT。AIProviderFailures 維持原有 2 筆，本輪新增 0。

GPT 理由引用「TWSE 收盤歷史 9/15 42396.99 → 10/1 45041.66」；DeepSeek 理由引用指數突破 9/23 收盤高點，並指出 CLOSE_ONLY 的限制。原始模型文字保存於既有 DB，不將模型敘述當作已通過事實驗證。

## 驗證與限制

- Backend build：0 warnings / 0 errors。
- 全套 MySQL tests：93 passed / 0 failed / 0 skipped，包含原有 78 項與新增 15 項回歸測試。測試不呼叫付費 AI。
- Frontend npm run build 通過。本次未修改 Frontend 或對外分析結果 schema；Edge headless 實際登入既有 React :5173 商品頁，latest API HTTP 200，顯示分析 68 / 69 / 70，Usage 與 Force 回應一致，分析歷史可見，page errors=0。頁面驗證只有 GET，不觸發付費 AI。
- Yahoo 本身歷史不足並未被修好；官方 fallback 是明確的 source 選擇。若兩個來源都不可用，無法提供完整歷史，會保留限制而不偽造。
- 官方 CLOSE_ONLY 支援多日收盤趨勢，無法支持盤中波幅或成交量推論。Yahoo 最新報價與官方日收盤須區分，不保證盤中即有當日日線；缺當日日線會標 PARTIAL。
- 觸發判斷目前針對本 Task 的 0/1 bar 稀疏案例，沒有建立全市場的逐交易日完整性評分或自動選來源系統。
- 月報 identity / schema 改變時會拒絕資料並保存 safe error。Provider isolation、Claude Mock 和現有 DB 設計保留。

官方來源：[TWSE OpenAPI](https://openapi.twse.com.tw/)、[Taiwan50 月歷史](https://www.twse.com.tw/indicesReport/TAI50I?date=20260901&response=json)、[Yahoo Taiwan50 日價](https://query1.finance.yahoo.com/v8/finance/chart/%5ETSE50?interval=1d&range=1mo)。
