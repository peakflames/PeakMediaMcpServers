# Epic 8po1tRg: Narrate a spine — Complete

**Completed:** 2026-11-13
**Verified by:** Independent review via `/peak-workflow:wrapup-epic 8po1tRg` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

A signed-in caller with no OpenRouter key turns a whole script (1–40 clips) into speech with one
`synthesize_narration` call through the gateway's shared key, sees one MCP progress notification per
clip, and receives a markdown summary plus one JSON block of per-clip metadata with 15-minute download
URLs for 16-bit mono 24 kHz WAV files — never audio bytes. `list_voices` names the allowed models and
voices, and every invalid request is refused with an `ERROR:` before any spend. Synthesis is
cache-first through the shared audio cache.

## Key Files

| File | Purpose |
|------|---------|
| `src/PeakMediaMcpTools/TtsTools.cs`, `TtsTools_ListVoices.cs`, `TtsTools_SynthesizeNarration.cs` | The two tools (partial class, one tool per file; registered only in the gateway) |
| `src/PeakMediaMcpTools/Narration/NarrationRequestValidator.cs` | Pre-spend validation with the exact `ERROR:` texts |
| `src/PeakMediaMcpTools/Narration/NarrationService.cs` | Sequential cache-first orchestration and per-clip progress |
| `src/PeakMediaMcpTools/Narration/NarrationResultFormatter.cs` | Summary line, slow-clip warnings, single JSON block |
| `src/PeakMediaMcpTools/Narration/ClipCacheKey.cs`, `WavAudio.cs`, `WordCounter.cs`, `TtsCatalog.cs` | Cache key, PCM → WAV, words/wpm, model/voice catalogue |
| `src/PeakMediaMcpTools/OpenRouter/OpenRouterSpeechClient.cs` | `POST audio/speech` (PCM, ZDR, Gemini-only `instructions`) and generation cost |
| `src/PeakMediaRemoteMcpServer/Cache/ClipStore.cs`, `DownloadTokens.cs`, `ClipEndpoints.cs`, `AudioDirectory.cs` | Shared cache index + WAV files, hashed 15-minute download tokens, `GET /clips/{token}` |
| `src/PeakMediaRemoteMcpServer/Storage/SchemaMigrator.cs` | Migration 2: `audio_cache`, `clip_downloads` |
| `src/PeakMediaRemoteMcpServer/appsettings.json` | `Tts` model allow-list and voices, `Storage:AudioDirectory` |
| `tests/Fakes/FakeOpenRouter.cs` | Fake OpenRouter: PCM sized by wpm, per-clip cost, request log |
| `tests/PeakMediaRemoteMcpServer.Tests/Narration/` | `NarrationClient` helper (real broker sign-in) and one test per TOR |

## Key Decisions

