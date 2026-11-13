# Epic Xq463wX — Walking skeleton: Implemented

**Implemented:** 2026-11-12 · **By:** Todd Schavey · **Branch:** `feature/epic-Xq463wX-walking-skeleton`

## What Was Built

Both servers run end to end with no domain logic: the HTTP gateway `PeakMediaRemoteMcpServer` (GET `/version` without sign-in, `/mcp` behind a JwtBearer gate that answers 401 until the broker exists, SQLite bootstrap, refusal to start without sign-in config or the OpenRouter key) and the stdio server `PeakMediaMcpServer` (`--version`, exit codes 0/1/2, MCP over stdio with stdout reserved for the protocol). Both share `PeakMediaMcpTools` (version identity, Serilog setup with a UTC-dated daily file, `ToolErrorFormatter`), and come with process-level and in-process tests, `build.py`, and a tag-triggered container publish workflow.

## Deferrals

Count: 0

None

## Key Files

| File | Purpose |
|---|---|
| `PeakMediaMcpServers.sln`, `Directory.Build.props`, `.gitignore`, `.editorconfig` | Solution, single-source `<Version>0.1.0`, net11.0, ignores, code style |
| `src/PeakMediaMcpTools/ServerIdentity.cs` | Name + version from the informational version (used by `--version`, `/version`, startup line, `serverInfo`) |
| `src/PeakMediaMcpTools/LoggingSetup.cs` | Shared Serilog config: format template, stderr-only console, UTC daily file |
| `src/PeakMediaMcpTools/UtcDailyFileSink.cs` | Daily file named `<Name>_<yyyyMMdd>.log` with the UTC date |
| `src/PeakMediaMcpTools/ToolErrorFormatter.cs` | `ERROR: <problem>. <next action>` + credential redaction |
| `src/PeakMediaMcpServer/Program.cs`, `CommandLine.cs`, `OpenRouterKey.cs` | Stdio server: arg parsing, exit codes, key check, MCP stdio host |
| `src/PeakMediaRemoteMcpServer/Program.cs` | Gateway: startup line → validation → `BuildApp` (auth gate, `/version`, `/mcp`) |
| `src/PeakMediaRemoteMcpServer/Configuration/*.cs` | `McpAuthOptions`, `BrokerOptions`, `OpenRouterOptions`, `GatewayStartupValidator` |
| `src/PeakMediaRemoteMcpServer/Storage/*.cs` | `DatabasePathResolver`, `SqliteConnectionFactory`, `SchemaMigrator`, `StorageFaultSwitch`, `StorageOptions` |
| `src/PeakMediaRemoteMcpServer/appsettings*.json`, `Properties/launchSettings.json` | Non-secret settings; dev port 5108 |
| `tests/TestSupport/*` | `ServerProcess` harness, `RepoPaths`, `FreePort`, `TempDirectory`, `LogPatterns`, `ListLogger` |
| `tests/Fakes/FakeOpenRouter.cs` | Canned `audio/mpeg` speech endpoint (first used by the narration epic) |
| `tests/PeakMediaMcpServer.Tests/*`, `tests/PeakMediaRemoteMcpServer.Tests/*`, `tests/xunit.runner.json` | 35 tests |
| `build.py` | build / start / stop / status / run / test / mcp / log |
| `.github/workflows/docker-publish.yml` | `v*` tag → check tag = props version → container publish to Docker Hub |
| `docs/architecture.md`, `CHANGELOG.md`, `README.md` | §4, §5, §8, §9; 0.1.0 Added; quick start |

## Spec Deviations

| TOR ID | As-Written | As-Implemented | Reason |
|---|---|---|---|

No deviations — every anchor TOR is implemented as written.

## Key Decisions

- **Content root = binary directory.** `Program.CreateBuilder` sets `ContentRootPath = AppContext.BaseDirectory`, so `appsettings.json` loads and relative `Storage:DatabasePath` resolves the same way whatever the working directory. In local dev the database is therefore `src/PeakMediaRemoteMcpServer/bin/Debug/net11.0/data/peakmedia.db`; in the container `Storage__DatabasePath=/app/data/peakmedia.db`.
- **UTC-dated log file via a custom sink.** Serilog.Sinks.File's `rollingInterval: Day` rolls on the local clock, so `UtcDailyFileSink` names files with the UTC date (proven by a test run under `TZ=Pacific/Kiritimati`). Timestamps inside records stay local time with offset.
- **Startup line before validation.** The gateway writes `[INF] … starting` before checking its configuration, so a refused start still logs the startup line first, then the `[FTL]` reason, and exits 1 before Kestrel binds.
- **`/mcp` gate.** Stateless Streamable HTTP; JwtBearer with issuer/audience/lifetime/signature validation and an empty trusted-key set — Epic 7BASFbp supplies the broker's key, issuer and discovery documents. GET/DELETE `/mcp` are mapped behind the same gate so an unauthenticated caller always gets 401 (signed-in callers get 405).
- **`tools/list` answers before any tool exists** (`options.ToolCollection ??= []`); tools register from the `PeakMediaMcpTools` assembly in both servers, one partial-class file per tool.
- **Default logging providers cleared** in both hosts (the ASP.NET/Generic Host console logger writes stdout); Serilog is the only provider.
- **Process tests are serialized** (`tests/xunit.runner.json`) because they share each server's `logs/` directory next to the binary.
- **Container logs:** the image runs as the non-root `app` user; if `/app/logs` is not writable the file sink fails silently and stderr still carries every record. Revisit when the container is first deployed.
- **`build.py mcp …`** probes `/mcp` first and prints "requires sign-in (HTTP 401)" plainly until Epic 7BASFbp.

