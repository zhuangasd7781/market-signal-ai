# 進度與實作邊界

## Phase 1：完成

- 四層 C# solution、FastEndpoints、DI、nullable、CancellationToken。
- MySQL schema、Dapper repository、約束與 UTC history index。
- Memory 與 MySQL 示範模式，共用相同應用層。
- Mock user、五個商品（含台股及美股）、三個動態 provider、18 筆歷史訊號。
- 繁體中文深色首頁；只顯示商品、AI 訊號與分析時間。
- 登入示範入口、首頁、詳情、空狀態、載入 skeleton、錯誤與手機橫向捲動。
- 商品詳情含持倉區、獨立分析、Root Event、風險、多空觀點、失效條件與歷史。
- Docker、環境設定、健康檢查、結構化日誌與啟動文件。

先接好的下一階段功能：商品搜尋、追蹤增刪、持倉編輯與持久化。持倉編輯本身不觸發重新分析。

## Phase 2：尚未完成

- 真正的 Google OAuth、安全 cookie session、logout、CSRF token。
- 以 Google Subject ID 建立／載入 User；抽出 external identity 表以支援更多 provider。
- 真實登入後多使用者端對端驗證。目前只有 repository／server-side user scoping 測試，不能視為完整身份驗證。

## Phase 3：行情與分析管線已建立

- `IMarketDataProvider` + Yahoo 台股 adapter；原始 Yahoo JSON 不進入 Application／Domain。
- `MarketSnapshots`、`TradingDays` 與 Dapper repository；行情與分析每次新增歷史。
- `GET /api/products/{symbol}/market` 只讀 DB，`POST /api/products/{symbol}/analysis/force` 共用排程 runner。
- 台灣時區 08:30 交易日檢查，09:05／10:05／12:05／13:05 分析被追蹤商品。
- `IAIAnalyst` 抽象、mock adapter、timeout、回應驗證及 provider 個別失敗結果。

OpenAI Responses API 已接入，模型和 reasoning effort 從設定取得（預設 `gpt-6.1-sol`／`medium`），以 Structured Outputs 對應既有 Analysis 結構。可組合市場證據、決策和槓桿 ETF 規則；保存 prompt、原始回應、input/output token usage。GPT 失敗獨立記錄，不 fallback 為 mock。DeepSeek 已接入真實官方 API，Claude 仍為 `mock-v1`。尚未串接新聞與法人資料，已以本機 User Secrets 實際驗證 OpenAI / DeepSeek E2E。

## Phase 4 / 5

- 詳細 UI 已以 mock 呈現；接入真實 provider 的錯誤／進度與重新分析。
- Production authentication、API rate limiting、正式 migration 流程。
- Cloud Run／Cloud SQL／Secret Manager 設定；不建立 Kubernetes、回測或 AI 排名。

## API

所有 API 目前使用共用 demo user；任何 mutation 需 `X-Market-Signal: web` header。未開啟 CORS。

| Method | Route | 說明 |
| --- | --- | --- |
| GET | `/health` | 儲存層 readiness |
| GET | `/api/me` | 示範使用者與 `isMock` |
| GET | `/api/watchlist` | `{providers, items, isMock}`，單次載入首頁 |
| POST | `/api/watchlist` | `{productId}`，重複追蹤為 no-op |
| DELETE | `/api/watchlist/{productId}` | 移除追蹤，保留持倉與歷史 |
| GET | `/api/products/search?q=` | 代號或名稱，最多 30 筆 |
| GET | `/api/products/{symbol}?market=TW` | 商品，market 預設 TW |
| GET | `/api/products/{symbol}/position?market=TW` | `{position, quote:null}`；行情另由 market API 讀取 |
| PUT | `/api/products/{symbol}/position?market=TW` | `{quantity, averageCost}`，須先追蹤 |
| GET | `/api/products/{symbol}/analysis?market=TW` | 啟用 provider 的最新分析 |
| GET | `/api/products/{symbol}/analysis/history?market=TW` | 最近 100 筆，最新在前 |
| GET | `/api/ai/providers` | 啟用 provider，按 SortOrder 排序 |
| GET | `/api/products/{symbol}/market?market=TW` | DB 最新行情；無資料回 404 |
| POST | `/api/products/{symbol}/analysis/force` | Yahoo → DB → 已設定的 GPT／DeepSeek／mock Claude；需自訂 header |

歷史變化依同 user/product/provider 的最近兩筆 action 與 quantity 比較。首頁時間是該商品所有已顯示訊號中最新一次完整分析的時間；hover 會列出各啟用 AI 的分析時間，尚無分析者顯示「尚未分析」。單一訊號 hover 也可查看自身時間。「已變更」表示相對前次分析變化，不是「尚未讀取」。無分析不偽造持有；無行情時不顯示報酬。Swagger UI 位於 Development 的 `/swagger/`。

## 本次驗證結果

- `dotnet test MarketSignalAI.sln --no-restore`：13 項通過，含 Yahoo 解析、交易日、休市略過、舊報價拒絕、force 追加歷史與失敗隔離。
- TypeScript / Vite build 與 Docker Linux image build：通過。
- Playwright + Microsoft Edge + Docker MySQL：3 項通過。
- 桌面 1280px、手機 390px 截圖已檢查，手機只有表格區域橫向捲動。
- 重啟 backend 後，0050 測試持倉仍為 2 張、平均成本 35.5，已移除的追蹤不會被 seed 重新加入。
- `/health` 回報 `healthy`、`storage: MySql`。
- Docker MySQL 實測兩次 `00631L` force：快照 ID 1、2；每次三個 mock provider 各新增分析，最新行情 GET 由 DB 回傳。

測試後保留了上述示範持倉，但 0050 不在追蹤清單中；重新加入即可查看。

最新雙 Provider 實測與限制見 [ai-e2e.md](ai-e2e.md)。

Market Reference mapping、Backend CRUD、單批次重用與真實雙 Provider 驗證已完成，見 [market-references.md](market-references.md)。
