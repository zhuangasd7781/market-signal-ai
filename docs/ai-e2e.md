# GPT / DeepSeek 真實 End-to-End 驗證

日期：2026-10-01。沿用 IAIAnalyst、MarketContext、Analysis、既有 schema / Skills / Validator / Runner 和 MySQL repository；沒有建立第二套分析架構。先檢查 git status / diff、README 和既有 docs；保留原有未提交修改，不提交任何 Secret。

## 啟動與設定

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-api-mysql.ps1 -LiveAI
# 另開終端機，於 frontend 目錄執行 npm run dev
$headers = @{ 'X-Market-Signal' = 'web' }
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5080/api/products/00631L/analysis/force -Headers $headers
```

Api 的既有 UserSecretsId 自動載入 Development User Secrets；Key 僅由 IConfiguration 讀取 `OpenAI:ApiKey` / `DeepSeek:ApiKey`，不列出 secrets，也不記錄 Key。`-LiveAI` 只設定本次程序 Enabled=true、MarketWorker=false，避免背景額外 API 消耗。OpenAI 的既有環境設定相容性保留。

| Provider | 設定 | 本次實際回報 |
| --- | --- | --- |
| OpenAI | Model=gpt-6.1-sol、ReasoningEffort=medium | gpt-6.1-sol |
| DeepSeek | Model=deepseek-chat、BaseUrl=https://api.deepseek.com/ | deepseek-flash |
| Claude | MockAIAnalyst | mock-v1 |

兩個真 API Provider 的 Model、MaxOutputTokens、TimeoutSeconds 都可設定；DeepSeek BaseUrl 可設定。Enabled 預設 false，離線測試明確關閉真實 Provider。啟用後遇錯只記 Failure，不 fallback。

OpenAI 保留 Responses API 與 strict json_schema。DeepSeek 使用官方 `/chat/completions`、`response_format: json_object`，提示共用 schema，依序 JSON Parse → 必填欄位／enum 檢查 → Deserialize（禁止多餘欄位）→ 共用 Validator → 持倉規則檢查。共用現有 Skills 與相同的市場／持倉輸入序列化，前次決策只用各 Provider 自己的真實紀錄。

## 真實外部請求與 MySQL

使用既有 Force API 共執行三次。每次 Yahoo 行情與歷史日價、DeepSeek 和 OpenAI 真實 HTTP 都回 200；沒有 HTTP / 網路錯誤。

前兩次 DeepSeek 回應分別違反持倉规则與共用 Validation，未保存成成功結果。依真實錯誤補強共用無持倉指示與 DeepSeek JSON literal null / integer / 非空陣列指示，沒有放寬 Validator 或偽造答案。第三次兩個 Provider 全部完成。下表為最終一次：

| 項目 | 實際結果 |
| --- | --- |
| MarketSnapshots | INSERT ID 13，00631L，Price=39.62 |
| MarketTime | 2026-10-01 13:30:00 Asia/Taipei |
| FetchedAt | 2026-10-01 18:15:56.569167 Asia/Taipei |
| 歷史日價 | 20 筆保存於分析 InputSnapshotJson |
| DeepSeek AIAnalysisResults | INSERT ID 53，deepseek-flash，HOLD，quantity=null，confidence=45 |
| OpenAI AIAnalysisResults | INSERT ID 54，gpt-6.1-sol，HOLD，quantity=null，confidence=54 |
| Claude AIAnalysisResults | INSERT ID 55，mock-v1；沒有付費 API usage |
| GPT AIAnalysisUsage | AnalysisId=54，Input=4009，Output=908，Cached=0 |
| DeepSeek AIAnalysisUsage | AnalysisId=53，Input=2911，Output=892，Cached=384 |

MySQL SQL 比較分析 53 / 54 的 product、snapshot、history、position JSON，四項全部一致。00631L 當時沒有已保存持倉，兩個 Provider 都收到 null；沒有為測試捏造或修改持倉。usage 使用 API 真實數字、不估算；Provider / Model 可透過 AnalysisId 與 AIAnalysisResults / AIProviders 關聯取得。結果與 usage 在同一交易 INSERT。

## Failure 紀錄

| ID | Provider | 安全錯誤訊息 | Input | Output | Cached |
| --- | --- | --- | --- | --- | --- |
| 1 | deepseek | AI analysis exceeds the available position. | 2802 | 744 | 0 |
| 2 | deepseek | AI response failed validation. | 2858 | 632 | 384 |

上述 Failure 的 configured model 是 deepseek-chat；無成功分析 ID。當次 GPT 仍保存成功分析 49 / 51 與 usage。沒有以 Mock 代替失敗的 DeepSeek；第三次沒有新增 Failure。失敗已知 usage 存在 AIProviderFailures，成功 usage 存在 AIAnalysisUsage。HTTP / 網路未取得 usage 時保持 null，不解讀成零成本。

CachedTokens 是既有兩張 usage / failure 表的 nullable 增量欄位；API 啟動在既有冪等 migration 後檢查 information_schema，缺少才新增。現有與新資料庫、再次啟動都驗證可用。舊紀錄未提供 cache 時維持 null。

## 實際分析摘要（模型輸出，並非驗證過的投資結論）

GPT：HOLD / 54。短線 ETF 趨勢偏多，但 39.65 附近突破尚缺量能確認；無持倉、預算與風險額度，不給買進張數。短線偏多失效條件為更新後日收盤跌破 38.16；nextActions 保持 HOLD，等待價格／量能與資金資料。

DeepSeek：HOLD / 45。缺持倉與購買預算，維持觀望；短線反彈偏多，量能不足、槓桿耗損及缺標的指數資料限制信心。失效條件包含跌破 35.98 或無法站穩 39.6 且量縮；nextActions 等待指數、事件及部位大小資料。

完整 reasons / risks / bullCase / bearCase / invalidation / nextActions 均保存於 AnalysisJson，既有 latest / history API 可讀取。API 不傳 RawResponse、InputSnapshotJson 或 Key。

## Build、測試與瀏覽器

- Backend build 成功；Backend 測試含隔離 MySQL 共 65 項全部通過。
- 覆蓋兩個真實 adapter 註冊、schema / 缺欄位 / malformed JSON / enum / 持倉驗證、HTTP 401 / 402 / 429 / 500、取消、actual / cached usage，以及 GPT / DeepSeek 雙向失敗隔離。
- MySQL 測試驗證交易保存、failure、API 重啟讀回、cached usage 讀回及 migration 重複執行；隨機命名測試庫於結束清除，未清除既有 DB。
- Frontend TypeScript / Vite build 成功。
- Headless Microsoft Edge 實際登入既有示範 React Frontend，打開 `/products/00631L`，透過 Vite proxy 讀 MySQL latest API；確認 DeepSeek 53、GPT 54、Claude 55 的模型、reason 與 usage 對應，分析歷史可見，page errors=0。此瀏覽器驗證只 GET，不另消耗付費 API。
- 修正詳情頁「以下皆為示範內容」與全部「示範信心值」標籤；真實與 Mock 分析依既有 model 顯示，不改版。

可檢視 http://127.0.0.1:5173/products/00631L；backend http://127.0.0.1:5080。Docker :8080 仍是既有容器，未部署此次程式；它與本機程序共用 MySQL，因此可讀資料，但不是此次新版 backend。主機 User Secrets 不會自動提供給 Docker。

## 修改檔案

- Api：Program.cs、appsettings.json。
- Application：MarketContracts.cs、AnalysisResultValidator.cs。
- Domain：Models.cs。
- Infrastructure：OpenAIAnalyst.cs、OpenAIAnalystOptions.cs、DeepSeekAnalyst.cs（新增）、DeepSeekAnalystOptions.cs（新增）、AI/AnalysisProtocol.cs（新增）、MarketAnalysisRunner.cs、MySqlMarketStore.cs、MySqlSignalStore.cs。
- Frontend：src/main.tsx。
- Script：scripts/run-api-mysql.ps1。
- Tests：ApiTests.cs、OpenAIAnalystTests.cs、OpenAIFlowTests.cs、DeepSeekAnalystTests.cs（新增）、LiveProviderFlowTests.cs（新增）。
- Docs：README.md、docs/implementation.md、docs/openai-integration.md、docs/ai-e2e.md（新增）。

未修改既有 schema 或 Skills 檔案；共用 helper 僅抽出兩個真實 Provider 必須一致使用的既有內容。未進行 Commit；任務開始前的其他未提交修改全部保留。

## 已知限制

- DeepSeek JSON mode 不保證符合 schema；失敗時仍會拒收、記 Failure，沒有自動重試或假成功。真實輸出有非決定性。
- 結構／持倉 Validation 不等於事實核對：本次 DeepSeek 在 bullCase 將 39.62 描述為突破 39.65，並在 bearCase 提及供給日期區間之外的 8/22。結果原樣保留；這些文字是模型錯誤，不能視為已核實資訊。本次沒有新增事實驗證系統。
- Yahoo 報價是當日收盤價，擷取時已延遲約 4 小時 46 分；歴史資料與 previousClose 的日期／數值差異仍須留意。
- 沒有新聞、法人、標的指數、購買預算／風險額度；模型只能以供給 context 判斷。
- 共用 Runner 逐一執行 Provider，各有獨立 timeout / failure / 交易；沒有並行吞吐改造。
- demo 身份、共用帳戶與單程序排程仍維持既有設計；Claude 仍為 Mock。沒有 Billing Dashboard 或成本估價，也未進行部署。

官方參考：[OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)、[DeepSeek JSON Output](https://api-docs.deepseek.com/guides/json_mode/)、[DeepSeek Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)。

## DeepSeek V4 Pro 更新（2026-10-01）

目前 DeepSeek:Model 已改為 deepseek-v4-pro，仍由 IConfiguration 讀取並可覆寫。上述 deepseek-chat / deepseek-flash 為先前驗證記錄。

本機 :5080 重啟後，00631L 真實 Force Analysis snapshotId=17：DeepSeek 實際模型 deepseek-v4-pro，分析 ID 65，HOLD / confidence 56，inputTokens=10828、outputTokens=8177、cachedTokens=0；GPT ID 66，HOLD / confidence 55；Claude ID 67 仍為 mock-v1。本輪沒有 Provider Error，既有 React :5173 proxy 的 latest API 可讀取已保存的結果及 Usage。提交前 MySQL 全套測試 78 passed / 0 failed / 0 skipped，Frontend build 成功。