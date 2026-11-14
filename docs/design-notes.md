# PeakMedia MCP Servers — Design Decision Notes

> **Status:** Decisions as built through the user-detail-and-quota-editing epic (EwgA3Zf, v0.1.0).
> Decisions from later epics are added by `/peak-workflow:refresh-docs` from their session handoffs.

---

These notes capture design decisions and rationale that complement the Architecture Document.

---

## 1. Follow the BitbucketMcpServers Structure

**Decision:** Same solution shape (stdio console app, ASP.NET HTTP app, shared tools library,
xUnit tests with fakes), `build.py` helper, Serilog setup, C# conventions, and tag-triggered
container publish as `~/github/peakflames/BitbucketMcpServers`.

**Rationale:** One house style across the owner's MCP servers; proven patterns (McpAuth gate, stub
authorization server, fake upstream) carry over directly.

## 2. Hybrid: Shared-Key Gateway + Bring-Your-Own-Key Stdio

**Decision:** The HTTP gateway holds the shared OpenRouter key and requires Okta sign-in. The stdio
server reads the user's own `PEAK_MEDIA_OPENROUTER_API_KEY` (legacy `TTS_OPENROUTER_API_KEY`) and
keeps no database.

**Rationale:** The gateway's purpose is that users no longer need an OpenRouter key; a stdio server
on a user's machine cannot hide a shared key, so it only serves users who bring their own. The
variable name matches what the peak-media skills already read.

## 3. Okta Sign-In Required — No Anonymous Mode

**Decision:** `McpAuth` with Okta as issuer, org-only tenant. Two roles: `admin` (OIDC `groups` claim
contains `Admin:Group`) and `user`. Roles gate only the usage dashboard; every MCP tool behaves the
same for every signed-in caller. The gateway refuses to start with `McpAuth:Enabled` false
(Bitbucket allows it; this project does not).

**Rationale:** An unauthenticated gateway spends the shared OpenRouter credit for anyone who can
reach it, and per-user usage and quotas need a real caller identity. Admin-only views (all users'
usage, quota edits, cache statistics) need a role; tools do not, because limits are per-user quotas.

## 4. SQLite From Day One (Gateway Only)

**Decision:** `Storage:DatabasePath` (default `data/peakmedia.db`) holds a per-user usage log,
per-user quotas, and an audio cache. Unwritable directory → temp-path fallback with a loud warning,
as in Bitbucket's Broker.

**Rationale:** Accountability for shared credit (who spent what), cost control (quotas), and
avoiding paying twice for identical narration (cache).

## 5. .NET 11

**Decision:** Target `net11.0`.

**Rationale:** Owner's choice — the newest release; .NET 9 (Bitbucket's target) left support on 2026-11-10.

## 6. Single Version Source

**Decision:** One `<Version>` in `Directory.Build.props`; container tag derived from it.

**Rationale:** Bitbucket bumps the version in several csproj properties by hand; one source removes drift.

## 7. Image Generation Deferred

**Decision:** Text-to-speech first. Image generation through the same gateway is a growth note,
reflected only in the `PeakMedia` naming.

**Rationale:** The project is named `PeakMedia` and the layout leaves room for it, so a later epic can
add image generation behind the same gateway without renaming anything. Building it now would add
scope the text-to-speech epics have not yet proven.

## 8. Content Root Is the Binary Directory

**Decision:** `Program.CreateBuilder` sets `ContentRootPath` to `AppContext.BaseDirectory`. `appsettings.json`
loads, and a relative `Storage:DatabasePath` resolves, the same way from any working directory.

**Rationale:** Running from a different directory (IDE, `build.py`, a container) must not silently
create a second database or miss configuration. In local dev the database is
`src/PeakMediaRemoteMcpServer/bin/Debug/net11.0/data/peakmedia.db`; in the container it is
`/app/data/peakmedia.db` via `Storage__DatabasePath`.

## 9. UTC-Dated Daily Log File

**Decision:** `UtcDailyFileSink` names each log file `<Name>_<yyyyMMdd>.log` using the UTC date. Record
timestamps stay local time with offset.

**Rationale:** Serilog.Sinks.File's `rollingInterval: Day` rolls on the local clock, so a host in a
different timezone would split or merge a day's records inconsistently across servers. The sink is
verified by a test run under `TZ=Pacific/Kiritimati`.

## 10. Startup Record Before Configuration Validation

**Decision:** The gateway writes `[INF] PeakMediaRemoteMcpServer v<ver> starting` before
`GatewayStartupValidator` checks configuration. A refused start logs the startup line, then the
`[FTL]` reason, and exits 1 before Kestrel binds.

**Rationale:** Every run's first log record is the startup line, including refused starts, so an
operator reading the log always sees which version ran and why it stopped.

## 11. Bearer Gate on `/mcp`

