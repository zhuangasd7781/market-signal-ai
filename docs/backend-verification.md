# 後端驗證與手動測試

這份文件記錄 GPT 串接前的行情管線驗證；後續 GPT 與 usage 驗證見 [OpenAI 串接說明](openai-integration.md)。

驗證日期：2026-10-01。基底網址為 `http://localhost:8080`（Docker）；本機 dotnet 模式請換成啟動日誌的網址。

## 本次驗證結果

- `dotnet build MarketSignalAI.sln` 成功，零警告、零錯誤。
- `dotnet test MarketSignalAI.sln`：27 項通過。
- 最新後端容器重建、啟動成功；`/health` 回報 `healthy`、`MySql`。
- `/swagger/` 與 `/swagger/v1/swagger.json` 均正常，文件包含行情與 force endpoint。
- 真實 Yahoo `00631L.TW` 分鐘行情與歷史日價成功取得。MySQL 輸入快照包含 20 筆歷史日價；AI 結果仍為 `mock-v1`。
- 最新後端 force 回傳 snapshot 6 與三個 `COMPLETED` provider，商品歷史由 21 筆增至 24 筆。ID 與筆數會隨後續執行增加。
- 重建／重啟 API 前後，既有 21 筆商品分析及行情 fetchedAt 保持一致，確認資料持久化與 seed 不覆寫。
- 排程以測試時鐘驗證 08:30、09:05、10:05、12:05、13:05（台北時間）、同分鐘去重、跨日與失敗重試；驗證休市略過、晚啟動補查交易日、舊行情拒收、日曆失敗不執行分析。未調整主機時間，也未等候所有真實排程時段。

本次保留前次已完成的行情 adapter、DB schema/repository、分析 runner、API、Swagger 與 worker；新增錯誤資料防護與回歸測試，沒有修改前端。

## 使用 Swagger

開啟 `http://localhost:8080/swagger/`，展開 endpoint，按 **Try it out**。所有 POST／PUT／DELETE 需要 `X-Market-Signal: web`，Swagger 已提供預設值。JSON request body 的 Content-Type 使用 `application/json`。

## 各端點測試

下表路徑皆加上基底網址。商品查詢預設 `market=TW`；美股查詢另帶 `?market=US`。force 僅支援台股。

| 方法與路徑 | 輸入／操作 | 預期結果 |
| --- | --- | --- |
| `GET /health` | 無 | 200，`status: healthy`、`storage: MySql` |
| `GET /api/me` | 無 | 200，共用示範使用者與 `isMock: true` |
| `GET /api/ai/providers` | 無 | 200，目前啟用的 provider 清單 |
| `GET /api/products/search?q=00631L` | 代碼或名稱 | 200，商品清單；記下 productId |
| `GET /api/products/00631L` | 無 | 200，商品資料 |
| `GET /api/watchlist` | 無 | 200，providers、items 與各商品訊號 |
| `POST /api/watchlist` | `{"productId":1}`（或搜尋取得的 ID） | 204；重複加入不新增重複紀錄 |
| `GET /api/products/00631L/position` | 無 | 200，position 或 null；quote 欄目前為 null，行情另查 market |
| `PUT /api/products/00631L/position` | `{"quantity":6,"averageCost":33.74}` | 204；再 GET 確認。這會修改示範持倉，請先記下原值 |
| `GET /api/products/00631L/market` | 無 | 有快照回 200；尚無快照回 404；不向 Yahoo 發請求 |
| `POST /api/products/00631L/analysis/force` | 不需 body；商品須已追蹤 | 200，snapshotId 與各 provider 狀態、analysisId、model、result |
| `GET /api/products/00631L/analysis` | 無 | 200，各 provider 最新分析，不暴露 rawResponse |
| `GET /api/products/00631L/analysis/history` | force 前後各查一次 | 200，每次成功 force 新增三筆（啟用三個 mock provider 時）；最多回傳最新 100 筆 |
| `DELETE /api/watchlist/1` | 最後才測，或使用專門加入的商品 ID | 204；watchlist 移除，持倉與歷史保留。可 POST 重新加入 |

錯誤情境：不帶 mutation header 回 403；不存在的商品回 404；移除追蹤後 force 回 409；負數持倉回 400；Yahoo 不可用或 JSON 結構異常回 503。單一 AI 失敗會列在 force 的 provider 狀態內，其餘 provider 繼續執行。

## 最短行情驗證流程

```powershell
$base = 'http://localhost:8080'
$headers = @{ 'X-Market-Signal' = 'web' }
$before = Invoke-RestMethod "$base/api/products/00631L/analysis/history"
Invoke-RestMethod -Method Post -Uri "$base/api/products/00631L/analysis/force" -Headers $headers
Invoke-RestMethod "$base/api/products/00631L/market"
$after = Invoke-RestMethod "$base/api/products/00631L/analysis/history"
"History: $($before.Count) -> $($after.Count)"
```

## 排程驗證

```powershell
dotnet test MarketSignalAI.sln --filter 'FullyQualifiedName~ScheduleTests|FullyQualifiedName~WorkerTests'
docker compose logs -f backend
```

MySQL 模式 worker 預設啟用；Memory 模式不啟用。08:30 僅保存 `TradingDays`，其餘四個時段分析啟用且被追蹤的台股商品。日誌可看到 `Schedule started`、`Market OPEN/CLOSED`、`Market snapshot saved`、`AI analysis completed` 與 `Schedule completed`。

## 目前範圍與限制

- AI 是明確標示的 mock，尚未串接付費 AI API；Google OAuth 亦屬後續階段。
- 排程為單程序設計，停機時段不補跑，同分鐘重啟可能再次執行；尚未實作跨實例鎖。
- 交易日依 TWSE 年度日曆判定，不涵蓋未列入年度日曆的臨時休市。年度日曆為空、年份不符或格式異常時，本次修正會停止排程分析。
- Yahoo 可能延遲、限流或改版；force 使用可取得的最新行情，排程要求行情日期與交易日相同。