# Market Reference mapping 與真實驗證

2026-10-01 已完成 00631L → Yahoo Target + Taiwan50 / TAIEX → GPT / DeepSeek → MySQL → 既有 API / React。既有 schema、Action 與 Provider Failure isolation 保留；Reference 不產生任何獨立交易決策。

## Domain / DB

- `ReferenceInstrument` / `MarketReferenceInstruments`：Id、UserId、Symbol（完整 Yahoo ticker）、Name、Market。Instrument 與 Target Products 分開，因此指數不會變成追蹤商品或自動產生 Action。唯一鍵 `(UserId, Symbol)`。
- `ProductMarketReference` / `ProductMarketReferences`：Id、UserId、ProductId、ReferenceInstrumentId、ReferenceType。唯一鍵 `(UserId, ProductId, ReferenceType, ReferenceInstrumentId)`，同 Product 可有多個同類型 Reference，包括多個 SECTOR。
- 第一版類型是 UNDERLYING / BROAD_MARKET / SECTOR。複合外鍵 `(ReferenceInstrumentId, UserId)` 限制 mapping 只能引用自己擁有的 Instrument；所有讀寫都透過 ICurrentUser / UserId 限定。
- `004_market_references.sql` 只增兩張表，不修改 Products / MarketSnapshots / AIAnalysisResults。啟動會冪等建表；獨立建庫可在 001、002、003 後套用 004。
- 預設僅對既有 demo user 1 的 00631L 建立兩個 mapping，使用 SeedVersions `market-references-00631L-v1` 記錄首次設定。seed 與 marker 在同一交易；使用者刪除／修改後，重啟不補回或覆寫。其他商品與使用者沒有推測預設 Reference；Memory 模式從空 mapping 開始，可透過同一 CRUD API 自行設定。

## 最小 Backend CRUD

本機新版 API：http://127.0.0.1:5080，Swagger：http://127.0.0.1:5080/swagger/。所有 mutation 仍須 `X-Market-Signal: web`，不需要提供 AI Key。延續既有 demo 身份，尚未新增正式登入。

| 方法 | 路徑 | 用途／Body |
| --- | --- | --- |
| GET | `/api/market-reference-instruments` | 列出目前使用者的 Reference Instruments |
| POST | `/api/market-reference-instruments` | 建立，`{ "symbol": "^TWII", "name": "TAIEX", "market": "TW" }` |
| PUT | `/api/market-reference-instruments/{instrumentId}` | 修改，Body 同建立；會更新該使用者引用此 Instrument 的 mapping metadata |
| DELETE | `/api/market-reference-instruments/{instrumentId}` | 移除，須先解除引用它的所有 mappings |
| GET | `/api/products/{symbol}/references?market=TW` | 查看該商品目前 mapping，包含 Instrument 資訊 |
| POST | `/api/products/{symbol}/references?market=TW` | 新增，`{ "instrumentId": 取得的ID, "referenceType": "BROAD_MARKET" }` |
| PUT | `/api/products/{symbol}/references/{referenceId}?market=TW` | 修改，Body 同新增，可替換 Instrument 或 ReferenceType |
| DELETE | `/api/products/{symbol}/references/{referenceId}?market=TW` | 移除 mapping，保留 Instrument 與既有分析快照 |

POST 回 201，PUT 回 200，DELETE 回 204。重複 Instrument／mapping 或仍被引用的 Instrument 回 409；非法類型／ticker 回 400；不存在／不屬於目前使用者的 ID 回 404。Route ID 優先，不接受 Body 改寫操作對象。產品由既有 `(Market, Symbol)` 查詢，同類型可新增多個不同 Instruments。沒有 Reference 管理 Frontend UI。

新增 Instrument 並設定 mapping 的範例（僅示範 API；本次沒有替 2330 增加正式 mapping）：

```powershell
$base = 'http://127.0.0.1:5080'
$headers = @{ 'X-Market-Signal' = 'web' }
$instrument = Invoke-RestMethod -Method Post -Uri "$base/api/market-reference-instruments" -Headers $headers -ContentType 'application/json' -Body '{"symbol":"^SOX","name":"PHLX Semiconductor Index","market":"US"}'
$body = @{ instrumentId = $instrument.id; referenceType = 'SECTOR' } | ConvertTo-Json
$mapping = Invoke-RestMethod -Method Post -Uri "$base/api/products/2330/references?market=TW" -Headers $headers -ContentType 'application/json' -Body $body
Invoke-RestMethod "$base/api/products/2330/references?market=TW"
# 修改 mapping 時 PUT 上述 mapping.id；移除時 DELETE。
```

輸入完整 Yahoo ticker（如 `^TWII`、`^SOX`、`SPY`、`0050.TW`），不會自動補 `.TW`；格式經檢查並 URL encode，固定走 Yahoo chart endpoint。新增 metadata 不會自動上網驗證 ticker；分析當時若 Yahoo 無資料，會明確標示 Reference unavailable。Target 的既有台股數字 symbol 路徑維持原樣。

## 第一個真實 mapping 與 Yahoo 證據

