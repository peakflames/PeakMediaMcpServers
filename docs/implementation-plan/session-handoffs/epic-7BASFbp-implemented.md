# Epic 7BASFbp — Sign-in through the broker — Implemented

**Implemented:** 2026-11-12 · **By:** Todd Schavey · **Branch:** `feature/epic-7BASFbp-sign-in-broker`

## What Was Built

The gateway is now its own OAuth 2.1 authorization server (dynamic client registration, PKCE S256,
rotating refresh tokens) that delegates sign-in to the organization's OIDC provider (Okta), so a skill
user connects Claude Code with one `claude mcp add --transport http peak-media <G>/mcp` and one browser
sign-in. `/mcp` accepts only the gateway's own RS256 access tokens (`sub`, `email`, `role`), a 401 points at
`/.well-known/oauth-protected-resource`, and the owner-or-admin access rule lives in one place for later
epics. A real Okta Integrator org sign-in was observed end to end (admin, non-admin, unassigned).

## Deferrals

Count: 0

None

## Key Files

| File | Change |
|---|---|
| `src/PeakMediaRemoteMcpServer/Auth/McpAuthSetup.cs` | NEW — JwtBearer gate (gateway key, iss/aud, RS256, 30 s skew) + MCP challenge scheme + protected-resource metadata at `/.well-known/oauth-protected-resource`; `admin` policy |
| `src/PeakMediaRemoteMcpServer/Auth/Caller.cs`, `AccessRule.cs` | NEW — caller from the validated token only; owner-or-admin rule |
| `src/PeakMediaRemoteMcpServer/Broker/*.cs` | NEW — `SigningKeyProvider`, `JwtIssuer`, `PkceHelper`, `RedirectUriValidator`, `UpstreamOidcClient`, `BrokerJanitor`, service registration |
| `src/PeakMediaRemoteMcpServer/Broker/Endpoints/*.cs` | NEW — AS metadata, JWKS, `/register`, `/authorize`, `/oauth/callback`, `/token`, consent cookie, response models |
| `src/PeakMediaRemoteMcpServer/Broker/Storage/*.cs` | NEW — `AppMetaStore`, `UserStore`, `ClientStore`, `TransactionStore`, `CodeStore`, `RefreshTokenStore`, `TokenHashing` |
| `src/PeakMediaRemoteMcpServer/Storage/SchemaMigrator.cs` | Migration 1: `app_meta`, `users`, `oauth_clients`, `oauth_transactions`, `oauth_codes`, `oauth_refresh_tokens` |
| `src/PeakMediaRemoteMcpServer/Configuration/BrokerOptions.cs`, `AdminOptions.cs`, `GatewayStartupValidator.cs` | Broker settings (`IssuerUri`, scopes, groups claim, allowlist, lifetimes), `Admin:Group`, new startup refusals |
| `src/PeakMediaRemoteMcpServer/Program.cs`, `GlobalUsings.cs`, `appsettings.json` | Wiring; non-secret defaults |
| `tests/StubAuthorizationServer/` | NEW — `StubOidcIssuer` (stub OIDC provider on real Kestrel), added to the solution |
| `tests/PeakMediaRemoteMcpServer.Tests/TestSignIn.cs`, `GatewayTestHost.cs`, `GatewayProcess.cs` | Real-flow sign-in helper; host/process settings for the broker |
| `tests/PeakMediaRemoteMcpServer.Tests/ResourceServerGateTests.cs`, `SignedInHandshakeTests.cs`, `GatewayBrokerProcessTests.cs`, `Broker/*`, `Auth/*` | NEW TOR and supporting tests |
| `tests/manual/TOR-02-hKpbfCf.md` | NEW — Okta checklist, operator steps, observation record |
| `tests/TestSupport/ServerProcess.cs`, `GatewayStartupValidatorTests.cs`, `McpGateTests.cs` | Updated |
| `build.py` | `mcp` accepts `PEAK_MEDIA_ACCESS_TOKEN` or `--oauth`; `mcp info` works with fastmcp 4 |
| `README.md`, `docs/architecture.md`, `docs/design-notes.md`, `CHANGELOG.md` | Okta setup, endpoint table, schema v1, decisions |

## Spec Deviations

| TOR ID | As-Written | As-Implemented | Reason |
|---|---|---|---|

(none — every TOR is implemented as written)

## Key Decisions

- **Gateway access token:** RS256 JWT signed by an RSA-2048 key persisted in `app_meta` (survives
  restart); claims `iss` = `Broker:IssuerUri`, `aud` = `McpAuth:Resource`, `sub` (Okta subject), `email`,
  `role` (`admin`|`user`), `jti`; 60 minutes. Later epics build the caller with `Caller.From(User)` and
  decide access with `AccessRule.CanAccess(caller, ownerSubject)` / `RequireAdmin` — nowhere else.
- **Refresh tokens** are the gateway's own opaque secrets, stored hashed, rotated on every use, 30 days
  (`Broker:IssuedRefreshTokenLifetimeDays`). Refresh does not re-contact Okta: removing a person from the
  Okta app refuses new sign-ins at once, but an existing session lasts until its refresh token expires.
- **Role** is decided once per sign-in (`admin` when the groups claim contains `Admin:Group`) and stored
  on `users`; refresh re-reads it. A role change in Okta takes effect at the next sign-in.
- **Groups claim name is configurable** (`Broker:UpstreamGroupsClaim`, default `groups`): Okta's newer
  federated-claims screen reserves `groups`, so the live run used `peakmedia_groups` with the expression
  `user.getGroups({'group.profile.name': 'org.peakmedia'}).![profile.name]`.
- **Open DCR, loopback redirects only:** `/register` accepts loopback redirect URIs (any port) plus the
  `Broker:AllowedRedirectUris` allowlist (empty by default) — closes the open-DCR phishing hole. A client
  secret is issued even to `none` clients (Claude Code rejects a null secret).
