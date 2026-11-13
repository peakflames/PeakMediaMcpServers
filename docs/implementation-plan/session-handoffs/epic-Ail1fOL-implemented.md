# Epic Ail1fOL — Dashboard shell and My Usage — Implemented

**Date:** 2026-11-13 · **Branch:** `feature/epic-Ail1fOL-dashboard-shell-and-my-usage` · **By:** Todd Schavey (autopilot session)

## What Was Built

- The usage dashboard inside `PeakMediaRemoteMcpServer`: Blazor Interactive Server with Blazor Blueprint UI
  4.2 (`BlazorBlueprint.Components`, `BlazorBlueprint.Icons.Lucide`), design tokens in `wwwroot/theme.css`,
  app layout in `wwwroot/app.css`, and one app shell (`MainLayout`: brand, primary nav with `aria-current`,
  signed-in email, Theme select light/dark/system, Sign out, version footer, `<BbPortalHost />`).
- Dashboard sign-in: cookie session `pm_dashboard` started by an OpenID Connect client against the
  organization's provider (the broker's `Broker:Upstream*` settings; code + PKCE; callback `/signin-oidc`;
  `response_mode=query`). The session carries only `sub`/`email`/`role`; the admin role comes from the
  same rule the broker uses (`AdminGroup.Grants`, now shared). A selector default scheme keeps `/mcp`
  on JwtBearer only — the cookie can never authenticate `/mcp`.
- Screens: My Usage (S-01, `/`) with loading / empty / error / populated states, a cancelled state, Cancel
  after 10 s, month select (current + 11 previous months), "Go to current month", Retry, and the
  over-quota notice; Not Authorized (S-05) on every `/admin` address with HTTP 403 for non-admins (admins
  get 404 until Epic G8jMzOz adds their pages); Signed Out (S-06, static, brand-only layout).
- `UsageStatusReader`: one month-parameterized reader shared by the `my_usage` tool (`UsageLedger`) and the
  dashboard (`DashboardUsageService`), through `UsageStore`/`QuotaStore` and `AccessRule`.
- `POST /signout` (antiforgery-checked): clears only the browser session, redirects to `/signed-out`.
- New end-to-end project `tests/PeakMediaRemoteMcpServer.E2E` (Microsoft.Playwright 1.64.1, Chromium
  installed on first run) and xUnit tests under `tests/PeakMediaRemoteMcpServer.Tests/Dashboard/`.
- Amendment: TOR-09-fjQhX70 superseded by TOR-09-qu5yesw (CR-sziqlY7, owner-approved 2026-11-13).

## Deferrals

Count: 0

None

| TOR ID | Unmet | Why | Decision | Successor epic | By | Date |
|---|---|---|---|---|---|---|

## Key Files

- `src/PeakMediaRemoteMcpServer/Auth/DashboardAuthSetup.cs`, `Auth/AdminGroup.cs`, `Auth/McpAuthSetup.cs` (selector scheme, `dashboard` policy)
- `src/PeakMediaRemoteMcpServer/Components/` — `App.razor`, `Routes.razor`, `RedirectToSignIn.razor`, `Layout/MainLayout.razor`, `Layout/AuthLayout.razor`, `Pages/MyUsage.razor`, `Pages/NotAuthorized.razor`, `Pages/SignedOut.razor`
- `src/PeakMediaRemoteMcpServer/wwwroot/theme.css`, `wwwroot/app.css`, `wwwroot/js/theme-choice.js`
- `src/PeakMediaRemoteMcpServer/Dashboard/DashboardEndpoints.cs`
- `src/PeakMediaRemoteMcpServer/Usage/UsageStatusReader.cs`, `Usage/DashboardUsageService.cs`, `Usage/UsageLedger.cs`
- `src/PeakMediaRemoteMcpServer/Storage/SqliteConnectionFactory.cs` (internal replaceable `FaultSwitch`; delay honors cancellation)
- `src/PeakMediaRemoteMcpServer/Program.cs`, `PeakMediaRemoteMcpServer.csproj`
- `tests/PeakMediaRemoteMcpServer.E2E/*`, `tests/PeakMediaRemoteMcpServer.Tests/Dashboard/*`
- `tests/StubAuthorizationServer/StubOidcIssuer.cs` (also accepts `client_secret_post`)
- `docs/requirements/09-ux-baseline.feature.md`, `.tracing.json`, `docs/requirements/changes/2026-11-13-sziqlY7.toml`
- `docs/architecture.md` (§6 Frontend, §9 dashboard session and test fault switch)