**Decision:** `/mcp` uses JwtBearer with issuer, audience, lifetime and signature validation against the
gateway's own signing key (Epic 7BASFbp; the walking skeleton shipped it with an empty key set). `GET` and `DELETE` `/mcp` sit behind the same gate, so an
unauthenticated caller always gets 401; a signed-in caller gets 405, which the stateless Streamable HTTP
transport requires (it has no GET stream).

**Rationale:** The gate is in place from the first epic, so no request can reach a tool without
passing it. Mapping GET and DELETE behind the gate prevents routing from being revealed before sign-in.

## 12. Tool Registration and `tools/list` Before Any Tool

**Decision:** The gateway registers tools with `WithToolsFromAssembly(typeof(ToolErrorFormatter).Assembly)`,
one partial-class file per tool, and both servers register the shared `TtsTools` explicitly. `options.ToolCollection ??= []`
makes `tools/list` answer, empty, before the first tool exists.

**Rationale:** A new gateway tool file is picked up without wiring changes, and clients never see a protocol error
from listing tools on a fresh build. The stdio host does not scan the assembly, so only the tools it names are offered
(see §23 and §42).

## 13. Default Logging Providers Cleared

**Decision:** Both hosts call `builder.Logging.ClearProviders()` before adding Serilog. Serilog is the only
log provider.

**Rationale:** The default console logger writes to stdout. On the stdio server stdout belongs to the MCP
protocol; on the gateway, the rule is that nothing is written to stdout. Every record reaches stderr and the
daily file through one configuration.

## 14. Process-Level Tests Run Serially

**Decision:** Process-level tests are serialized (`tests/xunit.runner.json`). They run the real built binaries
(`ServerProcess`, scrubbed environment, separate stdout and stderr capture).

**Rationale:** Each server writes its daily log under `logs/` next to its binary, and parallel tests would
interleave those files. In-process tests (`GatewayTestHost`) stay separate because they use a fresh temp
database per test.

## 15. `build.py mcp` Signs In Or Says Why Not

**Decision:** `build.py mcp <command>` sends a gateway access token from `PEAK_MEDIA_ACCESS_TOKEN` as a
Bearer token (never printed), or with `--oauth` signs in through the browser with fastmcp's OAuth client.
Without either it probes `/mcp` and prints "requires sign-in (HTTP 401)" with both options.

**Rationale:** A plain message names the cause and the next action instead of a protocol error.

## 16. Container Runs as Non-Root

**Decision:** The published image runs as the non-root `app` user.

**Rationale:** The handoff does not record a deliberate choice; this is the SDK container default, and it
limits what a compromised process can write. The trade-off is in Known Issues (log directory writability).

## 17. The Gateway Is Its Own Authorization Server (Broker)

**Decision:** Ported from BitbucketMcpServers: the gateway runs DCR, `/authorize`, `/token` and issues its
own RS256 access tokens, delegating the actual sign-in to any OIDC provider (Okta's org authorization
server in practice). Differences from Bitbucket: upstream is OIDC (discovery from
`<UpstreamAuthority>/.well-known/openid-configuration`, read lazily so startup never contacts Okta);
identity comes from the validated ID token (`sub`, `email`, `groups`, nonce) with no user-info call; no
upstream tokens are stored; DCR is always on; no anonymous mode and no static clients; one SQLite
database (schema v1 in `SchemaMigrator`).

**Rationale:** The launch customer's Okta capabilities are unknown (custom authorization servers and DCR
are paid Okta features); a plain OIDC web app works on every Okta tier. Claude Code needs DCR to connect
with no client ID to paste.

## 18. Open DCR Restricted to Loopback Redirect URIs

**Decision:** `/register` accepts only loopback redirect URIs (`127.0.0.1`, `localhost`, `[::1]`, any port)
plus an explicit `Broker:AllowedRedirectUris` allowlist (empty by default). `/authorize` matches a
registered URI exactly, ignoring only the port of a loopback URI (RFC 8252 §7.3).

**Rationale:** With open registration, an attacker could register their own redirect URI and mail a victim
an `/authorize` link; after the victim signs in, the code would go to the attacker. Loopback redirects
can only reach the victim's own machine. A client secret is issued even to `none` clients because Claude
Code rejects a registration response without one.

## 19. Access Token Claims and Lifetime

**Decision:** Gateway access token: RS256 JWT, key generated once and persisted in `app_meta` (survives
restart), claims `iss` = `Broker:IssuerUri`, `aud` = `McpAuth:Resource`, `sub` (Okta subject), `email`,
`role` (`admin` | `user`), `jti`; 60 minutes. Role is decided once at sign-in: `admin` when the ID token's
`groups` claim contains `Admin:Group`; refresh re-reads it from `users`.

**Rationale:** Every later epic keys caller records on `sub` and decides access from the token alone
(`Caller` + `AccessRule`), with no per-request database lookup.

## 20. Refresh Without Re-Contacting Okta

