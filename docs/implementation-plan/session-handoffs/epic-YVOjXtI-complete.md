# Epic YVOjXtI: Stdio server and client contract — Complete

**Completed:** 2026-11-13
**Verified by:** Independent review via `/peak-workflow:wrapup-epic YVOjXtI` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

A solo user with their own OpenRouter key now gets the same `synthesize_narration` and `list_voices` tools
over stdio (`PeakMediaMcpServer`): no sign-in, no database, clips written into the local cache
(`PEAK_MEDIA_TTS_CACHE`, else `~/.cache/peak-media/tts`) and returned as `path` in place of the gateway's
`url`, plus an offline `fake/tone` model for testing. Both servers share one tool class, so their input
schemas and result fields match by construction. `docs/client-integration.md` is the written contract the
peak-media skills adopt: arguments and result fields, the cache-key rule, the selection order with no
fallback after `ERROR:`, the download-into-cache step, and the `claude mcp add` commands.

## Key Files

| File | Purpose |
|------|---------|
| `src/PeakMediaMcpServer/Program.cs` | Key check (exit 1 on missing key), narration services, registers `TtsTools` only |
| `src/PeakMediaMcpServer/OpenRouterKey.cs` | `PEAK_MEDIA_OPENROUTER_API_KEY`, else legacy `TTS_OPENROUTER_API_KEY` |
| `src/PeakMediaMcpServer/LocalClipStore.cs` | Local cache directory, WAV-validated hits, atomic save, `path` location |
| `src/PeakMediaMcpServer/SoloSpeechClient.cs` | Routes `fake/tone` offline, every other model to OpenRouter |
| `src/PeakMediaMcpServer/UnmeteredUsageLedger.cs` | No quota, no usage records, no database |
| `src/PeakMediaMcpServer/appsettings.json` | Gateway model catalogue plus `fake/tone` |
| `src/PeakMediaMcpTools/OpenRouter/ISpeechClient.cs`, `ToneSpeechClient.cs` | Speech seam; offline tone model |
| `src/PeakMediaMcpTools/Narration/IClipStore.cs`, `NarrationService.cs`, `NarrationResultFormatter.cs`, `NarrationAccount.cs`, `WavAudio.cs` | `url`/`path` location, host-specific failure wording, WAV validator |
| `src/PeakMediaMcpTools/TtsTools.cs`, `TtsUsageTools.cs` | Shared narration tools vs gateway-only `my_usage` |
| `docs/client-integration.md` | The client contract |
| `tests/TestSupport/StdioClient.cs` | Drives the built stdio server over stdin/stdout in tests |

## Key Decisions

