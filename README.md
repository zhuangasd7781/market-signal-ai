# Market Signal AI

以「追蹤商品 × AI 訊號」為核心的投資訊號看板。首頁僅呈現各 AI 的獨立訊號；持倉、推論與歷史放在商品詳情頁。不排名 AI，也不執行交易。

目前完成 `planning.txt` 的 **Phase 1**，並加入 Yahoo 台股行情、MySQL 快照、手動分析與同程序排程。已加入可選的 OpenAI Responses API（預設 `gpt-6.1-sol`、reasoning `medium`）與 DeepSeek 官方 API；未啟用時維持 `mock-v1`。Google OAuth 尚未實作。

## 快速開始：Docker

需求：Docker Desktop（Linux containers）。

```powershell
Copy-Item .env.example .env
# 編輯 .env，設定兩個本機 MySQL 密碼
docker compose up -d --build
```

如果 `.env` 已存在，保留現有檔案。首次建置會下載映像與套件。

- 網頁：http://localhost:8080 → 進入示範看板
- 健康檢查：http://localhost:8080/health
- Swagger UI：http://localhost:8080/swagger/（僅 Development）
- MySQL：`127.0.0.1:3307`；帳密在未納入 Git 的 `.env`
- 後端只開放容器內部連線，前端 Nginx 代理 `/api`，不需要 CORS。

```powershell
docker compose ps
docker compose logs --tail 50 backend
docker compose stop
docker compose start
```

資料保存在 `mysql-data` volume。一般停止、重建容器不會清掉資料。**`docker compose down -v` 會刪除資料，勿作為日常重啟命令。**

## 不用 Docker 的開發模式

需求：.NET 8 SDK（可使用 .NET 9 SDK 建置 net8.0）、Node.js 22.12+。

```powershell
dotnet restore MarketSignalAI.sln
dotnet run --project backend/MarketSignalAI.Api
```

另開終端：

```powershell
cd frontend
npm ci
npm run dev
```

開啟 http://localhost:5173。Vite 將 API 請求代理至 `localhost:5080`。預設記憶體儲存，重啟 API 後重置示範資料。無須 Google、GCP 或 AI 金鑰。

### 本機 API + Docker MySQL

```powershell
docker compose up -d mysql
./scripts/run-api-mysql.ps1
```

腳本只讀取此專案 `.env` 中的 MySQL 設定，將連線字串放入當前程序環境，不輸出密碼。再啟動 `npm run dev` 即可使用。

### 自行管理 MySQL

使用 MySQL 8.0+（Compose 使用 8.4）。建立空資料庫及專用帳戶，依序套用 `backend/MarketSignalAI.Infrastructure/Migrations/001_initial.sql` 、`002_market_pipeline.sql` 和 `003_openai_usage.sql`，然後設定 `ConnectionStrings__MySql`。連線字串加上 `DateTimeKind=Utc`。

Compose 只會在空 volume 首次初始化時執行 schema。既有 volume 啟動後端時會冪等建立這次新增的 `MarketSnapshots`、`TradingDays`、`AIAnalysisUsage` 和 `AIProviderFailures` 表；往後的 schema 變更仍須另行管理。Development + MySQL 模式啟動 API 時，會在交易中透過 `SeedVersions` 標記只寫入一次 demo users、products、providers 與 history。移除追蹤或變更 provider 後，重啟不會覆寫設定。

## 架構

```text
backend/
  MarketSignalAI.Domain/          商品、持倉、provider、標準分析模型
  MarketSignalAI.Application/     使用者、行情與分析介面；首頁聚合
  MarketSignalAI.Infrastructure/  Yahoo adapter、共用分析流程、MySQL + Dapper、schema
  MarketSignalAI.Api/             FastEndpoints、Swagger、排程觸發、DI
frontend/                        React + TypeScript + Vite、原生 CSS
tests/                           xUnit API 整合測試
docs/                            API 與下一階段清單
```

依賴方向：API → Infrastructure → Application → Domain；Application 不依賴資料庫。沒有 Entity Framework。

Provider 欄位取自後端 `AIProviders`，MySQL 模式可調整 `IsEnabled`、`DisplayName`、`SortOrder`。新增啟用的 provider 後，重新整理首頁會自動出現新欄；沒有分析顯示「尚未分析」。記憶體模式則從 `DemoData` 載入。

