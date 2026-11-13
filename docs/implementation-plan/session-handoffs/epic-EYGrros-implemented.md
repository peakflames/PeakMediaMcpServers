# Epic EYGrros — Quotas and usage: implemented

**Date:** 2026-11-13
**Branch:** `feature/epic-EYGrros-quotas-and-usage`
**Implemented by:** Todd Schavey (autopilot session)

## What Was Built

- **Usage log and quota overrides** (schema migration 3): `usage_log` (subject, at_utc, model, voice,
  characters, cost_nanousd, cache_key, cache_hit — no text column) indexed on `(subject, at_utc)`, and
  `quota_overrides` (subject, monthly_nanousd, set_by_subject, set_at_utc). Money is integer nano-dollars.
- **Stores** in `src/PeakMediaRemoteMcpServer/Usage/`: `UsageStore` and `QuotaStore` take an explicit
  acting `Caller` plus the owner subject and decide every read and write through `AccessRule`
  (`CanAccess` for reads/writes, `RequireAdmin` for `SetOverrideAsync`). `UsageLedger` (`IUsageLedger`)
  binds them to the signed-in caller from the validated access token. `UsageMonth` gives the UTC
  calendar-month window and reset time.
- **Charging** in `NarrationService`: each synthesized clip is charged at OpenRouter's reported
  `total_cost`; when it cannot be read, at `Tts:Models[].PriceUsdPerThousandCharacters × characters / 1000`
  with a `[WRN]` "cost was estimated from the configured price" record. Cache hits write a free hit row.
  On a mid-call failure the pending charges are awaited before the error returns; on cancellation the
  pending clips are charged (at the estimate if needed) and the cancellation propagates.
- **Quota gate** at the start of every `synthesize_narration` call (before any cache lookup or
  synthesis): `spent >= quota` → `ERROR: monthly quota of $Q reached ($S spent this month). Try again
  after <reset> UTC or ask an administrator to raise your quota.` A call that starts under quota runs to
  completion and, when it ends at or over quota, appends `You are now over your monthly quota ($S of $Q).`
- **`my_usage` tool** (gateway only): markdown `field | value` table — month, spent, quota, remaining,
  clips synthesized, cache hits, resets — dollars to the cent, UTC.
- **Formatter:** a free call's summary cost prints `$0.00`; any other cost keeps four decimals.
- **Config:** `Quota:DefaultMonthlyUsd` = 25 and per-model `PriceUsdPerThousandCharacters` in
  `appsettings.json`.

## Deferrals

Count: 0

None

## Key Files

| File | Change |
|---|---|
| `src/PeakMediaMcpTools/Narration/IUsageLedger.cs` | new — `IUsageLedger`, `UsageStatus`, `UsageEntry`, `UsageText` |
| `src/PeakMediaMcpTools/Narration/NarrationService.cs` | quota gate, `ChargeAsync`, hit rows, drain on failure/cancel, `NarrationRun` |
| `src/PeakMediaMcpTools/Narration/NarrationResultFormatter.cs` | `$0.00` for free calls, optional over-quota note |
| `src/PeakMediaMcpTools/Narration/TtsCatalog.cs` | `PriceUsdPerThousandCharacters`, `EstimateCost` |
| `src/PeakMediaMcpTools/OpenRouter/OpenRouterSpeechClient.cs` | `LookUpCostAsync` returns `decimal?` (null = unreadable) |
| `src/PeakMediaMcpTools/TtsTools_MyUsage.cs` | new — `my_usage` tool |
| `src/PeakMediaMcpTools/TtsTools.cs`, `TtsTools_SynthesizeNarration.cs` | ledger injected; result carries quota note |
| `src/PeakMediaRemoteMcpServer/Usage/*.cs` | new — `UsageStore`, `QuotaStore`, `UsageLedger`, `UsageMonth`, `NanoUsd` |
| `src/PeakMediaRemoteMcpServer/Configuration/QuotaOptions.cs` | new — `sealed class`, `Quota:DefaultMonthlyUsd` |
| `src/PeakMediaRemoteMcpServer/Storage/SchemaMigrator.cs` | migration 3 |
| `src/PeakMediaRemoteMcpServer/Program.cs`, `appsettings.json` | registration and config |
| `tests/PeakMediaRemoteMcpServer.Tests/Usage/*.cs` | new — TOR tests and `UsageSeed` helper |
| `tests/PeakMediaRemoteMcpServer.Tests/Narration/NarrationClient.cs` | `now:` clock start, `MyUsageAsync`, `ListToolsAsync`, `UsageTable` |
| `tests/.../Narration/CostLookupTests.cs`, `LiveOpenRouterTests.cs`, `ListVoicesTests.cs` | retargeted (see Key Decisions) |