不是將 0050 ETF 或其他 Top50 指數當作臺灣50。Yahoo 官方 search API 的 `FTSE TWSE Taiwan 50` 回報 `^TSE50`、quoteType=INDEX、exchange=TAI；據此再驗證 chart。

| 00631L ReferenceType | Instrument / Yahoo ticker | 當次值 | 漲跌 | 漲跌 % | 日价筆數 | Context 狀態 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| UNDERLYING | FTSE TWSE Taiwan 50 Index / `^TSE50` | 45,041.66 | +481.62 | +1.0808% | 1 | PARTIAL |
| BROAD_MARKET | TAIEX / `^TWII` | 48,353.49 | +721.49 | +1.5147% | 21 | AVAILABLE |

兩個指數 `interval=1m&range=1d` 均實際 HTTP 200、271 筆分鐘資料。`interval=1d&range=1mo` 均 HTTP 200，但 ^TSE50 僅 1 根日線；實際 Force 使用與 Target 相同的 30 日 period1 / period2 範圍，也僅回 1 筆。^TWII 回 21 筆。`^TSE50` 的稀疏歷史是實際限制，不用 ETF 代理、不補造歷史，不對只有一根日線宣稱已驗證多日趨勢。最新指數報價時間與 Target 當日收盤一致。

公開實測證據見 [market-reference-yahoo-evidence.json](market-reference-yahoo-evidence.json)，含 search discovery、chart HTTP 狀態、價格／previousClose、筆數與日價 OHLCV。指數 volume 回 0，Skills 明確禁止解讀成「市場沒有交易」。Change / ChangePercent 由該 Reference 最新 snapshot 的 price 與 chartPreviousClose 計算，不用月線第一根 close 假裝昨收。

## 統一分析輸入、Batch reuse 與 Persistence

Runner 在呼叫任何 Analyst 前依使用者 mapping 取得 Reference，組裝一份固定 MarketContext：product / snapshot / history / position / marketReferences / previousDecisions。兩個 Provider 都呼叫同一 AnalysisProtocol.Input，實際序列化 JSON 完全相同；Analyst 沒有選 benchmark 或抓行情的邏輯。

`previousDecisions` 是按 Provider 標記的「上輪各 Provider 最新真實決策」集合，排除 mock，於本輪開始時一次擷取，不把本輪 DeepSeek 結果臨時餵給 GPT。這取代先前每個 Provider 各自不同的前次決策輸入；legacy previousDecision 欄保留且在 Runner 統一為 null。Skill 要求各 Provider 使用自己的上輪決策作連續性參考，並獨立判斷共用市場證據。

ReferenceDataCache 僅存在單次 Force／RunAll batch。快照依 exact ticker 重用，日價依 `(ticker, from, through)` 重用，包含失敗結果；同批多個商品／使用者不重抓同一份資料。下一次 Force／batch 建新 cache，沒有 process-level stale cache、Redis 或 distributed cache。Reference 抓取失敗／日期不符／少於兩根日線會保存 Error 和 UNAVAILABLE／PARTIAL 狀態，維持 Target 分析、明確限制證據；不 fallback 其他標的。

InputSnapshotJson 保留原有商品／行情／持倉欄位，增加 marketReferences / previousDecisions，以及 **analysisInput**：它是提供給兩個真 API Provider 的完整市場 JSON。每個 Reference 包含 mapping id、type、symbol、name、market、當時 MarketSnapshot、currentValue、change、changePercent、歷史日價、狀態與錯誤，可還原實際數值，不只保存 ID。修改／刪除 mapping 不改寫歷史 InputSnapshotJson。

market-evidence / decision-rules Skills 明確限定：只有 Target Product 可有 Action；UNDERLYING / BROAD_MARKET / SECTOR 都只是 Evidence，不允許額外決策，不允許自行選 Reference、搜尋或修改 mapping。既有 analysis.schema.json 不变，多餘決策欄位仍遭驗證拒絕。

## 真實 End-to-End 結果

使用既有 Force API 執行一次，MarketSnapshots 新增 **ID 15**。沿用真實 User Secrets；所有 Yahoo chart、GPT Responses、DeepSeek Chat Completions 均回 HTTP 200。

| Provider | 實際 Model | Analysis ID | Action / Confidence | Input / Output / Cached Tokens |
| --- | --- | ---: | --- | --- |
| OpenAI | gpt-6.1-sol | 60 | HOLD / 57 | 8799 / 1086 / 0 |
| DeepSeek | deepseek-flash（設定 deepseek-chat） | 59 | HOLD / 52 | 9849 / 1024 / 0 |
| Claude | mock-v1 | 61 | HOLD / 60（示範） | null |

兩個真實模型的理由都引用臺灣50／TAIEX，並承認臺灣50僅一筆歷史的限制。Target 是 00631L，當時持倉 3 張、均價 34；Reference 沒有產生額外 Action。GPT 維持持有，認為 39.62 尚未突破 39.65、量能未確認；DeepSeek 亦維持持有，指出兩個指數當日方向一致，但不足以加碼。