商品以 `(Market, Symbol)` 唯一識別；路由用 `?market=US` 等參數避免跨市場同代號衝突。持倉數量單位由商品定義，例如台股「張」乘 `UnitSize=1000`；美股為「股」。商品詳情頁從 DB 顯示最新台股報價；首頁仍只顯示訊號。損益與報酬率尚未計算。

## 行情與排程

`YahooMarketDataProvider` 使用 Yahoo Finance chart endpoint `https://query1.finance.yahoo.com/v8/finance/chart/{symbol}.TW`：`interval=1m&range=1d` 取得最新行情，`interval=1d&period1=...&period2=...` 取得歷史日價。日開盤價取首筆有效分鐘開盤價，其餘價格與成交量取 chart `meta`；Yahoo JSON 只在 Infrastructure 解析。台股休市日依證交所年度日曆 `https://www.twse.com.tw/holidaySchedule/holidaySchedule?response=json&queryYear={民國年}` 判斷，週末直接視為休市。外部資料可能延遲或改版，詳情頁會顯示實際行情時間。

同一個 API process 的 Worker 以 `Asia/Taipei` 時間每 20 秒檢查時鐘。08:30 只寫入當天 `TradingDays` 的 `OPEN`／`CLOSED`；09:05、10:05、12:05、13:05 僅在 `OPEN` 時分析所有被追蹤的啟用台股商品。若服務在 08:30 後才啟動，下一個排程會先補查交易日。休市時不抓行情，也不呼叫 AI。排程若收到舊交易日的 Yahoo 報價，會跳過該商品。單一商品或 AI 失敗不會中止其餘商品／provider；完成的快照與分析永遠新增歷史紀錄。服務停機期間的時段不會補跑，重啟可能在同一分鐘再次觸發；目前是單實例個人版。

`GET /api/products/{symbol}/market` **只查 MySQL** 最新快照，尚無資料回 404；不直接向 Yahoo 發請求。`POST /api/products/{symbol}/analysis/force` 抓取最新 Yahoo 行情、儲存快照、建立含持倉與歷史日價的 `MarketContext`，再執行所有啟用 provider。OpenAI 可透過設定啟用真實 GPT；DeepSeek 可使用真實官方 API，Claude 仍為明確的 `mock-v1`。啟用 GPT 後，force 與排程會呼叫付費 OpenAI API；失敗只回報 `FAILED`，不會 fallback 為 mock。此 POST 仍需要示範模式的 `X-Market-Signal: web` header。

```powershell
$headers = @{ 'X-Market-Signal' = 'web' }
Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/products/00631L/analysis/force -Headers $headers
Invoke-RestMethod -Uri http://localhost:8080/api/products/00631L/market
Invoke-RestMethod -Uri http://localhost:8080/api/products/00631L/analysis/history
```

force 回應列出本次 `snapshotId` 和各 provider 的 `COMPLETED`／`FAILED`／`UNAVAILABLE` 狀態、`analysisId`、模型與正規化分析結果。Swagger UI 可直接測試，POST 的 `X-Market-Signal` header 預設為 `web`。若只以預設 Memory 模式啟動本機 API，Worker 不啟動，force 仍可手動執行；Docker MySQL 模式會啟動 Worker。

## 設定

| 設定 | 說明 |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | 目前必須為 `Development` |
| `Demo__Enabled` | 必須為 `true`；本機 launch profile 已設定 |
| `Storage__Provider` | `Memory`（預設）或 `MySql` |
| `ConnectionStrings__MySql` | MySQL 模式必要的連線字串 |
| `MYSQL_DATABASE`, `MYSQL_USER` | Compose 資料庫／專用帳戶 |
| `MYSQL_PASSWORD`, `MYSQL_ROOT_PASSWORD` | 僅存 `.env` 的本機密碼 |
| `MYSQL_PORT`, `FRONTEND_PORT` | Compose 主機連接埠，預設 3307／8080 |
| `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` | 下一階段預留，目前不讀取 |
| `MarketWorker__Enabled` | MySQL 模式預設 `true`；可設為 `false` 暫停排程 |

