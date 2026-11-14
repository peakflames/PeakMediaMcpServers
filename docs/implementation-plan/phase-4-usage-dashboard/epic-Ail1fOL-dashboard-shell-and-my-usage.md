# Epic Ail1fOL: Dashboard shell and My Usage

**Phase:** 4 — Usage dashboard
**Status:** Complete — 2026-11-14
**Dependencies:** Epic EYGrros (Quotas and usage — usage and quota figures)
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

> **Design:** UI epic — `/peak-workflow:start-epic` runs a Design pass with the design skills installed at that time (brand kit, `frontend-design`, Blazor Blueprint UI) for the app shell, navigation, theme toggle, My Usage, Not Authorized and Signed Out.

---

## Description

ConOps Scenario 8: any signed-in person opens the gateway in a browser, signs in through the same OIDC provider, and reads their own month — the same figures `my_usage` returns — while admin pages answer 403 for everyone else. This is the first dashboard epic, so it also does the work a walking skeleton would do for a UI: it installs Blazor Blueprint UI, defines the tokens in `theme.css`, and builds the one app shell (header, per-role navigation, theme toggle, footer version) that the later dashboard epics compose from.

My Usage (S-01) is the first reference screen: it renders the loading, empty, error and populated states and passes the baseline UX requirements anchored on it (screen states, focus visibility, cancel after 10 seconds, text contrast). No later epic installs a component library, defines tokens, or builds a second shell; `docs/architecture.md` records the token file and the screen-composition pattern. Look every `Bb*` component up through the `blazorblueprint` MCP server before writing markup.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-08-7gPJfwq | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-RiNk9re | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-XOxyf4r | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-iCZ63da | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-Z8HzUrl | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-F0Gau1p | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-qmHrQZN | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-fX9XW5F | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-v8yv7CR | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-TZrQMGu | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-0OVqiet | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-6kh4CnB | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-08-rxZtbxT | `docs/requirements/08-usage-dashboard.feature.md` |
| TOR-09-upSE1xN | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-9Orj70T | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-gadlg25 | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-ZbKGrFs | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-qu5yesw | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-NtBVF41 | `docs/requirements/09-ux-baseline.feature.md` |
| TOR-09-BLJrVJ6 | `docs/requirements/09-ux-baseline.feature.md` |

## Screens

| Screen | Wireframe | States this epic delivers |
|--------|-----------|---------------------------|
| S-01 My Usage | `docs/product-vision-planning/ux/wireframes/S-01-my-usage.html` | loading / empty / error / populated |
| S-05 Not Authorized | `docs/product-vision-planning/ux/wireframes/S-05-not-authorized.html` | populated |
| S-06 Signed Out | `docs/product-vision-planning/ux/wireframes/S-06-signed-out.html` | populated |

## Key Components

### Backend
- `src/PeakMediaRemoteMcpServer/Program.cs` — `AddBlazorBlueprintComponents()`, Razor components with Interactive Server render mode, OIDC cookie session through the same provider, the admin authorization policy (`groups` contains `Admin:Group`) with 403 → Not Authorized for every `/admin` address, sign-out
- A usage query service for one person's month, called through the access rule (own data only), honoring the test-only fault / latency switch

### Frontend
- `src/PeakMediaRemoteMcpServer/Components/` — `App.razor` (loads `theme.css` before the library CSS, `theme-init.js`), `Routes.razor` (interactive render mode), `_Imports.razor`, `Layout/MainLayout.razor` (app header, primary navigation with the current item marked, signed-in email, "Sign out", theme toggle, footer version, `<BbPortalHost />`)
- `src/PeakMediaRemoteMcpServer/wwwroot/theme.css` (shadcn-compatible `:root` / `.dark` tokens), `wwwroot/app.css`
- Pages: My Usage (`/`), Not Authorized, Signed Out — composed from the shell; Lucide icons only
- `tests/` — Playwright end-to-end tests against the running gateway, signed in through the stub issuer's test helper
