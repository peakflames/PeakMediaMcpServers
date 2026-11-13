# Epic 7BASFbp: Sign-in through the broker

**Phase:** 1 — Foundation
**Status:** Complete — 2026-11-12
**Dependencies:** Epic Xq463wX (Walking skeleton — running gateway, storage, test harness)
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

---

## Description

Makes sign-in real: the gateway becomes its own OAuth 2.1 authorization server (dynamic client registration, PKCE) and delegates the actual sign-in to the organization's OIDC provider, so a skill user connects Claude Code with one `claude mcp add` command and one browser sign-in, and nobody outside the organization can reach `/mcp`. The broker and the `McpAuth` resource-server gate are ported from BitbucketMcpServers.

This epic also establishes the one access rule (owner-or-permitted-role) that every later tool and page calls, and the test sign-in helper every later test uses — tests sign in through the stub OIDC issuer like any other client; there is no bypass. The real Okta round-trip (`TOR-02-hKpbfCf`) is operator-observed against a free Okta developer org.

The plan for this epic includes a plain checklist for whoever administers Okta: create an OIDC web app, register the redirect URI `<gateway>/oauth/callback` (local and production), assign the people who may sign in, add a `groups` claim to the ID token carrying the admin group, and set the client secret in `Broker__UpstreamClientSecret`.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-02-8R5ouns | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-eXSTt0Q | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-VOsRKOw | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-Mb6PQo5 | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-PosyPLa | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-DU3EQVF | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-QFiwMY7 | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-x3ci8yI | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-kx8Nl0i | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-KmwGCjA | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-hKpbfCf | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-NwWTAqU | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-02-GIUxnmc | `docs/requirements/02-sign-in-and-access.feature.md` |
| TOR-01-p6jE1nT | `docs/requirements/01-service-foundation.feature.md` |

## Key Components

- `src/PeakMediaRemoteMcpServer/Auth/` — `McpAuth` resource-server gate (401 with `WWW-Authenticate`, token validation) and the Broker ported from `~/github/peakflames/BitbucketMcpServers/src/BitbucketRemoteMcpServer/Auth/`: `/.well-known/oauth-protected-resource`, `/.well-known/oauth-authorization-server`, `/register`, `/authorize`, `/token`, `/oauth/callback`, signing key
- Broker option classes holding secrets as `sealed class` (never `record`)
- SQLite tables: users (OIDC subject — the owner key every later table carries — email, last seen) and broker state (clients, sign-in transactions, issued tokens, signing key)
- The access rule (owner-or-permitted-role) in one place, with the `user` and `admin` roles (`admin` = `groups` claim contains `Admin:Group`)
- `tests/StubAuthorizationServer/` — stub OIDC issuer; the test sign-in helper that walks the real broker flow and returns a gateway access token
- Okta administrator checklist in the epic plan; `appsettings.json` `McpAuth` / `Broker` non-secret settings
