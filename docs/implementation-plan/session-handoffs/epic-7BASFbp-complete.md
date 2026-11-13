# Epic 7BASFbp: Sign-in through the broker — Complete

**Completed:** 2026-11-12
**Verified by:** Independent review via `/peak-workflow:wrapup-epic 7BASFbp` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

The gateway is its own OAuth 2.1 authorization server — dynamic client registration, PKCE S256, rotating
refresh tokens — delegating sign-in to the organization's OIDC provider (Okta), so a skill user connects
Claude Code with one `claude mcp add --transport http peak-media <G>/mcp` and one browser sign-in, with no
client ID or OpenRouter key to paste. `/mcp` accepts only the gateway's own RS256 access tokens; a 401 points
at `/.well-known/oauth-protected-resource`. The owner-or-admin access rule (`AccessRule`) and the real-flow
test sign-in helper (`TestSignIn` + `StubOidcIssuer`) are in place for every later epic.

## Key Files

| File | Purpose |
|------|---------|
| `src/PeakMediaRemoteMcpServer/Auth/McpAuthSetup.cs` | JwtBearer resource-server gate (gateway key, iss/aud, RS256) + MCP challenge scheme and protected-resource metadata |
| `src/PeakMediaRemoteMcpServer/Auth/Caller.cs`, `AccessRule.cs` | Caller built only from a validated token; the single owner-or-admin access rule |
| `src/PeakMediaRemoteMcpServer/Broker/Endpoints/*.cs` | AS metadata, JWKS, `/register`, `/authorize`, `/oauth/callback`, `/token`, consent cookie |
| `src/PeakMediaRemoteMcpServer/Broker/*.cs` | `JwtIssuer`, `SigningKeyProvider`, `PkceHelper`, `RedirectUriValidator`, `UpstreamOidcClient`, `BrokerJanitor` |
| `src/PeakMediaRemoteMcpServer/Broker/Storage/*.cs` | Stores for users, clients, transactions, codes, refresh tokens, app metadata (secrets hashed) |
| `src/PeakMediaRemoteMcpServer/Storage/SchemaMigrator.cs` | Migration 1: `app_meta`, `users`, `oauth_clients`, `oauth_transactions`, `oauth_codes`, `oauth_refresh_tokens` |
| `src/PeakMediaRemoteMcpServer/Configuration/GatewayStartupValidator.cs` | Refuses to start without sign-in or with incomplete broker / OpenRouter settings |
| `tests/StubAuthorizationServer/StubOidcIssuer.cs` | Stub OIDC provider on real Kestrel |
| `tests/PeakMediaRemoteMcpServer.Tests/TestSignIn.cs` | Test sign-in through the real broker flow — the only way tests get a token |
| `tests/manual/TOR-02-hKpbfCf.md` | Okta administrator checklist and operator-observed procedure |

## Key Decisions