**Decision:** Refresh tokens are the gateway's own opaque secrets, stored hashed, rotated on every use,
30-day lifetime (`Broker:IssuedRefreshTokenLifetimeDays`). Refresh does not call Okta.

**Rationale:** The gateway never holds Okta tokens, so it has nothing to refresh upstream. The trade-off:
removing a person from the Okta app refuses new sign-ins immediately, but an existing session continues
until its refresh token expires (at most 30 days, shortened by lowering the setting).

## 21. One Access Rule

**Decision:** `AccessRule.CanAccess(caller, ownerSubject)` (owner or admin) and `AccessRule.RequireAdmin`
in `Auth/` are the only place access is decided; `Caller.From(ClaimsPrincipal)` builds the caller only
from the validated token. The `admin` authorization policy uses the same rule.

**Rationale:** Security Baseline — owner-or-permitted-role, decided in one place every route calls.

---

## 22. Configurable Groups Claim Name

**Decision:** The OIDC claim that carries group membership is configurable (`Broker:UpstreamGroupsClaim`,
default `groups`). `admin` is granted when that claim contains `Admin:Group`.

**Rationale:** Okta's federated-claims screen reserves the `groups` claim name, so an organization may need
to map its group claim under another name. The default keeps the documented behavior.

---

## 23. Narration Tools Are Gateway-Only

**Decision:** `TtsTools` carries no `[McpServerToolType]`. The gateway registers it explicitly with
`WithTools<TtsTools>()`; the stdio server's assembly scan does not offer the narration tools.

**Rationale:** Both tools need the shared audio cache and the shared key, which exist only on the gateway.
Keeping registration explicit stops the stdio server from advertising tools it cannot serve.

*Superseded in part by §42 (Epic YVOjXtI): the stdio server now serves `synthesize_narration` and
`list_voices` through the same `TtsTools` class; only `my_usage` remains gateway-only.*

---

## 24. Shared Cache Read Across Callers, Creator Recorded

**Decision:** The audio cache is read by content key (`ClipCacheKey`: SHA-256 of model, voice, style and
text) across all callers. `audio_cache.created_by_subject` records who created a clip; `clip_downloads.subject`
records who owns each download token.

**Rationale:** Identical clips cost money once, so a cache shared by key saves spend for everyone. This is a
deliberate exception to per-owner reads. The rule still holds for the caller-facing data: the download token
and its owner are per caller, and a clip's bytes are unreadable without a token derived from it.

---

## 25. Anonymous Download Links Carry Their Own Credential

**Decision:** `GET /clips/{token}` sits outside the bearer gate (`AllowAnonymous`). The token is 32 random bytes
in base64url (43 characters), valid for 15 minutes, reusable within that window, and stored only as SHA-256.
Expired tokens return 410 Gone; unknown tokens return 404.

**Rationale:** The links are handed to a player or a browser that holds no bearer token. An unguessable
token that cannot be derived from the clip is the access control for those links (TOR-04-DYooFQF,
TOR-04-SqAxPKp). Hashing means a read of the database does not yield working links.

---

## 26. Injected Clock for Expiry

**Decision:** All narration timestamps come from the injected `TimeProvider` as Unix milliseconds.

**Rationale:** Tests drive the 15-minute expiry with `FakeTimeProvider` instead of waiting, so the valid-at-14-minutes
and gone-at-16-minutes checks run in milliseconds.

---

## 27. Cost From the Generation Lookup

**Decision:** A clip's real cost comes from `GET /generation?id=` using the `X-Generation-Id` response header.
When the cost cannot be read, the clip is charged at the model's configured price with a warning logged
(§36; this replaces the earlier $0 fallback).

**Rationale:** The speech response does not carry its price; the generation record does. Charging the
configured price keeps a synthesis from failing over an unreadable cost and stops a caller's spend from
being understated. The live header and lookup are confirmed by the live gate (`build.py test-live`, see §66).

---

## 28. Named Failure Messages for Upstream Errors

**Decision:** A failed clip in `synthesize_narration` returns an `ERROR:` that names the clip and its position
(for example "clip 5 of 12") and says the earlier clips are saved. An out-of-credit (402) or rejected-key (401)
answer gets its own message, and the key never appears in it. Calls stop at the failing clip. The wording of
the account-specific advice is chosen per host (§44); the status in an unretried answer reads `HTTP <code>` on
both servers (§45).

**Rationale:** A caller who sees "clip 5 of 12 failed, clips 1–4 are saved" can retry without paying twice, and an
out-of-credit account needs an administrator, not a retry. The key is never echoed, so a failure cannot leak it.

---

## 29. One Retry for Transient OpenRouter Failures

**Decision:** A clip is retried once, after `OpenRouter:RetryDelayMs` (default 1000 ms), when OpenRouter times out,
the connection fails, or it answers 5xx. Each request has a `OpenRouter:ClipTimeout` (default 2 min), which also
bounds cost lookups because they share the `HttpClient`. 401, 402 and other answers are never retried.

