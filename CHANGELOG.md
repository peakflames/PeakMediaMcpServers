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
- Gateway `GET /version` returning `{name, version}` without sign-in; `/mcp` behind authorization
  (every request answered 401 until sign-in arrives).
- Stdio server `--version` flag (exit 0); invalid invocations exit 2 naming the next action; a missing
  `PEAK_MEDIA_OPENROUTER_API_KEY` exits 1.
- MCP handshake `serverInfo` reports the same name and version on both servers.
- Startup log line `<Name> v<version> starting` as the first record; plain-text log format to stderr and
  a daily file under `logs/` named with the UTC date; nothing written to stdout except the MCP protocol.
- The gateway refuses to start when `McpAuth:Enabled` is false, `Broker__UpstreamClientSecret` is unset,
  or `OpenRouter__ApiKey` is unset.
- SQLite storage bootstrap (`Storage:DatabasePath`, temp-path fallback with a warning, schema versioning).
