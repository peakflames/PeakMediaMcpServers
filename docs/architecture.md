# PeakMedia MCP Servers — Architecture Document

> **Status:** Describes the as-built system through the walking-skeleton epic (Xq463wX, v0.1.0).
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
| Tests | xUnit, ASP.NET TestHost | Fake OpenRouter + stub JWT issuer |
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

Every `/mcp` request goes through the JwtBearer resource-server gate (`RequireAuthorization`).
Until Epic 7BASFbp adds the broker (the gateway's own authorization server, which supplies the
signing key, issuer and OAuth discovery documents), **no** signing key is trusted, so every `/mcp`
request is answered 401. There is no anonymous mode and no development login.

Tool inventory: none yet. Tools arrive with the narration epic (see §5).

*(OAuth protected-resource metadata — Epic 7BASFbp.)*

---

## 5. Backend Architecture

**Project layout**

```
PeakMediaMcpServers.sln
Directory.Build.props              <Version> (single source of truth), net11.0, nullable, implicit usings
src/PeakMediaMcpTools/             shared library: MCP tools, ToolErrorFormatter, ServerIdentity, LoggingSetup, UtcDailyFileSink
src/PeakMediaMcpServer/            stdio console server (user's own OpenRouter key, no database)
src/PeakMediaRemoteMcpServer/      HTTP gateway (ASP.NET): Program.cs, Configuration/, Storage/
tests/PeakMediaMcpServer.Tests/    stdio tests (process-level)
tests/PeakMediaRemoteMcpServer.Tests/  gateway tests (in-process + process-level)
tests/TestSupport/                 ServerProcess harness, RepoPaths, FreePort, TempDirectory, LogPatterns, ListLogger
tests/Fakes/                       FakeOpenRouter (canned audio/mpeg at POST /api/v1/audio/speech)
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
   set, `OpenRouter:ApiKey` set. Failure → `[FTL] <message naming the next action>`, exit 1, before
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
the append-only `Migrations` list (empty today). A migration failure logs an error and startup
continues (Known Issue in design-notes §17). Every table that holds a caller's records carries the
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
- Fakes: `FakeOpenRouter`; the stub JWT issuer arrives with Epic 7BASFbp. Tests never call real
  OpenRouter or Okta.

---

## 6. Frontend Architecture

N/A — no user interface

---

## 7. Background Services

None yet. No background services exist in the walking skeleton; cache eviction or a usage-record
janitor would be added with the epics that need them.

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
  gateway refuses to start without them), `McpAuth__*`, `Broker__*` non-secret settings.

---

## 9. Security & Access

- The gateway refuses to start when `McpAuth:Enabled` is false, or when `Broker__UpstreamClientSecret`
  or `OpenRouter__ApiKey` is unset.
- `/mcp` requires an authenticated caller (JwtBearer: issuer, audience = `McpAuth:Resource`, lifetime,
  signed token). Until Epic 7BASFbp no signing key is trusted, so nothing passes.
- Secrets arrive only through environment variables; `appsettings*.json` holds non-secret values.
  Secret-holding option classes (`BrokerOptions`, `OpenRouterOptions`) are `sealed class`, never records.
- *(Okta sign-in through the broker and the per-user access rule — Epics 7BASFbp and later.)*
