# Epic 8nPWJzI — Failure recovery and cache eviction — Implemented

**Branch:** `feature/epic-8nPWJzI-failure-recovery-and-cache-eviction`
**Implemented:** 2026-11-13 by Todd Schavey

## What Was Built

- **Classified OpenRouter failures.** `SpeechError` (`SpeechFailureKind`: Timeout, Unreachable,
  ServerError, OutOfCredit, KeyRejected, Other; plus HTTP status and `Retried`) replaces the single
  generic failure from `OpenRouterSpeechClient.SynthesizeAsync`.
- **One retry** for timeout / unreachable / 5xx after `OpenRouter:RetryDelayMs` (default 1000 ms);
  401, 402 and other answers are never retried. The per-request timeout is `OpenRouter:ClipTimeout`
  (default 2 min) instead of the hard-coded 2 minutes.
- **Caller-facing messages** built in `NarrationService.SpeechFailure` via `ToolErrorFormatter.Format`:
  the named-clip timeout/unreachable/5xx message with the saved-clip range, the out-of-credit and
  rejected-key messages, and the old "could not synthesize clip N" for anything else. The call stops at
  the failing clip; earlier clips are already cached (unchanged behaviour, now proven by test).
- **LRU eviction.** `CacheEvictor.RunOnceAsync` removes the least-recently-used `audio_cache` rows and
  their WAV files until the total fits `Storage:CacheMaxGb` (GiB), and deletes download links that
  expired more than 24 h ago. `CacheEvictionService` runs it every `Storage:EvictionIntervalMinutes`
  (default 10). The gateway refuses to start when `Storage:CacheMaxGb` ≤ 0.
- **Secrets-in-log proof.** A child-process test runs the built gateway with the TOR's literal key and
  OIDC client secret, signs in, narrates (success and 401), and scans stderr and every file under `logs/`.

## Deferrals

Count: 0

None

## Key Files

| File | Change |
|---|---|
| `src/PeakMediaMcpTools/OpenRouter/SpeechError.cs` | New — failure kinds, `SpeechError`, `SpeechRetryPolicy` |
| `src/PeakMediaMcpTools/OpenRouter/OpenRouterSpeechClient.cs` | Classification + one retry; logs model, status, exception type only |
| `src/PeakMediaMcpTools/Narration/NarrationService.cs` | `SpeechFailure` message mapping; stops at failing clip |
| `src/PeakMediaRemoteMcpServer/Cache/CacheEvictor.cs` | New — LRU eviction + expired-link clean-up |
| `src/PeakMediaRemoteMcpServer/Cache/CacheEvictionService.cs` | New — periodic background runner |
| `src/PeakMediaRemoteMcpServer/Configuration/OpenRouterOptions.cs` | `ClipTimeout`, `RetryDelayMs` |
| `src/PeakMediaRemoteMcpServer/Storage/StorageOptions.cs` | `CacheMaxGb`, `EvictionIntervalMinutes` |
| `src/PeakMediaRemoteMcpServer/Configuration/GatewayStartupValidator.cs` | Refuses `CacheMaxGb` ≤ 0 |
| `src/PeakMediaRemoteMcpServer/Program.cs` | Registers retry policy, timeout, evictor, hosted service |
| `src/PeakMediaRemoteMcpServer/appsettings.json` | New settings with defaults |
| `tests/Fakes/FakeOpenRouter.cs` | `NoAnswer`, `RequestsFor`, `StartListeningAsync` (real port) |
| `tests/StubAuthorizationServer/StubOidcIssuer.cs` | Optional client secret |
| `tests/PeakMediaRemoteMcpServer.Tests/Narration/FailureRecoveryTests.cs` | New — TOR-03-*, TOR-04-PXIafcT |
| `tests/PeakMediaRemoteMcpServer.Tests/Narration/CacheEvictionTests.cs` | New — TOR-04-SGh52Vj, TOR-04-rYFhU7O, file/link clean-up |
| `tests/PeakMediaRemoteMcpServer.Tests/SecretsInLogTests.cs` | New — TOR-01-x5DXvRT, TOR-03-A312w2t (process level) |
| `README.md`, `CHANGELOG.md` | New settings and behaviour |

