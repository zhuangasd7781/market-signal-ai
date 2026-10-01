# Analysis History Table

TASK 5 uses a table for the latest 100 history records on the existing product page. Columns: Taipei time, provider, actual model, saved target price, action, quantity in product units, confidence and prompt version. A per-row button expands reasoning, risks, invalidation, next actions, reference evidence/data quality, position, configured model, skill identifiers and actual token usage. Missing legacy metadata displays an explicit unavailable label. It never shows raw response or full input JSON, and loading/expanding history never triggers analysis.

Backend metadata is supplied by the TASK 4 safe AnalysisView projection. TASK 5 starts from main 988a8ef, remains its own branch and is integrated after Prompt Management; no second history API or database redesign is introduced.

Validation: frontend production build passed; Edge Playwright `analysis-history.spec.ts` passed (table comparison, expanded evidence and tokens, older null metadata, collapse and 390px viewport without page overflow). API fixtures intercept all calls and assert no force analysis call. Integrated backend tests and real saved history/browser checks are recorded in the final remaining-tasks report.

The table scrolls horizontally on phones, while expanded detail wraps within the viewport. History remains limited to the existing most recent 100 records; this task does not add pagination or backtesting. Records written before prompt versioning correctly display “未記錄”.