## Spec Deviations

| TOR ID | As-Written | As-Implemented | Reason |
|---|---|---|---|
| TOR-09-fjQhX70 → TOR-09-qu5yesw | Every focused control's box lies outside the app header's box | Controls outside the header do not overlap it; header controls lie within the viewport | Amendment CR-sziqlY7 (owner-approved): the header's own controls (Sign out, nav, theme) cannot lie outside the header |
| (test layout) | E2E launches the built gateway as a child process with `PEAKMEDIA_TEST_STORAGE_FAULT` and restarts per scenario | E2E hosts the real gateway (`Program.BuildApp`, real Kestrel, real SQLite) in the test process | The TORs' fixed clock (2026-11-12 10:00 UTC) and clearing the fault switch mid-page (TOR-08-rxZtbxT) need in-process access; no restart-free alternative without a request-reachable test hook |

## Key Decisions

- Design skills used: frontend-design:frontend-design, design:design-system, design:accessibility-review.
- Blazor Blueprint MCP server: its tools were refused by the host in this unattended session, so every
  `Bb*` component, parameter and enum value was taken from the library's llms.txt
  (https://blazorblueprintui.com/llms.txt and its `llms/components/*.txt`, `llms/setup.txt`) and checked
  against the package's XML docs (`BlazorBlueprint.Components.xml` 4.2.0) — nothing from memory.
- Theme choice: the library's `BbDarkModeToggle` is two-state, but the TOR asks for light / dark / system.
  A labelled `BbNativeSelect` writes the library's own `bb-theme` storage entry (`isDarkMode`), so
  `theme-init.js` applies it before first paint; "System" removes the entry (OS preference).
- Dashboard pages use a named policy (`dashboard`, cookie scheme): Blazor components may not name
  authentication schemes directly.
- `response_mode=query` (not the handler's default `form_post`): a cross-site POST back from Okta would
  drop the SameSite=Lax correlation/nonce cookies.
- Cookies and the OIDC handler use the wall clock (`TimeProvider.System`), whatever clock usage uses.
- The Okta app needs `G/signin-oidc` added to its sign-in redirect URIs (release check).
- My Usage loads only in the interactive render; the server render shows the loading state, so the
  loading text appears within 1 s even when storage is slow.

## TOR Coverage (self-assessment)

| TOR | Result | Test | Implementation |
|---|---|---|---|
| TOR-08-7gPJfwq | PASS | `E2E/SignInTests.cs:11` | `Pages/MyUsage.razor:2`, `Auth/DashboardAuthSetup.cs` (cookie → OIDC challenge) |
| TOR-08-RiNk9re | PASS | `E2E/SignInTests.cs:43` | `Layout/MainLayout.razor:6-37` |
| TOR-08-XOxyf4r | PASS | `E2E/RolesTests.cs:11`, `:28`; `Tests/Dashboard/AdminRoleMappingTests.cs` | `Layout/MainLayout.razor:9-14` |
| TOR-08-iCZ63da | PASS | `E2E/RolesTests.cs:42` | `Pages/NotAuthorized.razor` |
| TOR-08-Z8HzUrl | PASS | `E2E/RolesTests.cs:69` | `Pages/NotAuthorized.razor` ("Go to My usage") |
| TOR-08-F0Gau1p | PASS | `E2E/SignInTests.cs:61` | `Dashboard/DashboardEndpoints.cs`, `Pages/SignedOut.razor` |
| TOR-08-qmHrQZN | PASS | `E2E/SignInTests.cs:90` | `Pages/SignedOut.razor` (full-load link to `/`) |
| TOR-08-fX9XW5F | PASS | `E2E/ThemeTests.cs:9` | `Layout/MainLayout.razor` Theme select, `wwwroot/js/theme-choice.js`, `wwwroot/theme.css` |
| TOR-08-v8yv7CR | PASS | `E2E/MyUsageTests.cs:17`; `Tests/Dashboard/DashboardUsageServiceTests.cs:13` | `Pages/MyUsage.razor` stat cards, `Usage/UsageStatusReader.cs` |
| TOR-08-TZrQMGu | PASS | `E2E/MyUsageTests.cs:49`; `Tests/Dashboard/DashboardUsageServiceTests.cs:46` | `Usage/DashboardUsageService.cs`, Month select |
| TOR-08-0OVqiet | PASS | `E2E/MyUsageTests.cs:70` | `Pages/MyUsage.razor` over-quota alert |
| TOR-08-6kh4CnB | PASS | `E2E/MyUsageTests.cs:88` | `Pages/MyUsage.razor` `GoToCurrentMonth` |
| TOR-08-rxZtbxT | PASS | `E2E/MyUsageTests.cs:113` | `Pages/MyUsage.razor` Retry → `LoadAsync` |
| TOR-09-upSE1xN | PASS | `E2E/UxStatesTests.cs:14` | `Pages/MyUsage.razor` loading state (server render) |
| TOR-09-9Orj70T | PASS | `E2E/UxStatesTests.cs:40` | `Pages/MyUsage.razor` empty state |
| TOR-09-gadlg25 | PASS | `E2E/UxStatesTests.cs:58` | `Pages/MyUsage.razor` error state |
| TOR-09-ZbKGrFs | PASS | `E2E/UxStatesTests.cs:77` | `Pages/MyUsage.razor` populated state |
| TOR-09-qu5yesw | PASS | `E2E/FocusAndContrastTests.cs:47` | `wwwroot/app.css` focus ring; header not sticky |
| TOR-09-NtBVF41 | PASS | `E2E/UxStatesTests.cs:101` | `Pages/MyUsage.razor` `OfferCancelLaterAsync` / `Cancel`; `Storage/SqliteConnectionFactory.cs` |
| TOR-09-BLJrVJ6 | PASS | `E2E/FocusAndContrastTests.cs:130` | `wwwroot/theme.css` token pairs |

## Verification Results (self-assessment)

| Gate | Result |
|---|---|
| `dotnet format PeakMediaMcpServers.sln --verify-no-changes` | PASS |
| `.venv/bin/python build.py build` | PASS |
| `.venv/bin/python build.py test` | PASS — 25 (stdio) + 21 (E2E) + 179 passed, 1 skipped (the opt-in live test, run by the next gate) |
| `.venv/bin/python build.py test-live` | PASS — 1 passed, 0 skipped (one real OpenRouter call) |
| `.venv/bin/python build.py smoke` | PASS |
| `build.py start` + `curl /version` + `build.py stop` | PASS — `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}` |
| stdio `--version` | PASS — `PeakMediaMcpServer v0.1.0`, exit 0 |
| UX Baseline (playwright-cli against the running gateway, signed in through the test OIDC issuer) | Screen states PASS (E2E); Keyboard & focus PASS (no modals on these screens); Forms N/A; Destructive actions N/A; Progress feedback PASS; Layout floor PASS (no horizontal scroll at 320 px and at 200 % zoom); Contrast PASS (light + dark); Reduced motion PASS (`prefers-reduced-motion` rule in `app.css`); Navigation PASS (unique titles, one H1 matching, `aria-current` on the current item) |