**示範模式共用 user 1，沒有真正的登入驗證。**「進入／離開示範」只控制瀏覽器導覽，不是安全邊界。API 從伺服器端 `ICurrentUser` 決定使用者，不接受客戶端 UserId。非 Development 環境會拒絕啟動，避免誤把匿名示範服務部署成正式產品。請勿輸入真實私人持倉。

## 驗證

```powershell
dotnet test MarketSignalAI.sln
cd frontend
npm run build
# 先啟動 Docker Compose；測試預設使用電腦已安裝的 Microsoft Edge
npm run test:e2e
```

端對端測試涵蓋登入入口、動態欄位、搜尋、追蹤增刪、持倉保存、分析歷史、手機表格及 API 驗證。測試會編輯示範商品 0050 的持倉，勿對真實資料執行。可設 `TEST_BASE_URL` 改指向 Vite 開發服務。截圖在 `frontend/test-results/`（不納入 Git）。

## Google OAuth / AI / GCP 後續

Google 登入將以 Subject ID 對應內部 User，透過新的 `ICurrentUser` 實作提供身分；使用伺服器端安全 cookie、登入狀態驗證與 CSRF 防護。不可把 email 當永久外部識別。

AI 階段已加入 `IAIAnalyst` 抽象、mock adapter、結構驗證、timeout 與各 provider 獨立錯誤；新增分析一律 append history，保存輸入快照與原始回應，前端只拿正規化結果。OpenAI Responses adapter 與可組合分析規則已實作；DeepSeek 真實 adapter 已實作，Claude 保留 mock。

Cloud Run 部署前先完成真實驗證並移除 demo guard，將 API 改為正式設定。容器與 JSON 結構化日誌已備妥；未來資料庫用 Cloud SQL for MySQL、機密用 Secret Manager。Production 不使用記憶體模式，也不需要 Kubernetes。完整進度見 [docs/implementation.md](docs/implementation.md)。

FastEndpoints 使用方式參照[官方文件](https://fast-endpoints.com/docs/get-started)。

後端驗證結果與各端點的手動測試方式見 [後端驗證文件](docs/backend-verification.md)。

GPT 啟用、環境變數、token usage 與失敗紀錄查詢見 [OpenAI 串接說明](docs/openai-integration.md)。

## 真實 GPT / DeepSeek End-to-End

本機 .NET User Secrets 已透過 `IConfiguration` 讀取 `OpenAI:ApiKey`、`DeepSeek:ApiKey`。不必把 Key 放進 `.env` 或 appsettings。使用既有 MySQL 啟動腳本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-api-mysql.ps1 -LiveAI
# 另開終端機
cd frontend
npm run dev
```

`-LiveAI` 在本次程序啟用 GPT / DeepSeek 並停用排程；Claude 保留 mock。前端位於 http://127.0.0.1:5173，後端 http://127.0.0.1:5080。Docker 服務不會因此自動更新，也不會取得主機 User Secrets。

2026-10-01 已以 00631L 實際完成 Yahoo → GPT / DeepSeek → MySQL → API → React。設定、實際結果、usage、失敗紀錄與限制見 [雙 Provider E2E 驗證](docs/ai-e2e.md)。

## Market References

已加入由使用者／系統明確設定的通用 Market Reference mapping，提供 Reference Instruments 與商品 mappings 的 Backend CRUD。00631L 已設定 UNDERLYING `^TSE50`（臺灣50）及 BROAD_MARKET `^TWII`（TAIEX），可在同商品設定多個 SECTOR；沒有 mapping 時不推測。

真實 GPT / DeepSeek 已收到完全相同的 Reference／Target／Position／previousDecisions，完整數值保存於 InputSnapshotJson。Yahoo 臺灣50最新報價成功，但本次 30 日日價僅一筆，Context 明確標為 PARTIAL。設計、API 使用、公開資料證據、78 項測試與本次分析 59／60 見 [Market Reference 說明](docs/market-references.md)。新版管理 API 位於本機 :5080；未更新 Docker image。


TASK 1 歷史修正：Taiwan50 mapping 仍為 ^TSE50；Yahoo 日價不足時採用明確標示的 TWSE TAI50I close-only 歷史。本次取得 21 筆並完成 GPT / DeepSeek 真實驗證，來源與品質保存於 InputSnapshot。上述一筆日价是先前驗證記錄；目前設計、證據與限制見 [Taiwan50 歷史修正](docs/tse50-history.md)。