**Rationale:** A single retry absorbs a brief upstream blip without hiding a sustained outage, and retrying 402 or
401 would only repeat a failure that a retry cannot fix. The bounded timeout keeps one stuck clip from holding a
call open indefinitely.

---

## 30. Finished Clips Cached Immediately

**Decision:** Each clip is written to the shared audio cache as soon as it is synthesized, not at the end of the call.

**Rationale:** When a later clip fails, a retry of the same call finds the earlier clips cached and pays only for the
clips still missing (TOR-04-PXIafcT).

---

## 31. LRU Eviction Bounded by Storage:CacheMaxGb

**Decision:** A background pass removes the least-recently-used clips once the cache exceeds `Storage:CacheMaxGb`.
Order is `last_used_utc`, then `created_utc`, then `rowid`. `ClipStore.FindAsync` refreshes `last_used_utc` on every
hit. An evicted clip's download links go with it through `ON DELETE CASCADE`. The gateway refuses to start when
`Storage:CacheMaxGb` is not greater than 0.

**Rationale:** The cache is shared, so it must have a ceiling that does not depend on any one caller. Least-recently-used
is the cheapest order that keeps the clips people are actually using; the explicit tie-break makes the order
deterministic for tests.

---

## 32. Expired Download Links Kept 24 Hours

**Decision:** The eviction pass deletes download links that expired more than 24 hours ago
(`CacheEvictor.ExpiredLinkRetention`). Links expired less than 24 hours ago are kept.

**Rationale:** Keeping an expired link briefly means a caller with a stale URL gets 410 Gone with the "run
`synthesize_narration` again" message rather than a bare 404.

---

## 33. Evictor as a Singleton on a Real Timer

**Decision:** `CacheEvictor` is a DI singleton. `CacheEvictionService` runs it every `Storage:EvictionIntervalMinutes`
(default 10) on a real `PeriodicTimer`, starting one interval after startup. Tests call `RunOnceAsync` directly.

**Rationale:** Tests run the same pass the background service runs, without waiting for the timer. The timer is not
the injected `TimeProvider`, so tests that advance the fake clock never trigger a background pass.

---

## 34. Quota Gate Runs Before the Cache Lookup

**Decision:** `NarrationService` checks the caller's month-to-date spend before any cache lookup. A caller whose
spend is at or above their quota is refused, with the spend, quota and reset time named, and no OpenRouter request
is made. Spend equal to the quota counts as over, both for the refusal and for the post-call note. The quota is checked
only at call start; a call that starts under quota runs to completion and notes in its result when the caller is then over.

**Rationale:** A quota that only limited uncached calls would let a caller with a full cache keep spending through
repeat calls, so the gate applies to every call. Checking at the start (not per clip) keeps a call whole once begun;
the trade-off is bounded overspend under concurrency (§66).

---

## 35. Money as Integer Nano-Dollars

**Decision:** Every amount (`usage_log.cost_nanousd`, `quota_overrides.monthly_nanousd`) is stored as an integer
nano-dollar count (`NanoUsd`). Display rounds to the cent (`UsageText.Dollars`).

**Rationale:** Monthly sums of fractional per-clip costs stay exact in SQLite with no floating-point drift, so the
quota comparison in §34 is decided on exact values. Rounding happens only at the display edge.

---

## 36. Configured Price Fallback When the Reported Cost Is Unreadable

**Decision:** When `GET /generation` cannot provide a clip's cost, the clip is charged at the model's
`PriceUsdPerThousandCharacters` (configured in `Tts:Models`) and a `[WRN]` record is logged. A clip with a readable
cost is charged that cost, and cache hits are charged nothing.

**Rationale:** Charging $0 on a read failure let the spend under-count against the quota. The configured price is
an estimate, so the fallback is visible in the log and is listed in §66 for calibration.

---

## 37. Usage Access Through the Caller-Bound Ledger

**Decision:** Tools use `IUsageLedger`, which `UsageLedger` binds to the caller from the validated access token.
`UsageStore` and `QuotaStore` take an explicit acting `Caller` and decide access through `AccessRule`, so the
usage dashboard can read any user's usage and set overrides as an administrator through the same rule.

**Rationale:** A tool can only ever see its own caller's records, and the dashboard reuses the same store code
with an explicit, admin-checked caller instead of a second access path (Security Baseline: one access rule).

---

## 38. A Failed Usage Write Does Not Fail the Call

**Decision:** When a `usage_log` row cannot be written, the failure is logged at Error and the call still returns its
audio. The clip is not charged.

**Rationale:** The audio has already been synthesized and paid for upstream; failing the caller's call over a
bookkeeping write would hide the audio without refunding the cost. The trade-off is an uncharged clip, recorded in
§66 as a known gap.

---

## 39. Month Boundaries in UTC

