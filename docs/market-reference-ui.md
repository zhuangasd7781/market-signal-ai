# Market Reference management UI (TASK 6)

Product detail now includes a Market References panel built on the existing user-scoped Backend CRUD. No database, provider, runner, or schema redesign is involved.

- List the product's mappings with Reference Type, exact Yahoo symbol, name, and market.
- Add, edit, and remove mappings. Multiple references of the same type remain supported by the existing unique `(Product, Type, Instrument)` key.
- Expand Reference Instruments management to create, edit, and remove reusable instruments. Editing an instrument affects all mappings that use it; the UI explains this before editing. Backend rejects deleting an instrument that is still mapped.
- Removal requires an inline confirmation. Existing historical InputSnapshotJson values remain intact.
- Empty mappings use only the target's data. UNDERLYING, BROAD_MARKET, and SECTOR are evidence, and the decision remains for the target product.
- Saving settings sends CRUD requests only and does not run analysis, verify external ticker availability, or consume AI tokens.
- Existing 00631L mappings are unchanged: UNDERLYING `^TSE50` / Taiwan50 and BROAD_MARKET `^TWII` / TAIEX. Instruments are data driven; the UI has no product-specific mapping seed.

The existing runner reads the saved mappings at the beginning of each analysis and shares the assembled context with both providers. A new regression test edits the mapping between runs, verifies both providers receive the same updated context, confirms an earlier input snapshot still contains the original reference, and verifies deleting the mapping results in no references on the next run. Provider calls in this test are capture doubles; this is not a claim of a new paid API E2E execution.

## Validation

- Backend build: 0 warnings / 0 errors.
- Frontend TypeScript / Vite production build: passed.
- MarketReferenceTests: 12 passed.
- Full Backend tests in this worktree: 113 passed, 1 MySQL integration test skipped because an isolated MySQL connection was not supplied. Final integrated MySQL validation is run by the root integration task.
- Microsoft Edge Playwright: 1 passed against a real isolated Memory Backend at 127.0.0.1:5086 and the branch's Vite UI at 127.0.0.1:5176. Browser CRUD creates instruments and mappings, reloads them, edits the type and instrument name, verifies an in-use instrument deletion is rejected, then removes mappings and instruments. At 390px viewport, no page overflow or browser exceptions occurred. No Force Analysis requests were made.

Opt-in browser test command (the backend must have Memory storage; production-like/live DB is rejected):

```powershell
$env:TEST_BASE_URL = 'http://127.0.0.1:5176'
$env:REFERENCE_TEST_API = 'http://127.0.0.1:5086'
node frontend/node_modules/@playwright/test/cli.js test --config frontend/playwright.config.ts frontend/tests/market-reference-ui.spec.ts
```

The opt-in test clears its isolated Memory reference fixtures before running and leaves them empty after success. Do not point it at a normal application instance.

## Limits

The Backend remains the source of truth and validates types, symbols, ownership, duplicates, and in-use instruments. The UI provides the three existing reference types; extending Backend type support should update the selector. Yahoo availability and data quality are determined during the next analysis, not during UI editing. Concurrent edits use the existing last-write behavior; no new optimistic locking is introduced. Existing demo authentication and persistence mode limitations are unchanged.