- `TtsTools` has no `[McpServerToolType]`; the gateway registers it with `WithTools<TtsTools>()` so the stdio server's assembly scan does not offer tools needing the shared cache.
- The shared cache is read by content key across callers by design; `audio_cache.created_by_subject` records the creator and `clip_downloads.subject` the token owner.
- `/clips/{token}` is anonymous: the 43-character random token (stored only as SHA-256) is the credential (TOR-04-DYooFQF).
- Timestamps use the injected `TimeProvider` (Unix ms) so tests drive expiry with `FakeTimeProvider`.
- Real cost comes from `GET /generation?id=` via the `X-Generation-Id` header; when missing the clip is recorded at $0 with a warning log — confirm in the by-hand OpenRouter round trip.
- Upstream failures return a generic `ERROR: OpenRouter could not synthesize clip N. …`; specific 401/402/timeout wording belongs to a later epic.

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-03-Vaata1w | The gateway shall list, through list_voices, every allowed model with its voices, its default voice and whether style is sent, and name the default model | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ListVoicesTests.cs:20 |
| TOR-03-nt9zDad | The gateway shall refuse a synthesize_narration call naming a model that is not allowed, before any synthesis | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:10 |
| TOR-03-mPpT1yR | The gateway shall refuse a synthesize_narration call naming a voice the model does not have, before any synthesis | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:22 |
| TOR-03-HLJlVSo | The gateway shall match voice names case-insensitively | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:36 |
| TOR-03-IcVxy00 | The gateway shall refuse a synthesize_narration call containing a clip with empty text, before any synthesis | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:49 |
| TOR-03-eyAaGVV | The gateway shall refuse a synthesize_narration call with more than 40 clips, before any synthesis | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:62 |
| TOR-03-LeqCLZu | The gateway shall refuse a synthesize_narration call containing a clip longer than 4,000 characters, before any synthesis | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:74 |
| TOR-03-9ruVWBA | The gateway shall accept a synthesize_narration call at the limits of 40 clips and 4,000 characters per clip | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:87 |
| TOR-03-uMFP7Oc | The gateway shall refuse the model "fake/tone" | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:104 |
| TOR-03-IyOofVh | The gateway shall synthesize a spine of clips in one synthesize_narration call for a signed-in caller who has no OpenRouter key | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/SpineTests.cs:8 |
| TOR-03-E04hqrD | The gateway shall return, for every clip, the fields n, cacheKey, durationMs, words, wpm, cached, costUsd, sampleRate and url in one fenced JSON block | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ResultTests.cs:7 |
| TOR-03-RevSWV4 | The gateway shall summarize each synthesize_narration result in markdown with the clip count, cached count, cost of the call and median words per minute | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ResultTests.cs:26 |
| TOR-03-J5FXU3j | The gateway shall return no audio bytes in a synthesize_narration result | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ResultTests.cs:41 |
| TOR-03-ehw1xk6 | The gateway shall send one MCP progress notification per clip while a synthesize_narration call runs | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ProgressTests.cs:8 |
| TOR-03-eKeTzs6 | The gateway shall deliver each clip as a 16-bit mono 24 kHz WAV file | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/WavTests.cs:8 |
| TOR-03-0gjBH7R | The gateway shall count a clip's words without its mood tags when reporting words and wpm | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/WordCountTests.cs:7 |
| TOR-03-SVUzauN | The gateway shall warn about every clip whose words per minute are below 70% of the median of its call | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/SlowClipTests.cs:8 |
| TOR-03-UYCubxo | The gateway shall request each uncached clip from OpenRouter's /api/v1/audio/speech with PCM output and zero data retention | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/OpenRouterRequestTests.cs:11 |
| TOR-03-Ah5Slpu | The gateway shall send the style to Gemini models as "instructions" and shall send no style to MAI models | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/OpenRouterRequestTests.cs:28 |
| TOR-03-JKfDgpt | The gateway shall use a clip's own style in place of the call's style when the clip gives one | `docs/requirements/03-narration.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/OpenRouterRequestTests.cs:43 |
| TOR-04-lRsfqBV | The gateway shall name every clip by the lowercase hex SHA-256 of model, voice, style and text joined by NUL bytes, followed by ".wav" | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/CacheKeyTests.cs:9 |
| TOR-04-TgW6vsE | The gateway shall treat a clip as uncached when its model, voice, style or text differs from every cached clip | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/CacheKeyTests.cs:22 |
| TOR-04-DYooFQF | The gateway shall serve a clip's WAV at its download URL without a bearer token, repeatedly, for 15 minutes | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/DownloadTests.cs:7 |
| TOR-04-bIGlR3O | The gateway shall answer a download of an expired clip URL with 410 Gone and a problem-details body saying to call synthesize_narration again | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/DownloadTests.cs:29 |
| TOR-04-SqAxPKp | The gateway shall issue download URLs whose token cannot be derived from the clip | `docs/requirements/04-shared-audio-cache.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/DownloadTests.cs:49 |
| TOR-01-Ji4u6Ta | The gateway shall return every MCP tool failure as a result whose text starts with "ERROR:" and names the problem and the next action | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/ValidationTests.cs:117 |
| TOR-01-B0JJ3Jt | The gateway shall answer a failed request to a non-MCP HTTP endpoint with an RFC 9457 problem-details body whose detail names the problem and the next action | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Narration/DownloadTests.cs:74 |

## Requirement Changes

None

## Verification Summary

### Counts
- TOR Requirements: 27/27 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 5/5 PASS (lint, build, tests, run the tool, access control); N/A — visual/console and UX Baseline (no UI changed), Removal gate (not a removal epic), Deferred-value check (not the walking skeleton)
- Tests: 121 passed, 0 skipped, 0 failed

### Highlights
- ✅ TOR-03-IyOofVh — 12-clip spine in one call with no caller key; every upstream request carried the gateway key and every URL downloaded a WAV (SpineTests.cs:8, NarrationService.cs:22)
- ✅ TOR-03-nt9zDad / mPpT1yR / IcVxy00 / eyAaGVV / LeqCLZu / uMFP7Oc / TOR-01-Ji4u6Ta — exact `ERROR:` texts with zero upstream requests (ValidationTests.cs, NarrationRequestValidator.cs:40-105); `fake/tone` refusal also confirmed on the live gateway
- ✅ TOR-04-DYooFQF / bIGlR3O / SqAxPKp — hashed 32-byte base64url tokens, valid at 1 and 14 min, 410 problem+json at 16 min (DownloadTokens.cs:22,66, ClipEndpoints.cs:34)
- ✅ TOR-03-ehw1xk6 — 12 progress notifications before the result with the specified messages (ProgressTests.cs:8, NarrationService.cs:93)
- ✅ Live gateway signed in through real Okta: `mcp tools` lists exactly `list_voices` and `synthesize_narration`; no secrets or Bearer tokens in either log

### Conclusion
Every one of the 27 TORs is traced by a test that mirrors its Given/When/Then, passes, and exercises code that realizes the behavior on inspection. All gates pass — including the live signed-in `mcp tools` check the implementer could not run unattended — and the access-control gate holds: owner columns on both new tables and anonymous access only for the unguessable download token that TOR-04-DYooFQF requires.

### Manual verification performed: No

## Known Issues / Follow-ups

- Expired `clip_downloads` rows are never deleted — the table grows without bound until cache housekeeping lands.
- `ClipCacheKey.IsWellFormed` is unused (dead code).
- Tool failures are normal text results rather than `isError: true` — satisfies TOR-01-Ji4u6Ta as written; revisit if clients need the flag.
- `.autopilot/test-changes.jsonl` (autopilot's test-change log) is tracked on this branch; `.autopilot/` is not in `.gitignore`.
- Real OpenRouter synthesis (and the `X-Generation-Id` cost lookup) has not been exercised against the live service — by-hand check, as planned.
