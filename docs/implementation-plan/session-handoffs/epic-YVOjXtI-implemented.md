# Epic YVOjXtI — Stdio server and client contract — Implemented

**Implemented:** 2026-11-13
**Branch:** `feature/epic-YVOjXtI-stdio-server-and-client-contract`
**By:** Todd Schavey (autopilot session)

## What Was Built

- **Stdio narration** (`src/PeakMediaMcpServer`): after the key resolves (`PEAK_MEDIA_OPENROUTER_API_KEY`,
  else legacy `TTS_OPENROUTER_API_KEY`), the server registers exactly `synthesize_narration` and
  `list_voices` from the shared `TtsTools` class. Clips go into the local cache (`PEAK_MEDIA_TTS_CACHE`, else
  `~/.cache/peak-media/tts`) under their cache key, written to a temp file then moved atomically; the result
  carries `path` in place of `url`. A cached file is served (free, no OpenRouter call) only when it is a
  complete 16-bit mono 24 kHz WAV. No database: `UnmeteredUsageLedger` never refuses and records nothing.
- **`fake/tone`**: an offline model (sine tone, 150 wpm, cost 0) routed by `SoloSpeechClient`; listed by
  `list_voices` alongside the gateway's four models (stdio `appsettings.json` = gateway catalogue + `fake/tone`,
  guarded by a drift test).
- **Shared seams** (`src/PeakMediaMcpTools`): `ISpeechClient` (implemented by `OpenRouterSpeechClient` and
  `ToneSpeechClient`); `IClipStore.LocateAsync` + `LocationKey` (`url`/`path`) replacing
  `IssueDownloadUrlAsync`; `ClipResult.Location`; the formatter writes it under the store's key and phrases the
  footer for url vs path; `WavAudio.ReadDurationMs` validates a WAV; `NarrationAccount` words key/credit/storage
  failures for the gateway (unchanged text) or a solo user (own key, own cache directory).
- **Tool-class split**: `my_usage` moved to the gateway-only `TtsUsageTools`; the gateway registers both
  classes (still three tools — smoke confirmed).
- **`docs/client-integration.md`**: arguments and result fields of all three tools, the cache-key rule, the
  selection order with no fallback after `ERROR:`, the download-into-cache step (skip existing, verify WAV, 410 =
  expired link, stdio `path` needs no download) and the `claude mcp add` commands for both servers.
- **Tests**: 17 stdio process tests (`tests/PeakMediaMcpServer.Tests/SoloNarration/`) against the real built
  server and `FakeOpenRouter` on a real port; gateway-project tests for the contract (`StdioContractTests`), the
  doc (`ClientIntegrationDocTests`) and the WAV validator (`WavValidationTests`); shared `StdioClient` helper in
  `tests/TestSupport/`.

## Deferrals

Count: 0

None

## Key Files

| File | Role |
|---|---|
| `src/PeakMediaMcpServer/Program.cs` | `AddNarration`: catalogue, local store, unmetered ledger, OpenRouter client with the user's key, `WithTools<TtsTools>()` |
| `src/PeakMediaMcpServer/LocalClipStore.cs` | Local cache directory resolution, WAV-validated hits, atomic save, `path` location |
| `src/PeakMediaMcpServer/SoloSpeechClient.cs` | Routes `fake/tone` to the tone client, everything else to OpenRouter |
| `src/PeakMediaMcpServer/UnmeteredUsageLedger.cs` | No quota, no records, no database |
| `src/PeakMediaMcpServer/SoloOpenRouterOptions.cs` | `OpenRouter:*` settings without a key property |
| `src/PeakMediaMcpServer/appsettings.json` | Gateway catalogue + `fake/tone`; OpenRouter defaults |
| `src/PeakMediaMcpTools/OpenRouter/ISpeechClient.cs`, `ToneSpeechClient.cs` | Speech seam and the offline tone model |
| `src/PeakMediaMcpTools/Narration/IClipStore.cs`, `NarrationResultFormatter.cs`, `NarrationService.cs`, `NarrationAccount.cs`, `WavAudio.cs` | url/path location, host-specific failure wording, `HTTP <code>` wording, WAV validator |
| `src/PeakMediaMcpTools/TtsTools.cs`, `TtsUsageTools.cs`, `TtsUsageTools_MyUsage.cs` | Shared tools vs gateway-only `my_usage` |
| `src/PeakMediaRemoteMcpServer/Program.cs`, `Cache/ClipStore.cs` | Gateway registers `ISpeechClient` and both tool classes; `LocateAsync` rename |
| `docs/client-integration.md` | The client contract |
| `tests/TestSupport/StdioClient.cs` | Drives the stdio server over stdin/stdout |

