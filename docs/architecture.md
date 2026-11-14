# PeakMedia MCP Servers — Architecture Document

> **Status:** Describes the as-built system through the admin-usage-and-cache-overview epic (G8jMzOz, v0.1.0):
> the admin Usage and Cache screens and their admin gate (G8jMzOz); the usage dashboard shell, sign-in and My Usage
> screen (Ail1fOL); the gateway's per-user usage log, monthly quotas and `my_usage` tool (EYGrros); the stdio
> server's local clip cache and shared narration tools (YVOjXtI). The User detail admin screen is not yet built.
> Sections for capabilities not yet built say so explicitly. Run `/peak-workflow:refresh-docs`
> after completing epics to keep this document aligned with the code.

---

## 1. System Overview

PeakMedia MCP Servers is a C# MCP gateway that lets Claude Code skills (`/web2video`,
`/plate-factory`) generate text-to-speech through one shared, server-held OpenRouter key instead of
each user supplying their own. Callers authenticate with the organization's Okta tenant; the
gateway records per-user usage, enforces per-user quotas, and caches audio. It deploys as a single
Linux container (`peakflames/peak-media-remote-mcp-server`) with a mounted data volume. A stdio
console variant serves single users with their own OpenRouter key.

---

## 2. Tech Stack

| Layer | Technology | Purpose |
|---|---|---|
| Runtime | .NET 11 (`net11.0`) | All projects |
| HTTP server | ASP.NET, `ModelContextProtocol.AspNetCore` | MCP Streamable HTTP at `/mcp` (dev port 5108) |
| Stdio server | Console app, `ModelContextProtocol` | MCP over stdio, user's own key, local clip cache, no database |
| Shared tools | `PeakMediaMcpTools` class library | MCP tools + OpenRouter client |
| Caller auth | JwtBearer behind `McpAuth` options; Okta issuer | OAuth 2.1 resource-server gate, org-only |
| Database | SQLite (`Microsoft.Data.Sqlite`) | Usage log, quotas, audio cache (gateway only) |
| Errors | FluentResults | Result pattern |
| Logging | Serilog | Plain text to daily file + stderr |
| Dashboard UI | Blazor Interactive Server, Blazor Blueprint UI 4.2 (`BlazorBlueprint.Components`, `BlazorBlueprint.Icons.Lucide`) | Usage dashboard inside the HTTP gateway (§6) |
| Dashboard session | Cookie `pm_dashboard` + OpenID Connect (`Microsoft.AspNetCore.Authentication.OpenIdConnect`) | Browser sign-in through the same provider as the broker; never authenticates `/mcp` |
| Tests | xUnit, ASP.NET TestHost; Microsoft.Playwright 1.64.1 (E2E) | Fake OpenRouter + stub OIDC issuer (`StubOidcIssuer`); Chromium end-to-end tests |
| Container | .NET SDK container publish | Docker Hub image on `v*` tags |

---

## 3. Data Sources

- **OpenRouter** — `POST https://openrouter.ai/api/v1/audio/speech` (one request per uncached clip, raw PCM output, zero data retention); `GET /generation?id=` for the real cost of each clip (the generation ID arrives in the `X-Generation-Id` response header). Shared key (`OpenRouter__ApiKey`) on the gateway; the user's own key (`PEAK_MEDIA_OPENROUTER_API_KEY`, legacy `TTS_OPENROUTER_API_KEY`) on stdio. Stdio clips never touch the shared key or the gateway.
- **Local clip cache (stdio)** — one WAV file per clip, named by its cache key, in `PEAK_MEDIA_TTS_CACHE` or `~/.cache/peak-media/tts`. The same directory the peak-media skills download gateway clips into. No index, no eviction; a file counts as a hit only when it is a complete WAV.
- **Okta** — OIDC issuer whose discovery document and JWKS validate caller tokens.
- **SQLite** — `Storage:DatabasePath` (default `data/peakmedia.db`): usage log, quotas, audio cache index, download tokens, eviction history, each caller-owned row keyed by Okta subject.
- **Audio files** — the cached WAV files under `Storage:AudioDirectory` (default `audio/` beside the database; `/app/data/audio` in the container). Bounded by `Storage:CacheMaxGb` (default 10): least-recently-used clips are evicted (see §7).

