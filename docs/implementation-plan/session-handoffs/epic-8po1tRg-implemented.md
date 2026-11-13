# Epic 8po1tRg — Narrate a spine — Implemented

**Branch:** `feature/epic-8po1tRg-narrate-a-spine`
**Implemented:** 2026-11-13
**By:** Todd Schavey (autopilot session, resumed from two `wip(autopilot)` commits)

## What Was Built

A signed-in caller with no OpenRouter key can now narrate a whole script with one
`synthesize_narration` call through the gateway, and ask `list_voices` which models and voices are
allowed.

- **`list_voices`** — markdown table of the four allowed models (default voice, style sent), the
  default model, and each model's voices, all from the `Tts` section of `appsettings.json`.
- **`synthesize_narration`** — validates the whole request before any spend (1–40 clips, 1–4,000
  characters per clip, allowed model, voice matched case-insensitively and sent in catalogue casing),
  then runs clip by clip in order: cache hit → free; miss → one `POST /api/v1/audio/speech`
  (`response_format: "pcm"`, `provider.zdr: true`, `instructions` only for style-sending Gemini
  models, clip style over call style, gateway key as Bearer), PCM wrapped as 16-bit mono 24 kHz WAV
  and stored at once. Real cost comes from `GET /api/v1/generation?id=` (0 and a warning log when
  unavailable). One MCP progress notification per clip (`clip N/T cached` /
  `clip N/T synthesized (X.X s audio, $Y.YYYY)`).
- **Result** — markdown summary line (`{n} clips · {c} cached · ${cost} this call · median {m} wpm`),
  a warning for every clip under 70% of the median wpm, and exactly one fenced JSON block with
  `n, cacheKey, durationMs, words, wpm, cached, costUsd, sampleRate, url` per clip. Text only.
- **Shared cache** — cache key `sha256(model\0voice\0style\0text)` + `.wav`; schema migration 2 adds
  `audio_cache` (records `created_by_subject`) and `clip_downloads` (owner `subject`, token hash
  only); WAV files under `Storage:AudioDirectory` (default `audio/` beside the database).
- **Downloads** — `GET /clips/{token}` without sign-in: 43-character base64url random tokens, fresh
  per result, reusable for 15 minutes (`TimeProvider`); unknown → 404 and expired → 410 RFC 9457
  problem details with the TOR wording.

## Deferrals

Count: 0

None

## Key Files

| File | Purpose |
|---|---|
| `src/PeakMediaMcpTools/TtsTools.cs`, `TtsTools_ListVoices.cs`, `TtsTools_SynthesizeNarration.cs` | The two tools (partial class, one tool per file) |
| `src/PeakMediaMcpTools/Narration/TtsCatalog.cs` | `Tts` options + catalogue lookup |
| `src/PeakMediaMcpTools/Narration/NarrationRequestValidator.cs` | Pre-spend validation, exact `ERROR:` texts |
| `src/PeakMediaMcpTools/Narration/ClipCacheKey.cs`, `WavAudio.cs`, `WordCounter.cs` | Cache key, PCM → WAV, words / wpm |
| `src/PeakMediaMcpTools/Narration/NarrationService.cs` | Sequential cache-first orchestration + progress |
| `src/PeakMediaMcpTools/Narration/NarrationResultFormatter.cs` | Summary, slow-clip warnings, JSON block |
| `src/PeakMediaMcpTools/Narration/IClipStore.cs` | Host seam for cache + download links |
| `src/PeakMediaMcpTools/OpenRouter/OpenRouterSpeechClient.cs` | Speech request + generation cost |
| `src/PeakMediaRemoteMcpServer/Cache/ClipStore.cs`, `DownloadTokens.cs`, `ClipEndpoints.cs`, `AudioDirectory.cs` | Gateway cache, tokens, `/clips/{token}` |
| `src/PeakMediaRemoteMcpServer/Storage/SchemaMigrator.cs` | Migration 2 (`audio_cache`, `clip_downloads`) |
| `src/PeakMediaRemoteMcpServer/Program.cs` | Narration services, typed `openrouter` HttpClient, `WithTools<TtsTools>()`, `MapClipEndpoints()` |
| `src/PeakMediaRemoteMcpServer/appsettings.json` | `Tts` catalogue, `Storage:AudioDirectory` |
| `tests/Fakes/FakeOpenRouter.cs` | PCM by wpm, per-clip cost, `/generation`, parsed request log, status override |
| `tests/PeakMediaRemoteMcpServer.Tests/Narration/*` | `NarrationClient` helper + one test per TOR |

