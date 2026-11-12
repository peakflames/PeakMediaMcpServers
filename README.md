# PeakMedia MCP Servers

> An Okta-protected MCP gateway that lets Claude Code skills generate text-to-speech through one shared OpenRouter key — no per-user API key needed.

## Install

Pull the container image (published on each `v*` release tag):

```bash
docker pull peakflames/peak-media-remote-mcp-server
```

## Quick Start

```bash
python build.py start                  # build and start locally on port 5108
curl http://localhost:5108/version     # {"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}
```

Point your MCP client at `http://localhost:5108/mcp` (Streamable HTTP) and sign in with Okta.

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
