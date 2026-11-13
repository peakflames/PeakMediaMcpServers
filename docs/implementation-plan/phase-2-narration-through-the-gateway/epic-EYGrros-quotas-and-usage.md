# Epic EYGrros: Quotas and usage

**Phase:** 2 — Narration through the gateway
**Status:** Complete — 2026-11-13
**Dependencies:** Epic 8po1tRg (Narrate a spine — synthesis and cache hits), Epic 8nPWJzI (Failure recovery and cache eviction — partial-failure behavior)
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

---

## Description

ConOps Scenarios 3 and 4: every synthesized clip is charged to the caller at the real cost OpenRouter reports, cache hits cost nothing, and a caller whose month-to-date spend has reached their monthly quota is refused before any synthesis with an error naming the spend, the quota and the reset time. `my_usage` tells each caller where they stand — and only where *they* stand.

This is what makes sharing one OpenRouter key safe: nobody is surprised by a refusal and nobody can run up the organization's bill unchecked. The epic owns the usage log (never the narration text), the default quota and per-user overrides (edited later from the dashboard in Epic EwgA3Zf), and proves usage, overrides and cached audio survive a restart.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-05-MUV56KT | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-dFazrDm | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-DIrxTPy | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-3JuMxwL | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-3cXUiMQ | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-N0KTSFQ | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-HqJn98D | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-zi52cgw | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-efm0UOd | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-babYeNE | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-bLVMjT4 | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-0pHv4Fb | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-WmxtWmi | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-05-ryNGNXA | `docs/requirements/05-quotas-and-usage.feature.md` |
| TOR-04-csNgnyQ | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-04-2qCsalR | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-04-xYMUNYV | `docs/requirements/04-shared-audio-cache.feature.md` |
| TOR-02-dj60B0R | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-Dc8Z0Ge | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-01-65SaYiJ | `docs/requirements/01-service-foundation.feature.md` |

## Key Components

- `src/PeakMediaMcpTools/TtsTools_MyUsage.cs` — gateway only; month, spent, quota, remaining, clips synthesized, cache hits, resets; US dollars to the cent, UTC
- `src/PeakMediaRemoteMcpServer/` — usage log (subject, time, model, voice, characters, cost, cache key, hit/miss — no text), quota store (configured default $25 + per-user overrides with set-by and set-at), the per-call quota gate (checked at call start, calendar month in UTC), the price fallback with its warning when the reported cost cannot be read
- Every usage and quota read goes through the access rule from Epic 7BASFbp — a caller sees only rows under their own OIDC subject
- A quota-override service method the dashboard will call later; tests set overrides through it (never by mocking the database)
- `appsettings.json` — default monthly quota, model prices for the fallback