## Spec Deviations

| TOR ID | As-Written | As-Implemented | Reason |
|---|---|---|---|
| — | — | — | No deviation: every anchor is implemented as written. |

## Key Decisions

- **Tools registered only in the gateway.** `TtsTools` carries no `[McpServerToolType]`; the gateway
  adds it with `WithTools<TtsTools>()`, so the stdio server's assembly scan does not offer tools whose
  services (shared cache) it does not have. Stdio handshake tests still pass.
- **Logging in the tools library** uses Serilog's static `Log` (no `Microsoft.Extensions.Logging`
  extension dependency); upstream failures log status/exception type only — never the key or body.
- **Timestamps** in `audio_cache` / `clip_downloads` are Unix milliseconds from the injected
  `TimeProvider`, so `FakeTimeProvider` drives expiry in tests.
- **Cost lookup** reads the `X-Generation-Id` response header and `GET /generation`; if OpenRouter
  does not provide it, the clip is recorded at $0 with a warning — to confirm in the by-hand real
  OpenRouter round trip.
- **Upstream failures** return a generic `ERROR: OpenRouter could not synthesize clip N. …`;
  retry/timeout/402/401 wording belongs to a later epic.
- `BrokerStorageTests` asserted schema version 1; it now compares against the highest migration
  version (reason logged in `.autopilot/test-changes.jsonl`).

## TOR Coverage