---

## 4. API Design

| Endpoint | Auth | Behavior |
|---|---|---|
| `GET /version` | none | `200 {"name":"PeakMediaRemoteMcpServer","version":"<semver>"}` |
| `POST /mcp` | bearer token | MCP Streamable HTTP, stateless mode. `serverInfo` carries the same name and version as `/version` |
| `GET` / `DELETE /mcp` | bearer token | 401 without a token; 405 for a signed-in caller (stateless transport has no GET stream) |
| `GET /.well-known/oauth-protected-resource` | none | RFC 9728 metadata: `resource` = `McpAuth:Resource` (G/mcp), `authorization_servers` = [G]. Served by the MCP SDK's auth scheme at exactly this path (`ResourceMetadataUri`) |
| `GET /.well-known/oauth-authorization-server` | none | RFC 8414 metadata: `G/authorize`, `G/token`, `G/register`, `jwks_uri`, `code_challenge_methods_supported:["S256"]`, `response_types_supported:["code"]`, grants `authorization_code refresh_token`, token auth `none client_secret_post client_secret_basic`, `authorization_response_iss_parameter_supported:true` |
| `GET /.well-known/jwks.json` | none | Public half of the token-signing key (RS256) |
| `POST /register` | none | RFC 7591 dynamic client registration, always on. 201 with `client_id`, `client_secret` (issued even for `none`), `redirect_uris`. Redirect URIs must be loopback (any port) or in `Broker:AllowedRedirectUris`, else 400 `invalid_redirect_uri` |
| `GET /clips/{token}` | none (the token is the credential) | Serves the WAV behind a download URL: 200 `audio/wav` for a live token, repeatedly, for 15 minutes; 404 for an unknown token; 410 Gone with an RFC 9457 problem-details body telling the caller to run `synthesize_narration` again for an expired one; 503 if the cache index cannot be read. Tokens are 43-character base64url values stored only as SHA-256 |
| `GET /authorize` | none | Unknown client / unregistered redirect → bare 400 JSON. Missing `response_type=code` or S256 `code_challenge` → redirect to the client with `error`. Otherwise stores a transaction, sets the `pm_consent` cookie, 302 to the OIDC provider with `redirect_uri=G/oauth/callback`, scopes, `state`=transaction ID, nonce, the gateway's own S256 challenge |
| `GET /oauth/callback` | none (consent cookie) | Unknown state → 400; cookie mismatch → 403; provider `error` → redirect to the client with that error, nothing stored; success → ID token validated, `users` upserted, one-time code → redirect `?code&state&iss` |
| `POST /token` | client auth | `authorization_code` (PKCE S256 verified, single-use code) and `refresh_token` (rotated). Response `access_token` (RS256 JWT, 60 min), `token_type`, `expires_in`, `refresh_token`. Failures: 400 `invalid_grant` / `invalid_request`, 401 `invalid_client` |
| `GET /signin-oidc` | none (OIDC state and nonce cookies) | Dashboard sign-in callback, handled by the OpenID Connect scheme `pm_dashboard_oidc` (code + PKCE, `response_mode=query`). On success sets the `pm_dashboard` session cookie and continues to the dashboard. Must be registered in the Okta app's sign-in redirect URIs |
| `POST /signout` | antiforgery token | Clears only the browser dashboard session (the cookie); does not sign the person out of the provider. Redirects to Signed Out (`/signed-out`). Invalid antiforgery → 400 problem-details |

Dashboard pages are Blazor routes, not API endpoints (§6): `/` (My Usage, S-01), `/admin` (Usage, S-02, administrators only), `/admin/cache` (Cache, S-04, administrators only), `/admin/{*Path}` (Not Authorized, S-05: 403 for non-administrators, 404 for administrators on any other `/admin` address), `/signed-out` (Signed Out, S-06).

Every `/mcp` request goes through the resource-server gate (`RequireAuthorization`): JwtBearer
validates the gateway's own access token (issuer = `Broker:IssuerUri`, audience = `McpAuth:Resource`,
RS256 with the gateway's signing key, lifetime, 30 s skew). A 401 is challenged by the MCP scheme, so it
carries `WWW-Authenticate: Bearer resource_metadata="G/.well-known/oauth-protected-resource"`. There is
no anonymous mode and no development login. G is `Broker:IssuerUri`.