## Spec Deviations

No TOR deviation. Two Key Components notes differ from the spec's wording:

- The spec places "stop at the failing clip and name it" in `TtsTools_SynthesizeNarration.cs`; it lives
  in `NarrationService` (the clip loop the tool already delegates to). Same behaviour.
- The spec mentions a "Serilog redaction check"; no redactor was added because no code path logs the
  key or the client secret. The check is the process-level `SecretsInLogTests`, which scans stderr and
  every log file. A leak would fail that test.

## Key Decisions

- **Download-link retention 24 h.** Expired download links are deleted only once they expired more than
  24 h ago, so a link that expired minutes ago still answers 410 (TOR-04-bIGlR3O), not 404.
- **Retry/timeout settings.** `OpenRouter:RetryDelayMs` (default 1000) and `OpenRouter:ClipTimeout`
  (default `00:02:00`, also bounds cost lookups because they share the `HttpClient`). The in-process
  test host sets 1 ms and 1 s. The plan said 300 ms. 1 s leaves room for a busy parallel test run
  without slowing the timeout test much (~2 s).
- **Eviction-on-demand seam.** `CacheEvictor` is a DI singleton with a public `RunOnceAsync`, so tests
  run the same pass the background service runs and need no clock or timer. The service uses a real
  `PeriodicTimer` (not the `TimeProvider`), so tests that advance the fake clock never trigger it.
- **LRU order** is `last_used_utc, created_utc, rowid`. A cache hit already refreshes `last_used_utc`
  (`ClipStore.FindAsync`). Evicted clips' download links go by `ON DELETE CASCADE`.
- **Error number** uses the clip's `n` and the call's total. The saved-clip range is positional (clips
  before the failing one, cache hits included).

## TOR Coverage (implementer's self-assessment — not trusted by wrapup)

| TOR | Result | Test | Implementation |
|---|---|---|---|
| TOR-03-u0NLjMV | PASS | `FailureRecoveryTests.cs:14` | `OpenRouterSpeechClient.cs:65` (transient → one retry) |
| TOR-03-5607RO5 | PASS | `FailureRecoveryTests.cs:30` | `OpenRouterSpeechClient.cs:101`, `NarrationService.cs:53,130` |
| TOR-03-FQcysYo | PASS | `FailureRecoveryTests.cs:49` | `SpeechError.KindFor` (402, not transient), `NarrationService.cs:140` |
| TOR-03-A312w2t | PASS | `FailureRecoveryTests.cs:63`, `SecretsInLogTests.cs:31` | `NarrationService.cs:144`; client logs status only |
| TOR-04-PXIafcT | PASS | `FailureRecoveryTests.cs:79` | `NarrationService.cs` save-before-next (unchanged), `ClipStore.cs:82` |
| TOR-04-SGh52Vj | PASS | `CacheEvictionTests.cs:39` | `CacheEvictor.cs:45,59` |
| TOR-04-rYFhU7O | PASS | `CacheEvictionTests.cs:64` | `ClipStore.cs:53` (hit refresh), `CacheEvictor.cs:45` |
| TOR-01-x5DXvRT | PASS | `SecretsInLogTests.cs:16` | No logging of key/secret; `OpenRouterSpeechClient` logs model/status/type only |

Mechanical checks: every TOR ID found under `tests/`. The scan for placeholder words found only the
stub OIDC issuer test double, which is supposed to be there.

## Verification Results (implementer's self-assessment)

| Gate | Result |
|---|---|
| Lint (`dotnet format --verify-no-changes`) | PASS (exit 0) |
| Build (`build.py build`) | PASS |
| Tests (`build.py test`) | PASS — stdio 8/8; gateway 132 passed, 1 skipped (the live test, run separately below) |
| Live upstream (`build.py test-live`) | PASS — 1 passed, 0 skipped (one real OpenRouter call) |
| Run the tool — `/version` | PASS — `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}` |
| Run the tool — `build.py mcp tools` | CANNOT VERIFY — needs a browser Okta sign-in (`--oauth`); the callback timed out after 300 s with no sign-in. Tool inventory is unchanged by this epic and covered by the in-process signed-in `tools/list` tests |
| Error log after start | PASS — no error entries |
