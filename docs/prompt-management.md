# Prompt management (TASK 4)

The immutable `PromptVersions` table is scoped by user and numbered per user. `UserActivePrompts` selects one owned version with a composite foreign key. A new user gets `investment-analysis-v1` from the existing market-evidence and decision-rules skills once; startup does not overwrite content or reactivate v1 after a user change. Creating a version inserts a new row and changes the selector in one transaction. An active-row lock serializes simultaneous creates. No API updates/deletes version content.

- `GET /api/prompts`: `{activeVersionId, versions:[{id,version,content,createdAt}]}`.
- `POST /api/prompts`: `{content}` creates and activates a new version (201).
- `PUT /api/prompts/active`: `{versionId}` selects a previously saved owned version (204).
- Mutations require the existing `X-Market-Signal: web` header. Empty/whitespace or content over 30,000 characters is rejected. User identity comes exclusively from `ICurrentUser`.
- `/settings/prompts`: view active content, edit/save a new version, inspect old versions, activate old content. Save and page loads only call settings APIs, never Yahoo or AI.

## Applied rules and historical trace

The runner reads and freezes one active prompt per user/product run before the first provider executes. GPT and DeepSeek consume that same captured core content and the same serialized target/references/position/prior input. DeepSeek adds only its existing JSON API formatting. The embedded schema and CLR validation remain fixed outside user prompt management. Mandatory target-only/evidence-only rules, input handling and position guards are appended after the editable core; leveraged ETF guidance remains a fixed existing skill. Model and provider selection remain the TASK 2 source of truth.

Each result's existing `InputSnapshotJson` now saves `promptVersion`, `promptVersionId`, `promptSnapshot` (ID, version, actual core content, applied skill contents), `skillIdentifiers`, configured/actual model, and existing frozen market input and actual provider instructions. No parallel result persistence or database redesign was introduced. Nested `promptSnapshot` fields follow the existing stored CLR PascalCase; top-level trace fields are camelCase. The identifiers hash the actual applied core content (`investment-analysis-core:SHA256`) and leveraged ETF skill (`leveraged-etf:SHA256`, where relevant), avoiding inaccurate attribution to original templates after edits. Actual complete provider instructions remain in the snapshot for reliable reproduction.

Analysis API views add `promptVersion` and safe `context`: `targetPrice`, `marketReferences` (captured values/history/source/quality), `position` (quantity/averageCost), `skillIdentifiers`, `configuredModel`, `promptVersionId`. They never expose raw provider responses, full prompt text or input JSON. Legacy rows without trace metadata return null prompt/context fields; malformed legacy JSON does not break the history endpoint.

## Validation

- Backend build: zero warnings/errors.
- Disposable Compose MySQL full suite: 122 passed, zero skipped/failed. Prompt cases cover immutability, user isolation, validation, mutation header, no settings AI calls, shared prompt freeze while the first provider changes active version, v1/v2 historical snapshots, actual OpenAI/DeepSeek HTTP adapter payloads with equal core/input and unchanged schema, legacy malformed input. The existing single MySQL fixture also verifies concurrent inserts, restart persistence, old content preservation and foreign-user activation rejection.
- Frontend build passed.
- Edge: 2 browser tests passed. Mocked contract verifies responsive layout and version navigation; real isolated Memory backend verifies real API creation, reload, old content and activation restoration. Zero force-analysis requests. The production backend and paid APIs were not called by this branch's validation.

Run the real browser API test only on an isolated backend:

```powershell
$env:TEST_BASE_URL='http://127.0.0.1:5174'
$env:TEST_PROMPT_LIVE='1'
$env:TEST_PROMPT_BACKEND='http://127.0.0.1:5084' # Optional when Vite already proxies the isolated backend
npx playwright test tests/prompts.spec.ts
```

## Limits

Real paid v1/v2 analysis and MySQL input/usage evidence are verified during final integration by the root task, not claimed by this branch. Memory mode is deliberately ephemeral; MySQL persists across restarts. The existing demo user/authentication scope is unchanged. Fixed leveraged guidance and schema are not editable in this phase. Version listing is adequate for personal use and has no CMS/pagination. Settings affect the next captured run and do not rewrite or restart an in-flight analysis.
