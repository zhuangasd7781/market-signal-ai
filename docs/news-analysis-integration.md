# 市場情報接入投資分析

## 行為與邊界

在目前 main 直接實作，沿用 MarketContext / MarketAnalysisRunner / AnalysisProtocol。新增巢狀 NewsEvidenceContext，不將原始文章或 collection audit 欄位傳入投資模型。

Runner 在 Provider 之前透過 INewsEvidenceResolver resolve 一次：單商品 Force 共用一份快照；Scheduled batch 所有商品共用同一份。OpenAIAnalyst / DeepSeekAnalyst 的依賴與資料取得責任不變，不查新聞 DB、不搜尋、不呼叫 Flash。

預設從 INewsContextStore 取得最新 AVAILABLE / PARTIAL snapshot。一般 Force 與 Scheduled 不會 Refresh News，也不會產生額外 Search / Flash 成本。無有效新聞或讀取失敗，傳入 UNAVAILABLE 空事件，仍執行商品分析。

NewsIntelligence:MaxAgeMinutes 預設 180，集中於 Backend Configuration，可用環境變數 NewsIntelligence__MaxAgeMinutes 覆寫。以 resolve 時間與 GeneratedAt 判斷：恰好 180 分鐘仍 FRESH；超過標示 STALE 並保留完整舊情報。未來時間標示 UNKNOWN，避免誤認最新。Status 保留 collection 的 AVAILABLE / PARTIAL；Freshness 是獨立欄位。

## Force API / UI

POST /api/products/00631L/analysis/force（X-Market-Signal: web）：

```json
{
  "providers": ["openai", "deepseek"],
  "refreshNewsBeforeAnalysis": true
}
```

不傳 refreshNewsBeforeAnalysis：套用目前使用者在 AI 設定頁儲存的偏好（初始 false）。明確 false：重用現有 NewsContext。明確 true：先 Refresh rolling 72h，成功後 freeze 新情報，再抓 Target、History、References 與 TW Market Context，最後執行 Provider。

Refresh 失敗：沿用上一份有效 Context，NewsRefreshFailed=true；沒有上一份則 UNAVAILABLE。使用者取消會傳遞 cancellation，不偷偷繼續。Provider selection / disabled 設定仍先驗證，不因無效請求額外 Refresh。

商品頁保留「強制分析」；「分析前更新市場情報」已移入 AI 設定頁的「市場情報設定」，預設關閉。僅執行已啟用 Providers；UI 載入本身沒有付費呼叫。提交期間 disable 並以 ref 防止重複提交。分析卡片與歷史展開呈現 NewsContext ID、更新時間、Freshness、事件數與 Refresh failure，連結 /news/{id} 查看不可變歷史。

## Prompt / Snapshot

AI/Skills/news-evidence.md 共用強制語意規則：Direction 不映射 Action、事件 Sources 不重複加權、STALE / UNKNOWN / RECAP / 低信心降低權重、分辨 PublishedAt 與 EventTime、缺新聞明確說明、禁止補新聞或自搜尋；相較 prior decision 若由新聞改變 action，指出 observable supplied event 並連結價格與風險報酬。

既有使用者 Active Prompt 不覆寫、不新增假版本；本次強制共用規則加入 AnalysisProtocol 與帶內容 SHA256 的 skill snapshot，所以歷史可還原實際規則。投資 JSON Schema 與 ADD/HOLD/REDUCE/EXIT enum 不變。

InputSnapshotJson.newsContext 保存完整 NewsEvidenceContext；analysisInput.newsContext 是實際供兩個 live adapters 序列化的 Web JSON。包含 ID、UTC 時間窗口、Generation、resolve 時間、MaxAge、Status、Freshness、Refresh failure、實際 Events/Source 值。沒有 RawSearchSnapshotJson / RawResponse / 完整文章。

## DB

010_analysis_news_context.sql：AIAnalysisResults 新增 nullable BIGINT NewsContextId 與 IX_AIAnalysisResults_NewsContextId。使用既有 MySqlMarketStore 啟動時欄位檢查，首次套用，後續重啟不重加欄位。原有 Results 保持 NULL。這是 indexed logical reference，沒有新增外鍵（沿用目前啟動 migration 順序，NewsContexts 在後面建立）；完整 input snapshot 為歷史還原來源。

Result / Usage 仍沿用同一交易 INSERT。新聞表與投資表不 redesign，沒有 AnalysisRun。

## 真實 DEV E2E：2026-10-03