| TOR | Result | Test | Implementation |
|---|---|---|---|
| TOR-03-Vaata1w | PASS | `Narration/ListVoicesTests.cs:20` | `TtsTools_ListVoices.cs:12`, `appsettings.json` `Tts` |
| TOR-03-nt9zDad | PASS | `Narration/ValidationTests.cs:10` | `NarrationRequestValidator.cs:56` |
| TOR-03-mPpT1yR | PASS | `Narration/ValidationTests.cs:22` | `NarrationRequestValidator.cs:110` |
| TOR-03-HLJlVSo | PASS | `Narration/ValidationTests.cs:36` | `TtsCatalog.cs` `FindVoice`, `NarrationRequestValidator.cs:60` |
| TOR-03-IcVxy00 | PASS | `Narration/ValidationTests.cs:49` | `NarrationRequestValidator.cs:79` |
| TOR-03-eyAaGVV | PASS | `Narration/ValidationTests.cs:62` | `NarrationRequestValidator.cs:48` |
| TOR-03-LeqCLZu | PASS | `Narration/ValidationTests.cs:74` | `NarrationRequestValidator.cs:85` |
| TOR-03-9ruVWBA | PASS | `Narration/ValidationTests.cs:87` | `NarrationRequestValidator.cs:30-31` |
| TOR-03-uMFP7Oc | PASS | `Narration/ValidationTests.cs:104` | `NarrationRequestValidator.cs:56` (not in allow-list) |
| TOR-03-IyOofVh | PASS | `Narration/SpineTests.cs:8` | `NarrationService.cs:23`, `ClipEndpoints.cs:45` |
| TOR-03-E04hqrD | PASS | `Narration/ResultTests.cs:7` | `NarrationResultFormatter.cs:18`, `NarrationService.cs` `ClipResult` |
| TOR-03-RevSWV4 | PASS | `Narration/ResultTests.cs:26` | `NarrationResultFormatter.cs:28` |
| TOR-03-J5FXU3j | PASS | `Narration/ResultTests.cs:41` | `TtsTools_SynthesizeNarration.cs` (string result) |
| TOR-03-ehw1xk6 | PASS | `Narration/ProgressTests.cs:8` | `NarrationService.cs:93` |
| TOR-03-eKeTzs6 | PASS | `Narration/WavTests.cs:8` | `WavAudio.cs:15` |
| TOR-03-0gjBH7R | PASS | `Narration/WordCountTests.cs:7` | `WordCounter.cs:11` |
| TOR-03-SVUzauN | PASS | `Narration/SlowClipTests.cs:8` | `NarrationResultFormatter.cs:38` |
| TOR-03-UYCubxo | PASS | `Narration/OpenRouterRequestTests.cs:11` | `OpenRouterSpeechClient.cs:28`, `Program.cs` `AddNarration` |
| TOR-03-Ah5Slpu | PASS | `Narration/OpenRouterRequestTests.cs:28` | `OpenRouterSpeechClient.cs:33` |
| TOR-03-JKfDgpt | PASS | `Narration/OpenRouterRequestTests.cs:43` | `NarrationRequestValidator.cs:101` |
| TOR-04-lRsfqBV | PASS | `Narration/CacheKeyTests.cs:9` | `ClipCacheKey.cs:10` |
| TOR-04-TgW6vsE | PASS | `Narration/CacheKeyTests.cs:22` | `NarrationService.cs:32`, `ClipStore.cs` `FindAsync` |
| TOR-04-DYooFQF | PASS | `Narration/DownloadTests.cs:7` | `DownloadTokens.cs:18`, `ClipEndpoints.cs:45` |
| TOR-04-bIGlR3O | PASS | `Narration/DownloadTests.cs:29` | `ClipEndpoints.cs:36` |
| TOR-04-SqAxPKp | PASS | `Narration/DownloadTests.cs:49` | `DownloadTokens.cs:22` |
| TOR-01-Ji4u6Ta | PASS | `Narration/ValidationTests.cs:117` | `NarrationRequestValidator.cs:42` |
| TOR-01-B0JJ3Jt | PASS | `Narration/DownloadTests.cs:74` | `ClipEndpoints.cs:42` |

All test paths are under `tests/PeakMediaRemoteMcpServer.Tests/`; implementation paths under
`src/PeakMediaMcpTools/` or `src/PeakMediaRemoteMcpServer/Cache/`.

## Verification Results

| Check | Result |
|---|---|
| Lint — `dotnet format PeakMediaMcpServers.sln --verify-no-changes` | PASS (exit 0) |
| Build — `.venv/bin/python build.py build` | PASS |
| Tests — `.venv/bin/python build.py test` | PASS — 8/8 stdio, 113/113 gateway (28 new narration tests incl. signed-in `tools/list` inventory) |
| Run — `build.py start`, `curl /version` | PASS — `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}` |
| Run — `curl /clips/not-a-real-token` | PASS — 404, `application/problem+json`, exact title/status/detail |
| Run — `build.py mcp tools` on the live server | NOT RUN live — `/mcp` needs a real sign-in (no Okta in the unattended run); the signed-in `tools/list` is verified in-process (`Narration/ListVoicesTests.cs` `Tools_list_offers_synthesize_narration_and_list_voices`) |
| Stdio — `dotnet run --project src/PeakMediaMcpServer -- --version` | PASS — `PeakMediaMcpServer v0.1.0`, exit 0 |
| TOR ID grep under `tests/` | PASS — all 27 IDs found |
| Incomplete-work marker grep | PASS — only `StubOidcIssuer` (the test double's name) matches |
| Real OpenRouter / Okta round trip | By hand, not run in this session (as planned) |