**MCP tools.** The gateway serves all three tools behind the `/mcp` gate. The stdio server serves
`synthesize_narration` and `list_voices` with identical arguments and result fields; the only difference
is that each clip entry carries a local file `path` in place of the gateway's `url`. The stdio server
does not offer `my_usage`. The client-facing contract is `docs/client-integration.md`.

| Tool | Gateway | Stdio |
|---|---|---|
| `list_voices` | yes | yes (includes `fake/tone`) |
| `synthesize_narration` | yes (`url`, quota-gated) | yes (`path`, no quota) |
| `my_usage` | yes | no |

| Tool | Annotations | Behavior |
|---|---|---|
| `list_voices` | read-only, idempotent | Lists the allowed models (`Tts:Models`) with their voices, default voice, whether a style is sent, and names the default model |
| `synthesize_narration` | — | Turns 1–40 clips (1–4,000 characters each) into speech through the shared key. Refused with `ERROR:` before any synthesis when the caller's month-to-date spend has reached their quota (checked before the cache lookup, so cached calls are refused too). Cache-first; one progress notification per clip; each synthesized clip is charged to the caller at OpenRouter's reported cost (cache hits cost nothing). Returns a markdown summary plus one fenced JSON block of per-clip metadata (`n`, `cacheKey`, `durationMs`, `words`, `wpm`, `cached`, `costUsd`, `sampleRate`, `url`). `url` is a 15-minute download link to a 16-bit mono 24 kHz WAV. No audio bytes in the result. A call that starts under quota runs to completion and notes in its result when the caller is then over quota. Every invalid request is refused with `ERROR:` before any upstream call |
| `my_usage` | read-only, idempotent | The caller's own UTC calendar month: amount spent, monthly quota, remaining amount (to the cent), clips synthesized, cache hits and the reset time. Works for callers who are over quota. Reads only through the caller-bound `IUsageLedger` |

Upstream failures in `synthesize_narration` name the failing clip (`ERROR: … clip N of M …`) and say that earlier clips are saved. A transient failure (timeout, unreachable, 5xx) is retried once after `OpenRouter:RetryDelayMs`; a 402 (out of credit) or 401 (rejected key) is reported without a retry and never reveals the key. Calls stop at the failing clip (design-notes §28–§29).

---

## 5. Backend Architecture

**Project layout**

