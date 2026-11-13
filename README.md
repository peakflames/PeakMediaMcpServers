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

```bash
export OpenRouter__ApiKey=...                   # never commit these
export Broker__UpstreamClientSecret=...
.venv/bin/python build.py start                 # build and start locally on port 5108
curl http://localhost:5108/version              # {"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}
.venv/bin/python build.py test                  # run the test suite
.venv/bin/python build.py stop
```

Point your MCP client at `http://localhost:5108/mcp` (Streamable HTTP) and sign in with Okta
(sign-in arrives in a later release; until then `/mcp` answers 401).

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
