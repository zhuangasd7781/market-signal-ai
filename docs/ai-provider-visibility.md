# AI Provider visibility

AI 設定新增「顯示」checkbox。顯示與啟用分析分開設定：隱藏只移除首頁欄位與商品頁卡片，Enabled 才控制排程／Force API 的分析呼叫。分析歷史保留，不刪除 Provider 或分析紀錄。

GET `/api/ai/settings` 增加 `visible`；PUT `/api/ai/settings/{provider}` 可提交 `visible`。舊 Client 未提交此欄位時保留目前顯示狀態。設定由 Backend 按 UserId 保存，所有 Provider 預設顯示。設定讀寫不呼叫 AI。

Migration 008 在既有 AIProviderSettings 增加 IsVisible BOOLEAN NOT NULL DEFAULT TRUE。Backend 啟動時檢查欄位後套用一次；既有 Enabled 與 Model 不變。Memory store 使用相同設定模型。

開發環境：本機 DEV 前端 http://127.0.0.1:5173，API http://127.0.0.1:5080，DB 連 Docker UAT MySQL（127.0.0.1:3307）。API Key 使用本機 .NET User Secrets。Docker 應用為 UAT，未來雲端為 PROD。

驗證：Backend 測試 137 passed、1 既有 MySQL opt-in skipped；DEV MySQL 已套用 migration 008 且讀寫 visible 正常。測試確認 user scope、舊 PUT 保留 visibility、隱藏與 Enabled 獨立、隱藏不刪除歷史、設定不呼叫分析。Frontend browser suite 全部通過，包含顯示／隱藏後重新整理、恢復、首頁與商品頁一致以及保留歷史。
