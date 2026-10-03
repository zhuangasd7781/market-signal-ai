# Analysis Pipeline 行為不變重構

直接在目前 main 開發，保留既有 nginx.conf 修改與 Task 筆記。沒有新增 API、DB migration、Prompt 修改、UI 或排程行為。

## 責任拆分

原本 MarketAnalysisRunner 169 行（含空行），同時處理 product/users/provider selection/settings、news resolve、Yahoo snapshot persistence/history/returns、TW context、references、position、previous decisions、prompt、shared input、provider execution/model/timeout/validation、success/failure persistence、batch reuse。

- MarketAnalysisRunner：product / tracking users、provider settings 與 requested subset 驗證、resolve shared news、管理 Force / batch evidence scope、依序委派 builder 與 execution、彙整結果。Constructor 10 → 7 個依賴（含 logger）。
- AnalysisContextBuilder：FetchMarketAsync 取得與保存 target quote、檢查 trade date、history 與 TW context／returns；BuildAsync 讀取 position、previous decisions、references、active prompt，與已 resolve 的 NewsEvidenceContext 組成 MarketContext，產生一次 shared JSON input。它沒有 News resolver 或 analyst dependency。
- AIProviderExecutionService：adapter lookup、WithModel、timeout、呼叫 analyst、AnalysisResultValidator、result / failure normalization、usage 與現有 IMarketStore success/failure persistence。沒有 ISignalStore、行情／TW／Reference／News／Prompt provider 依賴。
- AnalysisInputSnapshot：internal static 序列化 helper，單一 audit snapshot 組裝處；保留舊 property order/casing/value 與 NewsContextId 追溯。沒有另建 persistence interface，Store 的 transaction 寫入不變。
- AnalysisEvidenceScope / ProductMarketEvidence / PreparedAnalysisContext：internal 暫態資料，包裝 request/batch caches 與已取得的證據，不進入 API/DB serialization，也不是新增 Domain model。

所有元件位於既有 Infrastructure，沿用 Application 的 abstraction。DI 只新增兩個 scoped concrete services；沒有 service locator、circular dependency、新框架或新增細碎 interface。

## 行為保留

provider selection/settings 先驗證，然後 resolve news，再取得商品 evidence、建立每個使用者完整 context，最後執行所有 selected providers。先前決策在本輪任何 provider INSERT 前取得；不把本輪 DeepSeek result 填進 GPT 的 previous decisions。每個 user 的 providers 共享同一 MarketContext instance / news / references / prompt / previous decisions。

批次共用同一 AnalysisEvidenceScope：ReferenceDataCache、TwMarketContextBatchCache 保留原實作；News resolver 每 batch 一次。各 Force 另開 scope。refresh-before-analysis、stored preference/explicit override、failure fallback、no-news、STALE 均沿用原服務與 Endpoint。

位置限制校驗保留在現有 OpenAIAnalyst / DeepSeekAnalyst 的 AnalysisProtocol.ValidatePosition；沒有改變 adapter 的 schema、prompt 或 validation。Execution 繼續使用原本 Runner 的 AnalysisResultValidator，沒有增加會改變 Mock / 自訂 adapter 行為的新驗證。

## 測試

在修改 Runner 前，以 deterministic fake market/reference/TW/position/prior/prompt/news/provider 建立 Fixtures/analysis-input-before-refactor.json，25,669 bytes。重構後以完整字串逐 byte 比對，包含 root audit JSON、embedded analysisInput、Prompt/skills/news/value/units；不是只比欄位或 ID。

所有既有 assertions 保留；只將舊 constructor 的 test setup 換成 AnalysisRunnerFixture 組裝兩個元件，沒有 production compatibility constructor 或雙路徑。新 tests 可透過 InternalsVisibleTo 直接測 component boundary。

新測試涵蓋 builder 不重新取得已 supplied target/news、Reference/TW scope reuse/reset、execution write-only store boundary、failure隔離與usage/model、相同 context 與 previous decisions、不污染原shared JSON、NewsContextId persistence、missing adapter / timeout 之後仍執行下一個 provider。

Backend build 0 warning / error；198 pass / 1 既有 opt-in MySQL skip（199 total）。Frontend build success；25 Playwright tests pass（隔離 Memory backend，未使用 UAT 做 CRUD regression）。

## 刻意保留的現有限制

MarketContext records / IReadOnlyList 提供既有 shared/frozen 語意，但 collection 未全面改為強制 deep immutability；不在此 refactor 改型別或 serialization。WithModel 在 provider try/catch 之前，adapter 配置錯誤可能成為 product-level failure；維持既有處理次序，沒有順便修 bug。Force 目前分析所有追蹤該商品的 user，Google Login 前需要另外釐清 multi-user orchestration，這次不改 demo 行為。

AnalysisRun 未實作。未來可在 Runner 建立 run identity，傳入 PreparedAnalysisContext / execution persistence；context building 與 provider execution 已分離，無須把資料取得再加回 Runner。仍需獨立設計 DB/API 的 AnalysisRun contract。

## 真實 DEV Force 驗證

- DeepSeek-only + explicit refreshNewsBeforeAnalysis=false：Result #82，actual model deepseek-v4-pro，HOLD / 60%，NewsContext #4（20 events），refresh 前後 NewsContext ID 皆 4。Input 31001 / Output 5069 / Cached 5248；本次只有 deepseek provider 被執行。
- GPT + DeepSeek + refreshNewsBeforeAnalysis=true：先建立 NewsContext #5，20 events；Flash input 22211 / output 3409 / cached 1408。DeepSeek Result #83，deepseek-v4-pro，HOLD / 60%，input 30506 / output 3908 / cached 5248。GPT Result #84，gpt-6.1-sol，HOLD / 60%，input 29531 / output 1545 / cached 0。
- Results #83/#84 的整份 analysisInput SHA256 同值 38e0b3014f0cad6a10f676f2783ee31df16cff82600e43bd7a577d2460feca47；NewsContextId=5 / eventCount=20。Previous decisions 的 DeepSeek 時間均為 #82 的 15:54:40.931166Z，沒有把本輪 #83 的 15:56:58.307266Z 混入 GPT input。
- 三筆 Results / Usage 正常 INSERT，這輪 ProviderFailures 0。API GET 能讀取新結果。為 E2E temporary enable GPT，finally 恢復原先 false，DeepSeek true / Claude false 不變。使用者原本 News refresh preference 為 true，未修改；第一個 reuse 驗證明確傳 false 覆寫。
- 使用暫時 console harness 直接組裝重構後相同 runner/builder/execution 與真實 MySQL / Yahoo / TWSE / DeepSeek，驗證 schedule/batch，沒有新增 API 或把測試入口加進產品。2026-10-03 真實 Saturday 被 calendar 判為 CLOSED，scheduled path 不產生 AI analysis，不 Refresh News。
- 真實 DEV 28 組 browser views（7 routes ×1280/390 ×light/dark）：console/page error 0、overflow 0、mutation/付費 request 0。

- 真實 RunAllAsync 批次成功：當時追蹤商品為 00631L 與 2330；只執行 enabled DeepSeek（GPT disabled）。00631L Result #85，HOLD / 60%，input 30396 / output 5507 / cached 5248。2330 Result #86，HOLD / 70%，input 20440 / output 4449 / cached 3328。兩者 NewsContextId=5；batch Refresh calls=0，無 product failure。並非 Mock 結果。