```
PeakMediaMcpServers.sln
Directory.Build.props              <Version> (single source of truth), net11.0, nullable, implicit usings
src/PeakMediaMcpTools/             shared library: MCP tools (TtsTools, TtsTools_ListVoices, TtsTools_SynthesizeNarration; gateway-only TtsUsageTools_MyUsage), ToolErrorFormatter, ServerIdentity, LoggingSetup, UtcDailyFileSink
  Narration/                       NarrationService (quota gate, cache-first, sequential, per-clip progress and charging), NarrationRequestValidator, NarrationResultFormatter, NarrationAccount (host-specific failure wording), ClipCacheKey, WavAudio, WordCounter, TtsCatalog, IClipStore, IUsageLedger (UsageStatus, UsageEntry, UsageText)
  OpenRouter/                      ISpeechClient (speech seam), OpenRouterSpeechClient: POST audio/speech (PCM, ZDR) and GET generation?id= for cost; ToneSpeechClient (offline fake/tone); SpeechError (failure kinds), SpeechRetryPolicy
src/PeakMediaMcpServer/            stdio console server (user's own OpenRouter key, local clip cache, no database): Program, OpenRouterKey, LocalClipStore, SoloSpeechClient (routes fake/tone offline), UnmeteredUsageLedger, SoloOpenRouterOptions, CommandLine
src/PeakMediaRemoteMcpServer/      HTTP gateway (ASP.NET): Program.cs, Configuration/, Storage/, Auth/, Broker/, Cache/
  Auth/                            McpAuthSetup (JwtBearer + MCP challenge scheme, admin and dashboard policies), DashboardAuthSetup (cookie + OIDC dashboard session), AdminGroup (groups → admin rule, shared with the broker), Caller, AccessRule
  Cache/                           ClipStore (shared audio cache index + WAV files), AudioDirectory, DownloadTokens, ClipEndpoints (GET /clips/{token}), CacheEvictor (LRU eviction + expired-link clean-up; records each evicted clip in `cache_evictions`), CacheEvictionService (hosted, periodic), CacheStatsReader (admin Cache statistics)
  Broker/                          the gateway's authorization server: SigningKeyProvider, JwtIssuer, PkceHelper,
                                   RedirectUriValidator, UpstreamOidcClient, BrokerJanitor
  Broker/Endpoints/                metadata, JWKS, register, authorize, callback, token, consent cookie
  Broker/Storage/                  AppMetaStore, UserStore, ClientStore, TransactionStore, CodeStore, RefreshTokenStore, TokenHashing
  Usage/                           UsageStore (usage log reads/writes; admin-gated `ListMonthAsync`), QuotaStore (default quota + admin overrides), UsageLedger (binds stores to the caller from the token), UsageMonth (UTC calendar-month window, NanoUsd money type), UsageStatusReader (month-parameterized reader shared by `my_usage` and My Usage), DashboardUsageService (dashboard facade over the reader), AdminUsageService (admin Usage report: sort, OVER, totals, hit rates)
  Components/                      Blazor root (`App.razor`, `Routes.razor`, `_Imports.razor`, `RedirectToSignIn.razor`), `AdminOnly.razor` (admin gate for the admin pages), `PageLoad.cs` (shared load state: loading → loaded / error / cancelled, Cancel after 10 s), `Layout/` (MainLayout app shell, AuthLayout brand-only), `Pages/` (MyUsage, AdminUsage, AdminCache, NotAuthorized, SignedOut)
  Dashboard/                       DashboardEndpoints (`POST /signout`)
  wwwroot/                         theme.css (design tokens), app.css (app layout), js/theme-choice.js (light / dark / system)
tests/PeakMediaMcpServer.Tests/    stdio tests (process-level; SoloNarration/ drives the built binary over stdin/stdout via tests/TestSupport/StdioClient.cs)
tests/PeakMediaRemoteMcpServer.Tests/  gateway tests (in-process + process-level); Dashboard/ (usage service, admin role mapping, shell served)
tests/PeakMediaRemoteMcpServer.E2E/  Playwright (Chromium) end-to-end tests: hosts the real gateway (`Program.BuildApp`, real Kestrel, real SQLite) in-process and signs in through the stub issuer's redirect flow
tests/StubSignIn/                  PeakMediaStubSignIn — console helper for `build.py smoke` (stub issuer + broker sign-in over a line protocol)
tests/TestSupport/                 ServerProcess harness, RepoPaths, FreePort, TempDirectory, LogPatterns, ListLogger
tests/Fakes/                       FakeOpenRouter (canned PCM at POST /api/v1/audio/speech, cost via generation lookup)
tests/PeakMediaRemoteMcpServer.Tests/Narration/  one test file per narration TOR group; NarrationClient signs in through the real broker
tests/StubAuthorizationServer/     StubOidcIssuer — stub OIDC provider (Okta stand-in) on real Kestrel
tests/manual/                      operator-observed checklists (TOR-02-hKpbfCf: real Okta round-trip; TOR-08-nLHs0pq: admin landing after real Okta sign-in)
build.py                           build / start / stop / test / mcp / log helper (run from .venv)
```

**Request → MCP tool path.** A tool is one file in `PeakMediaMcpTools`, a partial class per tool
group named `TtsTools_<ToolName>.cs`, holding one `[McpServerTool(Name = "…"), Description("…")]`
method that returns markdown. Both servers register tools with
`WithToolsFromAssembly(typeof(ToolErrorFormatter).Assembly)`, so a new file is picked up by both.
On the gateway: Kestrel → routing → authentication (JwtBearer) → authorization → `MapMcp("/mcp")`
→ tool method. `options.ToolCollection ??= []` makes `tools/list` answer (empty) before the first
tool exists.

Narration tools are registered explicitly: `TtsTools` (shared: `list_voices`, `synthesize_narration`) is
registered by both servers with `WithTools<TtsTools>()`; `TtsUsageTools` (`my_usage`) only by the gateway,
which also scans the tools assembly with `WithToolsFromAssembly`. The stdio host does not scan.