**Decision:** The quota month is the calendar month in UTC; spend is counted from 00:00 UTC on the first day
(`UsageMonth`), and `my_usage` reports the reset time in UTC.

**Rationale:** One boundary for every caller and server, independent of the host's timezone (the same reasoning as
§9 for log file names).

---

## 40. Narration Text Is Never Stored

**Decision:** `usage_log` holds the model, voice, character count, cost, cache key and cache-hit flag. It holds no
narration text, and nothing that logs usage writes it.

**Rationale:** Narration can be sensitive and is not needed for accounting; the cache key identifies the clip. Verified
by a test that the marker text is absent from the database, its WAL and SHM files, stderr and the log directory.

---

## 41. Local Clip Cache for the Stdio Server

**Decision:** The stdio server caches each clip as one WAV file named by its cache key, in `PEAK_MEDIA_TTS_CACHE` when
set, else `~/.cache/peak-media/tts`. A file is a hit only when it parses as a complete 16-bit mono 24 kHz WAV; a damaged
file is synthesized again and overwritten. Files are written to a temporary name and moved into place. Each clip entry
returns the file's `path` where the gateway returns a `url`. There is no database and no eviction.

**Rationale:** The directory is the one the peak-media skills download gateway clips into, so a clip already fetched from
the gateway is a hit for the solo user, and the reverse. A file is its own index, so a solo install needs no database. Validating
the WAV before serving it keeps a truncated file from being returned as audio.

---

## 42. One Shared Tool Class for Both Servers

**Decision:** `TtsTools` (`list_voices`, `synthesize_narration`) is one class in `PeakMediaMcpTools`, registered by both
servers. `TtsUsageTools` (`my_usage`) is a separate class registered only by the gateway, because the stdio server has no usage log.

**Rationale:** Both servers' input schemas and result fields match by construction, so the client contract (`docs/client-integration.md`)
cannot drift between them. The stdio server does not offer `my_usage` because it has nothing to report: the caller is never over quota
and records nothing.

---

## 43. Offline `fake/tone` Model

**Decision:** The stdio server's `list_voices` lists the model `fake/tone`, and `ToneSpeechClient` synthesizes it offline,
with no network call and no cost. `SoloSpeechClient` routes each clip by model: `fake/tone` offline, every other model to OpenRouter.
The gateway refuses `fake/tone`.

**Rationale:** It lets a client exercise the full narration path, including cache and WAV output, without an OpenRouter key or spend. The gateway does
not offer it because the shared key must not fund offline test audio.

---

## 44. Account Wording Chosen Per Host

**Decision:** `NarrationAccount` words the key, credit and storage failures for the host. The gateway tells the caller to ask an
administrator; the stdio server tells the solo user to check their own OpenRouter key and their own cache directory.

**Rationale:** Advice that is right on one host is wrong on the other: a solo user is their own administrator. The same tool code produces
both messages, so the failure is handled the same way and only its advice differs.

---

## 45. Uniform `HTTP <code>` Failure Wording

**Decision:** A clip whose 5xx answer persists after its one retry fails with `(HTTP <code>, retried once)` on both servers. Before Epic YVOjXtI the gateway read
`status <code>`.

**Rationale:** One format for every host means a client parses one string. No requirement pinned the old wording, so the change
was low-risk. Any client that parsed `status <code>` would need updating (see §66).

---

## 46. Client Contract Tested Against the Running Servers

**Decision:** The contract document's tool names, argument names and result fields are checked against the running gateway's
`tools/list` output, and its cache-key rule is checked against `ClipCacheKey.For`. These contract and document tests live in the
gateway test project. The stdio cache directory is logged at Debug only, because it contains the user's home path.

**Rationale:** The contract document is only useful while it matches the servers, so its tests read the servers rather than
a copy of their schemas. The gateway test project hosts them because the `my_usage` schema exists only there.

---

## 47. Theme Choice Is a Labelled Select, Not the Two-State Toggle

**Decision:** The theme control is a labelled `BbNativeSelect` offering light, dark and system. It writes the
library's own `bb-theme` storage entry, so `theme-init.js` applies the choice before first paint. "System"
removes the entry and follows the OS preference.

**Rationale:** The library's `BbDarkModeToggle` is two-state and cannot offer "System". Writing the library's
own key keeps one source of truth for the theme script.

---

## 48. Dashboard Session Carries Identity Only; One Access Rule

**Decision:** The dashboard's cookie principal carries only `sub`, `email` and `role`. Dashboard access is decided
by `Caller.From` and `AccessRule`, the same as for the MCP tools. Pages require the named `dashboard` policy (cookie
scheme). The default authenticate scheme `pm_selector` sends `/mcp` and bearer requests to JwtBearer and everything
else to the cookie, so the cookie never authenticates `/mcp`.

**Rationale:** Security Baseline — one access rule. Blazor components cannot name authentication schemes directly,
so the policy carries the scheme name. The selector keeps a browser session from ever asserting a caller on `/mcp`.

