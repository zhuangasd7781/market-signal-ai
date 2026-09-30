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

先接好的下一階段功能：商品搜尋、追蹤增刪、持倉編輯與持久化。持倉目前不會觸發重新分析；既有 demo 訊號仍是固定快照。

## Phase 2：尚未完成

- 真正的 Google OAuth、安全 cookie session、logout、CSRF token。
- 以 Google Subject ID 建立／載入 User；抽出 external identity 表以支援更多 provider。
- 真實登入後多使用者端對端驗證。目前只有 repository／server-side user scoping 測試，不能視為完整身份驗證。

## Phase 3：尚未完成

- `IMarketDataProvider` 與 mock/live adapters。
- `IAIAnalyst`、DeepSeek adapter、分模組 prompt skills。
- `POST /api/products/{symbol}/analysis`、timeout、取消、provider 個別失敗結果。
- 正規化與驗證 AI 回應、完整輸入快照與不可變歷史寫入。
- Position-aware、槓桿 ETF 特別規則、多空對照、降低缺資料時的信心值。

資料模型已預留上述欄位；seed 有 mock snapshot 與 raw response，但不是完整分析引擎。

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
| GET | `/api/products/{symbol}/position?market=TW` | `{position, quote:null}` |
| PUT | `/api/products/{symbol}/position?market=TW` | `{quantity, averageCost}`，須先追蹤 |
| GET | `/api/products/{symbol}/analysis?market=TW` | 啟用 provider 的最新分析 |
| GET | `/api/products/{symbol}/analysis/history?market=TW` | 最近 100 筆，最新在前 |
| GET | `/api/ai/providers` | 啟用 provider，按 SortOrder 排序 |

歷史變化依同 user/product/provider 的最近兩筆 action 與 quantity 比較。首頁時間是該商品所有已顯示訊號中最新一次完整分析的時間；hover 會列出各啟用 AI 的分析時間，尚無分析者顯示「尚未分析」。單一訊號 hover 也可查看自身時間。「已變更」表示相對前次分析變化，不是「尚未讀取」。無分析不偽造持有；不提供市場資料時不顯示報酬。

目前不提供分析生成 endpoint，避免以固定 mock 訊號假冒真實 AI 呼叫。

## 本次驗證結果

- `dotnet test MarketSignalAI.sln --no-restore`：5 項通過。
- TypeScript / Vite build 與 Docker Linux image build：通過。
- Playwright + Microsoft Edge + Docker MySQL：3 項通過。
- 桌面 1280px、手機 390px 截圖已檢查，手機只有表格區域橫向捲動。
- 重啟 backend 後，0050 測試持倉仍為 2 張、平均成本 35.5，已移除的追蹤不會被 seed 重新加入。
- `/health` 回報 `healthy`、`storage: MySql`。

測試後保留了上述示範持倉，但 0050 不在追蹤清單中；重新加入即可查看。
