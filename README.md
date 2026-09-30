# Market Signal AI

以「追蹤商品 × AI 訊號」為核心的投資訊號看板。首頁僅呈現各 AI 的獨立訊號；持倉、推論與歷史放在商品詳情頁。不排名 AI，也不執行交易。

目前完成 `planning.txt` 的 **Phase 1**，並先接好示範帳戶的追蹤增刪與持倉編輯。Google OAuth、真實行情、AI 呼叫與分析排程尚未實作。所有分析均明確標記為 mock，不應用於投資決策。

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

使用 MySQL 8.0+（Compose 使用 8.4）。建立空資料庫及專用帳戶，將 `backend/MarketSignalAI.Infrastructure/Migrations/001_initial.sql` 套用一次，然後設定 `ConnectionStrings__MySql`。連線字串加上 `DateTimeKind=Utc`。

Compose 只會在空 volume 首次初始化時執行 schema；未來 migration 需另行套用，不會在每次啟動自動修改 schema。Development + MySQL 模式啟動 API 時，會在交易中透過 `SeedVersions` 標記只寫入一次 demo users、products、providers 與 history。移除追蹤或變更 provider 後，重啟不會覆寫設定。

## 架構

```text
backend/
  MarketSignalAI.Domain/          商品、持倉、provider、標準分析模型
  MarketSignalAI.Application/     使用者／儲存介面、首頁聚合與訊號變化判斷
  MarketSignalAI.Infrastructure/  MySQL + Dapper、記憶體模式、schema、demo seed
  MarketSignalAI.Api/             ASP.NET Core + FastEndpoints、DI、錯誤處理
frontend/                        React + TypeScript + Vite、原生 CSS
tests/                           xUnit API 整合測試
docs/                            API 與下一階段清單
```

依賴方向：API → Infrastructure → Application → Domain；Application 不依賴資料庫。沒有 Entity Framework。

Provider 欄位取自後端 `AIProviders`，MySQL 模式可調整 `IsEnabled`、`DisplayName`、`SortOrder`。新增啟用的 provider 後，重新整理首頁會自動出現新欄；沒有分析顯示「尚未分析」。記憶體模式則從 `DemoData` 載入。

商品以 `(Market, Symbol)` 唯一識別；路由用 `?market=US` 等參數避免跨市場同代號衝突。持倉數量單位由商品定義，例如台股「張」乘 `UnitSize=1000`；美股為「股」。尚無報價時不計算損益。

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
| `DEEPSEEK_API_KEY` | 下一階段預留，目前不讀取 |

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

AI 階段加入 `IAIAnalyst`、DeepSeek adapter、結構驗證、timeout、各 provider 獨立錯誤與 prompt skill composition；新增分析一律 append history，保存輸入快照與原始回應，前端只拿正規化結果。目前沒有假裝成功的「重新分析」按鈕。

Cloud Run 部署前先完成真實驗證並移除 demo guard，將 API 改為正式設定。容器與 JSON 結構化日誌已備妥；未來資料庫用 Cloud SQL for MySQL、機密用 Secret Manager。Production 不使用記憶體模式，也不需要 Kubernetes。完整進度見 [docs/implementation.md](docs/implementation.md)。

FastEndpoints 使用方式參照[官方文件](https://fast-endpoints.com/docs/get-started)。