## Spec Deviations

| TOR ID | As-Written | As-Implemented | Reason |
|---|---|---|---|
| — | — | — | No deviation: all 19 anchors implemented as written. |

## Key Decisions

1. **`HTTP <code>` wording for upstream failures (both servers).** TOR-06-fTgpl4m requires
   `(HTTP 503, retried once)`; the shared text said `(status 503, …)`. Both servers share one code path, so the
   shared wording changed. Only two gateway test literals pinned `status` (`FailureRecoveryTests.cs:84,108`); no
   TOR did. Recorded in `.autopilot/test-changes.jsonl`. No amendment needed.
2. **Tool-class split.** `TtsTools` holds only `synthesize_narration` + `list_voices`, so both servers' schemas
   are identical by construction (proven by TOR-06-PY6kH1r); `my_usage` lives in gateway-only `TtsUsageTools`.
3. **Tool descriptions de-gatewayed.** "this gateway allows" → "this server allows", "the gateway's default
   model" → "the server's default model"; the synthesize description names `url` (gateway) or `path` (stdio).
   No test or TOR pinned the old text.
4. **Solo failure wording (`NarrationAccount`).** A rejected key / no credit / unwritable cache on the stdio
   server points at the user's own key or cache directory instead of "ask an administrator". Gateway text is
   unchanged (its TOR-03 tests still pass). Covered by `SoloKeyTests.Rejected_key_tells_the_solo_user_to_fix_their_own_key`.
