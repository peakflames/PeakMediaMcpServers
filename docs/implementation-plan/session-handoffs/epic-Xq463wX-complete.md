# Epic Xq463wX: Walking skeleton — Complete

**Completed:** 2026-11-12
**Verified by:** Independent review via `/peak-workflow:wrapup-epic Xq463wX` (model: claude-opus-5-5, fresh session)

## Deferrals
Count: 0 (undisclosed: 0, waived: 0, fixed at wrapup: 0, amended: 0, pending: 0)

None

## What Was Built

Both servers run end to end with no domain logic: the HTTP gateway `PeakMediaRemoteMcpServer` answers `GET /version` without sign-in, gates `/mcp` behind a bearer-token requirement (401 until the broker epic supplies signing keys), bootstraps SQLite, and refuses to start without sign-in configuration or the shared OpenRouter key; the stdio server `PeakMediaMcpServer` prints `--version`, uses exit codes 0/1/2, and serves MCP over stdio with stdout reserved for the protocol. Both log in one plain-text format to stderr and a UTC-dated daily file, report the single `Directory.Build.props` version everywhere, and ship with process-level tests, `build.py`, and a tag-triggered container publish workflow.

## Key Files

| File | Purpose |
|------|---------|
| `Directory.Build.props` | Single version source (`0.1.0`), `net11.0` |
| `src/PeakMediaMcpTools/ServerIdentity.cs` | Name + version used by `--version`, `/version`, startup line, `serverInfo` |
| `src/PeakMediaMcpTools/LoggingSetup.cs`, `UtcDailyFileSink.cs` | Shared Serilog setup: stderr-only console, UTC-dated daily file under `logs/` |
| `src/PeakMediaMcpTools/ToolErrorFormatter.cs` | `ERROR: <problem>. <next action>` with credential redaction |
| `src/PeakMediaMcpServer/Program.cs`, `CommandLine.cs`, `OpenRouterKey.cs` | Stdio server: arg parsing, exit codes, key check, MCP stdio host |
| `src/PeakMediaRemoteMcpServer/Program.cs` | Gateway: startup line → validation → `BuildApp` (auth gate, `/version`, `/mcp`) |
| `src/PeakMediaRemoteMcpServer/Configuration/GatewayStartupValidator.cs` | Refuses to start without McpAuth, broker secret, or OpenRouter key |
| `src/PeakMediaRemoteMcpServer/Storage/*.cs` | SQLite path resolution with temp fallback, migrator, test-only fault switch |
| `tests/TestSupport/ServerProcess.cs` | Process harness: real binaries, separate stdout/stderr capture, scrubbed env |
| `build.py`, `.github/workflows/docker-publish.yml` | Build helper; `v*` tag → container publish to Docker Hub |

## Key Decisions

