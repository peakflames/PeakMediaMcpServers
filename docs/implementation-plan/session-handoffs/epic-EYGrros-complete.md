# Epic EYGrros: Quotas and usage — Complete

**Completed:** 2026-11-13
**Verified by:** Independent review via `/peak-workflow:wrapup-epic EYGrros` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

Every synthesized clip is now charged to its caller at the cost OpenRouter reports (or the model's configured price, with a warning, when that cost cannot be read); cache hits cost nothing. A caller whose month-to-date spend (calendar month, UTC) has reached their monthly quota — $25.00 by default, or an administrator's override — is refused before any synthesis with an error naming the spend, the quota and the reset time. The new `my_usage` tool shows each caller their own month, spend, quota, remaining amount, clips synthesized, cache hits and reset time; usage, overrides and cached audio survive a restart, and narration text is never stored or logged.

## Key Files

| File | Purpose |
|------|---------|
| `src/PeakMediaMcpTools/Narration/IUsageLedger.cs` | `IUsageLedger`, `UsageStatus`, `UsageEntry`, `UsageText` (dollars to the cent, UTC) |
| `src/PeakMediaMcpTools/Narration/NarrationService.cs` | Quota gate at call start, per-clip charging (reported cost or configured-price estimate), free hit rows, over-quota note |
| `src/PeakMediaMcpTools/TtsTools_MyUsage.cs` | `my_usage` tool |
| `src/PeakMediaRemoteMcpServer/Usage/UsageStore.cs` | Usage log reads/writes through `AccessRule` |
| `src/PeakMediaRemoteMcpServer/Usage/QuotaStore.cs` | Default quota + admin-only overrides (set-by, set-at) |
| `src/PeakMediaRemoteMcpServer/Usage/UsageLedger.cs` | Binds the stores to the caller from the validated access token |
| `src/PeakMediaRemoteMcpServer/Usage/UsageMonth.cs` | UTC calendar-month window; `NanoUsd` exact money storage |
| `src/PeakMediaRemoteMcpServer/Storage/SchemaMigrator.cs` | Migration 3: `usage_log`, `quota_overrides` (owned by `subject`) |
| `src/PeakMediaRemoteMcpServer/appsettings.json` | `Quota:DefaultMonthlyUsd`, per-model `PriceUsdPerThousandCharacters` |

## Key Decisions

- Spend equal to the quota counts as over (`spent >= quota`), both for the refusal and the post-call note.
- Money is stored as integer nano-dollars so monthly sums are exact; display rounds to the cent.
- An unreadable OpenRouter cost is charged at the model's configured price with a `[WRN]` record (previously recorded as $0).
- The quota gate runs before the cache lookup, so an over-quota caller is refused even for fully cached calls.
- Tools use the caller-bound `IUsageLedger`; `UsageStore`/`QuotaStore` take an explicit acting `Caller` so the dashboard (Epic EwgA3Zf) can read any user's usage and set overrides as an admin through the same access rule.
- A failed usage-row write is logged at Error and does not fail the call.

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-05-MUV56KT | The gateway shall charge the caller the real cost OpenRouter reports for each synthesized clip | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/ChargingTests.cs:13 |
| TOR-05-dFazrDm | The gateway shall charge a clip from the configured model price and log a warning when OpenRouter's reported cost cannot be read | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/ChargingTests.cs:33, tests/PeakMediaRemoteMcpServer.Tests/Usage/UsageProcessTests.cs:51 |
| TOR-05-DIrxTPy | The gateway shall keep the charge for clips already synthesized when a call fails part-way | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/ChargingTests.cs:53 |
| TOR-05-3JuMxwL | The gateway shall give every caller without an override the configured default monthly quota of $25.00 | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/QuotaTests.cs:9 |
| TOR-05-3cXUiMQ | The gateway shall let a call that starts under quota run to completion and shall note in its result when the caller is then over quota | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/QuotaTests.cs:22 |
| TOR-05-N0KTSFQ | The gateway shall refuse synthesize_narration before any synthesis when the caller's month-to-date spend exceeds their quota, naming the spend, the quota and the reset time | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/QuotaTests.cs:42 |
| TOR-05-HqJn98D | The gateway shall refuse synthesize_narration when the caller's month-to-date spend equals their quota exactly | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/QuotaTests.cs:58 |
| TOR-05-zi52cgw | The gateway shall refuse an over-quota caller even when every clip of the call is already cached | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/QuotaTests.cs:73 |
| TOR-05-efm0UOd | The gateway shall count month-to-date spend from 00:00 UTC on the first day of the calendar month | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/QuotaTests.cs:91 |
| TOR-05-babYeNE | The gateway shall apply an administrator's quota override to the caller's next call | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/QuotaTests.cs:115 |
| TOR-05-bLVMjT4 | The gateway shall report, through my_usage, the caller's month, spend, quota, remaining amount, clips synthesized, cache hits and reset time | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/MyUsageTests.cs:8 |
| TOR-05-0pHv4Fb | The gateway shall report the remaining amount as quota minus spend, in US dollars to the cent, when the caller is under quota | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/MyUsageTests.cs:31 |
| TOR-05-WmxtWmi | The gateway shall answer my_usage for a caller who is over quota | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/MyUsageTests.cs:45 |
| TOR-05-ryNGNXA | The gateway shall never store or log narration text | `docs/requirements/05-quotas-and-usage.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/UsageProcessTests.cs:16 |
| TOR-04-csNgnyQ | The gateway shall serve a clip one caller has synthesized to any other caller from the cache at no cost | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/CachedCallTests.cs:8 |
| TOR-04-2qCsalR | The gateway shall serve a repeated, unchanged spine entirely from the cache with zero OpenRouter calls | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/CachedCallTests.cs:33 |
| TOR-04-xYMUNYV | The gateway shall renew expired download URLs through a repeated call that costs nothing | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/CachedCallTests.cs:57 |
| TOR-02-dj60B0R | The gateway shall offer every signed-in caller exactly the tools synthesize_narration, list_voices and my_usage, whether or not the caller is an administrator | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/ToolListTests.cs:8 |
| TOR-02-Dc8Z0Ge | The gateway shall keep each caller's usage under that caller's own OIDC subject so that no caller sees another caller's records | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/IsolationTests.cs:8 |
| TOR-01-65SaYiJ | The gateway shall keep its usage records, quota overrides and cached audio under the configured data directory so that a restart on the same volume preserves them | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Usage/RestartPersistenceTests.cs:9 |

## Requirement Changes

None

## Verification Summary

### Counts
- TOR Requirements: 20/20 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 8/8 PASS (Build, Tests, Lint/format, Live upstream, Smoke, Run the tool, Access-control check, Requirements health; N/A: Visual/console and UX Baseline — no UI in this epic; Removal gate; Deferred-value check)
- Tests: 163 passed, 1 skipped (the live test, run separately by the Live upstream gate: 1 passed, 0 skipped), 0 failed

### Highlights
- ✅ Quota gate — exact refusal text with zero OpenRouter requests for over-quota and at-quota callers, and refusal even when every clip is cached (tests/PeakMediaRemoteMcpServer.Tests/Usage/QuotaTests.cs:42,58,73; src/PeakMediaMcpTools/Narration/NarrationService.cs:35)
- ✅ TOR-05-MUV56KT / TOR-05-dFazrDm — real reported cost charged; fallback at the configured price with a `[WRN]` record seen in a real gateway process's stderr (tests/PeakMediaRemoteMcpServer.Tests/Usage/UsageProcessTests.cs:51; src/PeakMediaMcpTools/Narration/NarrationService.cs:134)
- ✅ TOR-05-ryNGNXA — marker text absent from the database file and its -wal/-shm, stderr and logs/ of a real gateway process (tests/PeakMediaRemoteMcpServer.Tests/Usage/UsageProcessTests.cs:16)
- ✅ TOR-02-Dc8Z0Ge — every usage/quota read and write goes through `AccessRule`; the caller comes only from the validated token (src/PeakMediaRemoteMcpServer/Usage/UsageStore.cs:16, src/PeakMediaRemoteMcpServer/Usage/UsageLedger.cs:53)
- ✅ TOR-01-65SaYiJ — usage, override and cached clip survive a restart on the same Storage:DatabasePath; the real data/peakmedia.db migrated to schema 3

### Conclusion
Each of the 20 Requirements Anchors has a test that mirrors its Given/When/Then and passed when run independently, and source inspection confirmed the behavior is realized by the intended code path. Every quality gate passed in this session (including one real OpenRouter call), with no discrepancy against the implementer's self-assessment.

### Manual verification performed: No

## Known Issues / Follow-ups

- The fallback prices in `appsettings.json` (`Tts:Models[].PriceUsdPerThousandCharacters`) are uncalibrated estimates — calibrate against a real generation's `total_cost` before relying on the fallback.
- The quota is checked only at call start (by design, TOR-05-3cXUiMQ); concurrent calls can each start under quota, so overspend is bounded by concurrency × 40 clips.
- Display rounding: a spend just under quota (e.g. $24.996) shows "$25.00 spent, $0.00 remaining" while calls are still allowed — cosmetic.
- A failed usage-row write is logged at Error and leaves the clip uncharged (documented trade-off).
