# Epic EwgA3Zf: User detail and quota editing — Complete

**Completed:** 2026-11-14
**Verified by:** Independent review via `/peak-workflow:wrapup-epic EwgA3Zf` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

Administrators open one person's month from the email link on Usage (S-02) and see User Detail (S-03): a
month summary (spend against quota, OVER badge, clips, cache-hit rate), one row per clip newest first
(never the narration text), and their monthly quota. They can set a quota override (labelled field,
inline validation, "Saving…" progress, text confirmation or failure) or reset it through a "Use default…"
confirmation dialog whose safe option is the default; the change applies to the person's next call.

## Key Files

| File | Purpose |
|------|---------|
| `src/PeakMediaRemoteMcpServer/Components/Pages/AdminUserDetail.razor` | S-03 page: states, quota form, confirmation dialog, requests table |
| `src/PeakMediaRemoteMcpServer/Usage/UserDetailService.cs` | Report, save, reset, amount parsing, display name — admin only |
| `src/PeakMediaRemoteMcpServer/Usage/UsageStore.cs` | `ListUserMonthAsync` — one user's clips in a month (RequireAdmin) |
| `src/PeakMediaRemoteMcpServer/Usage/QuotaStore.cs` | `GetQuotaInfoAsync` (quota + is-default), `ClearOverrideAsync` (RequireAdmin) |
| `src/PeakMediaRemoteMcpServer/Components/Pages/AdminUsage.razor` | Email links to User Detail, carrying the month shown |
| `src/PeakMediaRemoteMcpServer/Components/Layout/MainLayout.razor` | `aria-current="true"` on Usage while a user's detail is shown |
| `src/PeakMediaRemoteMcpServer/wwwroot/app.css` | Quota form styles; requests table stacks into labelled lines below 720 px (wrapup fix) |
| `src/PeakMediaRemoteMcpServer/wwwroot/js/menu-focus.js` | `peakFocus.restore`, `peakForm.value` |
| `tests/PeakMediaRemoteMcpServer.E2E/UserDetailTests.cs` | Browser tests for every anchor TOR |
| `tests/PeakMediaRemoteMcpServer.Tests/Dashboard/UserDetailServiceTests.cs`, `QuotaStoreEditingTests.cs` | Service and store tests through the real sign-in and stores |

## Key Decisions

