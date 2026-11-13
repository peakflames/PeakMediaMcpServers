# Epic YVOjXtI: Stdio server and client contract

**Phase:** 3 — Solo use and client contract
**Status:** Not Started
**Dependencies:** Epic 8po1tRg (Narrate a spine — shared narration tools), Epic 8nPWJzI (Failure recovery and cache eviction — retry and failure errors), Epic EYGrros (Quotas and usage — `my_usage` contract for the document)
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

---

## Description

ConOps Scenario 6 and vision goal G6: a solo user with their own OpenRouter key gets the same `synthesize_narration` and `list_voices` over stdio — clips written straight into the local cache and returned as paths, no sign-in, no database — so the peak-media skills keep a single code path. The stdio server also accepts `fake/tone` for offline testing.

The epic closes with `docs/client-integration.md`, the written contract the peak-media plugin adopts in its own repository: the tool arguments and result fields, the selection order (connected server → personal key → beat grid, never falling back after a gateway error), the download-into-cache step, and the `claude mcp add` commands. The four document requirements are merged here because they are small and describe exactly what this epic finishes.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-06-WYyXusz | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-dX6DFFO | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-ej048Ep | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-emXCHe2 | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-heFgF6C | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-vtaQIaK | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-D8tj7pH | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-g9QibOG | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-UeA7aSw | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-i2lj5iF | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-fTgpl4m | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-PY6kH1r | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-bPXmWnj | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-FXNq0Rx | `docs/requirements/06-stdio-server.feature.md` |
| TOR-06-GC17sBD | `docs/requirements/06-stdio-server.feature.md` |
| TOR-07-LaYp8xj | `docs/requirements/07-client-integration.feature.md` |
| TOR-07-1XYMIbC | `docs/requirements/07-client-integration.feature.md` |
| TOR-07-8MvsuuM | `docs/requirements/07-client-integration.feature.md` |
| TOR-07-akAqnvh | `docs/requirements/07-client-integration.feature.md` |

## Key Components

- `src/PeakMediaMcpServer/Program.cs` — key resolution (`PEAK_MEDIA_OPENROUTER_API_KEY`, legacy `TTS_OPENROUTER_API_KEY`), the missing-key error on stderr with exit code 1, registration of the two shared tools only
- `src/PeakMediaMcpServer/` — local file cache store (`~/.cache/peak-media/tts/`, `PEAK_MEDIA_TTS_CACHE` override) returning `path` in place of `url`; the `fake/tone` synthesizer
- `src/PeakMediaMcpTools/` — the storage abstraction that lets the same tool code return `url` (gateway) or `path` (stdio)
- `tests/` — stdio process tests against the fake OpenRouter; a contract test comparing the two servers' tool schemas and result keys
- `docs/client-integration.md`
