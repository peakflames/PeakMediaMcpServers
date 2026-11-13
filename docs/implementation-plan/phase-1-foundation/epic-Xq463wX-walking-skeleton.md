# Epic Xq463wX: Walking skeleton

**Phase:** 1 — Foundation
**Status:** Implemented — 2026-11-12
**Dependencies:** —
**Planned by:** Todd Schavey
**Planned:** 2026-11-12
**Plan branch:** docs/tts-gateway-mvp

---

## Description

Stands both servers up end to end with no domain logic: the solution, the gateway (`PeakMediaRemoteMcpServer`), the stdio server (`PeakMediaMcpServer`), the shared tools library, the test projects, `build.py`, and the tag-triggered container publish. It delivers the tool-hygiene baseline — version exposure, the startup log line, the logging format and sinks, exit codes, stdout/stderr discipline — and the gateway's refusal to start without sign-in configuration or an OpenRouter key, so an operator can trust a deployment at a glance.

This epic establishes the architectural pattern every later slice follows: how a request reaches an MCP tool (one partial-class file per tool in `PeakMediaMcpTools`), how the gateway reaches SQLite, how errors are formatted, and where tests and fakes live. `docs/architecture.md` records that pattern once the skeleton is complete.

The gateway maps `/mcp` behind the authorization requirement from the start, with no anonymous mode and no development login: until Epic 7BASFbp adds the broker, every `/mcp` request is rejected. The dashboard's design system, app shell and reference screens are deliberately **not** in this epic — the owner chose to build the dashboard last, so Epics Ail1fOL and EwgA3Zf own them. `CLAUDE.md` holds no `TBD — set by the walking-skeleton epic` values to resolve.

## Requirements Anchors

> The TOR requirement IDs listed below are the acceptance criteria and verification baseline for
> this epic. Each ID maps to a Gherkin scenario in the referenced feature file, which is the only
> place its title and text live.
> `/peak-workflow:start-epic` reads each TOR's Given/When/Then to drive implementation and tests.
> `/peak-workflow:wrapup-epic` independently verifies each TOR's Given/When/Then is satisfied.

| TOR ID | Feature File |
|--------|--------------|
| TOR-01-9TZEFBD | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-zQT2lRH | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-p3K2fH3 | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-0wIkgxQ | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-7VjhchC | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-VoM1MJ7 | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-mDo59qy | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-6Smkepz | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-spPaZpz | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-6Q14afY | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-9QIaBsB | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-2lmaDWE | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-EzMqmjt | `docs/requirements/01-service-foundation.feature.md` |
| TOR-01-a1AeUH3 | `docs/requirements/01-service-foundation.feature.md` |

## Key Components

- `PeakMediaMcpServers.sln`, `Directory.Build.props` (`<Version>` — the single version source), `.gitignore` (.NET, `.venv/`, `data/`, `logs/`, `publish/`)
- `src/PeakMediaRemoteMcpServer/` — `Program.cs` (Serilog to stderr and a daily file under `logs/`, first log line with name and version, `GET /version`, start-up validation of `McpAuth:Enabled`, `Broker__UpstreamClientSecret` and `OpenRouter__ApiKey`, `/mcp` mapped behind the authorization requirement), `appsettings.json` (non-secret values only), `GlobalUsings.cs`
- `src/PeakMediaMcpServer/` — `Program.cs` (`--version`, exit code 2 on an invalid flag, MCP stdio transport, Serilog to stderr and file only — stdout carries MCP alone)
- `src/PeakMediaMcpTools/` — empty tool library with `ToolErrorFormatter` and the FluentResults conventions
- `src/PeakMediaRemoteMcpServer/Storage/` — SQLite bootstrap at `Storage:DatabasePath` with the temp-path fallback and its loud warning
- `tests/` — xUnit projects using `Microsoft.AspNetCore.Mvc.Testing`; `tests/Fakes/` fake OpenRouter scaffold; a process-level harness that runs the stdio binary and asserts stdout, stderr and exit code
- **Test-only fault / latency switch** — an environment variable read at start-up that makes the data source fail or delay, ignored when `ASPNETCORE_ENVIRONMENT=Production`; **test-only data reset** — each test points `Storage:DatabasePath` at a fresh temp directory
- `build.py` (build / start / stop / status / run / test / mcp / log), adapted from BitbucketMcpServers
- `.github/workflows/` — container publish to `peakflames/peak-media-remote-mcp-server` on `v*` tags (Release Protocol)
- `docs/architecture.md` — records the pattern above