- **No upstream tokens stored;** identity comes only from the validated ID token (JWKS signature, `iss`,
  `aud` = client ID, lifetime, nonce). Discovery is lazy, so startup never contacts Okta.
- **Startup refusals added:** missing `Broker:UpstreamAuthority` / `Broker:UpstreamClientId`, non-https
  authority (http allowed only on loopback), invalid `Broker:IssuerUri`, `McpAuth:Resource` ≠ IssuerUri + `/mcp`.
- **Protected-resource metadata path:** SDK 2.4.0 serves the configured `ResourceMetadataUri` path itself;
  no explicit mapping was needed.
- **Okta refuses unassigned people on its own page:** with the org authorization server, Okta shows
  "400 — User is not assigned to the client application" (`access_denied`) and does not redirect back to
  `/oauth/callback`. When a provider does redirect with an error (TOR-02-KmwGCjA, tested with the stub),
  the gateway passes the error to the client and stores nothing. Either way no code or token is issued.
- Upstream exchange uses `client_secret_basic` (Okta web-app default) and the gateway's own PKCE pair.

## TOR Coverage (self-assessment)

Implementer's own assessment — not trusted by `/peak-workflow:wrapup-epic`, which re-verifies independently.

| TOR ID | Verdict | Test | Implementation |
|---|---|---|---|
| TOR-02-8R5ouns | PASS | `tests/PeakMediaRemoteMcpServer.Tests/ResourceServerGateTests.cs:8` | `src/PeakMediaRemoteMcpServer/Auth/McpAuthSetup.cs:29,99`; `Program.cs:92` |
| TOR-02-eXSTt0Q | PASS | `ResourceServerGateTests.cs:23` | `Auth/McpAuthSetup.cs:99` (ResourceMetadata: Resource, AuthorizationServers = [G]) |
| TOR-02-VOsRKOw | PASS | `Broker/DiscoveryAndRegistrationTests.cs:7` | `Broker/Endpoints/AuthorizationServerMetadataEndpoint.cs:18` |
| TOR-02-Mb6PQo5 | PASS | `Broker/DiscoveryAndRegistrationTests.cs:24` | `Broker/Endpoints/RegisterEndpoint.cs:37,78` |
| TOR-02-PosyPLa | PASS | `Broker/AuthorizeAndCallbackTests.cs:8` | `Broker/Endpoints/AuthorizeEndpoint.cs:99,105` |
| TOR-02-DU3EQVF | PASS | `Broker/AuthorizeAndCallbackTests.cs:27` | `Broker/Endpoints/AuthorizeEndpoint.cs:56` |
| TOR-02-QFiwMY7 | PASS | `Broker/TokenEndpointTests.cs:9`; `GatewayBrokerProcessTests.cs` (real sockets) | `Broker/Endpoints/CallbackEndpoint.cs:66,102`; `TokenEndpoint.cs:65,124` |
| TOR-02-x3ci8yI | PASS | `Broker/TokenEndpointTests.cs:42` | `Broker/Endpoints/TokenEndpoint.cs:65` |
| TOR-02-kx8Nl0i | PASS | `Broker/TokenEndpointTests.cs:61` | `Broker/Endpoints/TokenEndpoint.cs:81,124` |
| TOR-02-KmwGCjA | PASS | `Broker/AuthorizeAndCallbackTests.cs:45` | `Broker/Endpoints/CallbackEndpoint.cs:56-57` |
| TOR-02-hKpbfCf | PASS (operator-observed pending) | `tests/manual/TOR-02-hKpbfCf.md` (observation recorded 2026-11-12) | whole broker; see Key Decisions |
| TOR-02-NwWTAqU | PASS | `ResourceServerGateTests.cs:42` (expired / malformed / foreign-key) | `Auth/McpAuthSetup.cs:72` |
| TOR-02-GIUxnmc | PASS | `ResourceServerGateTests.cs:61` | `Auth/McpAuthSetup.cs` (identity only from the validated bearer token) |
| TOR-01-p6jE1nT | PASS | `SignedInHandshakeTests.cs:8` | `Program.cs:75` |

## Verification Results (self-assessment)

| Gate | Result |
|---|---|
| Lint — `dotnet format PeakMediaMcpServers.sln --verify-no-changes` | PASS |
| Build — `.venv/bin/python build.py build` | PASS |
| Tests — `.venv/bin/python build.py test` | PASS — 8 stdio + 85 gateway, 0 failed |
| Run the tool — `/version` | PASS — `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}` |
| Run the tool — `POST /mcp` unauthenticated | PASS — 401, `WWW-Authenticate: Bearer resource_metadata="http://localhost:5108/.well-known/oauth-protected-resource"` |
| Run the tool — `/.well-known/oauth-protected-resource`, `/.well-known/oauth-authorization-server` | PASS — 200 each, documents as specified |
| Run the tool — `build.py mcp info` / `mcp tools` with a token from the stub sign-in flow | PASS — server info shown; tool list empty (no tools until the narration epic) |
| Run the tool — `build.py mcp --oauth` | CANNOT VERIFY — not run (would open a browser); Claude Code's own OAuth client was exercised against real Okta instead |
| Run the tool — stdio `--version` | PASS — `PeakMediaMcpServer v0.1.0`, exit 0 |
| Real Okta round-trip (operator-observed) | Observed: admin → `role admin`, non-admin → `role user`, unassigned → refused by Okta; log scanned, no secrets |
| Visual / UX Baseline | N/A — no UI in this epic |
| Mechanical checks | Every TOR ID cited under `tests/`; no incomplete-work markers in changed files (stub hits are the `StubOidcIssuer` test double only) |
