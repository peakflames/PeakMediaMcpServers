# Epic EwgA3Zf: User detail and quota editing

**Phase:** 4 — Usage dashboard
**Status:** Implemented — 2026-11-14
**Dependencies:** Epic G8jMzOz (Admin usage and cache overview — Usage screen and admin navigation)
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

> **Design:** UI epic — `/peak-workflow:start-epic` runs a Design pass with the design skills installed at that time (brand kit, `frontend-design`, Blazor Blueprint UI) for the User Detail table, the quota form and the "Use default…" confirmation dialog.

---

## Description

The second half of ConOps Scenario 5 and vision goal G8: from the Usage table an admin opens one person's month — one row per clip, never the narration text — and raises, lowers or resets that person's monthly quota; the change applies to the person's next call. This is the only place quotas are changed.

User Detail (S-03) is the second reference screen: its quota form and "Use default…" confirmation carry the baseline UX requirements for forms, modal focus, confirmation of irreversible actions, progress feedback, keyboard operation, the 320 px layout floor, control contrast, reduced motion, and page titles / H1 / current navigation item. Because several of those requirements also name My Usage (S-01), this epic re-verifies S-01 from Epic Ail1fOL against them.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-08-sfJ3IWh | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-JeFRxDI | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-i1syL2k | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-iUG0WAC | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-QNNE1fW | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-bjHma3b | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-6Vzjnrg | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-MCIZVXn | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-wX5QWUB | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-bydYtvm | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-09-dty904j | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-CYbsdon | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-7aP7tWP | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-YFn4LMC | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-pQcPvui | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-RA5qSsd | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-do58nOS | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-TpkiHjX | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-cTG2frO | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-1DjTmDv | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-P5YL2JO | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-GFQ80Xs | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-9irho1R | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-hdlqc7J | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-PVdyo7H | `docs/requirements/09-ux-baseline.feature.md` |

## Screens

| Screen | Wireframe | States this epic delivers |
|--------|-----------|---------------------------|
| S-03 User Detail | `docs/product-vision-planning/ux/wireframes/S-03-user-detail.html` | loading / empty / error / populated |

## Key Components

### Backend
- Admin query service for one user's usage rows in a month (time, model, voice, characters, cost, source) and month summary; quota set / clear through the override service from Epic EYGrros, recording the admin's subject and time — all behind the access rule's admin role and the test-only fault / latency switch

### Frontend
- Page: User Detail (`/admin/users/{id}`) — breadcrumb, "Month" select, quota form ("Monthly quota (USD)", "Save", inline validation text), "Use default…" `AlertDialog` with "Cancel" first and default — composed from the shell and design system of Epic Ail1fOL; no new component library, no new tokens
- Usage (S-02): the user email link to User Detail
- `tests/` — Playwright tests for the form, dialog, keyboard, 320 px / 200% zoom, contrast and reduced-motion requirements