---

## 49. OIDC Response Mode Is Query; Dashboard Cookies Use the Wall Clock

**Decision:** The dashboard's OpenID Connect client uses `response_mode=query` (code + PKCE, callback `/signin-oidc`)
instead of the handler's default `form_post`. Cookie and OIDC lifetimes use `TimeProvider.System`, whatever clock
usage reporting uses.

**Rationale:** A cross-site POST back from the provider would drop the SameSite=Lax correlation and nonce cookies;
a redirect carrying the query keeps them. Session lifetimes are real time, while the usage clock is injected so
tests can fix it.

---

## 50. My Usage Loads Only in the Interactive Render

**Decision:** My Usage renders its loading state on the server render and loads its figures only in the interactive
render.

**Rationale:** The loading text appears at once, so a slow storage read still shows visible progress within one
second (Progress feedback baseline), even when storage is slow.

---

## 51. Unknown Admin Addresses Answer 403 to Non-Administrators and 404 to Administrators

**Decision:** `/admin` (Usage) and `/admin/cache` (Cache) are the admin pages (Epic G8jMzOz). Every other `/admin`
address renders Not Authorized (S-05): non-administrators get HTTP 403 with a link back to My usage, and administrators
get HTTP 404 with a "Page not found" notice. The decision is made server-side by `AccessRule.IsAdmin`, never by the
front end.

**Rationale:** A non-administrator gets an explicit answer and a way out rather than a bare 404. An administrator who
mistypes an admin address gets a real not-found answer rather than a page that pretends the screen exists.

---

## 52. Focus Check Scoped to Controls Outside the Header; End-to-End Tests Host the Gateway In-Process

**Decision:** The focus-not-obscured requirement (TOR-09-qu5yesw, which replaces the earlier wording through
CR-sziqlY7) applies to controls outside the app header. Header controls (Sign out, primary navigation, theme) need
only lie within the viewport. End-to-end tests (`tests/PeakMediaRemoteMcpServer.E2E`) host the real gateway
in-process (`Program.BuildApp`, real Kestrel, real SQLite) instead of launching the built binary.

**Rationale:** A header's own controls cannot lie outside the header's box, so the original wording could not pass
for them. In-process hosting gives a fixed usage clock and lets a test clear the storage fault switch while a page
is open (TOR-08-rxZtbxT); a child process would need a request-reachable test hook to do that.

---

## 53. Admin Pages Are Gated Twice on the Server

**Decision:** Each admin page (`/admin`, `/admin/cache`) is wrapped in `AdminOnly`, which decides with `AccessRule.IsAdmin`,
and each admin data read calls `AccessRule.RequireAdmin` itself (`UsageStore.ListMonthAsync`, `CacheStatsReader`).

**Rationale:** A page guard alone would leave the data reads reachable by any path that calls the service. Checking in
both places keeps the rule in `AccessRule` (Security Baseline) while never letting a front-end check decide access.

---

## 54. Usage Counts Every Requested Clip; Hit Rate Is Whole Percent of Requests

**Decision:** On the admin Usage screen, "clips" counts every requested clip, synthesized and cache hits alike. The cache-hit
rate is hits divided by clips, rounded to a whole percent. The Total row's rate comes from the summed counts, not an average
of the per-user rates.

**Rationale:** A cache hit is still a clip the person asked for, so counting only synthesized clips would hide the cache's
benefit. Deriving the Total's rate from summed counts keeps it consistent with the rows above it; averaging rates would
weight a one-clip user the same as a heavy one.

---

## 55. Evictions Recorded in `cache_evictions` (Migration 4)

**Decision:** The "evicted this month" figure on the Cache screen is backed by the `cache_evictions` table (migration 4).
`CacheEvictor` writes one row per evicted clip, owned by the subject that created the clip. Counting starts when the
migration is applied; past evictions are not backfilled.

**Rationale:** The LRU pass leaves no history behind, so a count needs its own record. Owning each row by the clip's creator
follows the owner-column rule for every table that holds a caller's records. Not backfilling is honest: the past cannot
be reconstructed from the cache index.

---

## 56. Cache Sizes Are GiB

**Decision:** "GB" on the Cache screen means GiB (2^30 bytes), matching `Storage:CacheMaxGb`.

**Rationale:** The cap is set in the same units the screen shows, so a cap of 10 reads as 10 GB of the cache's size
without a unit conversion the operator has to remember.

---

## 57. The Cache Empty State Keeps Its Stat Cards

**Decision:** When the cache holds no clips, the Cache screen shows its empty text and all five stat cards with zero values,
and one Refresh button sits in the page header.

**Rationale:** The empty state stays distinguishable by visible text (UX Baseline), while the cards still answer "how big is
the cache, how many clips" with explicit zeros. One header button keeps Refresh in the same place in every state.

---

## 58. End-to-End Tests Wait for a Finished Load, Not for the Page to Appear

