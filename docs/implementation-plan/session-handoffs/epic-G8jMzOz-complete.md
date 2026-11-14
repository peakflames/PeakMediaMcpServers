# Epic G8jMzOz: Admin usage and cache overview — Complete

**Completed:** 2026-11-14
**Verified by:** Independent review via `/peak-workflow:wrapup-epic G8jMzOz` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

Administrators now get two dashboard screens nobody else is offered: Usage (`/admin`), which lists every user's month — spend against quota, an `OVER` badge where the quota is reached, characters, clips, cache-hit rate and a Total row — and Cache (`/admin/cache`), which shows the shared cache's size against its cap, clip count, this month's hit rate and evictions. Both screens render loading, empty, error and populated states, and below 480 CSS px the header's links, email, theme choice and Sign out collapse into a sheet behind a menu button.

## Key Files

| File | Purpose |
|------|---------|
| `src/PeakMediaRemoteMcpServer/Components/Pages/AdminUsage.razor` | Usage screen (S-02) |
| `src/PeakMediaRemoteMcpServer/Components/Pages/AdminCache.razor` | Cache screen (S-04) |
| `src/PeakMediaRemoteMcpServer/Components/AdminOnly.razor` | Admin gate: Not Authorized (S-05) with HTTP 403 for non-admins |
| `src/PeakMediaRemoteMcpServer/Components/Pages/NotAuthorized.razor` | `/admin/{*Path}` catch-all (403 for non-admins / 404 for admins) |
| `src/PeakMediaRemoteMcpServer/Components/PageLoad.cs` | Shared load state: Loading → Loaded / Error / Cancelled, Cancel after 10 s |
| `src/PeakMediaRemoteMcpServer/Components/Layout/MainLayout.razor` | Admin links and the < 480 px navigation sheet |
| `src/PeakMediaRemoteMcpServer/Usage/AdminUsageService.cs` | Usage report: sort, OVER, totals, hit rates |
| `src/PeakMediaRemoteMcpServer/Usage/UsageStore.cs` | `ListMonthAsync` — admin-gated, parameterized month query |
| `src/PeakMediaRemoteMcpServer/Cache/CacheStatsReader.cs` | Cache statistics (admin-gated) |
| `src/PeakMediaRemoteMcpServer/Cache/CacheEvictor.cs` | Records each evicted clip in `cache_evictions` |
| `src/PeakMediaRemoteMcpServer/Storage/SchemaMigrator.cs` | Migration 4: `cache_evictions` (owner column `created_by_subject`) |
| `tests/PeakMediaRemoteMcpServer.E2E/DashboardHost.cs` | E2E host; `WaitInteractiveAsync` fixed at wrapup to wait for the live circuit |
| `tests/manual/TOR-08-nLHs0pq.md` | Operator checklist for the real-Okta admin landing |

## Key Decisions

