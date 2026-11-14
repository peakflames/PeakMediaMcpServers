# Epic Ail1fOL: Dashboard shell and My Usage — Complete

**Completed:** 2026-11-14
**Verified by:** Independent review via `/peak-workflow:wrapup-epic Ail1fOL` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

Any signed-in person can open the gateway in a browser, sign in through the organization's OIDC provider, and read their own month on My Usage (S-01) — the same figures `my_usage` returns — with a month select, loading / empty / error / populated / cancelled states, an over-quota notice, and Cancel after 10 seconds. Every `/admin` address answers 403 with Not Authorized (S-05) for non-administrators, and "Sign out" ends the browser session on Signed Out (S-06). The epic installs Blazor Blueprint UI, the design tokens and the one app shell (brand, per-role navigation, light/dark/system theme, sign-out, version footer) the later dashboard epics compose from.

## Key Files

| File | Purpose |
|------|---------|
| `src/PeakMediaRemoteMcpServer/Auth/DashboardAuthSetup.cs` | Dashboard cookie session + OIDC client (code + PKCE, `/signin-oidc`); selector scheme keeps `/mcp` on JwtBearer only |
| `src/PeakMediaRemoteMcpServer/Auth/AdminGroup.cs` | The one groups → admin rule, shared with the broker callback |
| `src/PeakMediaRemoteMcpServer/Auth/McpAuthSetup.cs` | `dashboard` authorization policy (cookie scheme) |
| `src/PeakMediaRemoteMcpServer/Components/` | `App.razor`, `Routes.razor`, `Layout/MainLayout.razor` (app shell), `Layout/AuthLayout.razor`, `Pages/MyUsage.razor`, `Pages/NotAuthorized.razor`, `Pages/SignedOut.razor` |
| `src/PeakMediaRemoteMcpServer/wwwroot/theme.css`, `app.css`, `js/theme-choice.js` | Design tokens, app layout / focus / reduced-motion styles, theme choice |
| `src/PeakMediaRemoteMcpServer/Dashboard/DashboardEndpoints.cs` | `POST /signout` (antiforgery-checked) |
| `src/PeakMediaRemoteMcpServer/Usage/UsageStatusReader.cs`, `DashboardUsageService.cs` | One month-parameterized reader shared by `my_usage` and My Usage, through `AccessRule` |
| `src/PeakMediaRemoteMcpServer/Storage/SqliteConnectionFactory.cs` | Test fault switch replaceable in-process; delay honors cancellation |
| `tests/PeakMediaRemoteMcpServer.E2E/` | Playwright end-to-end tests against the real in-process gateway and stub OIDC issuer |
| `tests/PeakMediaRemoteMcpServer.Tests/Dashboard/` | Usage service, admin role mapping, `/version` and `/mcp` regression |

## Key Decisions

