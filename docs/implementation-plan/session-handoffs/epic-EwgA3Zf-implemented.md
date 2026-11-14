# Epic EwgA3Zf — User detail and quota editing: implemented

**Branch:** `feature/epic-EwgA3Zf-user-detail-and-quota-editing`
**Implemented:** 2026-11-14 by Todd Schavey (autopilot session)

## What Was Built

- **User Detail (S-03, `/admin/users/{email}?month=YYYY-MM`)** — opened from the email link on Usage
  (S-02), which passes the month shown. Breadcrumb "Usage › pat@example.com", one H1 (the email), a Month
  select, and the subtitle `2026-11 · $25.32 of $25.00 OVER · 400 clips · 25% cache hits`. A requests
  table with one row per clip, newest first: Time (UTC), Model, Voice, Characters, Cost (4 decimals;
  cached rows `$0.00`), Source (`synthesized` / `cached`). The narration text is never shown — the usage
  log does not hold it. Loading ("Loading usage for … " + Cancel after 10 s), empty ("No usage for … in …",
  quota form kept, "Back to Usage"), error ("Couldn't load usage for … " + Retry, quota form hidden) and
  populated states. Below 480 px the table becomes labelled stacked rows.
- **Quota form** — "Current quota: $25.00/month (the default)" / "(set by an administrator)"; a labelled
  "Monthly quota (USD)" field (initially the current quota, e.g. `25.00`), Save (a real submit button;
  Enter works) and "Use default…". Invalid amounts (negative, unreadable, empty, above $1,000,000) show
  the error under the field (`aria-invalid`, `aria-describedby`), move focus to it and store nothing.
  While saving: Save disabled and a "Saving…" status. Success: "Quota set to $60.00/month (was $25.00,
  the default). Applies to Pat's next call." Failure: "Couldn't save the quota. Check your connection and
  press Save again." with the typed amount kept.
- **Use default… confirmation** — a `BbAlertDialog` (role `alertdialog`): "Reset pat@example.com to the
  default quota?", Cancel first and focused, Escape cancels, focus returns to "Use default…"; confirming
  shows "Quota reset to the default ($25.00/month)."
- **Backend** — `QuotaStore.GetQuotaInfoAsync` (quota + is-default) and `ClearOverrideAsync` (admin
  only); `UsageStore.ListUserMonthAsync` (admin only; resolves the person by subject or by the email of
  exactly one users row; per-clip rows newest first); `UserDetailService` (report, save, reset, amount
  parsing, display name); `AdminUsageRow` gained `Subject`.
- **Shell** — every page title now uses ` · ` ("My usage · PeakMedia Gateway", …). On User Detail the
  Usage nav link (desktop and menu sheet) carries `aria-current="true"`; exact pages keep `"page"`.

## Deferrals

Count: 0

None

## Key Files

- `src/PeakMediaRemoteMcpServer/Components/Pages/AdminUserDetail.razor` (new)
- `src/PeakMediaRemoteMcpServer/Usage/UserDetailService.cs` (new)
- `src/PeakMediaRemoteMcpServer/Usage/QuotaStore.cs`, `Usage/UsageStore.cs`, `Usage/AdminUsageService.cs`
- `src/PeakMediaRemoteMcpServer/Components/Pages/AdminUsage.razor` (email links), `Components/Layout/MainLayout.razor` (nav current item),
  `Components/PageLoad.cs` (`Refresh`), page titles in `AdminOnly.razor`, `MyUsage.razor`, `AdminCache.razor`, `NotAuthorized.razor`, `SignedOut.razor`
- `src/PeakMediaRemoteMcpServer/wwwroot/app.css`, `wwwroot/js/menu-focus.js` (`peakFocus.restore`, `peakForm.value`)
- Tests: `tests/PeakMediaRemoteMcpServer.Tests/Dashboard/QuotaStoreEditingTests.cs`, `UserDetailServiceTests.cs`,
  `tests/PeakMediaRemoteMcpServer.E2E/UserDetailTests.cs` (new); `AdminUsageTests.cs`, `AdminCacheTests.cs` (title separator)

## Spec Deviations

- **Route identifier is the email, not the subject.** The plan's step 9 linked `/admin/users/{subject}`;
  the link uses the email as shown on S-02 (the store resolves it to the subject, and also accepts a
  subject for users with no users row). Reason: TOR-09-CYbsdon's error state must name
  "pat@example.com" while every storage read fails, and the wireframe's loading state names the email
  too — only the address can carry it then. Incoming request paths are not logged, so the email does not
  reach the log. If two users rows ever share one email, the page shows the error state rather than guess.
- **Reduced motion:** no new dialog-specific CSS — the existing global `prefers-reduced-motion` rule in
  `app.css` already brings every animation and transition (dialog and overlay included) to 0.01 ms;
  TOR-09-GFQ80Xs's test measures it on the dialog, its overlay and portal layers.
- No TOR Then clause was adjusted: every anchor reads "no deviation".

## Key Decisions

- Design skills used: `frontend-design:frontend-design`, `design:design-system`, `design:accessibility-review`
  (composed from the existing tokens and S-02 patterns; no new theme tokens were needed — the contrast test passes in light and dark).
- The `blazorblueprint` MCP server was **not connected** in this unattended session (pending approval;
  calls refused). Component APIs were read from the library's own XML docs in the local NuGet package
  (`BlazorBlueprint.Components` / `.Primitives` 4.2.0) instead of guessing. Findings: `BbAlertDialogCancel`
  without `AsChild` renders no button, so Cancel is a `BbButton` under `AsChild`; `BbAlertDialogContent`
  has no Escape option (unlike `BbDialogContent.CloseOnEscape`), so Escape is handled by an `@onkeydown`
  on the content; focus is returned to "Use default…" by `peakFocus.restore`.
