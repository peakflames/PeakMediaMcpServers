# Epic 8nPWJzI: Failure recovery and cache eviction — Complete

**Completed:** 2026-11-13
**Verified by:** Independent review via `/peak-workflow:wrapup-epic 8nPWJzI` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

When OpenRouter fails mid-narration, the gateway retries a clip once on a timeout, a broken connection or a 5xx answer, then stops at the failing clip with a message that names it ("clip 5 of 12") and says the earlier clips are saved; an out-of-credit (402) or rejected-key (401) answer is reported to the caller without a retry and without ever revealing the key. Finished clips are cached the moment they are made, so a retry pays only for the clips still missing. The shared audio cache is now bounded: a background service removes least-recently-used clips (a cache hit refreshes a clip's last-used time) once `Storage:CacheMaxGb` is exceeded, and deletes download links that expired more than 24 h ago.

## Key Files

| File | Purpose |
|------|---------|
| `src/PeakMediaMcpTools/OpenRouter/SpeechError.cs` | Failure kinds (`SpeechFailureKind`), `SpeechError`, `SpeechRetryPolicy` |
| `src/PeakMediaMcpTools/OpenRouter/OpenRouterSpeechClient.cs` | Classifies failures; one retry for timeout / unreachable / 5xx |
| `src/PeakMediaMcpTools/Narration/NarrationService.cs` | `SpeechFailure` caller-facing messages; stops at the failing clip |
| `src/PeakMediaRemoteMcpServer/Cache/CacheEvictor.cs` | LRU eviction to `Storage:CacheMaxGb` + expired download-link clean-up |
| `src/PeakMediaRemoteMcpServer/Cache/CacheEvictionService.cs` | Hosted service running the evictor every `Storage:EvictionIntervalMinutes` |
| `src/PeakMediaRemoteMcpServer/Configuration/GatewayStartupValidator.cs` | Refuses to start when `Storage:CacheMaxGb` ≤ 0 |
| `tests/PeakMediaRemoteMcpServer.Tests/Narration/FailureRecoveryTests.cs` | TOR-03-u0NLjMV, TOR-03-5607RO5, TOR-03-FQcysYo, TOR-03-A312w2t, TOR-04-PXIafcT |
| `tests/PeakMediaRemoteMcpServer.Tests/Narration/CacheEvictionTests.cs` | TOR-04-SGh52Vj, TOR-04-rYFhU7O, file and link clean-up |
| `tests/PeakMediaRemoteMcpServer.Tests/SecretsInLogTests.cs` | TOR-01-x5DXvRT, TOR-03-A312w2t at process level (stderr + `logs/`) |

## Key Decisions

- One retry after `OpenRouter:RetryDelayMs` (default 1000 ms); per-request timeout `OpenRouter:ClipTimeout` (default 2 min, also bounds cost lookups — they share the `HttpClient`).
- LRU order is `last_used_utc, created_utc, rowid`; `ClipStore.FindAsync` refreshes `last_used_utc` on every hit; evicted clips' download links go by `ON DELETE CASCADE`.
- Expired download links are kept 24 h past expiry so they still answer 410 rather than 404.
- `CacheEvictor.RunOnceAsync` is a DI singleton so tests run the same pass the background service runs; the service uses a real `PeriodicTimer`, not the `TimeProvider`.
- No Serilog redactor was added: no code path logs the key or the client secret; the process-level `SecretsInLogTests` is the guard.

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-03-u0NLjMV | The gateway shall retry a clip once when OpenRouter times out or answers with a 5xx status | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/FailureRecoveryTests.cs:14 |
| TOR-03-5607RO5 | The gateway shall fail a synthesize_narration call with an error naming the clip when OpenRouter does not answer for that clip after one retry | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/FailureRecoveryTests.cs:30 |
| TOR-03-FQcysYo | The gateway shall report an out-of-credit OpenRouter account without retrying | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/FailureRecoveryTests.cs:49 |
| TOR-03-A312w2t | The gateway shall report a rejected OpenRouter key without revealing the key | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/FailureRecoveryTests.cs:63, tests/PeakMediaRemoteMcpServer.Tests/SecretsInLogTests.cs:31 |
| TOR-04-PXIafcT | The gateway shall cache each clip the moment it is synthesized so that a retry after a failed call pays only for the clips still missing | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/FailureRecoveryTests.cs:79 |
| TOR-04-SGh52Vj | The gateway shall remove least-recently-used clips once the cache exceeds Storage:CacheMaxGb | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/CacheEvictionTests.cs:39 |
| TOR-04-rYFhU7O | The gateway shall refresh a clip's last-used time on every cache hit | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/CacheEvictionTests.cs:64 |
| TOR-01-x5DXvRT | The gateway shall never write the OpenRouter key or the OIDC client secret to its log | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/SecretsInLogTests.cs:16 |

## Requirement Changes

None

## Verification Summary

### Counts
- TOR Requirements: 8/8 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 6/6 PASS (Lint, Build, Tests, Live upstream, Run the tool, Access control; N/A: Visual/console and UX Baseline — no UI; Removal — not a removal epic)
- Tests: 140 passed, 1 skipped, 0 failed (the skipped test is the live OpenRouter test, run separately by the Live upstream gate: 1 passed, 0 skipped)

### Highlights
- ✅ TOR-03-u0NLjMV / TOR-03-5607RO5 — one retry on 503 (2 requests for clip 2, 4 total); unanswered clip 5 fails with the exact "clip 5 of 12" message and zero requests for clips 6–12 (FailureRecoveryTests.cs:14,30; OpenRouterSpeechClient.cs:63-79, NarrationService.cs:53)
- ✅ TOR-03-FQcysYo / TOR-03-A312w2t — 402 and 401 are not retried and return the exact admin-facing messages; the key is absent from the result, stderr and `logs/` of a real child-process gateway (SecretsInLogTests.cs:31)
- ✅ TOR-04-PXIafcT — after a failure at clip 5 the retry serves clips 1–4 cached at $0 and sends exactly 8 upstream requests (FailureRecoveryTests.cs:79)
- ✅ TOR-04-SGh52Vj / TOR-04-rYFhU7O — LRU eviction (CacheEvictor.cs:45) and last-used refresh on hit (ClipStore.cs:53), proven with room for exactly 3 clips
- ✅ TOR-01-x5DXvRT — the built gateway run with the TOR's literal key and secret leaves neither in stderr or `logs/`; a local start with real `.env` values also left neither in the log

### Conclusion
Every TOR is traced by a test that mirrors its Given/When/Then, exercises the real gateway pipeline against the fake OpenRouter, and passes; source inspection confirms the behaviour. The implementer's self-assessment matched every gate reproduced here — no gate discrepancies.

### Manual verification performed: No

## Known Issues / Follow-ups

- `build.py mcp tools` against real Okta was not run — the browser sign-in timed out (300 s) with no one present, as in the implementer's run. Tool inventory is unchanged by this epic and covered by in-process stub-signed-in `tools/list` tests.
- No project-wide Serilog redaction step exists (Security Baseline suggests one); the secret-free logs are guarded by `SecretsInLogTests` instead.
- Rare race: an eviction pass could remove a clip an in-flight call just stored or hit, before its download link is issued. LRU order makes this reachable only when the cache limit is smaller than one call's clips.
- Worst-case time to fail one clip is about 4 min (2 min timeout × 2 + 1 s delay); some MCP clients may time out first.
