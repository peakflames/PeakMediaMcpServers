# Epic 8po1tRg: Narrate a spine

**Phase:** 2 — Narration through the gateway
**Status:** In Progress
**Dependencies:** Epic 7BASFbp (Sign-in through the broker — signed-in callers)
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

---

## Description

The product's primary scenario (ConOps Scenario 2): a signed-in caller with no OpenRouter key turns a whole script into speech with one `synthesize_narration` call, sees progress clip by clip, and receives per-clip metadata with short-lived download URLs — never audio bytes in the model's context. `list_voices` tells the caller which models and voices are allowed, and every invalid request is refused before any spend with an `ERROR:` that names the fix.

The slice spans the shared tools, the OpenRouter client, the gateway's shared cache (key, store, hit detection) and the `/clips/{token}` download endpoint, because every result carries a download URL and synthesis is cache-first. At 27 requirements it is above the usual size guide; it was split from Epic 8nPWJzI along the Scenario 2 / Scenario 7 boundary and cannot be split further without leaving a Then clause unreachable. Charging and quotas arrive in Epic EYGrros — until then every caller is under quota.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-03-Vaata1w | `docs/requirements/03-narration.feature.md` |
| TOR-03-nt9zDad | `docs/requirements/03-narration.feature.md` |
| TOR-03-mPpT1yR | `docs/requirements/03-narration.feature.md` |
| TOR-03-HLJlVSo | `docs/requirements/03-narration.feature.md` |
| TOR-03-IcVxy00 | `docs/requirements/03-narration.feature.md` |
| TOR-03-eyAaGVV | `docs/requirements/03-narration.feature.md` |
| TOR-03-LeqCLZu | `docs/requirements/03-narration.feature.md` |
| TOR-03-9ruVWBA | `docs/requirements/03-narration.feature.md` |
| TOR-03-uMFP7Oc | `docs/requirements/03-narration.feature.md` |
| TOR-03-IyOofVh | `docs/requirements/03-narration.feature.md` |
| TOR-03-E04hqrD | `docs/requirements/03-narration.feature.md` |
| TOR-03-RevSWV4 | `docs/requirements/03-narration.feature.md` |
| TOR-03-J5FXU3j | `docs/requirements/03-narration.feature.md` |
| TOR-03-ehw1xk6 | `docs/requirements/03-narration.feature.md` |
| TOR-03-eKeTzs6 | `docs/requirements/03-narration.feature.md` |
| TOR-03-0gjBH7R | `docs/requirements/03-narration.feature.md` |
| TOR-03-SVUzauN | `docs/requirements/03-narration.feature.md` |
| TOR-03-UYCubxo | `docs/requirements/03-narration.feature.md` |
| TOR-03-Ah5Slpu | `docs/requirements/03-narration.feature.md` |
| TOR-03-JKfDgpt | `docs/requirements/03-narration.feature.md` |
| TOR-04-lRsfqBV | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-04-TgW6vsE | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-04-DYooFQF | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-04-bIGlR3O | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-04-SqAxPKp | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-01-Ji4u6Ta | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-B0JJ3Jt | `docs/requirements/01-service-foundation.feature.md` |

## Key Components

- `src/PeakMediaMcpTools/TtsTools_SynthesizeNarration.cs`, `TtsTools_ListVoices.cs` — one tool per partial-class file; markdown result with one fenced JSON block; per-clip MCP progress notifications
- `src/PeakMediaMcpTools/` — request validation (model, voice case-insensitive, 1–40 clips, 1–4,000 characters), cache key (`sha256(model\0voice\0style\0text)` + `.wav`), PCM → WAV wrapper (16-bit mono 24 kHz), words / wpm (mood tags such as `<Warm>` not counted), slow-clip warning, model allow-list and voice catalogue from configuration
- `src/PeakMediaMcpTools/OpenRouter/` — client for `POST /api/v1/audio/speech` (`response_format: "pcm"`, `provider.zdr: true`, style as `instructions` for Gemini only) and generation statistics for the real cost
- `src/PeakMediaRemoteMcpServer/` — cache index and audio directory on the data volume, download tokens (hash stored, 15 minutes, reusable), `GET /clips/{token}` with RFC 9457 problem details for 404 and 410, a clock abstraction so tests can advance time
- `tests/Fakes/` — fake OpenRouter returning canned PCM, per-clip costs and wpm, and recording the requests it received
- `appsettings.json` — model allow-list, default model and voices