## Spec Deviations

None. Every Requirements Anchor is realized as written (see TOR Coverage).

## Key Decisions

- **Spend equal to the quota counts as over** (TOR-05-HqJn98D): the gate refuses at `spent >= quota`, and
  the post-call note uses the same test, so a call ending at exactly the quota is told it is now over.
- **Nano-dollar storage:** costs are stored as integer nano-dollars so month sums are exact
  ($24.80 + $0.52 = $25.32); display rounds to the cent with `MidpointRounding.AwayFromZero`.
- **Fallback price replaces cost 0:** an unreadable OpenRouter cost is now charged at the model's
  configured price with a `[WRN]`, instead of being recorded as $0. `CostLookupTests.Cost_is_zero_when_generation_never_appears`
  was retargeted (renamed `Cost_is_estimated_from_configured_price_when_generation_never_appears`, cites
  TOR-05-dFazrDm). **The price values in `appsettings.json` are estimates** (flash-lite $0.002, flash
  $0.005, MAI flash $0.004, MAI $0.015 per 1,000 characters). They were not calibrated: the `test-live`
  run does not print the reported cost and OpenRouter's public model list carries no TTS prices. Calibrate
  against a real generation's `total_cost` before relying on the estimate.
- **Ledger is caller-bound; stores take an explicit actor.** The tools use `IUsageLedger` (caller from
  the access token only); `UsageStore`/`QuotaStore` take `Caller actor` + owner subject so the dashboard
  epic EwgA3Zf can read any user's usage and set overrides as an admin through the same access rule.
- **Gate is before the cache lookup**, so an over-quota caller is refused even when every clip is cached
  (TOR-05-zi52cgw) and no `last_used` is touched.
- A failed usage-row write is logged at Error and does not fail the call (the audio was already paid
  for); a failed quota/usage read before the call fails it with the existing storage-unavailable error.