- The User Detail route identifies the person by email (`/admin/users/{email}?month=`); the store resolves it to the subject, and fails rather than guesses if two users rows share an email.
- Escape on the `BbAlertDialog` is handled by an `@onkeydown` on the content (the component has no Escape option); focus returns to "Use default…" through `peakFocus.restore`.
- Save reads the field's on-screen value at submit (`peakForm.value`) because `BbInput`'s change notification can arrive after the submit.
- Quotas are rounded to the cent and capped at $1,000,000.
- Page titles across the dashboard use ` · ` ("My usage · PeakMedia Gateway").
- Design skills used: `frontend-design:frontend-design`, `design:design-system`, `design:accessibility-review` (the `blazorblueprint` MCP server was not connected during implementation; component APIs came from the package's XML docs).

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-08-sfJ3IWh | The dashboard shall open User Detail (S-03) for a user when their email link on Usage (S-02) is used | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/UserDetailTests.cs:60 |
| TOR-08-JeFRxDI | The dashboard shall list on User Detail (S-03) one row per clip of the user's month, newest first, without the narration text | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Dashboard/UserDetailServiceTests.cs:36, UserDetailTests.cs:100 |
| TOR-08-i1syL2k | The dashboard shall summarize on User Detail (S-03) the user's month: spend against quota, an "OVER" badge when reached, clips and cache-hit rate | `docs/requirements/08-usage-dashboard.feature.md` | PASS | UserDetailTests.cs:123 |
| TOR-08-iUG0WAC | The dashboard shall show on User Detail (S-03) the user's current monthly quota and whether it is the default | `docs/requirements/08-usage-dashboard.feature.md` | PASS | UserDetailTests.cs:142 |
| TOR-08-QNNE1fW | The dashboard shall save a quota override when an administrator enters an amount in "Monthly quota (USD)" and uses the "Save" button, and shall confirm it in text | `docs/requirements/08-usage-dashboard.feature.md` | PASS | UserDetailTests.cs:176, UserDetailServiceTests.cs:103 |
| TOR-08-bjHma3b | The dashboard shall tell the administrator in text when a quota cannot be saved and keep the entered amount | `docs/requirements/08-usage-dashboard.feature.md` | PASS | UserDetailTests.cs:200 |
| TOR-08-6Vzjnrg | The dashboard shall clear a user's quota override when an administrator confirms "Use default" on User Detail (S-03) | `docs/requirements/08-usage-dashboard.feature.md` | PASS | UserDetailTests.cs:280 |
| TOR-08-MCIZVXn | The dashboard shall show on User Detail (S-03) the rows of the month chosen in the "Month" select | `docs/requirements/08-usage-dashboard.feature.md` | PASS | UserDetailTests.cs:157 |
| TOR-08-wX5QWUB | The dashboard shall return an administrator from User Detail (S-03) to Usage (S-02) through the "Usage" breadcrumb link and, in the empty state, the "Back to Usage" link | `docs/requirements/08-usage-dashboard.feature.md` | PASS | UserDetailTests.cs:81 |
| TOR-08-bydYtvm | The dashboard shall reload User Detail (S-03) when the "Retry" button is used after a failed load | `docs/requirements/08-usage-dashboard.feature.md` | PASS | UserDetailTests.cs:483 |
| TOR-09-dty904j | The dashboard shall render the empty state of User Detail (S-03) in visible text while keeping the quota form available | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:439 |
| TOR-09-CYbsdon | The dashboard shall render the error state of User Detail (S-03) in visible text with a "Retry" button | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:457 |
| TOR-09-7aP7tWP | The dashboard shall allow every interactive control to be reached and operated by keyboard alone with no keyboard trap | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:531 |
| TOR-09-YFn4LMC | The dashboard shall move focus into a modal dialog on open, keep Tab within it, and return focus to the invoking control on close | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:325 |
| TOR-09-pQcPvui | The dashboard shall associate a label with every form field | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:262 |
| TOR-09-RA5qSsd | The dashboard shall identify an invalid form field in text next to it that names the problem and the fix, and shall move focus to the first invalid field | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:220 |
| TOR-09-do58nOS | The dashboard shall require an explicit confirmation before an irreversible action, with the safe option as the default and Escape cancelling | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:299 |
| TOR-09-TpkiHjX | The dashboard shall show a visible progress indicator within 1 second of starting any operation that takes longer than 1 second | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:241 |
| TOR-09-cTG2frO | The dashboard shall present every screen without horizontal scrolling, overlap or clipped controls at a viewport width of 320 CSS px | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:644 |
| TOR-09-1DjTmDv | The dashboard shall present every screen without horizontal scrolling, overlap or clipped controls at 200% zoom | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:662 |
| TOR-09-P5YL2JO | The dashboard shall render control boundaries and focus indicators at a contrast ratio of at least 3:1 against adjacent colors | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:738 |
| TOR-09-GFQ80Xs | The dashboard shall replace non-essential animation with an instant transition when the reduce-motion preference is set | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:406 |
| TOR-09-9irho1R | The dashboard shall give every screen a unique document title | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:503 |
| TOR-09-hdlqc7J | The dashboard shall show on every screen exactly one visible H1 that matches the document title | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:503 |
| TOR-09-PVdyo7H | The dashboard shall mark the current item of the primary navigation with aria-current | `docs/requirements/09-ux-baseline.feature.md` | PASS | UserDetailTests.cs:503 |

## Requirement Changes

None

## Verification Summary

### Counts
- TOR Requirements: 25/25 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 9/9 PASS (Build, Lint/format, Tests, Live upstream, Run the tool, UX Baseline — after a fix at wrapup, Access control, Visual/console, Wireframe fidelity — deviations noted); N/A: Brand compliance (no brand skill configured)
- Tests: 293 passed, 1 skipped (the live test, run separately by `test-live`: 1 passed, 0 skipped), 0 failed

### Highlights
- ✅ TOR-08-QNNE1fW / TOR-08-6Vzjnrg — exact confirmation texts; the new quota is checked through the real my_usage path (UserDetailServiceTests.cs:103, UserDetailTests.cs:176, UserDetailService.cs:118)
- ✅ TOR-08-JeFRxDI — two real synthesize_narration calls with the marker text; no field of the report holds it (UserDetailServiceTests.cs:36, UsageStore.cs ListUserMonthAsync)
- ✅ TOR-09-YFn4LMC / TOR-09-do58nOS / TOR-09-7aP7tWP — dialog focus in, trapped and returned; Cancel first and focused; Escape cancels; full Tab cycle wraps (AdminUserDetail.razor:105–132)
- 🔧 Layout floor gate (not a TOR) — at 200 % zoom the requests table was clipped by `overflow: hidden` (Source column unreachable; handoff reported PASS). Fixed at wrapup: the table stacks into labelled lines below 720 px; the TOR-09-1DjTmDv test now also fails on any clipped table cell
- ✅ Access control — `[Authorize]` + `AdminOnly`, the service re-checks the admin role, every store call goes through `AccessRule`; signed out the page redirects (302); no new tables

### Conclusion
Every one of the 25 Given/When/Then is mirrored by a passing test and confirmed by reading the implementation. The only defect found was in the UX Baseline's Layout floor at 200 % zoom; it is fixed and now guarded by the TOR-09-1DjTmDv test, and the full suite is green.

### Manual verification performed: No

## Known Issues / Follow-ups

- ⚠️ GATE DISCREPANCY: Layout floor — handoff reported PASS, reproduced FAIL (S-03 requests table 645 px in a 590 px `overflow: hidden` box at 640 CSS px). Fixed at wrapup.
- The Usage (S-02) table also clips between about 480 and 630 CSS px (outside the declared floor points; fine at 320 px and 200 % zoom).
- Email links on Usage and the "Back to Usage" link are not visually distinguished as links (no underline or colour); the wireframe underlines them.
- The quota field shows a red border while a save is in progress (`.autopilot/look/05`).
- "Use default" deletes the override row and its log line names no administrator, so who cleared a quota is not recorded (the spec's Key Components asked for the admin's subject and time; no TOR covers it).