## TOR Coverage (self-assessment)

Implementer's own assessment — not trusted by `/peak-workflow:wrapup-epic`, which re-verifies independently.

| TOR ID | Verdict | Test | Implementation |
|---|---|---|---|
| TOR-01-9TZEFBD | PASS | `tests/PeakMediaRemoteMcpServer.Tests/VersionEndpointTests.cs:5` | `src/PeakMediaRemoteMcpServer/Program.cs:87` |
| TOR-01-zQT2lRH | PASS | `tests/PeakMediaMcpServer.Tests/VersionFlagTests.cs:6` | `src/PeakMediaMcpServer/Program.cs:25` |
| TOR-01-p3K2fH3 | PASS | `tests/PeakMediaMcpServer.Tests/HandshakeTests.cs:6` | `src/PeakMediaMcpServer/Program.cs` (`ServerInfo` from `ServerIdentity`) |
| TOR-01-0wIkgxQ | PASS | `tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:6` | `src/PeakMediaRemoteMcpServer/Program.cs:21` |
| TOR-01-7VjhchC | PASS | `tests/PeakMediaMcpServer.Tests/StartupLogTests.cs:6` | `src/PeakMediaMcpServer/Program.cs:38` |
| TOR-01-VoM1MJ7 | PASS | `tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:45` | `src/PeakMediaMcpTools/LoggingSetup.cs:27` |
| TOR-01-mDo59qy | PASS | `tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:68` | `src/PeakMediaMcpTools/LoggingSetup.cs:28`, `UtcDailyFileSink.cs` |
| TOR-01-6Smkepz | PASS | `tests/PeakMediaMcpServer.Tests/LogFormatTests.cs:6` | `src/PeakMediaMcpTools/LoggingSetup.cs:27-28` |
| TOR-01-spPaZpz | PASS | `tests/PeakMediaMcpServer.Tests/InvalidInvocationTests.cs:6` | `src/PeakMediaMcpServer/Program.cs:19-20`, `CommandLine.cs` |
| TOR-01-6Q14afY | PASS | `tests/PeakMediaMcpServer.Tests/StdoutDisciplineTests.cs:6` | `src/PeakMediaMcpServer/Program.cs:48` (providers cleared), `LoggingSetup.cs:27` |
| TOR-01-9QIaBsB | PASS | `tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:110` | `src/PeakMediaRemoteMcpServer/Program.cs` (`ClearProviders` + Serilog stderr) |
| TOR-01-2lmaDWE | PASS | `tests/PeakMediaRemoteMcpServer.Tests/GatewayStartupRefusalTests.cs:6` | `src/PeakMediaRemoteMcpServer/Configuration/GatewayStartupValidator.cs`, `Program.cs:23-27` |
| TOR-01-EzMqmjt | PASS | `tests/PeakMediaRemoteMcpServer.Tests/GatewayStartupRefusalTests.cs:22` | as above |
| TOR-01-a1AeUH3 | PASS | `tests/PeakMediaRemoteMcpServer.Tests/GatewayStartupRefusalTests.cs:37` | as above |

## Verification Results (self-assessment)

| Gate | Result |
|---|---|
| Lint — `dotnet format PeakMediaMcpServers.sln --verify-no-changes` | PASS (no changes) |
| Build — `.venv/bin/python build.py build` | PASS |
| Tests — `.venv/bin/python build.py test` | PASS — 35 passed (8 stdio, 27 gateway), 0 failed, 0 skipped |
| Run — `build.py start` + `curl /version` | PASS — `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}`; `curl -i /mcp` → 401; `build.py mcp tools` → "requires sign-in (HTTP 401)"; `build.py log --tail 5` shows the startup line; `data/peakmedia.db` created |
| Stdio — `dotnet run --project src/PeakMediaMcpServer -- --version` | PASS — `PeakMediaMcpServer v0.1.0`, exit 0; `--bogus-flag` → exit 2 |
| Refusal by hand — `McpAuth__Enabled=false` | PASS — `[FTL] McpAuth:Enabled is false. …`, exit 1 |
| Container — local `dotnet publish /t:PublishContainer` to an archive | PASS — image `peakflames/peak-media-remote-mcp-server:0.1.0` on `aspnet:11.0`; the Docker Hub push runs on the first `v*` tag |
| Workflow YAML parse | PASS |
| Visual / console (UI only) | N/A — not a UI epic |