**Gateway startup order** (`src/PeakMediaRemoteMcpServer/Program.cs`):
1. `CreateBuilder` — content root is the binary's directory, whatever the working directory.
2. Serilog configured from `LoggingSetup` → first record `[INF] PeakMediaRemoteMcpServer v<ver> starting`
   (written even when startup is then refused, so every run's first record is the startup line).
3. `GatewayStartupValidator.Validate` — in order: `McpAuth:Enabled` true, `Broker:UpstreamClientSecret`
   set, `OpenRouter:ApiKey` set, `Broker:UpstreamAuthority` set and https (http only on loopback),
   `Broker:UpstreamClientId` set, `Broker:IssuerUri` absolute, `McpAuth:Resource` = `IssuerUri + "/mcp"`, `Storage:CacheMaxGb` > 0. Failure → `[FTL] <message naming the next action>`, exit 1, before
   Kestrel binds.
4. `BuildApp(builder)` (the test seam) — clears the default logging providers (the console logger
   writes stdout), adds Serilog, options, storage, auth, MCP; prepares storage; maps endpoints.

**Stdio startup order** (`src/PeakMediaMcpServer/Program.cs`): parse args (`--version` → stdout line,
exit 0; anything but no args → stderr message, exit 2) → Serilog → startup record → read
`PEAK_MEDIA_OPENROUTER_API_KEY` (legacy `TTS_OPENROUTER_API_KEY`; missing → exit 1) → generic host
with providers cleared + Serilog → narration services (`TtsCatalog` from `Tts`, `LocalClipStore`, `UnmeteredUsageLedger`,
`SoloSpeechClient` over an `HttpClient` that carries the key) → MCP stdio transport with `WithTools<TtsTools>()`.
The stdio server opens no database.

**Storage.** `Storage:DatabasePath` (relative → content root). `DatabasePathResolver` creates the
directory and probes writability; unwritable → `<temp>/peakmedia/peakmedia.db` with a Warning that it
does not survive a restart. `SqliteConnectionFactory` opens WAL connections and returns
`Result<SqliteConnection>`. `SchemaMigrator` runs at startup: creates `schema_version` and applies
the append-only `Migrations` list. **Schema v4** (Epic G8jMzOz): `cache_evictions(cache_key, created_by_subject, at_utc, size_bytes)` — one row per
clip the eviction pass removes, owned by the subject that created the clip, indexed by time and owner. The Cache
screen's "evictions this month" counts these rows, so counting starts when the migration is applied.
**Schema v3** (Epic EYGrros): `usage_log(id, subject, at_utc, model, voice, characters, cost_nanousd,
cache_key, cache_hit)` — one row per clip charged or served from cache, owned by the caller's subject;
it holds character counts and cache keys, never narration text. `quota_overrides(subject PK,
monthly_nanousd, set_by_subject, set_at_utc)` — administrator-set monthly quotas. Money is stored as
integer nano-dollars. The default quota is `Quota:DefaultMonthlyUsd` (25) in `appsettings.json`.
**Schema v2** (Epic 8po1tRg): `audio_cache(cache_key PK, size_bytes,
duration_ms, created_by_subject, created_utc, last_used_utc)` — the shared cache index, read by key across
callers, with the creating subject recorded; `clip_downloads(token_hash PK, cache_key → audio_cache ON
DELETE CASCADE, subject, created_utc, expires_utc)` — download tokens owned by the caller they were issued
to, stored as SHA-256 only. The WAV bytes live on disk under `Storage:AudioDirectory`, not in SQLite.
**Schema v1** (Epic 7BASFbp): `app_meta(key, value)` (token-signing
key); `users(subject PK, email, is_admin, first_seen_utc, last_seen_utc)` — the owner key every later
table references; `oauth_clients`; `oauth_transactions` (incl. the gateway's upstream PKCE verifier and
nonce, consent-cookie hash); `oauth_codes(code_hash, subject → users, …)`; `oauth_refresh_tokens(token_hash,
subject → users, client_id, expires_utc)`. Times are Unix seconds; codes, refresh tokens, client secrets
and the consent cookie are stored only as SHA-256 hashes; no upstream (Okta) token is stored. A migration failure logs an error and startup
continues (Known Issue in design-notes §59). Every table that holds a caller's records carries the
caller's Okta subject. `StorageFaultSwitch` (`PEAKMEDIA_TEST_STORAGE_FAULT=fail|delay:<ms>`) makes
storage fail or slow down for tests and screen-state checks; it is ignored, with a Warning, in Production.

**Errors.** FluentResults throughout. MCP tool errors: `ToolErrorFormatter.Format(problem, nextAction)`
→ `ERROR: <problem>. <next action>`, with bearer/basic values redacted; exception text is never echoed.

**Logging.** `LoggingSetup.CreateLogger(name, configuration)`: template
`{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}`; console sink
with `standardErrorFromLevel: Verbose` (stderr only); `UtcDailyFileSink` writes
`logs/<Name>_<yyyyMMdd>.log` next to the binary, named with the **UTC** date (Serilog.Sinks.File's own
daily roll uses the local date). Timestamps inside records are local time with offset. Minimum level
from `Logging:MinimumLevel` (default Information); `Microsoft.AspNetCore` / `Microsoft.Hosting` at Warning.
Neither server writes to stdout except the stdio server's MCP protocol and `--version` line.

**Tests.** Two styles:
- *In-process* — `GatewayTestHost` builds through `Program.BuildApp` on `UseTestServer` with in-memory
  configuration and a fresh temp database (the per-test data reset).
- *Process-level* — `ServerProcess` launches the built DLL (`dotnet <dll>`, `ArgumentList`, scrubbed
  environment) and captures stdout and stderr separately; used for everything the requirements state
  about streams, exit codes, startup refusal and log files. Parallelization is off (`tests/xunit.runner.json`)
  because process tests share each server's `logs/` directory.
- Fakes: `FakeOpenRouter`; `StubOidcIssuer` (`tests/StubAuthorizationServer/`) — discovery, JWKS,
  `/authorize` (auto-approves the configured next user, or `RefuseNext("access_denied")`), `/token`
  (checks client secret and PKCE, returns an RS256 ID token with `sub`, `email`, `groups`, `nonce`); it
  records every request. Tests never call real OpenRouter or Okta.
- Sign-in in tests: `TestSignIn.SignInAsync(gateway, stub, user)` walks the real broker flow
  (`/register` → `/authorize` → stub → `/oauth/callback` → `/token`) and returns the tokens. It is the
  only way a test obtains a gateway access token. `GatewayTestHost.StartAsync(stub)` points the gateway
  at the stub (G = `http://localhost`).

---

## 6. Frontend Architecture

The usage dashboard lives inside `PeakMediaRemoteMcpServer` (Epic Ail1fOL): Blazor, **Interactive
Server**, built with Blazor Blueprint UI (`BlazorBlueprint.Components` 4.2, `BlazorBlueprint.Icons.Lucide`).

- **Composition.** `Components/App.razor` loads `wwwroot/theme.css` (design tokens) →
  `_content/BlazorBlueprint.Components/blazorblueprint.css` → `wwwroot/app.css` (app layout), with
  `theme-init.js` in `<head>` (dark class before first paint). `Routes` runs Interactive Server
  everywhere except pages marked `[ExcludeFromInteractiveRouting]` (Signed Out, static SSR).
  `Components/Layout/MainLayout.razor` is the app shell: brand, primary nav (`aria-current` on the
  current item; "Usage" / "Cache" only when `AccessRule.IsAdmin`), signed-in email, Theme select
  (light / dark / system, `wwwroot/js/theme-choice.js`, stored in the library's `bb-theme` key), Sign
  out (antiforgery form POST), `<main>`, version footer, `<BbPortalHost />`. `AuthLayout` is brand-only.
- **Tokens.** `wwwroot/theme.css` holds shadcn-compatible neutral `:root` / `.dark` variables, one
  `--radius`, and the semantic `--over-quota` pair. Never edit the library's CSS.
- **Screens.** `Pages/MyUsage.razor` (S-01, `/`), `Pages/AdminUsage.razor` (S-02, `/admin`, administrators
  only), `Pages/AdminCache.razor` (S-04, `/admin/cache`, administrators only), `Pages/NotAuthorized.razor`
  (S-05, every other `/admin` address — HTTP 403 for non-administrators, 404 for administrators),
  `Pages/SignedOut.razor` (S-06, `/signed-out`). My Usage renders loading on the server render and loads in
  the interactive render (so the loading text appears at once); states loading / cancelled / error / empty /
  populated; a Cancel button appears after 10 s and cancels the in-flight query. Admin pages and My Usage
  share `PageLoad<T>` for these states. Each admin page is wrapped in `AdminOnly`, which decides with
  `AccessRule.IsAdmin` and never renders its content for other callers.
- **Navigation.** `MainLayout` shows "Usage" and "Cache" only to administrators. Below 480 CSS px the
  navigation, signed-in email, theme choice and Sign out move into a menu sheet (`BbSheet`).
- **Data.** `Usage/DashboardUsageService` → `Usage/UsageStatusReader` (shared with the `my_usage`
  tool through `UsageLedger`) → `UsageStore` / `QuotaStore` → `AccessRule`. The caller comes only
  from the session principal. Admin Usage reads `AdminUsageService` → `UsageStore.ListMonthAsync` and
  `QuotaStore`; admin Cache reads `CacheStatsReader` (size against `Storage:CacheMaxGb`, clip count, this
  month's hit rate, `cache_evictions` this month). Both enforce `AccessRule.RequireAdmin` in the data service.
- **Decisions.** Design rationale for the dashboard is in design-notes §47–§58.
- **Tests.** `tests/PeakMediaRemoteMcpServer.E2E` hosts the real gateway (`Program.BuildApp`) on real
  Kestrel in-process with a fixed usage clock, signs in through the stub issuer's real redirect flow,
  and drives Chromium with Microsoft.Playwright (installed on first run). The test fault switch is
  replaced in-process (`SqliteConnectionFactory.FaultSwitch`, internal) to clear a fault mid-test.

---

## 7. Background Services

- `BrokerJanitor` (hosted service) — every 10 minutes deletes expired sign-in transactions,
  authorization codes and refresh tokens. A failed sweep logs a warning and retries next tick.
- `CacheEvictionService` (hosted service) — every `Storage:EvictionIntervalMinutes` (default 10), starting one interval after startup, runs `CacheEvictor`. The pass removes least-recently-used clips (cache file and index row, with their download links) while the cache exceeds `Storage:CacheMaxGb`, and deletes download links that expired more than 24 hours ago. A failed pass logs a warning and retries next tick (design-notes §31–§33).

---

## 8. Container / Infrastructure

- SDK container publish from `PeakMediaRemoteMcpServer.csproj`: `ContainerRepository`
  `peakflames/peak-media-remote-mcp-server`, `ContainerImageTag=$(Version)`, base
  `mcr.microsoft.com/dotnet/aspnet:11.0`, env `Storage__DatabasePath=/app/data/peakmedia.db` (mount a
  volume at `/app/data`). Not trimmed or single-file (the MCP SDK uses reflection).
- `.github/workflows/docker-publish.yml` runs on `v*` tags: fails unless the tag equals
  `<Version>` in `Directory.Build.props`, publishes the container to Docker Hub, tags `latest`, syncs
  the README. Secrets `DOCKERHUB_USERNAME` / `DOCKERHUB_TOKEN` (organization secrets).
- Runtime environment: `OpenRouter__ApiKey`, `Broker__UpstreamClientSecret` (both required — the
  gateway refuses to start without them), `Broker__UpstreamAuthority`, `Broker__UpstreamClientId`,
  `Broker__IssuerUri` (the public URL), `McpAuth__Resource` (= IssuerUri + `/mcp`), `Admin__Group`,
  `Storage__AudioDirectory` (optional; defaults to `audio/` beside the database).
- Okta app redirect URIs: the sign-in redirect list needs both `<gateway>/oauth/callback` (broker) and
  `<gateway>/signin-oidc` (dashboard). The second is a release check, not yet confirmed on a real Okta tenant.
- Downloads are served from the gateway itself; `Broker__IssuerUri` must be the public URL that clients can reach, since download links are built from it.

---

## 9. Security & Access

- The gateway refuses to start when `McpAuth:Enabled` is false, or when `Broker__UpstreamClientSecret`
  or `OpenRouter__ApiKey` is unset.
- The gateway also refuses to start when the OIDC provider is not configured (`Broker:UpstreamAuthority`,
  `Broker:UpstreamClientId`) or `McpAuth:Resource` ≠ `Broker:IssuerUri + "/mcp"`, or when `Storage:CacheMaxGb` is not greater than 0.
- **Gate.** `/mcp` requires a gateway access token: RS256 JWT signed by the persisted key in `app_meta`,
  `iss` = G, `aud` = `McpAuth:Resource`, claims `sub` (OIDC subject), `email`, `role` (`admin`|`user`),
  `jti`, 60 minutes. Identity comes only from the validated token — no header, query parameter or cookie
  can assert a caller.
- **Broker.** The gateway is its own OAuth 2.1 authorization server and delegates sign-in to the OIDC
  provider (Okta): two PKCE pairs (client ↔ gateway, gateway ↔ Okta), nonce, consent-binding cookie,
  `client_secret_basic` upstream. Identity is read from the validated ID token (signature via the
  provider's JWKS, `iss`, `aud` = client ID, lifetime, nonce); upstream tokens are discarded.
- **DCR loopback restriction.** Registration is open, so redirect URIs must be loopback (any port) or in
  `Broker:AllowedRedirectUris` (empty by default) — an attacker cannot register their own redirect URI
  and mail a victim an `/authorize` link.
- **Refresh policy.** Refresh tokens are the gateway's own opaque secrets (hashed), rotated on every use,
  30 days. Refresh does not contact Okta, so removing a person from the Okta app takes effect for an
  existing session when its refresh token expires; new sign-ins are refused at once.
- **Roles and the access rule.** `admin` when the ID token's groups claim (`Broker:UpstreamGroupsClaim`, default `groups`) contains `Admin:Group`,
  decided at sign-in and stored on `users`. `Caller.From(principal)` builds the caller from the token;
  `AccessRule.CanAccess(caller, ownerSubject)` (owner or admin) and `AccessRule.RequireAdmin` are the one
  place every later tool and page decides access; the `admin` authorization policy uses the same rule.
- **Dashboard session.** Cookie scheme `pm_dashboard` (HttpOnly, SameSite=Lax, Secure on HTTPS, 8 h
  sliding, wall clock) started by the OpenID Connect scheme `pm_dashboard_oidc` against the same
  `Broker:Upstream*` client (code + PKCE, `response_mode=query`, callback `/signin-oidc` — register it in
  the Okta app's sign-in redirect URIs next to `/oauth/callback`; `client_secret_post`; nothing saved).
  The session principal carries only `sub`, `email`, `role` (role from `AdminGroup.Grants`, the same rule
  the broker uses) and the sign-in is recorded in `users`. Pages require the `dashboard` policy (cookie
  scheme); no session → 302 to the provider. The default authenticate scheme `pm_selector` sends `/mcp`
  and any `Authorization: Bearer` request to JwtBearer and everything else to the cookie, so the cookie
  can never authenticate `/mcp`. `POST /signout` (antiforgery) clears only the browser session.
- **Admin pages.** `/admin` (Usage) and `/admin/cache` (Cache) are gated twice on the server: `AdminOnly` on each
  page decides with `AccessRule.IsAdmin`, and the admin data reads (`UsageStore.ListMonthAsync`,
  `CacheStatsReader`) call `AccessRule.RequireAdmin` themselves. Non-administrators get 403 and no admin data.
- **Test fault switch.** `PEAKMEDIA_TEST_STORAGE_FAULT` (`fail` | `delay:<ms>`, ignored in Production) is
  read once at startup; the delay honors cancellation. In-process end-to-end tests replace it through the
  internal `SqliteConnectionFactory.FaultSwitch` — no request can reach it.
- **Quotas.** Every usage and quota read or write goes through `UsageStore`/`QuotaStore` with the caller
  from the validated token (`UsageLedger`), so the owner-or-admin rule of `AccessRule` applies. Only
  administrators may set quota overrides. Usage and quota records never carry narration text.
- **Stdio key handling.** The user's own key is read from the environment and held only on the stdio `HttpClient`'s
  Authorization header; it is never logged, and no stdio code path writes it to the local cache or stdout.
- Secrets arrive only through environment variables; `appsettings*.json` holds non-secret values.
  Secret-holding classes (`BrokerOptions`, `OpenRouterOptions`, token/registration responses, the
  sign-in transaction) are `sealed class`, never records. Logs never carry tokens, codes or secrets;
  the subject is logged only at Debug.
