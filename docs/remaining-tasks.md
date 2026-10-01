# Remaining tasks: integrated validation

2026-10-02. TASK 3–6 were developed in independent feature branches/worktrees from main 988a8ef. User explicitly authorized parallel agents, completion of all remaining tasks and merging/pushing to main. Shared DI, embedded migrations and React routes were merged preserving each feature. No backlog feature was added.

- TASK 3: [persistent schedule settings](analysis-schedule-settings.md), `/settings/schedule`. MySQL settings and daily claims; Taipei timezone; fixed 08:30 trading-day check remains separate. Changing/deleting analysis slots does not affect that check. Worker reads settings every tick; closed markets remain blocked.
- TASK 4: [immutable prompt versions](prompt-management.md), `/settings/prompts`. Each save creates a new version and activates it. Per-run shared prompt is frozen before providers; original history retains actual prompt, instructions, SHA256 skill identifiers, model and input. JSON schema remains fixed.
- TASK 5: [history comparison table](analysis-history-table.md), product Analysis History. Saved target price/action/quantity/confidence/model/prompt version plus expandable reasoning/references/usage. Theme colors follow existing light/dark theme. Opening a row scrolls its evidence into view on narrow screens and includes an accessible collapse control. No raw response or full input JSON appears in the UI.
- TASK 6: [reference management UI](market-reference-ui.md), product page. Uses existing backend CRUD; mappings change the next shared analysis input, while old snapshots remain unchanged. Supports multiple mappings of the same type. Existing 00631L mappings were preserved.

## Integrated checks

Backend Build: 0 warnings/errors. Disposable MySQL full suite: **129 passed / 0 failed / 0 skipped**. Frontend production build passed. **10 unique Edge tests passed**, including original flows, settings, history, real isolated API schedule/prompt/reference CRUD and mobile checks. Opt-in Reference test was separately enabled after the initial suite; final history fixes were retested. These UI tests used isolated Memory API 5087, not the production MySQL dataset, and made no paid AI calls.

Real MySQL API 5080 + React 5173 was then verified, including settings pages and expanding both new persisted versions. No frontend errors or extra Force Analysis calls. Widths 390/768/1024/1280 had no page overflow. History details verified readable in both themes. Raw provider responses remain private to persistence.

## Real Prompt v1 → v2 analysis

Only the currently enabled DeepSeek was explicitly selected; OpenAI OFF and Claude OFF were preserved. Local background worker stayed OFF during live verification. Both requests used real Yahoo target/reference quotes and the existing explicit TWSE fallback for Taiwan50 history; neither analysis was Mock.

| Prompt | Snapshot | Analysis ID | Actual model | Action | Confidence | Input tokens | Output tokens | Cached tokens |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| investment-analysis-v1 | 21 | 73 | deepseek-v4-pro | HOLD | 57% | 10707 | 4425 | 384 |
| investment-analysis-v2 | 22 | 74 | deepseek-v4-pro | HOLD | 58% | 10659 | 5630 | 768 |

Both target prices were 39.62. Both input snapshots contain two references, Taiwan50 TWSE/TAI50I **21 close-only daily rows**, and TAIEX Yahoo/^TWII **20 daily rows**. Taiwan50 status AVAILABLE. TAIEX is explicitly PARTIAL because Yahoo history ends September 30 while quote time is October 1; this source limitation is retained in snapshot evidence and UI, with no fabricated October 1 daily bar.

DB verification: DeepSeek results 24→26; usage rows 8→10; OpenAI results 24/usage8 and Claude results24/usage0 unchanged. Provider failures remain2, added0. HTTP client logs show exactly2 DeepSeek POST requests and0 OpenAI POST requests. Each prompt snapshot content exactly matches its immutable stored version, and saved actual instructions start with that version content. Both snapshots contain the two effective skill identifiers. Original Active Prompt v1 restored after testing; v2 and both historical results remain available.

Evidence: [remaining-tasks-evidence.json](remaining-tasks-evidence.json).

## Limits and current local state

Existing demo authentication still uses user1. Memory settings are ephemeral; MySQL settings persist. Schedule uses the existing20-second polling worker, does not replay missed slots and does not retry claimed failed/interrupted slots automatically; manual Force is required. Trading-day checks may repeat after restart but do not directly call AI. Local live verification runs with worker OFF to prevent unplanned paid requests; saved auto-analysis settings apply when the existing worker is enabled through configuration.

No fresh paid OpenAI request was made in this round because its saved setting is OFF; both real adapters' identical custom prompt/input and unchanged schema were covered by HTTP contract tests. Claude remains Mock. History before version management has no prompt version and is shown as 未記錄. Existing most-recent100 limit remains. Docker application images were not redeployed; the verified local services are5080/5173.