AIAnalysisResults 與 AIAnalysisUsage 正常 INSERT；SQL 確認分析 59 / 60 的 `$.analysisInput` JSON **完全相同**，皆有 2 個 References、2 個 prior provider decisions、3 張持倉。完整 snapshot／歷史均已保存在兩筆 InputSnapshotJson。Provider Failure 數量維持原有 2 筆，本次沒有新增、没有降級 Mock。

Microsoft Edge headless 實際登入 http://127.0.0.1:5173，開啟 00631L 商品頁；既有 API latest / history 回 200，畫面與理由顯示 DeepSeek 59、GPT 60、Claude 61，usage 與 Force 回傳一致，歷史可讀，page errors=0。Frontend 沒有修改。Swagger 新增 CRUD routes；Docker :8080 仍是既有容器，管理 Reference 請使用新版本機 :5080。

## Build / Tests

- Backend build：0 warnings / 0 errors。
- 全部 Backend tests（含隔離 MySQL）：**78 passed，0 failed，0 skipped**，原有測試全部仍通過。
- 新測試：CRUD／跨使用者隔離、同商品多 SECTOR、重複鍵、刪除仍被引用的 Instrument、Route ID precedence、非法 type／ticker、seed 不重建刪除 mapping、Yahoo exact ticker、0 index volume、無 mapping 不推測、批次去重與失敗重用、下一 request 重新抓取、兩個真 adapter request input 完全相同及 InputSnapshotJson 保存。
- Frontend TypeScript / Vite build 通過，既有 React 真實瀏覽器讀回驗證通過。

## 本次修改檔案

- Domain：`backend/MarketSignalAI.Domain/MarketReferences.cs`（新增）。
- Application：`backend/MarketSignalAI.Application/MarketReferenceContracts.cs`（新增）、`MarketContracts.cs`。
- Api：`backend/MarketSignalAI.Api/MarketReferenceEndpoints.cs`（新增）、`Program.cs`。
- Infrastructure：`MySqlMarketReferenceStore.cs`（新增）、`MemoryMarketReferenceStore.cs`（新增）、`ReferenceDataCache.cs`（新增）、`Migrations/004_market_references.sql`（新增）、`MarketSignalAI.Infrastructure.csproj`、`YahooMarketDataProvider.cs`、`MarketAnalysisRunner.cs`、`AI/AnalysisProtocol.cs`、`AI/Skills/market-evidence.md`、`AI/Skills/decision-rules.md`。
- Tests：`MarketReferenceTests.cs`（新增）、`MarketProviderTests.cs`、`OpenAIFlowTests.cs`。
- Docs：`README.md`、`docs/implementation.md`、`docs/openai-integration.md`、`docs/market-references.md`（新增）、`docs/market-reference-yahoo-evidence.json`（新增）。

沒有修改 OpenAIAnalyst／DeepSeekAnalyst 的 API 協定或另造分析 schema；沒有 Frontend 管理系統、額外技術指標、正式登入、其他 Provider 或部署。既有未提交修改保留，没有 Commit 任何 Secret。

## 已知限制

- ^TSE50 在本次 Yahoo 30 日範圍僅有 1 根日價；仍能取得最新值、漲跌及這一根真實日價，但不能驗證多日 underlying 趨勢。
- Yahoo 報價延遲，價格／歷史 previousClose 也可能不一致；各序列與時間必須區分。沒有 fallback 資料源、ETF 替代或捏造資料。
- Reference 取不到時有明確狀態與錯誤，不讓單一 Reference 中止既有 Target／Provider 流程；能否形成投資結論仍由各模型依資料限制判斷。
- CRUD 支援通用 Yahoo ticker／Market metadata；現有 Force／排程 Target 仍只支援台股，未擴充成完整美股分析流程，其他市場 reference 尚未真實驗證。Yahoo 日價目前延續既有台灣時區日期解析；跨市場日價邊界需在美股任務中另行驗證。
- demo 身份仍固定 user 1。資料層已做 UserId 範圍與外鍵隔離，但正式多使用者登入不在本次 scope。
- cache 只限同一個 Force／batch，獨立同時請求不共享；startup migration 沿用單 instance 前提，無 distributed lock。
- schema／持倉 Validation 不等於模型敘述事實核對；原始分析文字保持可追溯，不將信心值當作校準機率。

Yahoo 查證來源：[官方 Search](https://query1.finance.yahoo.com/v1/finance/search?q=FTSE%20TWSE%20Taiwan%2050&quotesCount=10&newsCount=0)、[臺灣50 Chart](https://query1.finance.yahoo.com/v8/finance/chart/%5ETSE50?interval=1d&range=1mo)、[TAIEX Chart](https://query1.finance.yahoo.com/v8/finance/chart/%5ETWII?interval=1d&range=1mo)。


TASK 1 歷史修正：Taiwan50 mapping 仍為 ^TSE50；Yahoo 日價不足時採用明確標示的 TWSE TAI50I close-only 歷史。本次取得 21 筆並完成 GPT / DeepSeek 真實驗證，來源與品質保存於 InputSnapshot。上述一筆日价是先前驗證記錄；目前設計、證據與限制見 [Taiwan50 歷史修正](tse50-history.md)。