- `NarrationService` takes the model's price from the validated request's `TtsModelOptions` rather than
  injecting `TtsCatalog` (the plan's wording) — same data, one less dependency.
- A free call's summary prints `$0.00` (TOR-04-2qCsalR) while paid calls keep four decimals
  (`$0.0100`, TOR-05-MUV56KT); `LiveOpenRouterTests` now expects `$0.00` for its cached repeat.
- The [WRN] log half of TOR-05-dFazrDm is checked against the built gateway as a child process (stderr in
  the real log format); the charged amount is checked in-process.

## TOR Coverage

Implementer's own self-assessment; `/peak-workflow:wrapup-epic` re-verifies independently.

| TOR ID | Test(s) | Result |
|---|---|---|
| TOR-05-MUV56KT | `Usage/ChargingTests.TOR_05_MUV56KT_charges_the_real_reported_cost` (+ live test) | PASS |
| TOR-05-dFazrDm | `ChargingTests.TOR_05_dFazrDm_unreadable_cost_is_charged_at_the_configured_price`, `UsageProcessTests.TOR_05_dFazrDm_estimated_cost_is_logged_as_a_warning` | PASS |
| TOR-05-DIrxTPy | `ChargingTests.TOR_05_DIrxTPy_partial_failure_keeps_the_charges` | PASS |
| TOR-05-3JuMxwL | `QuotaTests.TOR_05_3JuMxwL_default_quota_is_25_without_override` | PASS |
| TOR-05-3cXUiMQ | `QuotaTests.TOR_05_3cXUiMQ_call_started_under_quota_completes_and_notes_overage` | PASS |
| TOR-05-N0KTSFQ | `QuotaTests.TOR_05_N0KTSFQ_over_quota_call_is_refused_before_any_synthesis` | PASS |
| TOR-05-HqJn98D | `QuotaTests.TOR_05_HqJn98D_spend_equal_to_quota_is_refused` | PASS |
| TOR-05-zi52cgw | `QuotaTests.TOR_05_zi52cgw_over_quota_caller_is_refused_even_when_every_clip_is_cached` | PASS |
| TOR-05-efm0UOd | `QuotaTests.TOR_05_efm0UOd_month_to_date_spend_resets_at_midnight_utc_on_the_first` | PASS |
| TOR-05-babYeNE | `QuotaTests.TOR_05_babYeNE_admin_override_applies_to_the_next_call` | PASS |
| TOR-05-bLVMjT4 | `MyUsageTests.TOR_05_bLVMjT4_reports_month_spend_quota_remaining_clips_hits_and_reset` | PASS |
| TOR-05-0pHv4Fb | `MyUsageTests.TOR_05_0pHv4Fb_remaining_is_quota_minus_spend_to_the_cent` | PASS |
| TOR-05-WmxtWmi | `MyUsageTests.TOR_05_WmxtWmi_answers_when_over_quota` | PASS |
| TOR-05-ryNGNXA | `UsageProcessTests.TOR_05_ryNGNXA_narration_text_is_never_stored_or_logged`, `SchemaTests` | PASS |
| TOR-04-csNgnyQ | `CachedCallTests.TOR_04_csNgnyQ_another_callers_clip_is_served_from_the_cache_for_free` | PASS |
| TOR-04-2qCsalR | `CachedCallTests.TOR_04_2qCsalR_repeated_spine_is_served_entirely_from_the_cache` | PASS |
| TOR-04-xYMUNYV | `CachedCallTests.TOR_04_xYMUNYV_expired_urls_are_renewed_at_no_cost` | PASS |
| TOR-02-dj60B0R | `ToolListTests.TOR_02_dj60B0R_every_caller_gets_exactly_the_same_three_tools` | PASS |
| TOR-02-Dc8Z0Ge | `IsolationTests.TOR_02_Dc8Z0Ge_usage_is_kept_under_each_callers_own_subject`, `QuotaTests.Non_admin_cannot_set_overrides_or_reach_another_callers_records`, `SchemaTests` | PASS |
| TOR-01-65SaYiJ | `RestartPersistenceTests.TOR_01_65SaYiJ_usage_overrides_and_cache_survive_a_restart` | PASS |

## Verification Results

| Gate | Result |
|---|---|
| `dotnet format PeakMediaMcpServers.sln --verify-no-changes` | PASS (exit 0) |
| `.venv/bin/python build.py build` | PASS |
| `.venv/bin/python build.py test` | PASS — 8/8 stdio; 155 passed, 1 skipped (the live test, run separately) gateway |
| `.venv/bin/python build.py test-live` | PASS — 1 passed, 0 failed, 0 skipped (one real OpenRouter call, ~$0.001) |
| `.venv/bin/python build.py smoke` | PASS — signed in as smoke@example.com; tools `list_voices`, `my_usage`, `synthesize_narration` |
| `build.py start` + `curl /version` | PASS — `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}`; the real database at `bin/Debug/net11.0/data/peakmedia.db` migrated to schema 3 (`usage_log`, `quota_overrides` present) |
| `build.py mcp tools` (no token) | 401 sign-in required, as designed; the signed-in tool list is covered by smoke, the Okta round trip is a release check |
| `build.py stop` | PASS |
| `dotnet run --project src/PeakMediaMcpServer -- --version` | PASS — `PeakMediaMcpServer v0.1.0`, exit 0 |
| TOR-ID grep over `tests/` | PASS — all 20 IDs found |
| Placeholder grep over changed files | PASS — hits only the `StubOidcIssuer` test helper and its CHANGELOG mention (legitimate) |
