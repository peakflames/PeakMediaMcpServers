# PeakMedia MCP Servers — Design Decision Notes

> **Status:** Decisions as built through the walking-skeleton epic (Xq463wX, v0.1.0). Decisions
> from later epics are added by `/peak-workflow:refresh-docs` from their session handoffs.

---

These notes capture design decisions and rationale that complement the Architecture Document.

---

## 1. Follow the BitbucketMcpServers Structure

**Decision:** Same solution shape (stdio console app, ASP.NET HTTP app, shared tools library,
xUnit tests with fakes), `build.py` helper, Serilog setup, C# conventions, and tag-triggered
container publish as `~/github/peakflames/BitbucketMcpServers`.

**Rationale:** One house style across the owner's MCP servers; proven patterns (McpAuth gate, stub
authorization server, fake upstream) carry over directly.

## 2. Hybrid: Shared-Key Gateway + Bring-Your-Own-Key Stdio

**Decision:** The HTTP gateway holds the shared OpenRouter key and requires Okta sign-in. The stdio
server reads the user's own `PEAK_MEDIA_OPENROUTER_API_KEY` (legacy `TTS_OPENROUTER_API_KEY`) and
keeps no database.

**Rationale:** The gateway's purpose is that users no longer need an OpenRouter key; a stdio server
on a user's machine cannot hide a shared key, so it only serves users who bring their own. The
variable name matches what the peak-media skills already read.

## 3. Okta Sign-In Required — No Anonymous Mode

**Decision:** `McpAuth` with Okta as issuer, org-only tenant. Two roles: `admin` (OIDC `groups` claim
contains `Admin:Group`) and `user`. Roles gate only the usage dashboard; every MCP tool behaves the
same for every signed-in caller. The gateway refuses to start with `McpAuth:Enabled` false
(Bitbucket allows it; this project does not).

**Rationale:** An unauthenticated gateway spends the shared OpenRouter credit for anyone who can
reach it, and per-user usage and quotas need a real caller identity. Admin-only views (all users'
usage, quota edits, cache statistics) need a role; tools do not, because limits are per-user quotas.

## 4. SQLite From Day One (Gateway Only)

**Decision:** `Storage:DatabasePath` (default `data/peakmedia.db`) holds a per-user usage log,
per-user quotas, and an audio cache. Unwritable directory → temp-path fallback with a loud warning,
as in Bitbucket's Broker.

**Rationale:** Accountability for shared credit (who spent what), cost control (quotas), and
avoiding paying twice for identical narration (cache).

## 5. .NET 11

**Decision:** Target `net11.0`.

**Rationale:** Owner's choice — the newest release; .NET 9 (Bitbucket's target) left support on 2026-11-10.

## 6. Single Version Source

**Decision:** One `<Version>` in `Directory.Build.props`; container tag derived from it.

**Rationale:** Bitbucket bumps the version in several csproj properties by hand; one source removes drift.

## 7. Image Generation Deferred

**Decision:** Text-to-speech first. Image generation through the same gateway is a growth note,
reflected only in the `PeakMedia` naming.

**Rationale:** The project is named `PeakMedia` and the layout leaves room for it, so a later epic can
add image generation behind the same gateway without renaming anything. Building it now would add
scope the text-to-speech epics have not yet proven.

## 8. Content Root Is the Binary Directory

**Decision:** `Program.CreateBuilder` sets `ContentRootPath` to `AppContext.BaseDirectory`. `appsettings.json`
loads, and a relative `Storage:DatabasePath` resolves, the same way from any working directory.

**Rationale:** Running from a different directory (IDE, `build.py`, a container) must not silently
create a second database or miss configuration. In local dev the database is
`src/PeakMediaRemoteMcpServer/bin/Debug/net11.0/data/peakmedia.db`; in the container it is
`/app/data/peakmedia.db` via `Storage__DatabasePath`.

## 9. UTC-Dated Daily Log File