**Decision:** Browser tests that act right after sign-in wait for a page's first data load to finish over the live circuit.
Waiting for the mere presence of `[data-state]` is not enough, because the server prerender already shows `data-state="loading"`.

**Rationale:** The prerendered page exists before its event handlers are attached. A test that clicks in that gap fails
intermittently or on every run, which is what happened before the wrapup fix to `DashboardHost.WaitInteractiveAsync`.

---

## 59. User Detail Is Addressed by Email and Resolved to the Subject

**Decision:** The User Detail route is `/admin/users/{email}?month=`. The store resolves the email to the person's
Okta subject; if two `users` rows share an email, the lookup fails rather than guessing.

**Rationale:** The Usage screen links each row by its email, which is what an administrator sees and can read in a
link. Every stored record is keyed by subject (Security Baseline), so the subject stays the owner and the email is
only the address. Failing on a duplicate avoids showing one person's month as another's.

---

## 60. Dialog Escape Handled on the Content; Focus Returned Explicitly

**Decision:** The "Use default…" confirmation (`BbAlertDialog`) handles Escape with an `@onkeydown` on its content,
because the component has no Escape option. On close, focus returns to the "Use default…" control through
`peakFocus.restore` (`wwwroot/js/menu-focus.js`). The safe option, Cancel, is the default and is focused on open.

**Rationale:** The UX Baseline requires Escape to cancel a destructive action and focus to return to the invoking
control. The library does not provide both, so the page supplies them. The Cancel-first order keeps Enter on the
safe path.

---

## 61. Save Reads the Field's On-Screen Value at Submit

**Decision:** Saving a quota reads the amount field's on-screen value at submit (`peakForm.value`, `menu-focus.js`)
rather than the value bound through `BbInput`'s change notification.

**Rationale:** The change notification for a field can arrive after the submit event, so a bound value can be one
keystroke behind what the administrator typed. Reading the field at submit makes the saved amount match the screen.

---

## 62. Quota Amounts Rounded to the Cent and Capped at $1,000,000

**Decision:** A quota is accepted when it is $0.00 or more and $1,000,000.00 or less, and it is stored rounded to the
cent. Other input gets an inline message naming the fix (for example "Enter an amount of $0.00 or more, for example 60").

**Rationale:** Money is stored as integer nano-dollars (§35), and the screen shows cents, so a quota finer than a cent
would look wrong. The cap stops a typo from creating an effectively unlimited quota. Amounts are checked in
`UserDetailService.ParseAmount` before any store call, so the inline messages and the stored value agree.

---

## 63. Requests Table Stacks Into Labelled Lines Below 720 px

**Decision:** Below 720 CSS px the User Detail requests table stops being a grid and each cell becomes a labelled line
(`data-th`), so no column is clipped.

**Rationale:** At 200% zoom (640 CSS px) the table's `overflow: hidden` box clipped the Source column, which was
unreachable. The first handoff reported the layout floor as passing; the wrapup check reproduced the clipping and
fixed it. The browser test for the layout floor now fails on any clipped table cell, so the rule is guarded.

---

## 64. Usage Emails Link to User Detail With the Month Shown

**Decision:** Each email on the Usage screen links to `/admin/users/{email}?month=…`, carrying the month the table
shows. On User Detail the "Usage" navigation item is marked `aria-current="true"`, because the page sits under Usage.

**Rationale:** An administrator who drills into a person returns to the same month, and the navigation still shows
where they are. Marking Usage rather than no item keeps the current-item rule (WCAG 2.4.6 navigation baseline) true
on the detail page.

---

## 65. Page Titles Use a Middle Dot Separator

**Decision:** Every dashboard page title follows the form `<Screen> · PeakMedia Gateway`, for example
"My usage · PeakMedia Gateway".

**Rationale:** One separator across the dashboard keeps titles consistent and unique per screen (UX Baseline,
document title). The earlier em-dash form on the Usage page was changed to match.

---

## 66. Known Issues and Deferred Work

- **Quota is checked only at call start.** Concurrent calls can each start under quota, so overspend is bounded by
  concurrency × 40 clips (§34).
- **Fallback prices are uncalibrated.** The `PriceUsdPerThousandCharacters` values in `appsettings.json` are
  estimates (§36); calibrate against a real generation's `total_cost` before relying on the fallback.
- **Display rounding at the quota edge.** A spend just under quota (for example $24.996) shows "$25.00 spent,
  $0.00 remaining" while calls are still allowed. Cosmetic.
- **Failed usage writes leave the clip uncharged** (§38), logged at Error.
- **Eviction can race an in-flight call.** A pass could remove a clip that an in-flight call has just stored or hit,
  before its download link is issued. LRU order makes this reachable only when the cache limit is smaller than one
  call's clips.
- **Worst-case failure time is about 4 minutes per clip.** Two 2-minute timeouts plus the 1 s delay. Some MCP clients
  may time out before that.