5. **Doc and contract tests live in the gateway test project** (the plan suggested the stdio project): TOR-07-LaYp8xj
   says "the gateway's … input schemas" and must see `my_usage`, which only the gateway exposes. The gateway test
   project gets a build-order-only reference to the stdio server (`ReferenceOutputAssembly="false"`, so the
   gateway's `appsettings.json` in the test bin is not overwritten — checked).
6. **WAV validator tests in a new file** (`WavValidationTests.cs`) instead of editing the existing `WavTests.cs`.
7. **Cache directory logged at Debug only** — it contains the user's home path.

## TOR Coverage

All tests pass (`build.py test`: 25/25 stdio project, 163 passed + 1 live skipped in gateway project).

| TOR ID | Result | Test | Implementation |
|---|---|---|---|
| TOR-06-WYyXusz | PASS | `tests/PeakMediaMcpServer.Tests/SoloNarration/SoloKeyTests.cs:9` | `src/PeakMediaMcpServer/OpenRouterKey.cs:9`, `Program.cs` (Fatal + return 1) |
| TOR-06-dX6DFFO | PASS | `SoloKeyTests.cs:26` | `OpenRouterKey.cs:17`; `Program.cs:97` (Bearer = resolved key) |
| TOR-06-ej048Ep | PASS | `SoloKeyTests.cs:44` | `OpenRouterKey.cs:14-18` (new name first) |
| TOR-06-emXCHe2 | PASS | `SoloToolSetTests.cs:8` | `Program.cs:57` (`WithTools<TtsTools>()`); `src/PeakMediaMcpTools/TtsTools.cs` |
| TOR-06-heFgF6C | PASS | `LocalCacheTests.cs:9` | `LocalClipStore.cs:19` (dir), `:66` (atomic save), `:77` (path); `NarrationResultFormatter.cs:81` |
| TOR-06-vtaQIaK | PASS | `LocalCacheTests.cs:33` | `LocalClipStore.cs:19-27` |
| TOR-06-D8tj7pH | PASS | `LocalCacheTests.cs:51` | `LocalClipStore.cs:42` (WAV-validated hit); `NarrationService` cache-hit path |
| TOR-06-g9QibOG | PASS | `FakeToneTests.cs:8` | `src/PeakMediaMcpTools/OpenRouter/ToneSpeechClient.cs`; `SoloSpeechClient.cs:11` |
| TOR-06-UeA7aSw | PASS | `UpstreamTests.cs:8` | `OpenRouterSpeechClient.cs:55-56` |
| TOR-06-i2lj5iF | PASS | `UpstreamTests.cs:24` | `NarrationService.cs:243` |
| TOR-06-fTgpl4m | PASS | `UpstreamTests.cs:41` | `OpenRouterSpeechClient.cs:70` (one retry); `NarrationService.cs:275` (`HTTP <code>`) |
| TOR-06-PY6kH1r | PASS | `tests/PeakMediaRemoteMcpServer.Tests/Narration/StdioContractTests.cs:9` | shared `TtsTools`; `NarrationResultFormatter.cs:81` (url ↔ path) |
| TOR-06-bPXmWnj | PASS | `SoloValidationTests.cs:8` | `NarrationRequestValidator.cs:110` (shared) |
| TOR-06-FXNq0Rx | PASS | `SoloToolSetTests.cs:20` (+ drift guard in same file) | `src/PeakMediaMcpServer/appsettings.json:112` |
| TOR-06-GC17sBD | PASS | `SoloLogTests.cs:11` | key only on `HttpClient` header (`Program.cs:97`); no text logged in shared code |
| TOR-07-LaYp8xj | PASS | `tests/PeakMediaRemoteMcpServer.Tests/ClientIntegrationDocTests.cs:14` | `docs/client-integration.md` §Tools, §The cache key |
| TOR-07-1XYMIbC | PASS | `ClientIntegrationDocTests.cs:56` | `docs/client-integration.md` §Selection order |
| TOR-07-8MvsuuM | PASS | `ClientIntegrationDocTests.cs:71` | `docs/client-integration.md` §Download into the cache |
| TOR-07-akAqnvh | PASS | `ClientIntegrationDocTests.cs:89` | `docs/client-integration.md` §Connecting |

Mechanical checks: every TOR ID is found under `tests/`; the incomplete-work grep found only references to the
existing test OIDC issuer (test infrastructure, named for its role), none describing incomplete TOR behavior.

## Verification Results

| Gate | Result |
|---|---|
| Lint — `dotnet format PeakMediaMcpServers.sln --verify-no-changes` | PASS |
| Build — `build.py build` | PASS |
| Tests — `build.py test` | PASS (25 + 163 passed, 1 live test skipped as designed) |
| Live upstream — `build.py test-live` | PASS (1 passed, 0 skipped; one real OpenRouter call, ~$0.001) |
| Smoke — `build.py smoke` | PASS (signed in through the test sign-in server; `synthesize_narration`, `list_voices`, `my_usage` listed) |
| Gateway `/version` — `build.py start`, `curl`, `stop` | PASS (`{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}`) |
| Stdio `--version` | PASS (`PeakMediaMcpServer v0.1.0`, exit 0) |
| Hand run — stdio `synthesize_narration` with `fake/tone` | PASS (WAV in `PEAK_MEDIA_TTS_CACHE`: RIFF/WAVE, PCM 16-bit mono 24 kHz) |
| UX Baseline | N/A (no UI in this epic) |