- Clips on Usage count every requested clip (synthesized + cache hits); cache-hit rate = hits / clips, whole percent; the Total's rate comes from the summed counts.
- "Evicted this month" is backed by the new `cache_evictions` table (migration 4); counting starts when the migration is applied.
- GB on Cache means GiB (2^30 bytes), matching `Storage:CacheMaxGb`.
- The admin gate is enforced twice on the server: `AdminOnly` on each page and `AccessRule.RequireAdmin` in each data service.
- The Cache empty state also shows the five zero cards (TOR-08-irZrgRw's Given needs a Clips card showing "0"); one Refresh button, in the page header.
- E2E tests that interact right after sign-in must wait for a finished data load (the prerender already shows `data-state="loading"`), not for the mere presence of `[data-state]`.

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-08-nLHs0pq | The dashboard shall land a member of the Okta admin group on My Usage (S-01) with the administrator navigation after a real Okta sign-in | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/manual/TOR-08-nLHs0pq.md |
| TOR-08-D1L4ilb | The dashboard shall show a member of the admin group the "My usage", "Usage" and "Cache" navigation links | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminNavTests.cs:14 |
| TOR-08-leBAXoJ | The dashboard shall collapse the navigation, the signed-in email and the "Sign out" button into a sheet behind a menu button below 480 CSS px | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminNavTests.cs:30 |
| TOR-08-kX399h8 | The dashboard shall list on Usage (S-02) one row per user for the chosen month, sorted by Spent with the highest first | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminUsageTests.cs:38 |
| TOR-08-nzR0l2p | The dashboard shall mark on Usage (S-02) every user whose spend has reached their quota with an "OVER" badge | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminUsageTests.cs:56 |
| TOR-08-nFpaAG7 | The dashboard shall show on Usage (S-02) a "Total" footer row summing the month | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminUsageTests.cs:73 |
| TOR-08-rnr0ApF | The dashboard shall show an empty state on Usage (S-02) for a month in which nobody requested narration | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminUsageTests.cs:89 |
| TOR-08-S8MYBHC | The dashboard shall show an error state with a working "Retry" button on Usage (S-02) when the usage data cannot be loaded | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminUsageTests.cs:113 |
| TOR-08-VHkMu93 | The dashboard shall show on Cache (S-04) the cache's size, size cap, clip count, hit rate this month and evictions this month | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminCacheTests.cs:16 |
| TOR-08-5ZEXDBu | The dashboard shall show an empty state on Cache (S-04) when the cache holds no clips | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminCacheTests.cs:49 |
| TOR-08-NeOMGCI | The dashboard shall show an error state with a working "Retry" button on Cache (S-04) when the cache statistics cannot be loaded | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminCacheTests.cs:67 |
| TOR-08-irZrgRw | The dashboard shall reload the figures on Cache (S-04) when the "Refresh" button is used | `docs/requirements/08-usage-dashboard.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.E2E/AdminCacheTests.cs:92 |

## Requirement Changes

None

## Verification Summary

### Counts
- TOR Requirements: 12/12 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 10/10 PASS
- Tests: 252 passed, 1 skipped (opt-in live test; `build.py test-live` ran it: 1 passed, 0 skipped), 0 failed

### Highlights
- ✅ TOR-08-nLHs0pq — the checklist `tests/manual/TOR-08-nLHs0pq.md` mirrors the Gherkin, and the admin links render from `AccessRule.IsAdmin` (`MainLayout.razor:9-15`). operator-observed: "Confirmed line by line by the owner. Yes: the browser should show My Usage (S-01) Yes: the app header's navigation should show the "My usage", "Usage" and "Cache" links Real Okta sign-in (Integrator org) as an admin-group member at http://localhost:5108/, running the branch build b98f88a. Owner: done, signed in and it works!" (recorded by Todd Schavey on 2026-11-14, before this session)
- ✅ TOR-08-kX399h8 / nzR0l2p / nFpaAG7 — Usage table, `OVER` where spend ≥ quota, Total row (`AdminUsageTests.cs:38,56,73`, `AdminUsageService.cs:47-58`); the query is parameterized and admin-gated (`UsageStore.ListMonthAsync`)
- ✅ TOR-08-VHkMu93 — five stat cards and subtitle (`AdminCacheTests.cs:16`, `CacheStatsReader.cs:48`); evictions recorded per clip by `CacheEvictor`
- ✅ TOR-08-leBAXoJ — < 480 px sheet: focus stays inside, Escape closes it and returns focus to the menu button (`AdminNavTests.cs:30`)
- 🔧 Tests gate fixed during wrapup — `DashboardHost.WaitInteractiveAsync` returned on the server prerender, before the circuit attached its handlers, so `ThemeTests.TOR_08_fX9XW5F` failed every run (on `develop` too) and `TOR_08_rnr0ApF` failed intermittently; it now waits for the page's first load to finish over the live circuit (test-only change)

### Conclusion
For every requirement, a test mirrors its Given/When/Then and passed in the verifier's own runs, and the source realizes the behavior. Both admin screens are gated twice on the server. The one requirement that rests on an observation is backed by the owner's recorded line-by-line confirmation of a real Okta sign-in. The one failing gate was a test-helper timing bug, fixed and re-verified with three clean full E2E runs.

### Manual verification performed: Yes
Recorded by autopilot from the owner's decision: Todd Schavey reviewed `tests/manual/TOR-08-nLHs0pq.md` and the 18 epic screenshots on 2026-11-14 (branch tip b98f88a) and answered "Looks right" — "Confirmed line by line by the owner. Yes: the browser should show My Usage (S-01) Yes: the app header's navigation should show the "My usage", "Usage" and "Cache" links Real Okta sign-in (Integrator org) as an admin-group member at http://localhost:5108/, running the branch build b98f88a. Owner: done, signed in and it works!"

## Known Issues / Follow-ups

- GATE DISCREPANCY (resolved): the implementer handoff reported E2E 37/37; the verifier reproduced 35/37 until the `WaitInteractiveAsync` fix.
- An unknown `/admin/...` address as an administrator shows the browser's own HTTP 404 page (screenshot 12), not the app's "Page not found" notice that `NotAuthorized.razor` renders — the 404 body appears to be replaced. Not a requirement of this epic.
- `UsageStore.ListMonthAsync` (like the existing `UsageStore` reads) does not catch `SqliteException`; a real query error on Usage would escape `PageLoad` instead of showing the error state. `CacheStatsReader` does catch it.
- At 200 % zoom (640 px) emails in the Usage table wrap mid-word; readable, no horizontal scroll.
- Wireframe deviations: emails are not yet links to S-03 (epic EwgA3Zf); the Total Quota cell is blank instead of "—".
