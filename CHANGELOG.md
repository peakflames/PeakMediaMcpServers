# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

### Added
### Changed
### Fixed

---

## [0.1.0] — UNDER DEVELOPMENT

### Added
- Solution skeleton: `PeakMediaRemoteMcpServer` (HTTP gateway), `PeakMediaMcpServer` (stdio server),
  `PeakMediaMcpTools` (shared library), test projects, `build.py`, and a tag-triggered container publish
  to Docker Hub (`peakflames/peak-media-remote-mcp-server`).
- Gateway `GET /version` returning `{name, version}` without sign-in; `/mcp` behind authorization.
- Stdio server `--version` flag (exit 0); invalid invocations exit 2 naming the next action; a missing
  `PEAK_MEDIA_OPENROUTER_API_KEY` exits 1.
- MCP handshake `serverInfo` reports the same name and version on both servers.
- Startup log line `<Name> v<version> starting` as the first record; plain-text log format to stderr and
  a daily file under `logs/` named with the UTC date; nothing written to stdout except the MCP protocol.
- The gateway refuses to start when `McpAuth:Enabled` is false, `Broker__UpstreamClientSecret` is unset,
  or `OpenRouter__ApiKey` is unset.
- SQLite storage bootstrap (`Storage:DatabasePath`, temp-path fallback with a warning, schema versioning).
- Okta sign-in through the gateway's own OAuth 2.1 authorization server (broker): protected-resource and
  authorization-server metadata, dynamic client registration (loopback redirect URIs only, plus an
  allowlist), `/authorize` with PKCE S256, `/oauth/callback` validating the OIDC ID token, `/token` with
  authorization-code and rotating refresh-token grants. Connect Claude Code with
  `claude mcp add --transport http peak-media <gateway>/mcp` and one browser sign-in — no client ID to paste.
- `/mcp` accepts the gateway's RS256 access tokens (`sub`, `email`, `role`); a 401 points at
  `/.well-known/oauth-protected-resource`. The `admin` role comes from the Okta `groups` claim (`Admin:Group`).
- The access rule (owner or admin) in one place, `users` table and schema v1 for sign-in state.
- The gateway refuses to start without `Broker:UpstreamAuthority` / `Broker:UpstreamClientId`, or when
  `McpAuth:Resource` is not `Broker:IssuerUri` + `/mcp`.
- `build.py mcp` accepts a token in `PEAK_MEDIA_ACCESS_TOKEN` or signs in with `--oauth`.
- Okta administrator checklist in the README; stub OIDC issuer and `TestSignIn` helper for tests.
- `docs/architecture.md` and `docs/design-notes.md` describe the as-built walking skeleton, its design
  decisions, and known issues.