**Decision:** `UtcDailyFileSink` names each log file `<Name>_<yyyyMMdd>.log` using the UTC date. Record
timestamps stay local time with offset.

**Rationale:** Serilog.Sinks.File's `rollingInterval: Day` rolls on the local clock, so a host in a
different timezone would split or merge a day's records inconsistently across servers. The sink is
verified by a test run under `TZ=Pacific/Kiritimati`.

## 10. Startup Record Before Configuration Validation

**Decision:** The gateway writes `[INF] PeakMediaRemoteMcpServer v<ver> starting` before
`GatewayStartupValidator` checks configuration. A refused start logs the startup line, then the
`[FTL]` reason, and exits 1 before Kestrel binds.

**Rationale:** Every run's first log record is the startup line, including refused starts, so an
operator reading the log always sees which version ran and why it stopped.

## 11. Bearer Gate on `/mcp` Before the Broker Exists

**Decision:** `/mcp` uses JwtBearer with issuer, audience, lifetime and signature validation and an
**empty** trusted-key set. Every bearer token is rejected until the broker epic (7BASFbp) supplies
signing keys, issuer and discovery documents. `GET` and `DELETE` `/mcp` sit behind the same gate, so an
unauthenticated caller always gets 401; a signed-in caller gets 405, which the stateless Streamable HTTP
transport requires (it has no GET stream).

**Rationale:** The gate is in place from the first epic, so no request can reach a tool without
passing it. Mapping GET and DELETE behind the gate prevents routing from being revealed before sign-in.

## 12. Tool Registration and `tools/list` Before Any Tool

**Decision:** Both servers register tools with `WithToolsFromAssembly(typeof(ToolErrorFormatter).Assembly)`,
one partial-class file per tool. `options.ToolCollection ??= []` makes `tools/list` answer, empty, before
the first tool exists.

**Rationale:** A new tool file is picked up by both servers without wiring changes, and clients never see
a protocol error from listing tools on a fresh build.

## 13. Default Logging Providers Cleared

**Decision:** Both hosts call `builder.Logging.ClearProviders()` before adding Serilog. Serilog is the only
log provider.

**Rationale:** The default console logger writes to stdout. On the stdio server stdout belongs to the MCP
protocol; on the gateway, the rule is that nothing is written to stdout. Every record reaches stderr and the
daily file through one configuration.

## 14. Process-Level Tests Run Serially

**Decision:** Process-level tests are serialized (`tests/xunit.runner.json`). They run the real built binaries
(`ServerProcess`, scrubbed environment, separate stdout and stderr capture).

**Rationale:** Each server writes its daily log under `logs/` next to its binary, and parallel tests would
interleave those files. In-process tests (`GatewayTestHost`) stay separate because they use a fresh temp
database per test.

## 15. `build.py mcp` Probes `/mcp` First

**Decision:** `build.py mcp <command>` probes `/mcp` before running any command and prints
"requires sign-in (HTTP 401)" plainly until the broker epic lands.

**Rationale:** Until sign-in exists, the MCP commands cannot succeed. A plain message names the cause instead
of a protocol error.

## 16. Container Runs as Non-Root

**Decision:** The published image runs as the non-root `app` user.

**Rationale:** The handoff does not record a deliberate choice; this is the SDK container default, and it
limits what a compromised process can write. The trade-off is in Known Issues (log directory writability).

---

## 17. Known Issues and Deferred Work

- **Migration failure does not stop startup.** `PrepareStorage` logs an error and keeps starting when the
  schema migration fails. Decide whether to refuse to start once real tables exist.
- **Log directory must be writable in the container.** If `/app/logs` is not writable, the file sink fails
  silently; stderr still carries every record. Check on first deployment.
- **Startup-log test reads a shared file.** The startup-log test reads the first record of the shared daily
  file, which may come from an earlier run that day. It stays valid because every run's first record is the
  startup line.
- **Caller sign-in is not built.** `/mcp` answers 401 to every caller until the broker epic (7BASFbp) lands.
  Okta round-trip verification is manual against a free Okta developer org.
