# TASK 2：AI Provider settings

分支 `feature/ai-provider-settings` 從最新 `origin/main` / `e1f5adf` 建立。開始檢查時 main 尚為 1651b52，但建立分支時遠端 main 已更新為 TASK 1 的 e1f5adf（remote reflog: update by push，2026-10-02 00:04:07 +0800）；本分支自然繼承已合併的 TASK 1，沒有 cherry-pick 或自行 merge。兩輪新分析的 Taiwan50 TWSE history 都是 21 筆。本輪只實作 TASK 2，未實作 Schedule / Prompt / History Table / Reference UI。

## 設計與操作

新增 `AIProviderSettings(UserId, Provider, Enabled, ConfiguredModel)`，主鍵 UserId + Provider，Users foreign key。沿用 MySQL / Memory stores、既有 Runner、Analysts 和 schema。UserId 由 ICurrentUser 取得，設定適用該使用者所有商品；目前仍是共用 demo user 1。

首次設定來自既有 OpenAI / DeepSeek configuration；Claude 預設 OFF / mock-v1。MySQL INSERT IGNORE 只初始化缺少的設定，不覆寫使用者修改。之後 DB 是 Enabled / ConfiguredModel 的來源；啟動參數 -LiveAI 不會重新啟用已保存為 OFF 的 provider。Memory 儲存重啟會重置，MySQL 設定持久化。

OpenAI / DeepSeek 不再以 Disabled 自動產生 Mock 分析；Disabled 直接跳過 adapter，不呼叫 API、不建立成功或失敗假結果。Claude 可以啟用，但本階段只允許 mock-v1。既有歷史與 latest API 保留所有舊 Provider 結果，停用不會刪除或隱藏歷史。

新設定頁 `/settings/ai`，從上方導航或 footer 進入。可以查看 Provider、勾選 Enabled、修改 Configured Model 並儲存，顯示最近分析 Actual Model。Claude 的模型唯讀，由 Backend 回傳。React 沒有 OpenAI / DeepSeek model 常數，不提供 API Key 欄位。GET / PUT 設定只讀寫資料，不執行 AI。

API：

| Method | Route | Body / Response |
| --- | --- | --- |
| GET | /api/ai/settings | Provider / DisplayName / Enabled / ConfiguredModel / ActualModel / IsMock |
| PUT | /api/ai/settings/{provider} | `{ "enabled": false, "configuredModel": "model-id" }`，成功 204 |
| POST | /api/products/00631L/analysis/force | 可不帶 body；或 `{ "providers": ["DeepSeek"] }` |

Mutation 仍須 X-Market-Signal: web。Force providers 不分大小寫，必須是非空、無重複的已知 Provider codes；指定已停用者回 400，先在設定啟用。空 array、null 成員、未知 Provider、重複或 malformed JSON 在抓行情之前被拒絕。省略 body / providers 或 providers=null 使用 Enabled 列表；若全部 OFF，可保存 Yahoo snapshot，但不執行任何 AI。

```powershell
$base = 'http://127.0.0.1:5080'
$headers = @{ 'X-Market-Signal' = 'web' }
Invoke-RestMethod "$base/api/ai/settings"
Invoke-RestMethod -Method Put "$base/api/ai/settings/openai" -Headers $headers -ContentType 'application/json' -Body '{"enabled":false,"configuredModel":"gpt-6.1-sol"}'
Invoke-RestMethod -Method Post "$base/api/products/00631L/analysis/force" -Headers $headers -ContentType 'application/json' -Body '{"providers":["DeepSeek"]}' -TimeoutSec 420
```

Worker 沿用 RunAll / Runner，同樣讀取 Enabled 設定。每輪開始擷取設定，模型透過 WithModel 建立單輪 adapter options，不修改共用 Options 或其他 Provider。進行中的分析使用啟動當時設定，更新影響下一輪，不中斷已送出的請求。

ConfiguredModel 保存於本輪 InputSnapshotJson，Force response 也回傳 configuredModel；AIAnalysisResults.Model / AnalysisView.Model 仍保存 API 真實回傳模型。例：configured-deep / reported-deepseek 由測試驗證，不把配置名稱冒充實際名稱。現有 token / cached token 儲存和 Provider Failure isolation 保留，API Key 仍只由 IConfiguration / User Secrets 取得。

## 真實驗證

證據：[ai-provider-settings-evidence.json](ai-provider-settings-evidence.json)。

1. Edge 實際登入 React :5173 設定頁，OpenAI OFF / DeepSeek ON / Claude OFF；透過 UI 修改 DeepSeek Model 後還原，再 reload，狀態保存。設定操作前後 analysis / usage DB 計數相同；UI 無 Force 請求、無 page errors，390px 手機版無頁面溢出。
2. 不帶 body 真實 Force：snapshot 19，僅 DeepSeek ID 71，deepseek-v4-pro，HOLD / 55；tokens 10746 / 6485 / cached 384。
3. 暫時 OpenAI ON，POST providers=[DeepSeek]：snapshot 20，僅 DeepSeek ID 72，deepseek-v4-pro，HOLD / 58；tokens 10588 / 6763 / cached 2304。完成後 OpenAI 恢復 OFF。
4. 本機 Backend HTTP client logs：DeepSeek POST 2 次、OpenAI POST 0 次。DB：DeepSeek result 22→24、Usage 6→8；OpenAI result 24 / Usage 8 / latest ID 69 完全不變；Claude result 24 / latest ID 70 不變。ProviderFailures 保持原有 2，新增 0。
5. 既有 React 商品頁 latest HTTP 200，DeepSeek 顯示 ID 72，Usage 一致，歷史可讀，page errors=0；只做 GET，沒有額外 AI 呼叫。

## Tests / 限制

- 全套隔離 MySQL tests：113 passed / 0 failed / 0 skipped。原有 Provider isolation / snapshot / usage / reference tests 保留。舊測試 fixtures 明確啟用 Mock，Production OpenAI / DeepSeek 沒有 fallback Mock。
- 新測試包括：設定 GET / SAVE 不執行 Analyst、user scoping、validation、Enabled filter、指定 DeepSeek 時 GPT HTTP count=0、batch filter、全部 OFF、model 更新下一輪生效且歷史不變、Configured / Actual 差異、optional / malformed body、MySQL 重啟 seed 不覆写 OFF。
- AI 設定 Playwright regression：1 passed，所有 API 以 route fixtures 隔離，不呼叫付費 AI。React build 通過。Backend 最終 build：0 warnings / 0 errors。本機使用 -LiveAI 重啟後，再讀設定仍為 OpenAI OFF / DeepSeek ON / Claude OFF，已保存的模型亦不變。
- Model 儲存只驗證 ID 格式，不會呼叫外部 API 查驗是否存在；真正分析時若無權限、模型不存在或 Secret 缺失，沿用 Provider Failure，不假造成功。
- ActualModel 是該 Provider 最近一筆歷史模型，可能是舊 Mock；切換 ConfiguredModel 不改寫舊結果。更完整的 Prompt / Schedule 設定屬後續獨立任務。
