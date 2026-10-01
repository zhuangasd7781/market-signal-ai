# OpenAI GPT 串接

OpenAI provider 使用 **Responses API**。模型與 reasoning effort 完全從 configuration 讀取，預設值放在 `backend/MarketSignalAI.Api/appsettings.json`：

```json
"OpenAI": {
  "Enabled": false,
  "Model": "gpt-6.1-sol",
  "ReasoningEffort": "medium",
  "MaxOutputTokens": 8192
}
```

未啟用時維持明確的 OpenAI mock；啟用後只會使用 `OpenAIAnalyst`，呼叫失敗絕不降級為 mock success。DeepSeek 可啟用真實 API；Claude 繼續使用 `mock-v1`。API key 不放在 appsettings 或 Git 中。

## 啟用

本機透過 .NET User Secrets 提供 `OpenAI:ApiKey` 和 `DeepSeek:ApiKey`，由既有 `IConfiguration` 讀取。不要輸出 Key 或把 Key 寫入 appsettings / Git。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-api-mysql.ps1 -LiveAI
```

`-LiveAI` 啟用兩個真實 Provider 並停用排程。沒有此選項時依原設定決定是否啟用；停用時仍為明確的 mock。Model、ReasoningEffort、MaxOutputTokens、TimeoutSeconds 由設定取得。啟用但設定錯誤會在該 Provider 執行時記錄 Failure，不阻止其他 Provider，也不降級為 mock。

主機 User Secrets 不會自動進入 Docker；本機開發以 http://127.0.0.1:5080 的 API 和 http://127.0.0.1:5173 的 Vite 前端驗證。不要將主機 Secret 打包進 image。

## 呼叫與結果

```powershell
$headers = @{ 'X-Market-Signal' = 'web' }
# Docker 基底；本機 dotnet 模式改成 http://localhost:5080
$base = 'http://localhost:8080'
$run = Invoke-RestMethod -Method Post -Uri "$base/api/products/00631L/analysis/force" -Headers $headers
$run.providers | Select-Object provider,status,model,usage,error
Invoke-RestMethod "$base/api/products/00631L/analysis"
Invoke-RestMethod "$base/api/products/00631L/analysis/history"
```

成功的 `openai` 結果會使用 OpenAI 回應中的實際 model ID，並含：

```json
"usage": {
  "inputTokens": 1234,
  "outputTokens": 567
}
```

上列數字僅為格式示例。mock 與舊歷史的 usage 為 null。HTTP 401、429、5xx、逾時、refusal、incomplete 或無效 JSON 都會產生 provider `FAILED`，不寫入成功分析。其他 provider 可繼續完成，所以 force 的 HTTP 200 不代表所有 provider 成功；須檢查各自 status。

## 資料與分析規則

- 僅傳入商品、行情、歷史日價、持倉數量／成本與前次真實決策，不傳 API Key 或使用者帳戶識別資料。
- 使用 `text.format.type=json_schema`、`strict=true` 對應目前統一 Analysis 結構，保留既有 API 結果格式。
- 分別載入 `AI/Skills/market-evidence.md`、`decision-rules.md`，槓桿商品再加入 `leveraged-etf.md`。檔案隨 assembly 打包，不依賴執行目錄。
- 沒有新聞或法人資料時必須承認資料不足，不捏造 Root Event；不把獲利本身當成賣出理由。數量依商品單位，不能減碼超過已知持倉。
- 前次 mock 訊號不會當成真實決策餵給 GPT。成功紀錄的 InputSnapshotJson 保存完整分析 context、實際 instructions 與 reasoning effort，RawResponse 保存 OpenAI 原始回應。
- `store=false`；應用程式自行將成功分析與必要歷史保存於 MySQL。各 Provider 的 TimeoutSeconds 預設為 180 秒，可設為 1–600 秒，沒有自動重試。`MaxOutputTokens` 同時限制可見輸出及 reasoning tokens；不足時會失敗並保留回應提供的 usage。

## MySQL 與成本統計準備

`003_openai_usage.sql` 新增兩張表，API 啟動會冪等建立；自行初始化時依序套用 001、002、003。

- `AIAnalysisUsage`：AnalysisId、InputTokens、OutputTokens、CachedTokens，與成功分析在同一交易寫入。
- `AIProviderFailures`：provider、商品、使用者、model、reasoning effort、安全錯誤訊息、時間與可取得的 token usage。

查詢成功分析的 usage：

```sql
SELECT a.Id, a.Model, a.CreatedAt, u.InputTokens, u.OutputTokens
FROM AIAnalysisResults a
JOIN AIAnalysisUsage u ON u.AnalysisId = a.Id
ORDER BY a.Id DESC;
```

統計已知成功與失敗用量：

```sql
SELECT Model, SUM(InputTokens) AS InputTokens, SUM(OutputTokens) AS OutputTokens
FROM (
  SELECT a.Model, u.InputTokens, u.OutputTokens
  FROM AIAnalysisResults a JOIN AIAnalysisUsage u ON u.AnalysisId = a.Id
  UNION ALL
  SELECT Model, InputTokens, OutputTokens FROM AIProviderFailures
) runs
GROUP BY Model;
```

失敗時若 OpenAI 仍回傳 usage（例如 incomplete），會保存；逾時、網路中斷等無回應情境使用 null，不能解讀為零成本。成功的 RawResponse 也保留原始 usage 明細，包括 API 提供的快取／reasoning token 資料。此階段尚未計算金額，未來須結合模型、當時費率、快取明細與帳單核對。

## 驗證

```powershell
dotnet build MarketSignalAI.sln
dotnet test MarketSignalAI.sln
# Docker MySQL 已啟動時，執行完整測試（含隔離資料庫）：
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-backend-mysql.ps1
```

2026-10-01 驗證：47 項測試通過，含 Responses 請求內容、預設模型／effort、設定覆寫、schema 欄位、usage、拒絕／未完成／限流／無效回應、provider 失敗隔離、MySQL 交易保存、API 重啟與排程資料庫流程。測試使用 HTTP 替身，不消耗 API 額度；MySQL 測試使用新建的隨機命名資料庫並於結束清除。

本機 Docker 已更新，`http://127.0.0.1:8080/health`、Swagger、watchlist 與 Yahoo force 均驗證正常；003 新表已建立。2026-10-01 13:05 的真實排程也完成三個追蹤商品（GPT 未啟用，分析仍為 mock）。若 localhost 在本機解析緩慢，可直接使用 127.0.0.1。

**2026-10-01 已完成真實雙 Provider E2E**：GPT 回報 `gpt-6.1-sol`，分析 ID 54；DeepSeek 設定 `deepseek-chat`、回報 `deepseek-flash`，分析 ID 53。兩筆實際 usage 已保存並在 React 商品页確認。完整紀錄見 [ai-e2e.md](ai-e2e.md)。

官方參考：[GPT-6.1 Sol](https://developers.openai.com/api/docs/models/gpt-6.1-sol)、[Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)、[Reasoning 與 usage](https://developers.openai.com/api/docs/guides/reasoning)。
最新 Context 已加入 marketReferences，以及共享、Provider 標記的 previousDecisions；兩個 Provider 使用相同 analysisInput。新版設計與實測见 [Market Reference 說明](market-references.md)。
