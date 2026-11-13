# Epic G8jMzOz: Admin usage and cache overview

**Phase:** 4 — Usage dashboard
**Status:** Not Started
**Dependencies:** Epic Ail1fOL (Dashboard shell and My Usage — dashboard shell, sign-in and roles)
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

> **Design:** UI epic — `/peak-workflow:start-epic` runs a Design pass with the design skills installed at that time (brand kit, `frontend-design`, Blazor Blueprint UI) for the Usage table and the Cache stat cards.

---

## Description

The first half of ConOps Scenario 5: an admin signs in, sees the "Usage" and "Cache" navigation links nobody else is offered, and reads every user's month on one table — spend against quota, an `OVER` badge where the quota is reached, a Total row — plus whether the shared cache is earning its keep. This is the only place organization-wide usage is visible.

The epic adds the admin navigation (including its collapsed sheet below 480 px) and the two overview screens, composed from the shell and patterns of Epic Ail1fOL. The real-Okta admin landing (`TOR-08-nLHs0pq`) is operator-observed.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-08-nLHs0pq | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-D1L4ilb | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-leBAXoJ | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-kX399h8 | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-nzR0l2p | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-nFpaAG7 | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-rnr0ApF | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-S8MYBHC | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-VHkMu93 | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-5ZEXDBu | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-NeOMGCI | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-irZrgRw | `docs/requirements/08-usage-dashboard.feature.md` |

## Screens

| Screen | Wireframe | States this epic delivers |
|--------|-----------|---------------------------|
| S-02 Usage | `docs/product-vision-planning/ux/wireframes/S-02-usage.html` | loading / empty / error / populated |
| S-04 Cache | `docs/product-vision-planning/ux/wireframes/S-04-cache.html` | loading / empty / error / populated |

## Key Components

### Backend
- Admin query services: usage per user for a month (spent, quota, characters, clips, cache-hit rate, totals) and cache statistics (size, cap, clips, hit rate and evictions this month) — both behind the access rule's admin role, both honoring the test-only fault / latency switch

### Frontend
- `src/PeakMediaRemoteMcpServer/Components/Layout/MainLayout.razor` — "Usage" and "Cache" links for admins only (absent, not disabled, for others); the navigation sheet below 480 px
- Pages: Usage (`/admin`), Cache (`/admin/cache`) — composed from the skeleton shell and design system of Epic Ail1fOL; no new component library, no new tokens
- `tests/` — Playwright tests signed in as an admin through the stub issuer
