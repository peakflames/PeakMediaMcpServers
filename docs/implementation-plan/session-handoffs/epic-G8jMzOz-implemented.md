# Epic G8jMzOz — Admin usage and cache overview: implemented

**Branch:** `feature/epic-G8jMzOz-admin-usage-and-cache-overview`
**Implemented:** 2026-11-13 by Todd Schavey (autopilot session)

## What Was Built

- **Usage (S-02, `/admin`)** — every user's month on one table: Email, Spent, Quota, Characters, Clips,
  Cache-hit rate, Status; sorted by Spent, highest first; an `OVER` badge where spend has reached the
  quota (`spent >= quota`); a "Total" footer row; subtitle
  `2026-11 · 3 users · 1 over quota · sorted by Spent, highest first`; a Month select (the same twelve
  months as My Usage); loading / empty ("No usage in …" + "Go to current month") / error ("Couldn't load
  usage for … " + Retry) / populated states. Below 480 px the table becomes labelled stacked rows.
- **Cache (S-04, `/admin/cache`)** — five stat cards (Size, Size cap, Clips, Hit rate this month,
  Evicted this month), the subtitle `12.4 GB of 20 GB used (62%) · least-recently-used clips are removed
  first when full`, one Refresh button in the page header, and loading / empty ("The cache is empty") /
  error ("Couldn't load cache statistics. …" + Retry) / populated states.
- **Navigation sheet** — below 480 CSS px the header's links, signed-in email, theme choice and Sign out
  move into a `BbSheet` behind an "Open menu" button; Escape or the close button returns focus to the
  menu button; choosing a link closes the sheet. Administrator links stay administrator-only.
- **Admin gate** — `AdminOnly.razor` wraps both pages: non-administrators get Not Authorized (S-05) with
  HTTP 403 and the page content is never rendered. Unknown `/admin/…` addresses still answer 403 to
  non-administrators and now 404 "This administrator page does not exist." to administrators.
- **Backend** — `UsageStore.ListMonthAsync` (one grouped, parameterized query; admin only),
  `AdminUsageService` (sorting, OVER, totals, hit rates), `CacheStatsReader` (size, cap, clips, this
  month's hit rate across all users, evictions this month; admin only), and migration 4
  `cache_evictions` (with `created_by_subject` owner column), written by `CacheEvictor` once per evicted clip. All reads honor the test fault switch.
- **Shared loader** — `Components/PageLoad.cs`: Loading → Loaded / Error / Cancelled, Cancel offered after
  10 s, a newer load supersedes an older one. My Usage is unchanged.

## Deferrals

Count: 0

None

## Key Files

| File | Role |
|------|------|
| `src/PeakMediaRemoteMcpServer/Components/Pages/AdminUsage.razor` | Usage screen (S-02) |
| `src/PeakMediaRemoteMcpServer/Components/Pages/AdminCache.razor` | Cache screen (S-04) |
| `src/PeakMediaRemoteMcpServer/Components/AdminOnly.razor` | Admin gate: 403 + S-05 for non-admins |
| `src/PeakMediaRemoteMcpServer/Components/Pages/NotAuthorized.razor` | `/admin/{*Path}` catch-all (403 / 404) |
| `src/PeakMediaRemoteMcpServer/Components/SetStatus.razor` | Sets the first render's HTTP status |
| `src/PeakMediaRemoteMcpServer/Components/PageLoad.cs` | Shared load state / cancel / stale-result guard |
| `src/PeakMediaRemoteMcpServer/Components/Layout/MainLayout.razor` | Menu button + navigation sheet |
| `src/PeakMediaRemoteMcpServer/wwwroot/js/menu-focus.js` | Returns focus to the menu button on close |
| `src/PeakMediaRemoteMcpServer/wwwroot/app.css` | Sheet, table, OVER badge, < 480 px rules |
| `src/PeakMediaRemoteMcpServer/Usage/AdminUsageService.cs` | Admin usage report |
| `src/PeakMediaRemoteMcpServer/Usage/UsageStore.cs` | `ListMonthAsync` (admin-gated query) |
| `src/PeakMediaRemoteMcpServer/Cache/CacheStatsReader.cs` | Cache statistics (admin-gated) |
| `src/PeakMediaRemoteMcpServer/Cache/CacheEvictor.cs` | Records each eviction |
| `src/PeakMediaRemoteMcpServer/Storage/SchemaMigrator.cs` | Migration 4 `cache_evictions` |
| `tests/PeakMediaRemoteMcpServer.E2E/AdminNavTests.cs`, `AdminUsageTests.cs`, `AdminCacheTests.cs` | Browser tests, signed in as an admin through the stub issuer |
| `tests/PeakMediaRemoteMcpServer.Tests/Dashboard/AdminUsageServiceTests.cs`, `CacheStatsReaderTests.cs` | Service tests incl. access rule |
| `tests/manual/TOR-08-nLHs0pq.md` | Operator checklist for the real-Okta admin landing |

## Spec Deviations

| TOR ID | As-Written | As-Implemented | Reason |
|--------|-----------|----------------|--------|
| TOR-08-5ZEXDBu / TOR-08-irZrgRw | Wireframe S-04 empty state shows the message and a Refresh button, no cards | The empty message is shown **and** the five stat cards (all zero) below it | TOR-08-irZrgRw's Given has the Clips card showing "0" on an empty cache; the TOR wins over the wireframe (resolved at plan approval) |
| TOR-08-5ZEXDBu | Wireframe S-04 shows a second Refresh button inside the empty region | One Refresh button, in the page header (inside the main region) | Two identically named buttons would be ambiguous for keyboard and screen-reader users; the TOR's "main content region should contain a Refresh button" is met |
| TOR-08-kX399h8 | Wireframe S-02 shows each email as a link to User Detail (S-03) | Emails are plain text | User Detail (S-03) and its link arrive with Epic EwgA3Zf (TOR-08-sfJ3IWh); no dead link ships here |

All other anchors: no deviation.

## Key Decisions

- **Clips** on Usage counts every requested clip (synthesized + cache hits); Cache-hit rate = hits / clips,
  rounded to a whole percent. The Total's hit rate is computed from the summed counts.
- **Evicted this month** needs a data source the gateway did not have (evictions were only logged):
  migration 4 adds `cache_evictions`, owned like every table: its `created_by_subject` column is copied
  from the evicted clip's `audio_cache` row; read only through the admin-gated `CacheStatsReader`. Counting starts when the migration is applied, as the Cache footnote says.
- **GB** on Cache means GiB (2^30 bytes), matching `Storage:CacheMaxGb`; one decimal (`0.#`).
- **Admin gate** is one component (`AdminOnly`) calling `AccessRule`; the data services check the admin
  role again (`AccessRule.RequireAdmin`), so a page that forgot the wrapper still reads nothing.
- **The sheet holds the theme choice** as well as the links, email and Sign out, since the header hides
  it below 480 px too.
- **Focus return:** `BbSheetTrigger AsChild` + `BbButton` did not restore focus on close in this version,
  so `menu-focus.js` focuses the menu button when the person closes the sheet.
- **Blazor Blueprint lookups:** the `blazorblueprint` MCP server's tools were refused in this unattended
  run, so `BbSheet*`, `BbBadge`, `BbButton` and `LucideIcon` were looked up in the installed package's
  XML API docs (`BlazorBlueprint.Components` 4.2.0, `BlazorBlueprint.Icons.Lucide` 2.0.2). v4.2 has no
  `BbTable`; the Usage table is a semantic `<table>` styled in `app.css`.
- Design skills used: `frontend-design:frontend-design` (+ Blazor Blueprint API docs as above). Choices:
  right-aligned tabular figures, Spent in bold to echo My Usage's lead card, OVER badge in the existing
  `--over-quota` token, no new tokens.

## TOR Coverage

| TOR | Verdict | Test | Implementation |
|-----|---------|------|----------------|
| TOR-08-nLHs0pq | PASS (operator-observed pending) | `tests/manual/TOR-08-nLHs0pq.md`; stub-issuer counterpart `AdminNavTests.cs:14` | `MainLayout.razor:9` |
| TOR-08-D1L4ilb | PASS | `E2E/AdminNavTests.cs:14` | `MainLayout.razor:9` |
| TOR-08-leBAXoJ | PASS | `E2E/AdminNavTests.cs:30` | `MainLayout.razor:35`, `app.css:366` |
| TOR-08-kX399h8 | PASS | `E2E/AdminUsageTests.cs:38`, `Tests/Dashboard/AdminUsageServiceTests.cs:30` | `AdminUsage.razor:68`, `AdminUsageService.cs:47` |
| TOR-08-nzR0l2p | PASS | `E2E/AdminUsageTests.cs:56`, `AdminUsageServiceTests.cs:50` | `AdminUsageService.cs:49`, `AdminUsage.razor:97` |
| TOR-08-nFpaAG7 | PASS | `E2E/AdminUsageTests.cs:73`, `AdminUsageServiceTests.cs:73` | `AdminUsage.razor:107` |
| TOR-08-rnr0ApF | PASS | `E2E/AdminUsageTests.cs:89`, `AdminUsageServiceTests.cs:93` | `AdminUsage.razor:60` |
| TOR-08-S8MYBHC | PASS | `E2E/AdminUsageTests.cs:113`, `AdminUsageServiceTests.cs:107` | `AdminUsage.razor:51` |
| TOR-08-VHkMu93 | PASS | `E2E/AdminCacheTests.cs:16`, `CacheStatsReaderTests.cs:60`, `Narration/CacheEvictionTests.cs:105` | `CacheStatsReader.cs:48`, `CacheEvictor.cs:73`, `SchemaMigrator.cs:122` |
| TOR-08-5ZEXDBu | PASS | `E2E/AdminCacheTests.cs:49`, `CacheStatsReaderTests.cs:81` | `AdminCache.razor:57` |
| TOR-08-NeOMGCI | PASS | `E2E/AdminCacheTests.cs:67`, `CacheStatsReaderTests.cs:95` | `AdminCache.razor:43` |
| TOR-08-irZrgRw | PASS | `E2E/AdminCacheTests.cs:92` (the new clip is a row in the cache index of the real temp database) | `AdminCache.razor:15` |

## Verification Results

| Gate | Result |
|------|--------|
| `dotnet format PeakMediaMcpServers.sln --verify-no-changes` | PASS |
| `build.py build` | PASS |
| `build.py test` | PASS — stdio 25/25, gateway 190 passed + 1 skipped (the opt-in live test), E2E 37/37 |
| `build.py test-live` | PASS — 1 passed, 0 skipped (one real OpenRouter call, ~$0.001) |
| `build.py smoke` | PASS |
| `build.py start` + `curl /version` + `stop` | PASS — `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}` |
| stdio `--version` | PASS — `PeakMediaMcpServer v0.1.0`, exit 0 |
| `playwright-cli` against the running gateway (admin via stub issuer) | PASS — Usage and Cache in all four states, 400 px sheet, 320 px, 640 px (200 % zoom), light / dark; 403 for a non-admin; no console errors |
| UX: Screen states | PASS — loading / empty / error / populated on both screens (E2E + pictures) |
| UX: Keyboard & focus | PASS — sheet traps focus, Escape closes it and focus returns to the menu button (`AdminNavTests.cs:30`); focus ring on all controls (existing rule) |
| UX: Forms | N/A — no form fields beyond the Month select (labelled) |
| UX: Destructive actions | N/A — none on these screens |
| UX: Progress feedback | PASS — loading text within 1 s (`AdminUsageTests`/`AdminCacheTests` loading tests); Cancel after 10 s through `PageLoad` |
| UX: Layout floor | PASS — no horizontal scroll at 320 px and 640 px (E2E `Table_collapses_…` + playwright-cli) |
| UX: Contrast | PASS — `FocusAndContrastTests.TOR_09_BLJrVJ6_admin_usage_and_cache_text_contrast_in_light_and_dark` |
| UX: Reduced motion | PASS — existing global `prefers-reduced-motion` rule also covers the sheet's slide-in |
| UX: Navigation | PASS — unique titles "Usage — PeakMedia Gateway" / "Cache — PeakMedia Gateway", one H1 each, `aria-current` on the current link (header and sheet) |
| Release check (real Okta) | Not run — by hand at release (`tests/manual/TOR-08-nLHs0pq.md`) |
