# PeakMedia MCP Servers — Architecture Document

> **Status:** Describes the as-built system through the sign-in epic (7BASFbp, v0.1.0).
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
| Stdio server | Console app, `ModelContextProtocol` | MCP over stdio, user's own key |
| Shared tools | `PeakMediaMcpTools` class library | MCP tools + OpenRouter client |
| Caller auth | JwtBearer behind `McpAuth` options; Okta issuer | OAuth 2.1 resource-server gate, org-only |
| Database | SQLite (`Microsoft.Data.Sqlite`) | Usage log, quotas, audio cache (gateway only) |
| Errors | FluentResults | Result pattern |
| Logging | Serilog | Plain text to daily file + stderr |
| Tests | xUnit, ASP.NET TestHost | Fake OpenRouter + stub OIDC issuer (`StubOidcIssuer`) |
| Container | .NET SDK container publish | Docker Hub image on `v*` tags |

---

## 3. Data Sources

- **OpenRouter** — `POST https://openrouter.ai/api/v1/audio/speech`; shared key (`OpenRouter__ApiKey`) on the gateway, user's own key (`PEAK_MEDIA_OPENROUTER_API_KEY`) on stdio.
- **Okta** — OIDC issuer whose discovery document and JWKS validate caller tokens.
- **SQLite** — `Storage:DatabasePath` (default `data/peakmedia.db`): usage log, quotas, audio cache, each caller-owned row keyed by Okta subject.

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
| `GET /authorize` | none | Unknown client / unregistered redirect → bare 400 JSON. Missing `response_type=code` or S256 `code_challenge` → redirect to the client with `error`. Otherwise stores a transaction, sets the `pm_consent` cookie, 302 to the OIDC provider with `redirect_uri=G/oauth/callback`, scopes, `state`=transaction ID, nonce, the gateway's own S256 challenge |
| `GET /oauth/callback` | none (consent cookie) | Unknown state → 400; cookie mismatch → 403; provider `error` → redirect to the client with that error, nothing stored; success → ID token validated, `users` upserted, one-time code → redirect `?code&state&iss` |
| `POST /token` | client auth | `authorization_code` (PKCE S256 verified, single-use code) and `refresh_token` (rotated). Response `access_token` (RS256 JWT, 60 min), `token_type`, `expires_in`, `refresh_token`. Failures: 400 `invalid_grant` / `invalid_request`, 401 `invalid_client` |

Every `/mcp` request goes through the resource-server gate (`RequireAuthorization`): JwtBearer
validates the gateway's own access token (issuer = `Broker:IssuerUri`, audience = `McpAuth:Resource`,
RS256 with the gateway's signing key, lifetime, 30 s skew). A 401 is challenged by the MCP scheme, so it
carries `WWW-Authenticate: Bearer resource_metadata="G/.well-known/oauth-protected-resource"`. There is
no anonymous mode and no development login. G is `Broker:IssuerUri`.

Tool inventory: none yet. Tools arrive with the narration epic (see §5).

---

## 5. Backend Architecture

**Project layout**

```
PeakMediaMcpServers.sln
Directory.Build.props              <Version> (single source of truth), net11.0, nullable, implicit usings
src/PeakMediaMcpTools/             shared library: MCP tools, ToolErrorFormatter, ServerIdentity, LoggingSetup, UtcDailyFileSink
src/PeakMediaMcpServer/            stdio console server (user's own OpenRouter key, no database)
src/PeakMediaRemoteMcpServer/      HTTP gateway (ASP.NET): Program.cs, Configuration/, Storage/, Auth/, Broker/
  Auth/                            McpAuthSetup (JwtBearer + MCP challenge scheme, admin policy), Caller, AccessRule
  Broker/                          the gateway's authorization server: SigningKeyProvider, JwtIssuer, PkceHelper,
                                   RedirectUriValidator, UpstreamOidcClient, BrokerJanitor
  Broker/Endpoints/                metadata, JWKS, register, authorize, callback, token, consent cookie
  Broker/Storage/                  AppMetaStore, UserStore, ClientStore, TransactionStore, CodeStore, RefreshTokenStore, TokenHashing
tests/PeakMediaMcpServer.Tests/    stdio tests (process-level)
tests/PeakMediaRemoteMcpServer.Tests/  gateway tests (in-process + process-level)
tests/TestSupport/                 ServerProcess harness, RepoPaths, FreePort, TempDirectory, LogPatterns, ListLogger
tests/Fakes/                       FakeOpenRouter (canned audio/mpeg at POST /api/v1/audio/speech)
tests/StubAuthorizationServer/     StubOidcIssuer — stub OIDC provider (Okta stand-in) on real Kestrel
tests/manual/                      operator-observed checklists (TOR-02-hKpbfCf: real Okta round-trip)
build.py                           build / start / stop / test / mcp / log helper (run from .venv)
```

