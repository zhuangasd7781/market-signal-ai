# News Intelligence Pipeline

本階段直接實作於 main；新聞不注入 MarketContext / AnalysisProtocol，也不改既有投資 Action、排程或 Provider 設定。新增的 Domain NewsContext 是可直接供未來統一 AnalysisInput 引用的獨立 snapshot，不能由投資 Provider 自行搜尋。

## 流程與來源

每次手動刷新捕捉 TimeProvider.GetUtcNow() 作為 windowEnd，windowStart=end-72h；沒有舊快照搜尋快取。來源與所有結果擷取完成後，Backend 再檢查 PublishedAt、時間品質與窗口，依 URL 去重，再按三類題材分配篇數上限（預設總上限 90）。UNKNOWN publication times 不送入 Flash，數量保留在 audit。時間均以 UTC 保存，UI 使用 Asia/Taipei。

INewsSearchProvider / INewsIntelligenceCollector / INewsContextStore / INewsContextRefresher 在 Application；RSS、Flash、Dapper 在 Infrastructure，沿用 MarketAnalysisRunner 類似分層。RSS 是可運作的無 Search API Key 方案，不等於完整網路搜尋。

已實際取得 HTTP 200 的來源：
- Federal Reserve 全部新聞稿與演說：https://www.federalreserve.gov/feeds/feeds.htm
- CNBC Economy：https://www.cnbc.com/id/20910258/device/rss/rss.html
- CNBC Technology：https://www.cnbc.com/id/19854910/device/rss/rss.html
- NVIDIA 新聞稿：https://nvidianews.nvidia.com/releases.xml
- 中央社產經／科技：https://www.cna.com.tw/about/rss.aspx

BLS RSS 實測 HTTP 403，未採用；Microsoft news feed 實測只有舊項目，未採用。無 HTML 搜尋爬蟲、模型自行搜尋或假新聞 fallback。單一 feed 故障列於 coverage；全部 feed 失敗不產生 Context。RSS feed 僅保留近期條目，無法保證涵蓋窗口內所有 Fed、債券、FX、科技公司與台灣新聞，因此本 provider 的成功 Context 仍標 PARTIAL。

## Flash 與驗證

News:Model 預設 deepseek-flash；DeepSeek:ApiKey / BaseUrl 使用既有設定與 User Secrets，獨立於投資 DeepSeek ConfiguredModel。官方目前建議模型名稱：https://api-docs.deepseek.com/。News Refresh 是獨立的人工付費操作；投資 Provider Enabled 不控制新聞 collector。載入 UI 或 GET 不呼叫任何 AI。

AI/news-intelligence.md 與 AI/news.schema.json 是獨立的新聞 Prompt / extraction schema。JSON mode 後進行必填欄位、額外欄位、型別、enum、長度、分數、來源 ID、事件數量、來源重複使用與交易指令驗證。模型只輸出 sourceIds；Publisher / URL / PublishedAt / SourceType 由原始來源資料重建，不接受模型捏造來源。

Articles 合併成 Events；同一 sourceId 不得同時用於兩個 Event。語意合併仍由模型完成，不能保證絕對正確，來源數不代表獨立確認數或事件權重。最多 20 事件，Prompt 優先精選 8–12 個有資訊價值的事件。

EventTime 和 PublishedAt 分離。可確認的完整時間必須有 supplied title/snippet 的精確原文時間證據；date-only 舊事件可保留 EventTime=null、RECAP 與原文日期證據，不假定午夜。來源不支持的模型推定時間清為 null / UNKNOWN，TimeQualityReason 明確保存此 normalization，原始模型輸出仍保存供 audit。格式錯誤、無來源、重複、schema 錯誤或交易指令仍令刷新失敗，不產生成功 Context。

## Persistence / API

Migration: 009_news_intelligence.sql（啟動時 idempotent 建表；沒有既有資料表 redesign）。
- NewsContexts：UTC window/generation、configured/actual model、狀態、計數、Token、normalized ContextJson、RawSearchSnapshotJson、NormalizedInputJson、PromptSnapshot、SchemaSnapshot、RawCollectorResponse。
- NewsEvents：事件與時間、方向、重要性、相關程度、信心值。
- NewsEventSources：原始來源與發布時間。
- NewsRefreshFailures：stage、safe error、實際 model/usage；無效模型輸出可保留於 RawCollectorResponse，HTTP error body 不保存／不反射。