- Gateway access token: RS256 JWT (key persisted in `app_meta`), claims `iss`, `aud`, `sub` (Okta subject — the owner key), `email`, `role` (`admin`|`user`), `jti`; 60 minutes. Later epics use `Caller.From(User)` and `AccessRule.CanAccess` / `RequireAdmin` — nowhere else.
- Refresh tokens are the gateway's own opaque secrets, hashed, rotated on every use, 30 days; refresh does not re-contact Okta, and the role is re-read from `users` (set at sign-in from `Admin:Group`).
- Groups claim name is configurable (`Broker:UpstreamGroupsClaim`) because Okta's federated-claims screen reserves `groups`.
- Open DCR restricted to loopback redirect URIs plus the `Broker:AllowedRedirectUris` allowlist; a client secret is issued even to `none` clients (Claude Code requires one).
- No upstream tokens are stored; identity comes only from the validated ID token (signature, iss, aud, lifetime, nonce).

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-02-8R5ouns | The gateway shall reject an unauthenticated request to /mcp with 401 and a WWW-Authenticate header pointing at its protected-resource metadata | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/ResourceServerGateTests.cs:8 |
| TOR-02-eXSTt0Q | The gateway shall publish protected-resource metadata naming itself as the authorization server | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/ResourceServerGateTests.cs:23 |
| TOR-02-VOsRKOw | The gateway shall publish authorization-server metadata listing its authorize, token and registration endpoints with S256 PKCE | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Broker/DiscoveryAndRegistrationTests.cs:7 |
| TOR-02-Mb6PQo5 | The gateway shall register an MCP client through dynamic client registration without any pre-shared client ID | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Broker/DiscoveryAndRegistrationTests.cs:24 |
| TOR-02-PosyPLa | The gateway shall redirect an authorization request to the organization's OIDC sign-in page | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Broker/AuthorizeAndCallbackTests.cs:8 |
| TOR-02-DU3EQVF | The gateway shall refuse an authorization request that carries no PKCE code challenge | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Broker/AuthorizeAndCallbackTests.cs:27 |
| TOR-02-QFiwMY7 | The gateway shall return an authorization code to the client's redirect URI after a successful sign-in and exchange it for an access token and a refresh token | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Broker/TokenEndpointTests.cs:9 |
| TOR-02-x3ci8yI | The gateway shall refuse a token request whose code verifier does not match the code challenge | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Broker/TokenEndpointTests.cs:42 |
| TOR-02-kx8Nl0i | The gateway shall issue a new access token in exchange for a valid refresh token | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Broker/TokenEndpointTests.cs:61 |
| TOR-02-KmwGCjA | The gateway shall issue no authorization code and no token when the identity provider refuses the sign-in | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/Broker/AuthorizeAndCallbackTests.cs:45 |
| TOR-02-hKpbfCf | The gateway shall complete a real Okta sign-in started by "claude mcp add --transport http" with no client ID to paste | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/manual/TOR-02-hKpbfCf.md (operator-observed) |
| TOR-02-NwWTAqU | The gateway shall reject a request to /mcp whose access token is expired, malformed or not issued by the gateway | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/ResourceServerGateTests.cs:42 |
| TOR-02-GIUxnmc | The gateway shall ignore any identity asserted by a request header or query parameter other than a gateway access token | `docs/requirements/02-sign-in-and-access.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/ResourceServerGateTests.cs:61 |
| TOR-01-p6jE1nT | The gateway shall report in the MCP initialize handshake the same version that GET /version returns | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/SignedInHandshakeTests.cs:8 |

## Requirement Changes

None

## Verification Summary

### Counts
- TOR Requirements: 14/14 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 6/6 PASS (Lint, Build, Tests, Run the tool, Access control, Requirements health; N/A: UX Baseline / Visual — no UI, Removal, Deferred-value)
- Tests: 93 passed, 0 skipped, 0 failed

### Highlights
- ✅ TOR-02-QFiwMY7 / TOR-02-kx8Nl0i — full register → authorize → callback → token → refresh flow opens `/mcp`, in-process and against the real binary on real ports, with no token, code or secret in stderr or the log (TokenEndpointTests.cs:9,61, GatewayBrokerProcessTests.cs:10; TokenEndpoint.cs:44-129)
- ✅ TOR-02-NwWTAqU / TOR-02-GIUxnmc — expired, malformed and foreign-key tokens get 401; `X-User` / `?user=` are ignored; identity comes only from a validated RS256 token (ResourceServerGateTests.cs:42,61; McpAuthSetup.cs:57-77, Caller.cs:9)
- ✅ TOR-02-x3ci8yI — code consumed before any check; PKCE mismatch → `invalid_grant`, no token (TokenEndpointTests.cs:42; TokenEndpoint.cs:55-67)
- ✅ TOR-02-hKpbfCf — operator-observed: "i was never asked for a client id or a openrouter key"; Claude Code `/mcp` shows `peak-media · connected`; gateway log shows `Sign-in completed with role admin.` (22:44–22:47) and no JWT-like strings
- ✅ TOR-01-p6jE1nT — signed-in initialize reports `serverInfo.version` equal to `GET /version` (SignedInHandshakeTests.cs:8; Program.cs:75)

### Conclusion
Every requirement has a test that mirrors its Given/When/Then and passes in an independent run, the implementation was inspected per requirement, and the real Okta round-trip was observed by the operator. The gateway has no anonymous mode, refuses unauthenticated `/mcp` with the correct metadata pointer, and derives identity only from its own signed tokens — sufficient to close this epic.

### Manual verification performed: Yes
Real Okta Integrator org sign-in from Claude Code (admin, non-admin and unassigned users); peak-media connected; never asked for a client ID or an OpenRouter key.

## Known Issues / Follow-ups

- `/token` verifies a client secret only when one is presented — a client registered as `client_secret_post` can redeem without it, and the refresh grant accepts a request with no `client_id`. PKCE and refresh rotation still protect these; consider tightening.
- Refresh does not re-contact Okta: removing a person from the Okta app ends access only when their refresh token expires (up to 30 days). Document in operations notes, or add a revocation path with the admin dashboard.
- `oauth_clients`, `oauth_transactions` and `app_meta` carry no owner column — broker infrastructure that exists before any caller; caller-owned tables carry `subject`.
- A Debug-level log line records the Okta subject (opaque ID, not email); not emitted at the default Information level.
