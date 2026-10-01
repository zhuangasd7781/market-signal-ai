# Analysis schedule settings (TASK 3)

Feature branch: `feature/analysis-schedule-settings`, based on main `988a8ef`.

`AnalysisScheduleSettings` stores per-user Enabled and JSON daily HH:mm times. `AnalysisScheduleClaims` has a unique `(UserId, TradeDate, MinuteOfDay)` key. Migration 006 is embedded and seeds the current demo user only when absent. Saved settings survive restarts. Memory mode is intentionally ephemeral.

GET `/api/settings/analysis-schedule` returns Enabled, Times, Timezone (`Asia/Taipei`), TradingDayCheck (`08:30`). PUT the same URL with `{ "enabled": true, "times": ["09:05", "11:15"] }` atomically replaces the list, supporting add/edit/remove in one save. Mutations require `X-Market-Signal: web`. Empty lists are supported; malformed/duplicate times and more than 24 slots return 400. UserId comes from ICurrentUser, never request input.

UI `/settings/schedule` provides toggle and add/edit/delete times plus save, reachable from desktop navigation and mobile footer. Loading/saving never invokes analysts. Settings take effect at the next worker tick without rebuilding. Force Analysis remains independent.

Worker retains the existing 20-second timer, converts UTC to Asia/Taipei, and reads saved settings each tick. The fixed 08:30 calendar check remains independent, even when automatic analysis is off or all times deleted. Existing MarketScheduleExecutor checks/persists OPEN/CLOSED and only runs scheduled analysis on OPEN. An analysis time at 08:30 executes after the independent check.

Claims are persisted BEFORE analysis. Once claimed, a daily minute is never automatically retried, even after failure, time edits or restart. This prevents repeating an API request that might already have been accepted upstream. Failed/interrupted analysis requires manual Force Analysis. Calendar failures still retry during 08:30. Missed analysis minutes are not replayed. Calendar check is idempotent but may repeat after restart; it never directly calls an AI. Claims are retained (no pruning introduced in this task). Existing demo is single-user/single-instance; multi-user worker dispatch is outside scope.

Validation on 2026-10-02:
- Backend build: 0 warnings/errors.
- Disposable Compose MySQL full suite: 119 passed, zero failed/skipped. Schedule persistence/reseed and claims across fresh store instances verified within existing MySQL integration fixture, avoiding concurrent schema initialization.
- Deterministic worker tests verify Taipei times, dynamic setting edits, disabled/empty schedule retaining calendar check, daily deduplication across worker recreation, next-day execution and failure claim policy. Existing CLOSED day test remains passing.
- Frontend production build passed.
- Edge browser tests: mock-backed full add/edit/remove/disable/reload/mobile flow and real isolated Memory API save/reload. No analysis endpoint called. Isolated backend port5083 worker disabled; Vite5178. No live settings, production worker or paid AI requests changed.
