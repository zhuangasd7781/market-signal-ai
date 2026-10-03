# WTX& 盤中市場參考

00631L 的 Mapping 透過既有 CRUD API 設定，並非 Seed 或 Analyst hardcode：
- UNDERLYING → ^TSE50（臺灣50；維持原設定）
- BROAD_MARKET → ^TWII（TAIEX；維持原設定）
- BROAD_MARKET → WTX&（台指期近一；新增）

Yahoo Taiwan 官方頁：https://tw.stock.yahoo.com/quote/WTX%26
此代碼不是 global Yahoo Chart API 支援的代碼（實測 HTTP 404）。台灣頁面自動更新所使用的 JSON service 實測 HTTP 200：
`https://tw.stock.yahoo.com/_td-stock/api/resource/StockServices.stockList;symbols=WTX%26;autoRefresh={unixMillis}?returnMeta=true`

既有 Yahoo adapter 將含 & 的本地 ticker 交由 YahooTaiwanIntradayQuote 讀取，不爬 HTML、不替換成每日報表。ReferenceDataCache 仍負責同 request/batch reuse，Provider 不取得行情。
每次分析取得最新可用 quote；這不是前端每秒推播訂閱。Yahoo 官方頁目前約每 60 秒重新請求，來源 exchangeDataDelayedBy 實測為 0，但不是零延遲 SLA。

snapshot.quoteMetadata 保存 Source、SourceSymbol、MarketStatus、PriceUnit（INDEX_POINTS）、VolumeUnit（CONTRACTS）、DelayMinutes、DataQuality 與 InstrumentDescription。snapshot 保留原始成交時間及擷取時間，所有資料進入兩個 Provider 相同 analysisInput 與 InputSnapshotJson。CLOSE 正規化為 CLOSED；休市 quote 標示 LATEST_CLOSED_QUOTE，OPEN 的舊 quote 標示 STALE。夜盤可能跨日，不強制套用 ETF 交易日期。

此資料是近月連續期貨，非臺灣50指數。來源未提供實際合約月份，不自行推測；來源未經驗證的日線歷史留空，HistoryMetadata=INSUFFICIENT，Reference Status=PARTIAL。行情失敗沿用 Reference isolation，不用收盤報表或 Mock 補值。來源 JSON service 非正式承諾的穩定 API，若 Yahoo 改版需維護。

2026-10-03 休市實測：價格 49,346，前一收盤 48,669，漲跌 +677（約 +1.3910%），成交量 32,209 口，行情時間 2026-10-03 04:59:58 Asia/Taipei，來源標示延遲 0 分鐘。無法在休市驗證持續更新的新成交。

驗證使用真實 Yahoo target + 三個 references，分析階段使用隔離記憶體測試替身，確認相同 normalized context、兩份相同 analysisInput 與 snapshot metadata。沒有呼叫付費大模型，沒有往 UAT 寫入測試 AI 結果。UAT 僅透過 API 新增 requested instrument / mapping，不需 schema migration。