Context/Event/Source 於同一 transaction INSERT；沒有 UPDATE 舊 Context、API update/delete 或 Seed 假資料。表內 ContextJson 的 Id 為 INSERT 前的 0，讀取以 immutable parent row Id 覆蓋，避免為補 ID UPDATE 舊 snapshot。Token 沿用 Domain.TokenUsage 的真實 InputTokens / OutputTokens / CachedTokens；沒有 token 估算或成本 dashboard。新聞 usage 放在 NewsContexts / NewsRefreshFailures，因 AIAnalysisUsage 與投資商品分析綁定，不建立假的商品分析紀錄。

- POST /api/news-context/refresh：同 origin 與 X-Market-Signal: web；201 回傳 Context、id、eventCount、window、status。全域 singleton gate 防止同 instance 重複付費請求，409 表示更新中。失敗 503，latest 保留舊成功 Context。
- GET /api/news-context/latest：無資料 404。
- GET /api/news-context/{id}：固定歷史 snapshot；無資料 404。

API 不回傳完整 raw JSON audit 或 authorization 資訊。Audit 保存在 DB 供開發者稽核。現階段與既有專案相同使用 Demo / Development 模式；未增加登入或多使用者。

## UI / 驗證

/news：市場情報與立即更新；/news/{id}：固定歷史連結。主要導覽新增市場情報。Loading、防重送、中文錯誤、來源連結、事件／發布時間、方向、重要性、資料限制、Token 與來源涵蓋均可見，沿用既有 theme/CSS。

實測成功 NewsContext #1：
- UTC window：2026-09-30T14:37:53.8418422Z ～ 2026-10-03T14:37:53.8418422Z（exact rolling 72h）。
- GeneratedAt：2026-10-03T14:38:12.1046249Z。
- Search 155；窗口內 URL 去重 86；Flash input 72；20 Events / 29 Sources。
- Actual model deepseek-flash；tokens input 22526 / output 3141 / cached 512。
- PARTIAL：RSS 範圍與篇數上限；20 個 EventTime 全部 UNKNOWN，發布時間均具可用明確時區。不將發布時間當事件時間。
- MySQL INSERT、latest/by-ID GET、真實 React 瀏覽器均成功；沒有呼叫 GPT/V4 Pro 做交易決策。

整合期間 Flash 曾回傳 21 事件、或把發布時間誤當事件時間。這些在修正前被擋下並留下 NewsRefreshFailures；沒有假 Context。修正後維持 schema/source/action 校驗，為不可靠時間新增可稽核 UNKNOWN normalization。失敗與成功的 token 都是真實 provider 數字。

Backend build 0 warnings/errors；179 tests pass，1 opt-in MySQL analysis test skip。完整隔離 Memory 前端 suite 22 tests pass（包含新聞 UI 的 4 項測試）。真實 DEV light/dark、1280/390、新聞／歷史／首頁／商品／AI／排程／Prompt／Reference 頁唯讀檢查；沒有付費呼叫或 UAT 既有資料異動。

已知限制：無 paid Search provider / exhaustive archive；RSS 可能有限或短 snippet；semantic event merging/summary 不是獨立事實查核；事件精確時間通常不可確認；沒有自動排程刷新、新聞 CMS 或投資 Context 注入。所有這些是本階段刻意保留的範圍。

### 新聞列表與導覽

「我的追蹤」與「市場情報」使用明確導覽間距。新聞列表初始顯示 10 筆，捲到底以 IntersectionObserver 載入下一批 10 筆；使用同一份已取得的 Context，不重新搜尋、不呼叫 AI。Refresh 或切換歷史 Context 時重新從 10 筆開始。桌機／手機、深色／淺色實際瀏覽器確認 10 → 20 筆，無 overflow、console error 或額外 mutation request。