**Request → MCP tool path.** A tool is one file in `PeakMediaMcpTools`, a partial class per tool
group named `TtsTools_<ToolName>.cs`, holding one `[McpServerTool(Name = "…"), Description("…")]`
method that returns markdown. Both servers register tools with
`WithToolsFromAssembly(typeof(ToolErrorFormatter).Assembly)`, so a new file is picked up by both.
On the gateway: Kestrel → routing → authentication (JwtBearer) → authorization → `MapMcp("/mcp")`
→ tool method. `options.ToolCollection ??= []` makes `tools/list` answer (empty) before the first
tool exists.

**Gateway startup order** (`src/PeakMediaRemoteMcpServer/Program.cs`):
1. `CreateBuilder` — content root is the binary's directory, whatever the working directory.
2. Serilog configured from `LoggingSetup` → first record `[INF] PeakMediaRemoteMcpServer v<ver> starting`
   (written even when startup is then refused, so every run's first record is the startup line).
3. `GatewayStartupValidator.Validate` — in order: `McpAuth:Enabled` true, `Broker:UpstreamClientSecret`
   set, `OpenRouter:ApiKey` set, `Broker:UpstreamAuthority` set and https (http only on loopback),
   `Broker:UpstreamClientId` set, `Broker:IssuerUri` absolute, `McpAuth:Resource` = `IssuerUri + "/mcp"`. Failure → `[FTL] <message naming the next action>`, exit 1, before
   Kestrel binds.
4. `BuildApp(builder)` (the test seam) — clears the default logging providers (the console logger
   writes stdout), adds Serilog, options, storage, auth, MCP; prepares storage; maps endpoints.

**Stdio startup order** (`src/PeakMediaMcpServer/Program.cs`): parse args (`--version` → stdout line,
exit 0; anything but no args → stderr message, exit 2) → Serilog → startup record → read
`PEAK_MEDIA_OPENROUTER_API_KEY` (legacy `TTS_OPENROUTER_API_KEY`; missing → exit 1) → generic host
with providers cleared + Serilog → MCP stdio transport.

**Storage.** `Storage:DatabasePath` (relative → content root). `DatabasePathResolver` creates the
directory and probes writability; unwritable → `<temp>/peakmedia/peakmedia.db` with a Warning that it
does not survive a restart. `SqliteConnectionFactory` opens WAL connections and returns
`Result<SqliteConnection>`. `SchemaMigrator` runs at startup: creates `schema_version` and applies
the append-only `Migrations` list. **Schema v1** (Epic 7BASFbp): `app_meta(key, value)` (token-signing
key); `users(subject PK, email, is_admin, first_seen_utc, last_seen_utc)` — the owner key every later
table references; `oauth_clients`; `oauth_transactions` (incl. the gateway's upstream PKCE verifier and
nonce, consent-cookie hash); `oauth_codes(code_hash, subject → users, …)`; `oauth_refresh_tokens(token_hash,
subject → users, client_id, expires_utc)`. Times are Unix seconds; codes, refresh tokens, client secrets
and the consent cookie are stored only as SHA-256 hashes; no upstream (Okta) token is stored. A migration failure logs an error and startup
continues (Known Issue in design-notes §23). Every table that holds a caller's records carries the
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

N/A — no user interface

---

## 7. Background Services

- `BrokerJanitor` (hosted service) — every 10 minutes deletes expired sign-in transactions,
  authorization codes and refresh tokens. A failed sweep logs a warning and retries next tick.

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
  `Broker__IssuerUri` (the public URL), `McpAuth__Resource` (= IssuerUri + `/mcp`), `Admin__Group`.

---

## 9. Security & Access

- The gateway refuses to start when `McpAuth:Enabled` is false, or when `Broker__UpstreamClientSecret`
  or `OpenRouter__ApiKey` is unset.
- The gateway also refuses to start when the OIDC provider is not configured (`Broker:UpstreamAuthority`,
  `Broker:UpstreamClientId`) or `McpAuth:Resource` ≠ `Broker:IssuerUri + "/mcp"`.
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
- Secrets arrive only through environment variables; `appsettings*.json` holds non-secret values.
  Secret-holding classes (`BrokerOptions`, `OpenRouterOptions`, token/registration responses, the
  sign-in transaction) are `sealed class`, never records. Logs never carry tokens, codes or secrets;
  the subject is logged only at Debug.