- `TtsTools` holds only `synthesize_narration` and `list_voices`; `my_usage` moved to gateway-only `TtsUsageTools`, so both servers' schemas are identical by construction.
- Upstream failure text now reads `(HTTP <code>, retried once)` on both servers (was `status <code>` on the gateway); no requirement pinned the old wording.
- `NarrationAccount` words key / credit / storage failures for the gateway (ask an administrator) or the solo user (own key, own cache directory).
- The stdio cache directory is logged at Debug only (it contains the user's home path); the user's key lives only on the `HttpClient` Authorization header.
- Contract and doc tests live in the gateway test project because TOR-07-LaYp8xj needs the gateway's `my_usage` schema.

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-06-WYyXusz | The stdio server shall exit with code 1 and name the fix on standard error when no OpenRouter key is set | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/SoloKeyTests.cs:9 |
| TOR-06-dX6DFFO | The stdio server shall accept the legacy TTS_OPENROUTER_API_KEY when PEAK_MEDIA_OPENROUTER_API_KEY is not set | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/SoloKeyTests.cs:26 |
| TOR-06-ej048Ep | The stdio server shall use PEAK_MEDIA_OPENROUTER_API_KEY in preference to TTS_OPENROUTER_API_KEY when both are set | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/SoloKeyTests.cs:44 |
| TOR-06-emXCHe2 | The stdio server shall offer exactly the tools synthesize_narration and list_voices | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/SoloToolSetTests.cs:8 |
| TOR-06-heFgF6C | The stdio server shall write each clip into the local cache directory under its cacheKey and return its path in place of a url | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/LocalCacheTests.cs:9 |
| TOR-06-vtaQIaK | The stdio server shall write clips into the directory named by PEAK_MEDIA_TTS_CACHE when it is set | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/LocalCacheTests.cs:33 |
| TOR-06-D8tj7pH | The stdio server shall serve a clip already present in the local cache directory without calling OpenRouter | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/LocalCacheTests.cs:51 |
| TOR-06-g9QibOG | The stdio server shall accept the model "fake/tone" and synthesize its clips without calling OpenRouter | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/FakeToneTests.cs:8 |
| TOR-06-UeA7aSw | The stdio server shall request each uncached clip from OpenRouter with PCM output and zero data retention | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/UpstreamTests.cs:8 |
| TOR-06-i2lj5iF | The stdio server shall send one MCP progress notification per clip while a synthesize_narration call runs | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/UpstreamTests.cs:24 |
| TOR-06-fTgpl4m | The stdio server shall retry a clip once on a timeout or 5xx answer and then fail with an error naming the clip | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/UpstreamTests.cs:41 |
| TOR-06-PY6kH1r | The stdio server shall expose synthesize_narration and list_voices with the same arguments and result fields as the gateway, differing only in path for url | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/StdioContractTests.cs:9 |
| TOR-06-bPXmWnj | The stdio server shall refuse invalid synthesize_narration input with the same error text as the gateway | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/SoloValidationTests.cs:8 |
| TOR-06-FXNq0Rx | The stdio server shall list the model "fake/tone" in list_voices alongside the models the gateway allows | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/SoloToolSetTests.cs:20 |
| TOR-06-GC17sBD | The stdio server shall never write narration text or the user's OpenRouter key to its log | `docs/requirements/06-stdio-server.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/SoloNarration/SoloLogTests.cs:11 |
| TOR-07-LaYp8xj | The client integration document shall state the arguments and result fields of synthesize_narration, list_voices and my_usage as the running servers expose them | `docs/requirements/07-client-integration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/ClientIntegrationDocTests.cs:14 |
| TOR-07-1XYMIbC | The client integration document shall state the skill's selection order and that a gateway error never falls back to a personal key | `docs/requirements/07-client-integration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/ClientIntegrationDocTests.cs:56 |
| TOR-07-8MvsuuM | The client integration document shall describe the download-into-cache step for gateway results | `docs/requirements/07-client-integration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/ClientIntegrationDocTests.cs:71 |
| TOR-07-akAqnvh | The client integration document shall give the "claude mcp add" command for the gateway and for the stdio server, both under the name "peak-media" | `docs/requirements/07-client-integration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/ClientIntegrationDocTests.cs:89 |

## Requirement Changes

None

## Verification Summary

### Counts
- TOR Requirements: 19/19 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 8/8 PASS (lint, build, tests, live upstream, smoke, gateway `/version`, stdio `--version`, access control) — N/A: UX Baseline, visual / console (no UI in this epic)
- Tests: 188 passed, 1 skipped, 0 failed (the skip is the live OpenRouter test, which passed 1/1 under its own `test-live` gate)

### Highlights
- ✅ TOR-06-WYyXusz / dX6DFFO / ej048Ep — key resolution proven against the real built stdio process: exit 1 with the exact stderr text and empty stdout; the fake OpenRouter sees the right Bearer token (SoloKeyTests.cs:9–56, src/PeakMediaMcpServer/OpenRouterKey.cs:12)
- ✅ TOR-06-heFgF6C / vtaQIaK / D8tj7pH — clips land at `H/.cache/peak-media/tts/<cacheKey>` (or `PEAK_MEDIA_TTS_CACHE`) as valid 16-bit mono 24 kHz WAVs; a valid cached file is served free with zero upstream requests; a truncated one is re-synthesized (LocalCacheTests.cs, src/PeakMediaMcpServer/LocalClipStore.cs:19–75)
- ✅ TOR-06-PY6kH1r — the gateway (signed-in caller) and the stdio server return byte-identical input schemas; the clip-entry keys match with `url` → `path` (StdioContractTests.cs:9, shared src/PeakMediaMcpTools/TtsTools.cs)
- ✅ TOR-06-fTgpl4m — clip 2 is requested exactly twice, then the exact `(HTTP 503, retried once)` error; clip 3 is never requested; clip 1 is saved (UpstreamTests.cs:41, NarrationService.cs SpeechFailure)
- ✅ TOR-07-LaYp8xj — the doc test reads argument names from the running gateway's `tools/list`, so the document cannot drift from the schemas; the cache-key rule matches `ClipCacheKey.For` (SHA-256 of `model\0voice\0style\0text` + `.wav`)

### Conclusion
Each of the 19 Given/When/Then scenarios is mirrored by a test that runs against the real built stdio
binary, the in-process gateway, or the committed document, and every test passed in an independent run.
Inspection of the source confirms each behavior is realized by the code path the test exercises, and every
quality gate (including one real OpenRouter round trip) passed.

### Manual verification performed: No

## Known Issues / Follow-ups

- The missing-key message reaches stderr through Serilog, so the line carries a timestamp and `[FTL]` before `Error: …`. It meets TOR-06-WYyXusz ("contains"), but a bare `Error: …` line would match the CLAUDE.md stdio-startup example more closely.
- `SoloSpeechClient.LookUpCostAsync` picks the tone client by the literal generation id `fake-tone`; harmless, but routing by model would be sturdier.
- The upstream failure wording changed from `status <code>` to `HTTP <code>` on the gateway as well; any client parsing the old text (none known) would need updating.