- Theme choice is a labelled `BbNativeSelect` (light / dark / system) writing the library's own `bb-theme` storage entry, so `theme-init.js` applies it before first paint; the library's two-state toggle could not offer "System".
- The dashboard session principal carries only `sub` / `email` / `role`, so `Caller.From` and `AccessRule` decide for the dashboard exactly as for the MCP tools; the cookie never authenticates `/mcp`.
- OIDC uses `response_mode=query` so the SameSite=Lax correlation and nonce cookies survive the provider's redirect back.
- My Usage loads only in the interactive render; the server render shows the loading state at once.
- Administrators get 404 on `/admin*` until Epic G8jMzOz adds their pages.
- Wrapup added `<link rel="icon" href="data:," />` to `App.razor` so pages load with no console error.

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-08-7gPJfwq | The dashboard shall redirect a browser with no session to the organization's OIDC sign-in page | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/SignInTests.cs:9 |
| TOR-08-RiNk9re | The dashboard shall land a person on My Usage (S-01) after a successful sign-in and show their email in the app header | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/SignInTests.cs:41 |
| TOR-08-XOxyf4r | The dashboard shall show a person outside the admin group the "My usage" navigation link only | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/RolesTests.cs:9 |
| TOR-08-iCZ63da | The dashboard shall answer every /admin address opened by a non-administrator with 403 and Not Authorized (S-05) | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/RolesTests.cs:40 |
| TOR-08-Z8HzUrl | The dashboard shall take a person from Not Authorized (S-05) to My Usage (S-01) through the "Go to My usage" link | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/RolesTests.cs:67 |
| TOR-08-F0Gau1p | The dashboard shall end the browser session and show Signed Out (S-06) when the "Sign out" button is used | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/SignInTests.cs:59 |
| TOR-08-qmHrQZN | The dashboard shall start a new sign-in from the "Sign in" link on Signed Out (S-06) | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/SignInTests.cs:88 |
| TOR-08-fX9XW5F | The dashboard shall offer a light, dark and system theme choice and apply the dark theme through the "dark" class on the html element | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/ThemeTests.cs:7 |
| TOR-08-v8yv7CR | The dashboard shall show on My Usage (S-01) the same figures my_usage returns for the signed-in person | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/MyUsageTests.cs:15 |
| TOR-08-TZrQMGu | The dashboard shall show on My Usage (S-01) the figures of the month chosen in the "Month" select | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/MyUsageTests.cs:47 |
| TOR-08-0OVqiet | The dashboard shall show a quota notice on My Usage (S-01) when the person's spend has reached their quota | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/MyUsageTests.cs:68 |
| TOR-08-6kh4CnB | The dashboard shall return My Usage (S-01) to the current month when the "Go to current month" button is used | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/MyUsageTests.cs:86 |
| TOR-08-rxZtbxT | The dashboard shall reload My Usage (S-01) when the "Retry" button is used after a failed load | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/MyUsageTests.cs:111 |
| TOR-09-upSE1xN | The dashboard shall render an explicit loading state, distinguishable by visible text, while a data-bearing screen loads | `docs/requirements/09-ux-baseline.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/UxStatesTests.cs:12 |
| TOR-09-9Orj70T | The dashboard shall render an explicit empty state, distinguishable by visible text, when a data-bearing screen has nothing to show | `docs/requirements/09-ux-baseline.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/UxStatesTests.cs:38 |
| TOR-09-gadlg25 | The dashboard shall render an explicit error state, distinguishable by visible text, when a data-bearing screen fails to load | `docs/requirements/09-ux-baseline.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/UxStatesTests.cs:56 |
| TOR-09-ZbKGrFs | The dashboard shall render an explicit populated state on a data-bearing screen that has data | `docs/requirements/09-ux-baseline.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/UxStatesTests.cs:75 |
| TOR-09-qu5yesw | The dashboard shall display a visible focus indicator on the focused control that is not hidden behind the app header | `docs/requirements/09-ux-baseline.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/FocusAndContrastTests.cs:45 |
| TOR-09-NtBVF41 | The dashboard shall offer a cancel control for any operation that takes longer than 10 seconds | `docs/requirements/09-ux-baseline.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/UxStatesTests.cs:99 |
| TOR-09-BLJrVJ6 | The dashboard shall render body text at a contrast ratio of at least 4.5:1, and large text at least 3:1, in both the light and the dark theme | `docs/requirements/09-ux-baseline.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/FocusAndContrastTests.cs:128 |

## Requirement Changes

- CR-sziqlY7 supersede — TOR-09-fjQhX70 → TOR-09-qu5yesw — Focus-not-obscured check applies to controls outside the app header (approved via epic-branch, by Todd Schavey)

## Verification Summary

### Counts
- TOR Requirements: 20/20 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 10/10 PASS (Brand compliance N/A — no brand skill configured)
- Tests: 226 passed, 0 skipped, 0 failed (suite 225 + `test-live` 1)

### Highlights
- ✅ TOR-08-7gPJfwq / RiNk9re / F0Gau1p / qmHrQZN — sign-in, sign-out and re-sign-in through the real OIDC redirect flow against the stub issuer (E2E/SignInTests.cs:9, Auth/DashboardAuthSetup.cs:34)
- ✅ TOR-08-iCZ63da — every `/admin` address answers 403 + Not Authorized, decided server-side by `AccessRule.IsAdmin` (E2E/RolesTests.cs:40, Pages/NotAuthorized.razor:39)
- ✅ TOR-08-v8yv7CR — dashboard and `my_usage` share one `UsageStatusReader`; equality asserted (Tests/Dashboard/DashboardUsageServiceTests.cs:16, Usage/UsageStatusReader.cs:10)
- ✅ TOR-09-upSE1xN / NtBVF41 — loading text within 1 s; Cancel after 10 s actually cancels the load (E2E/UxStatesTests.cs:12, Pages/MyUsage.razor:171)
- ✅ TOR-09-BLJrVJ6 / qu5yesw — contrast measured on every text element in light and dark; focus ring and header-overlap at a 480 px viewport (E2E/FocusAndContrastTests.cs:45)
- 🔧 Console gate — `/favicon.ico` 404 on every page load fixed at wrapup (empty inline favicon in `App.razor`)

### Conclusion
Every TOR has a test that mirrors its Given/When/Then and passed in the verifier's own run, and source inspection confirms each behavior; the E2E tests drive the real gateway (Kestrel, SQLite, OIDC redirect flow) and the running gateway was exercised with `playwright-cli`. The one failing gate (console error) was fixed at wrapup and re-verified.

### Manual verification performed: No

## Known Issues / Follow-ups

- Wireframe deviations: S-01 over-quota notice lacks the "Over" badge; S-05 lacks the paragraph "Your own usage is on My usage. If you need administrator access, ask whoever runs the gateway."; S-06 body text differs and lacks the hint under "Sign in".
- Release check: add `<gateway>/signin-oidc` to the Okta app's sign-in redirect URIs.
- The implementer could not reach the Blazor Blueprint MCP server and used the library's llms.txt (the fallback CLAUDE.md allows).
