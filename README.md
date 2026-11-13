# PeakMedia MCP Servers

> An Okta-protected MCP gateway that lets Claude Code skills generate text-to-speech through one shared OpenRouter key — no per-user API key needed.

## Install

Pull the container image (published on each `v*` release tag):

```bash
docker pull peakflames/peak-media-remote-mcp-server
```

## Quick Start

Prerequisites: .NET 11 SDK, Python 3.11+, and the project venv
(`python3 -m venv .venv && .venv/bin/pip install psutil fastmcp`).

The gateway refuses to start without its secrets, which arrive only through the environment:

| Variable | Purpose |
|---|---|
| `OpenRouter__ApiKey` | The organization's shared OpenRouter key |
| `Broker__UpstreamClientSecret` | OIDC client secret of the organization's Okta sign-in app |

Non-secret settings (environment or `appsettings.json`):

| Setting | Purpose |
|---|---|
| `Broker__UpstreamAuthority` | The Okta org URL, e.g. `https://<org>.okta.com` |
| `Broker__UpstreamClientId` | Client ID of the Okta sign-in app |
| `Broker__IssuerUri` | The gateway's public URL (default `http://localhost:5108`) |
| `McpAuth__Resource` | `<Broker__IssuerUri>/mcp` (default `http://localhost:5108/mcp`) |
| `Admin__Group` | Okta group whose members get the `admin` role (empty: nobody) |
| `Broker__UpstreamGroupsClaim` | ID-token claim holding group names (default `groups`) |
| `OpenRouter__ClipTimeout` | How long one OpenRouter request may take before it is a timeout (default `00:02:00`) |
| `OpenRouter__RetryDelayMs` | Wait before the one retry of a timed-out, unreachable or 5xx clip (default `1000`) |
| `Storage__CacheMaxGb` | Audio cache limit in GB; least-recently-used clips are evicted above it (default `10`, must be > 0) |
| `Storage__EvictionIntervalMinutes` | Minutes between cache eviction passes (default `10`) |

```bash
export OpenRouter__ApiKey=...                   # never commit these
export Broker__UpstreamClientSecret=...
export Broker__UpstreamAuthority=https://<org>.okta.com Broker__UpstreamClientId=...
.venv/bin/python build.py start                 # build and start locally on port 5108
curl http://localhost:5108/version              # {"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}
.venv/bin/python build.py test                  # run the test suite
.venv/bin/python build.py stop
```

Connect Claude Code with one command and sign in once in the browser — no client ID or OpenRouter
key to paste (the gateway registers Claude Code itself through dynamic client registration):

```bash
claude mcp add --transport http peak-media http://localhost:5108/mcp
# then in Claude Code: /mcp → peak-media → Authenticate → sign in on the Okta page
```

### Okta setup

One-time setup by an Okta administrator:

1. Admin Console → **Applications → Create App Integration** → **OIDC** → **Web Application**;
   grant type **Authorization Code** only.
2. Sign-in redirect URIs: `http://localhost:5108/oauth/callback` (local) and
   `https://<gateway-host>/oauth/callback` (production). No sign-out URI is needed.
3. **Assignments:** limit access to selected groups or people — anyone else is refused (`access_denied`).
4. **Directory → Groups:** create the admin group (e.g. `PeakMedia Admins`); set `Admin__Group` to its exact name.
5. App → **Sign On** → **Token claims (OIDC)** → **Add expression**: Name `peakmedia_groups` (Okta
   reserves `groups`), Expression `user.getGroups({'group.profile.name': 'org.peakmedia'}).![profile.name]`
   (name filter is *starts with*); set `Broker__UpstreamGroupsClaim=peakmedia_groups`.
   Legacy screen: OpenID Connect ID Token → **Groups claim type: Filter**, claim `groups`, regex `.*`.
6. Client ID → `Broker__UpstreamClientId`; client secret → `Broker__UpstreamClientSecret` (environment
   only); org URL → `Broker__UpstreamAuthority`.
7. Start the gateway and connect Claude Code as above.

Removing a person from the Okta app stops new sign-ins at once; an existing session ends when its
refresh token expires (`Broker:IssuedRefreshTokenLifetimeDays`, default 30).

### Stdio server (your own OpenRouter key)

```bash
export PEAK_MEDIA_OPENROUTER_API_KEY=...
dotnet run --project src/PeakMediaMcpServer -- --version   # PeakMediaMcpServer v0.1.0
dotnet run --project src/PeakMediaMcpServer                # serve MCP over stdio
```

## Documentation

- [Architecture](docs/architecture.md)
- [Design Notes](docs/design-notes.md)
- [Requirements](docs/requirements/) — TOR requirements baseline
- [Implementation Plan](docs/implementation-plan/) — epic registry; run `/peak-workflow:status` for the dashboard

## Development

See [CLAUDE.md](CLAUDE.md) for the project's development workflow conventions and
[CONTRIBUTING.md](CONTRIBUTING.md) (if present) for contributor guidelines.

## License

See [LICENSE](LICENSE).