只執行一次 refresh-before-analysis，temporary enabled OpenAI 完成後恢復 false；DeepSeek true / Claude false，visible 狀態均維持。Worker 關閉，DEV 使用既有 Docker MySQL。

- NewsContext #2，19 Events。
- UTC WindowStart 2026-09-30T15:10:05.429939Z，WindowEnd 2026-10-03T15:10:05.429939Z。
- GeneratedAt 2026-10-03T15:10:21.757294Z，台北 23:10:21。
- Flash actual model deepseek-flash；Input 22384 / Output 3148 / Cached 1408。
- DeepSeek Result #78，actual model deepseek-v4-pro，HOLD / 60%，Input 27274 / Output 5305 / Cached 2944。
- GPT Result #79，actual model gpt-6.1-sol，HOLD / 60%，Input 26346 / Output 1580 / Cached 0。
- 兩筆 AIAnalysisResults.NewsContextId=2；InputSnapshot 的 analysisInput.newsContext.events 均 19，Freshness=FRESH；SQL SHA2(news snapshot,256) 同值 ac038777fac42175ac74131fa8f3e2f2ce192de059a554135901644bbd8fa2c4。
- 兩筆 AIAnalysisUsage 已 INSERT；本輪 AIProviderFailures 0。

Backend Build 0 warnings / errors；193 tests pass、1 既有 opt-in MySQL test skip（total 194）。Frontend Build success；24 Playwright tests pass，全部在隔離 Memory test backend 執行，CRUD 不觸碰 UAT。

新測試涵蓋 freshness 邊界、refresh 成功順序、失敗有舊／無舊、storage failure、Force / batch reuse、latest-valid selection、snapshot persistence 與 API projection、兩個 live adapter HTTP body 同 JSON、共用 evidence-only instructions、Force API options、UI option / loading / error / dark mobile。fake APIs 不產生 CI 費用。

## 已知限制

RSS coverage 仍 PARTIAL；UNKNOWN 事件時間保持未知。新聞摘要是 supplied evidence，不代表事實查核或交易指令。UI freshness 記錄當次分析 resolve 狀態，不隨現在時間改寫舊分析。Refresh failure 具旗標與安全說明，詳細 collector failure 仍由既有 NewsRefreshFailures 追查。沒有額外自動新聞排程或 AnalysisRun。

真實 DEV 瀏覽器額外檢查 28 個畫面組合：商品／News #2／首頁／AI／排程／Prompt／Reference × 桌機1280、手機390 × light/dark。確認兩張分析卡片皆連結 NewsContext #2；console/page errors 0，layout overflow 0，mutation requests 0。手機深色 screenshot 已人工查看。

## 市場情報設定移至 AI 設定頁

/settings/ai 的「市場情報設定」可勾選「分析前更新市場情報」並儲存。設定是跨商品、依使用者持久化的手動 Force 偏好，非特定 Provider 欄位。商品頁不再顯示勾選框，Force body 不覆寫 Backend 偏好。Scheduled analysis 仍重用現有 NewsContext，沒有新增自動 Search/Flash 呼叫。

GET /api/ai/analysis-settings、PUT 同路徑（body: {"refreshNewsBeforeAnalysis":true}）；PUT 沿用 X-Market-Signal: web，必須提供 boolean。新增 migration 011_analysis_execution_settings.sql，AnalysisExecutionSettings(UserId PK/FK, RefreshNewsBeforeAnalysis BOOLEAN DEFAULT FALSE)，Memory / MySQL stores。設定頁 GET / PUT 不執行付費模型或刷新新聞。

本輪 Backend tests 194 pass / 1 skip；Frontend build success，25 Playwright tests pass。真實 DEV 瀏覽器驗證設定儲存後 reload 仍保留、商品頁勾選框已移除、desktop/mobile light/dark 無 overflow/error；付費呼叫 0。驗證暫時切換後還原原值 false。

## 商品頁後續整理

市場情報摘要移至 AI 報告上方的單一區塊，同快照去重；如果不同 Provider 最新報告使用不同歷史快照，標示各 Provider 的實際 Context，避免把新的新聞套到舊報告。強制分析按鈕放在分析歷史左邊；移除設定提示文字。市場參考標的新增收合，預設收起，展開後保留既有管理功能與未送出的編輯內容。

最後前端 Build 通過；預設收合後 6 項既有相關測試通過。實際 desktop/mobile 檢查預設收合、展開及按鈕左右排列正常，沒有 layout overflow 或付費 API 呼叫。