- Unknown person (no users row and no usage ever) → the error state.
- Save reads the field's on-screen value at submit (`peakForm.value`): `BbInput`'s change notification can
  reach the server after the form's submit, which made a fast Save use the previous amount.
- After a stored change the page shows the new quota at once (from the change itself) and then re-reads
  the month quietly, so a failed re-read never leaves the old quota on screen.
- Amounts are rounded to the cent; an upper bound of $1,000,000 keeps the nano-dollar column in range.
- The E2E check of "my_usage shows $X" reads `UsageStatusReader` (what the my_usage tool formats); the
  unit tests check the same through a real signed-in `my_usage` MCP call.

## TOR Coverage

| TOR | Result | Evidence |
|---|---|---|
| TOR-08-sfJ3IWh | PASS | E2E `TOR_08_sfJ3IWh_*` (click S-02 link → H1, Month 2026-11); unit `TOR_08_sfJ3IWh_*` (admin only, email resolves) |
| TOR-08-JeFRxDI | PASS | unit (real `synthesize_narration` with the marker text twice → cached first, synthesized second, no field holds the marker); E2E headers / rows / page content |
| TOR-08-i1syL2k | PASS | unit + E2E subtitle parts "2026-11", "$25.32 of $25.00", "OVER", "400 clips", "25% cache hits" |
| TOR-08-iUG0WAC | PASS | unit `GetQuotaInfoAsync`; E2E input "25.00" and "the default" |
| TOR-08-QNNE1fW | PASS | E2E exact text + quota $60.00; unit via `my_usage` MCP call |
| TOR-08-bjHma3b | PASS | E2E fault `fail` → exact text, input keeps "60"; unit SaveFailed |
| TOR-08-6Vzjnrg | PASS | E2E confirm → exact text + $25.00; unit via `my_usage` |
| TOR-08-MCIZVXn | PASS | E2E 5 rows → pick 2026-10 → 2 rows; unit |
| TOR-08-wX5QWUB | PASS | E2E "Back to Usage" and breadcrumb both reach Usage |
| TOR-08-bydYtvm | PASS | E2E fault cleared, Retry → requests table |
| TOR-09-dty904j | PASS | E2E empty texts, quota input + Save, Back to Usage |
| TOR-09-CYbsdon | PASS | E2E error text naming pat@example.com + Retry |
| TOR-09-7aP7tWP | PASS | E2E full Tab cycle in order, wraps to the first nav link; Enter on Save submits |
| TOR-09-YFn4LMC | PASS | E2E keyboard open, focus inside, 4× Tab stays inside, Escape returns focus to trigger |
| TOR-09-pQcPvui | PASS | E2E accessible names; clicking the label focuses the input |
| TOR-09-RA5qSsd | PASS | E2E aria-invalid, describedby text, focus, quota unchanged; unit |
| TOR-09-do58nOS | PASS | E2E alertdialog text, first button Cancel focused, Escape → still $60.00 |
| TOR-09-TpkiHjX | PASS | E2E 3 s latency: "Saving…" status within 1 s, Save disabled until done |
| TOR-09-cTG2frO | PASS | E2E S-01 and S-03 at 320 px: scrollWidth ≤ 320, controls inside, no overlaps |
| TOR-09-1DjTmDv | PASS | E2E S-01 and S-03 at 640 CSS px × device scale 2 (1280 px window at 200 %) |
| TOR-09-P5YL2JO | PASS | E2E boundary and focus-ring contrast ≥ 3:1 for quota input, Month select, Save — light and dark |
| TOR-09-GFQ80Xs | PASS | E2E reduced motion: dialog fully visible ≤ 50 ms after activation; durations ≤ 0.01 s on dialog, overlay and portal layers |
| TOR-09-9irho1R | PASS | E2E titles "My usage · PeakMedia Gateway", "pat@example.com · PeakMedia Gateway" |
| TOR-09-hdlqc7J | PASS | E2E one visible h1 each, matching |
| TOR-09-PVdyo7H | PASS | E2E only "My usage" has `aria-current="page"` on S-01; only "Usage" has `aria-current` on S-03 |

## Verification Results

- `dotnet format PeakMediaMcpServers.sln --verify-no-changes` — clean
- `.venv/bin/python build.py build` — succeeded
- `.venv/bin/python build.py test` — 25 + 208 (1 skipped: the live test, run separately) + 60 E2E passed; repeated runs green
- `.venv/bin/python build.py test-live` — 1 passed, 0 skipped (one real OpenRouter call, about $0.001)
- `.venv/bin/python build.py smoke` — signed in through the test sign-in server and listed the tools
- `build.py start` → `curl http://localhost:5108/version` → `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}` → `stop`
- `dotnet run --project src/PeakMediaMcpServer -- --version` → `PeakMediaMcpServer v0.1.0`, exit 0
- UX Baseline (Screen states, Keyboard & focus, Forms, Destructive actions, Progress feedback, Layout floor,
  Contrast, Reduced motion, Navigation) — checked in Chromium (Playwright) against the real gateway signed in
  through the test sign-in server; the E2E suite above and the screenshots in `.autopilot/look/` (not committed).
- Mechanical checks: every anchor TOR ID found in `tests/`; the unfinished-work keyword grep hits only the test sign-in server's class name
  test infrastructure and earlier `.autopilot/test-changes.jsonl` reasons.
