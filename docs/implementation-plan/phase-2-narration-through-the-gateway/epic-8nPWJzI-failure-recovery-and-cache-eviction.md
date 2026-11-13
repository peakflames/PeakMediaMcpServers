# Epic 8nPWJzI: Failure recovery and cache eviction

**Phase:** 2 — Narration through the gateway
**Status:** Implemented — 2026-11-13
**Dependencies:** Epic 8po1tRg (Narrate a spine — synthesis and the shared cache)
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

---

## Description

ConOps Scenario 7: when OpenRouter fails mid-spine the caller learns which clip failed and what to do, finished clips are never lost or bought twice, and the shared key never leaks into a message or a log. Each clip is retried once on a timeout or 5xx; an out-of-credit account and a rejected key are reported without retrying.

The epic also bounds the cache: least-recently-used clips are removed once `Storage:CacheMaxGb` is exceeded, and every hit refreshes a clip's last-used time, so the cache keeps what the organization actually reuses.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-03-u0NLjMV | `docs/requirements/03-narration.feature.md` |
| TOR-03-5607RO5 | `docs/requirements/03-narration.feature.md` |
| TOR-03-FQcysYo | `docs/requirements/03-narration.feature.md` |
| TOR-03-A312w2t | `docs/requirements/03-narration.feature.md` |
| TOR-04-PXIafcT | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-04-SGh52Vj | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-04-rYFhU7O | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-01-x5DXvRT | `docs/requirements/01-service-foundation.feature.md` |

## Key Components

- `src/PeakMediaMcpTools/OpenRouter/` — one retry per clip on timeout / 5xx, the per-clip timeout (120 s, configurable), mapping of timeout, 402 and 401 to `ToolErrorFormatter` messages that never echo `ex.Message` or the key
- `src/PeakMediaMcpTools/TtsTools_SynthesizeNarration.cs` — cache each clip the moment it is made; stop at the failing clip and name it
- `src/PeakMediaRemoteMcpServer/` — background service for LRU eviction at `Storage:CacheMaxGb` and expired download-token clean-up; last-used refresh on hit; Serilog redaction check for the OpenRouter key and the OIDC client secret
- `tests/Fakes/` — fake OpenRouter failure modes (no answer, 503 then success, 402, 401); a way for tests to run the eviction service on demand