- Content root is the binary directory, so `appsettings.json` and relative `Storage:DatabasePath` resolve the same from any working directory.
- Log file names carry the UTC date via a custom sink (Serilog's `rollingInterval: Day` uses local time); record timestamps stay local with offset.
- The gateway writes its startup line before validating configuration, so a refused start logs the startup line, then the `[FTL]` reason, and exits 1 before Kestrel binds.
- `/mcp` uses JwtBearer with an empty trusted-key set until Epic 7BASFbp; GET/DELETE `/mcp` sit behind the same gate so unauthenticated callers always get 401.
- Default logging providers are cleared in both hosts; Serilog is the only provider.
- Process-level tests are serialized because they share each server's `logs/` directory.

## Requirements Implemented

| TOR ID | Title | Feature File | Verdict | Test Reference |
|--------|-------|--------------|---------|----------------|
| TOR-01-9TZEFBD | The gateway shall expose its name and semantic version at GET /version as JSON {"name", "version"} without requiring sign-in | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/VersionEndpointTests.cs:5 |
| TOR-01-zQT2lRH | The stdio server shall print its name and semantic version to standard output when invoked with --version, exiting with code 0 | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/VersionFlagTests.cs:6 |
| TOR-01-p3K2fH3 | The stdio server shall report in the MCP initialize handshake the same version that --version prints | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/HandshakeTests.cs:6 |
| TOR-01-0wIkgxQ | The gateway shall emit, as its first log record on startup, an Information record containing its name and semantic version | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:6 |
| TOR-01-7VjhchC | The stdio server shall emit, as its first log record on startup, an Information record containing its name and semantic version | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/StartupLogTests.cs:6 |
| TOR-01-VoM1MJ7 | The gateway shall write every log record as plain text in the form "<timestamp with milliseconds and UTC offset> [<three-letter level>] <message>" | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:45 |
| TOR-01-mDo59qy | The gateway shall write its log records to a daily rolling file under 'logs/' next to the binary as well as to standard error | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:68 |
| TOR-01-6Smkepz | The stdio server shall write every log record as plain text in the form "<timestamp with milliseconds and UTC offset> [<three-letter level>] <message>" to standard error and to a daily rolling file under 'logs/' | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/LogFormatTests.cs:6 |
| TOR-01-spPaZpz | The stdio server shall exit with code 2 on an invalid invocation and name the next action on standard error | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/InvalidInvocationTests.cs:6 |
| TOR-01-6Q14afY | The stdio server shall write only MCP protocol messages to standard output and shall write every log and diagnostic line to standard error | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaMcpServer.Tests/StdoutDisciplineTests.cs:6 |
| TOR-01-9QIaBsB | The gateway shall write nothing to standard output | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:110 |
| TOR-01-2lmaDWE | The gateway shall refuse to start when McpAuth:Enabled is false | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/GatewayStartupRefusalTests.cs:6 |
| TOR-01-EzMqmjt | The gateway shall refuse to start when the upstream sign-in provider is not configured | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/GatewayStartupRefusalTests.cs:22 |
| TOR-01-a1AeUH3 | The gateway shall refuse to start when the OpenRouter key is not set | `docs/requirements/01-service-foundation.feature.md` | PASS | tests/PeakMediaRemoteMcpServer.Tests/GatewayStartupRefusalTests.cs:37 |

## Requirement Changes

None

## Verification Summary

### Counts
- TOR Requirements: 14/14 PASS, 0 FAIL, 0 CANNOT VERIFY (0 waived, 0 fixed at wrapup, 0 amended, 0 pending)
- Quality Gates: 7/7 PASS (N/A: UX Baseline — no UI; Visual/console — no UI; Access control beyond sign-in bypass — no user data)
- Tests: 35 passed, 0 skipped, 0 failed

### Highlights
- ✅ TOR-01-9TZEFBD / TOR-01-zQT2lRH / TOR-01-p3K2fH3 — one version everywhere: `/version` unauthenticated → `0.1.0`, `--version` → `PeakMediaMcpServer v0.1.0` exit 0, handshake `serverInfo.version` equals the flag (src/PeakMediaMcpTools/ServerIdentity.cs:10)
- ✅ TOR-01-2lmaDWE / TOR-01-EzMqmjt / TOR-01-a1AeUH3 — startup refusals tested as real processes: exact stderr message, non-zero exit, `/version` never answers (src/PeakMediaRemoteMcpServer/Configuration/GatewayStartupValidator.cs:19, Program.cs:23)
- ✅ TOR-01-mDo59qy — log file named by UTC date, proven under `TZ=Pacific/Kiritimati` (tests/PeakMediaRemoteMcpServer.Tests/GatewayLoggingProcessTests.cs:68, src/PeakMediaMcpTools/UtcDailyFileSink.cs:27)
- ✅ TOR-01-6Q14afY / TOR-01-9QIaBsB — stdout discipline: default providers cleared, Serilog console sends every level to stderr (src/PeakMediaMcpTools/LoggingSetup.cs:27)
- ✅ No sign-in bypass — `/mcp` GET and POST → 401; only `/version` is anonymous; no test secrets in log files

### Conclusion
Every requirement is traced by a test that mirrors its Given/When/Then against the real built binaries, all pass, and source inspection confirms each behavior. All gates reproduce independently with no discrepancy against the implementer's self-assessment.

### Manual verification performed: Yes
Ran the happy path by hand (terminal screenshot): `build.py start` → `curl /version` returned `{"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}`; `curl -i /mcp` → `401 Unauthorized` with `WWW-Authenticate: Bearer`; `build.py log --tail 5` showed `[INF] PeakMediaRemoteMcpServer v0.1.0 starting`; with `OpenRouter__ApiKey` unset, `build.py run` logged `[FTL] OpenRouter__ApiKey is not set. …` and exited; `dotnet run --project src/PeakMediaMcpServer -- --version` printed `PeakMediaMcpServer v0.1.0`.

## Known Issues / Follow-ups

- `PrepareStorage` logs an error and keeps starting when the schema migration fails — decide whether to refuse start once real tables exist.
- Container runs as non-root `app`; if `/app/logs` is not writable the file sink fails silently (stderr still carries every record) — check on first deployment.
- The startup-log test reads the shared daily file's first record, which may come from an earlier run that day; still valid because every run's first record is the startup line.