- **No Serilog redactor exists.** The Security Baseline suggests one. Instead, `SecretsInLogTests` guards that no code
  path logs the key or the client secret.
- **`.autopilot/` is tracked.** The autopilot test-change log (`.autopilot/test-changes.jsonl`) is committed on
  feature branches, and `.autopilot/` is not in `.gitignore`.
- **`ClipCacheKey.IsWellFormed` is unused.** Dead code; remove or use it when the cache key is parsed from a URL.
- **Tool failures are normal text results, not `isError: true`.** This satisfies TOR-01-Ji4u6Ta as written.
  Revisit if MCP clients need the error flag.
- **Live OpenRouter is not run in CI.** Tests use `FakeOpenRouter`. The live gate (`build.py test-live`) is a
  by-hand run with a real key, and it covers the speech call and the `X-Generation-Id` cost lookup.
- **`/token` verifies a client secret only when one is presented.** A client registered as
  `client_secret_post` can redeem a code without presenting its secret, and the refresh grant accepts a
  request with no `client_id`. PKCE and refresh-token rotation still protect both paths; consider tightening.
- **A Debug-level log line records the Okta subject.** The subject is an opaque ID, not an email, and the
  line is not emitted at the default Information level.
- **Migration failure does not stop startup.** `PrepareStorage` logs an error and keeps starting when the
  schema migration fails. Decide whether to refuse to start once real tables exist.
- **Log directory must be writable in the container.** If `/app/logs` is not writable, the file sink fails
  silently; stderr still carries every record. Check on first deployment.
- **Startup-log test reads a shared file.** The startup-log test reads the first record of the shared daily
  file, which may come from an earlier run that day. It stays valid because every run's first record is the
  startup line.
- **Real Okta round-trip is operator-observed.** Tests use the stub OIDC issuer; the live Okta sign-in
  (TOR-02-hKpbfCf) is checked by hand with `tests/manual/TOR-02-hKpbfCf.md`.
- **Okta removal is not immediate for existing sessions** (§20): up to the refresh-token lifetime.
- **Stdio missing-key line carries a log prefix.** The stderr message reaches stderr through Serilog, so it begins with a
  timestamp and `[FTL]` before `Error: …`. A bare `Error: …` line would match the stdio startup example in CLAUDE.md more closely.
- **Stdio cost lookup routes `fake/tone` by generation ID.** `SoloSpeechClient.LookUpCostAsync` matches the literal ID
  `fake-tone`. This works; routing by model would be sturdier.
- **Upstream failure wording changed on the gateway** (§45). Any client that parsed `status <code>` needs updating.
- **Dashboard wireframe deviations.** S-01's over-quota notice lacks the "Over" badge; S-05 lacks the paragraph
  pointing to the usage page for non-administrators; S-06's body text and the hint under "Sign in" differ from
  `docs/product-vision-planning/ux/screens.md`.
- **Dashboard sign-in callback is not yet confirmed on a real Okta tenant.** The Okta app needs `<gateway>/signin-oidc`
  as a sign-in redirect URI next to `/oauth/callback`. Tests cover the flow with the stub issuer; the release check
  covers Okta.
- **Usage query errors bypass the error state.** `UsageStore.ListMonthAsync` does not catch `SqliteException` (unlike
  `CacheStatsReader`), so a real query error on the admin Usage screen escapes `PageLoad` instead of showing the error
  state. Catch it the same way `CacheStatsReader` does.
- **Unknown admin address shows the browser's 404 page.** For an administrator, an unknown `/admin/...` address shows the
  browser's own HTTP 404 page rather than the app's "Page not found" notice, so the response body appears to be replaced.
  Not part of any requirement; investigate when the admin pages next change.
- **Emails wrap mid-word at 200% zoom.** At 200% zoom (640 CSS px) emails in the admin Usage table wrap mid-word. Readable,
  with no horizontal scroll.
- **Admin wireframe deviations.** The Total row's Quota cell on the Usage screen is blank instead of "—"
  (`docs/product-vision-planning/ux/screens.md`).
- **Quota reset records no administrator.** "Use default…" deletes the override row, and its log line names neither the
  administrator nor the time. The Key Components of Epic EwgA3Zf asked for both; no TOR covers it.
- **Email links on Usage are not visually distinguished.** They have no underline or colour; the wireframe underlines them.
- **Quota field shows a red border while a save is in progress.** Cosmetic; seen in `.autopilot/look/05`.
- **Usage (S-02) table clips between about 480 and 630 CSS px.** Outside the declared layout-floor points (320 px and 200%
  zoom), so it passes the floor; the User Detail table does not clip there (§63).
- **Blazor Blueprint MCP server was unreachable in the implementing session.** Components were taken from the
  library's llms.txt and checked against its XML docs, the fallback CLAUDE.md allows. Re-check each `Bb*` usage through
  the `blazorblueprint` server once it connects.
