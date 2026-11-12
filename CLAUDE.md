# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

PeakMedia MCP Servers is a C# MCP gateway that lets Claude Code skills such as `/web2video` and
`/plate-factory` (peak-media plugin) generate text-to-speech through one shared, server-held
OpenRouter key, so individual users no longer need their own. Callers sign in through the
organization's Okta tenant; the gateway logs each caller's usage, enforces per-user quotas, and
caches generated audio. A stdio variant serves single users who bring their own OpenRouter key.
Image generation is a planned later addition; the project layout and names leave room for it.

## Tech Stack

Named by the owner: C# / .NET, following the project structure of
`~/github/peakflames/BitbucketMcpServers`. No reference stack sheet applies (named platform —
toolchain-table row "C# / .NET", with conventions taken from the Bitbucket repo).

**Product shape:** (questions asked by `/peak-workflow:setup` before the stack was chosen)

- **Q1 Same information on another device:** not asked (Hybrid — service + stdio)
- **Q2 Sign-in, or others see the data:** yes — callers must identify themselves before spending the shared OpenRouter credit ("McpAuth with Okta like the bitbucket one")
  - **Sign-in:** Okta (named provider) — org-only (the organization's Okta tenant)
  - **Roles:** no — every signed-in caller can use every tool; limits are per-user quotas, not roles
  - **Access rule:** owner-or-permitted-role
- **Q3 File attachments:** no
- **Q4 Updates on screen without the person acting:** yes — live progress updates while long narration renders (MCP progress notifications)
- **Q5 Product holds a secret of its own:** yes — implied by Q2 (and it holds the shared OpenRouter key)
- **Growth notes:** image generation through the same gateway — later, not built now

| Layer | Choice |
|---|---|
| Language / runtime | C# on .NET 11 (`net11.0`) — requires the .NET 11 SDK |
| Solution | `PeakMediaMcpServers.sln` |
| HTTP server | `src/PeakMediaRemoteMcpServer/` — ASP.NET (`Microsoft.NET.Sdk.Web`), MCP Streamable HTTP at `/mcp`, dev port 5108 |
| Stdio server | `src/PeakMediaMcpServer/` — console app, MCP stdio transport, user's own OpenRouter key, no database |
| Shared tools | `src/PeakMediaMcpTools/` — MCP tools + OpenRouter client |
| MCP SDK | `ModelContextProtocol`, `ModelContextProtocol.AspNetCore` |
| Caller auth | `McpAuth` OAuth 2.1 resource-server gate (`Microsoft.AspNetCore.Authentication.JwtBearer`), Okta as issuer, org-only |
| Upstream | OpenRouter (`POST https://openrouter.ai/api/v1/audio/speech`), shared key from env var `OpenRouter__ApiKey` (gateway) / `PEAK_MEDIA_OPENROUTER_API_KEY` (stdio) |
| Database | SQLite via `Microsoft.Data.Sqlite`, gateway only — `Storage:DatabasePath` default `data/peakmedia.db`: per-user usage log, per-user quotas, audio cache |
| Error handling | FluentResults |
| Logging | Serilog — plain text to a daily file and stderr |
| Tests | xUnit, `Microsoft.AspNetCore.Mvc.Testing` / `TestHost`; `tests/Fakes/` fake OpenRouter (canned audio); stub JWT issuer in place of Okta |
| Build helper | `build.py` (Python) adapted from Bitbucket; runs from the project venv `.venv/` holding `psutil` + `fastmcp` |
| Container | .NET SDK container publish → Docker Hub `peakflames/peak-media-remote-mcp-server` |
| CI | GitHub Actions on `v*` tags: publish container (same as Bitbucket); no pull-request CI |

## Local Environment

Run `build.py` from the project venv: `source .venv/bin/activate` once per terminal (or call
`.venv/bin/python build.py ...`). Claude Code's shell does not keep activation between commands —
always use `.venv/bin/python build.py ...` there.

```bash
python build.py build    # Build solution (auto-stops running app)
python build.py start    # Build and start in background (port 5108)
python build.py stop     # Stop background application
python build.py status   # Check if application is running
python build.py run      # Run in foreground
python build.py test     # Run the test suite (dotnet test)
python build.py mcp ping|info|tools|call <tool> '<json>'   # Talk to the running server
python build.py log [pattern] [--tail n] [--level error]   # Search the log file
```

Alternatives: `dotnet build PeakMediaMcpServers.sln`, `dotnet test`,
`dotnet run --project src/PeakMediaRemoteMcpServer`.

Prerequisites: .NET 11 SDK (`brew install --cask dotnet-sdk`), Python 3.11+, and the project venv:
`python3 -m venv .venv && .venv/bin/pip install psutil fastmcp` (Homebrew's Python refuses global
`pip install`; brew's `fastmcp` formula is the CLI only and cannot be imported, and `psutils` is unrelated).
Local runs need `OpenRouter__ApiKey` and the `McpAuth` settings in the environment (see Security Baseline).

Verification runs against the real, running project with its real local data. Never mock the
project's own API or database to make a check pass. (The OpenRouter upstream and the Okta issuer
are external — tests use `tests/Fakes/` and the stub JWT issuer; the real round-trip is checked by
hand against the real services.)

## Tool Hygiene & Operability

This section declares the project's conventions for the load-bearing tool-hygiene practices.
Each line is a baseline TOR requirement source — `/peak-workflow:capture-requirements` will
ensure at least one TOR exists per active line, written in the form appropriate to the
declared mechanism. Lines marked `N/A` are skipped. Project type and Version single source
of truth are declarations, not TOR sources.

**Project type:** Hybrid — Service or API (`PeakMediaRemoteMcpServer`, HTTP) + CLI tool (`PeakMediaMcpServer`, stdio console app)

**Version exposure:**
- HTTP server: GET `/version` returning JSON `{name, version}`, reachable without signing in; the MCP initialize handshake's `serverInfo.version` carries the same value.
- Stdio server: `--version` flag printing `PeakMediaMcpServer v<semver>` to stdout, exit 0; the MCP handshake's `serverInfo.version` carries the same value.

**Version stamped at log startup:** The first log line emitted on process startup includes the
tool name and semantic version, in the Logging format below:
`[INF] PeakMediaRemoteMcpServer v0.1.0 starting` / `[INF] PeakMediaMcpServer v0.1.0 starting`.

**Version single source of truth:** `<Version>` in `Directory.Build.props` at the repo root; the container tag is derived from it.

**Logging convention:**
- Levels: Serilog `Verbose / Debug / Information / Warning / Error / Fatal` (minimum level from configuration)
- Format: human-readable plain text, `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}`
- Sinks: daily rolling file under `logs/` next to the binary, plus console **to stderr only**
- Configured at: `src/PeakMediaRemoteMcpServer/Program.cs` and `src/PeakMediaMcpServer/Program.cs`

**Exit code convention:** (stdio server)
- 0 — success (`--version`, clean shutdown)
- 1 — operational failure (e.g., no OpenRouter key set, upstream unreachable at startup)
- 2 — invalid invocation (bad flags, missing required args)

**stdout / stderr discipline:** stdout carries the MCP protocol only (stdio server) or the
`--version` output; every diagnostic, progress, and log line goes to stderr or the log file.
The HTTP server logs to stderr and the file, never stdout.

**Error message standard:** User-facing errors name the problem AND the next user action.
- MCP tool results: `ERROR: <problem>. <next action>` built with `ToolErrorFormatter.Format(...)` — never echo `ex.Message`. Example: `ERROR: daily quota of 50,000 characters reached. Try again after 00:00 UTC or ask an administrator to raise your quota.`
- Non-MCP HTTP endpoints: an RFC 9457 problem-details body, e.g. `{"title": "Unauthorized", "status": 401, "detail": "No valid Okta token. Sign in again from your MCP client."}`
- Stdio startup: stderr, e.g. `Error: PEAK_MEDIA_OPENROUTER_API_KEY is not set. Export your OpenRouter key and restart.`

## Security Baseline

These are coding-standard reminders that apply to every epic. They are NOT requirements —
TORs verify positive observable behavior, and "do not X" invariants are hard to express as
Given/When/Then. They MUST be respected during implementation and reviewed during
`/peak-workflow:wrapup-epic`.

**No `shell=True` / `eval` with user input.**
Never pass user-supplied data to a shell interpreter without escaping. In Python, prefer
`subprocess.run([...])` with a list; never `subprocess.run(cmd, shell=True)` on user input.
In Node.js, prefer `child_process.execFile` over `exec`. In .NET, set `ProcessStartInfo`
arguments through `ArgumentList`, never a concatenated command string. In any language, never use
`eval` or `Function()` constructors on user input, and never build SQL by string concatenation.

**Do not log secrets or PII.**
Tokens, passwords, API keys, session IDs, and personally identifiable information must
never appear in logs. The structured logger should redact known-sensitive keys
(`password`, `token`, `secret`, `api_key`, `authorization`, `cookie`, etc.). Review log
output during `/peak-workflow:wrapup-epic` for accidental leakage.

**No secrets committed to the repo.**
`.env`, credential files, private keys, and any configuration containing real secrets must
be in `.gitignore`. Use environment variables, secret managers, or encrypted files (e.g.,
`sops`, `age`) for sensitive configuration.

`/peak-workflow:wrapup-epic` includes these as default review items unless the project type
makes them inapplicable.

**No sign-in bypass, ever — not even in development.**
Accounts are real from the first epic, through the project's auth layer — email and password,
the organization's named provider, or both. There is no development-only login, no
anonymous fallback, no "current user" that a request header or a query parameter can assert. A
temporary auth bypass that ships is a breach, not a shortcut, and one added "just for this demo"
is how it ships. Tests sign in through the real auth layer's test helper like any other client.

**Every read and write goes through the access rule.**
Access is owner-or-permitted-role, decided in one place that every route calls — never re-derived
per handler and never left to a front-end check. Adding a table means adding its owner column and
its access rule in the same change. `/peak-workflow:wrapup-epic` reviews both on every epic that
touches user data.

**Project-specific:**
- The gateway refuses to start when `McpAuth:Enabled` is false — unlike Bitbucket, there is no
  anonymous mode, because an open gateway spends the shared OpenRouter credit for anyone. Tests
  mint tokens with the stub JWT issuer; the real Okta round-trip is verified by hand.
- `appsettings*.json` is tracked and holds only non-secret values (model, default voice, `McpAuth`
  issuer/resource URIs, `Storage:*`). The OpenRouter key arrives only through `OpenRouter__ApiKey`;
  the gateway refuses to start without it.
- Option classes holding secrets are `sealed class`, never `record` (a record's `ToString()` prints every property).

## Peak Workflow

- Product vision: `docs/product-vision-planning/product-vision.md`
- Concept of operations: `docs/product-vision-planning/concept-of-operations.md`
- Requirements baseline: `docs/requirements/`
- Implementation plan: `docs/implementation-plan/` — run `/peak-workflow:status` for the dashboard

Commands: `/peak-workflow:discover`, `/peak-workflow:mockup`, `/peak-workflow:capture-requirements`,
`/peak-workflow:plan-project`, `/peak-workflow:add`, `/peak-workflow:triage <issue|description>`,
`/peak-workflow:start-epic <id>`, `/peak-workflow:wrapup-epic <id>`, `/peak-workflow:pause`,
`/peak-workflow:quick-fix <issue|description>`, `/peak-workflow:refresh-docs`, `/peak-workflow:status`,
`/peak-workflow:setup`, `/peak-workflow:upgrade`, `/peak-workflow:new-project`

## Verification & Quality Gates

**Test directories:** tests/

Run every applicable check before marking an epic Implemented or Complete:

- **Build:** `python build.py build`
- **Tests:** `python build.py test` (tests/)
- **Lint / format:** `dotnet format PeakMediaMcpServers.sln --verify-no-changes`
- **Run the tool:** `python build.py start` then `curl http://localhost:5108/version` → `{"name":"PeakMediaRemoteMcpServer","version":"<semver>"}` and `python build.py mcp tools` lists the tools; stdio: `dotnet run --project src/PeakMediaMcpServer -- --version` → `PeakMediaMcpServer v<semver>`, exit 0

## Important Reminders

- Hybrid by design: the HTTP gateway holds the shared key and requires Okta sign-in; the stdio
  server never touches the shared key — it reads the user's own `PEAK_MEDIA_OPENROUTER_API_KEY`
  (legacy fallback `TTS_OPENROUTER_API_KEY`, same as the peak-media skills) and has no database.
- Every usage-log, quota, and cache record that belongs to a caller carries the caller's Okta subject.
- SQLite lives at `Storage:DatabasePath` (default `data/peakmedia.db`; `/app/data` on a mounted
  volume in the container). An unwritable directory falls back to a temp path with a loud warning,
  as in Bitbucket — that fallback does not survive a restart.
- Tests never call real OpenRouter or real Okta — use `tests/Fakes/` and the stub JWT issuer.
- Image generation is a growth note only — do not build it until it is planned as an epic.

### C# Coding Conventions (from BitbucketMcpServers)

- Use `var` for all variables; curly braces for all blocks.
- Global usings in `GlobalUsings.cs`, not per-file usings.
- Prefer FluentResults over null handling or exceptions.
- MCP tools are partial classes, one tool per file: `TtsTools_<ToolName>.cs`, each a method with `[McpServerTool]`.
- Tool errors start with `ERROR:` and are built with `ToolErrorFormatter.Format(...)` — never echo `ex.Message`.
- Return tool results in markdown format.
- Multi-line attribute syntax — Description is a separate attribute:
  ```csharp
  [McpServerTool(Name = "tool_name"),
   Description("Tool description")]
  ```

## Reference Materials

- `~/github/peakflames/BitbucketMcpServers` — structural template: solution layout, `build.py`,
  `McpAuth` resource-server gate (`src/BitbucketRemoteMcpServer/Auth/`), Serilog setup, test
  infrastructure (`tests/StubAuthorizationServer`, `tests/Fakes`), container publish workflow.
- `~/github/peakflames/claude-plugins-peakflames/plugins/peak-media/skills` — the client skills
  (`web2video`, `plate-factory`, `video-spine`); `video-spine/references/voices.md` and
  `video-spine/scripts/spine.mjs` show today's direct OpenRouter TTS calls this gateway replaces.

## Git Workflow

**Remote:** https://github.com/peakflames/PeakMediaMcpServers (public) — default branch `develop`; `main` and
`develop` protected (pull request with 1 approval, no force-push, no deletion; admins may bypass).

- Base branch: `develop`. Epic, docs, and quick-fix branches are cut from it and merge back into it.
- Release branch: `main` — receives only release merges from `develop` and tagged releases.
- Default branch on the hosting service: `develop`.
- Branch protection (published projects): `main` and `develop` require a pull request with one
  approval and refuse force-pushes and deletion; administrators may bypass.
- Epic branches: `feature/epic-<id>-<short-name>`, where `<id>` is the epic's ID and `<short-name>`
  comes from the spec filename (`epic-a3f2K7p-user-auth.md` → `feature/epic-a3f2K7p-user-auth`).
- Quick-fix branches: `hotfix/issue-<N>-<slug>` when tied to a GitHub issue, otherwise `hotfix/<slug>`.
- Merges use `--no-ff`. Use explicit file paths in `git add`, never wildcards.
- Claude asks before every push to a remote.
- Never commit: `.env` and `.env.*` (except `.env.example`), credential and key files, `.venv/`, `data/`
  (SQLite databases), `logs/`, `bin/`, `obj/`, `publish/`, `appsettings.*.local.json`.

## CRITICAL: Verification Before Commit Rule

**NEVER commit code changes before verification!**

A successful build (compile) does NOT equal working code. The workflow MUST be:

1. **Implement** — Make the code changes
2. **Lint** — Run `dotnet format PeakMediaMcpServers.sln --verify-no-changes` (fix with `dotnet format PeakMediaMcpServers.sln`)
3. **Build** — Run `python build.py build`
4. **Verify** — Run tests, start the server, and exercise it with `curl` and `python build.py mcp ...`
5. **Commit** — ONLY after verification passed

**Why this matters:**
- Compiled code ≠ correct behavior
- API changes need endpoint verification
- Business logic needs functional testing
- Committing untested code pollutes git history with potential bugs

**Verification Workflow Example:**
```bash
dotnet format PeakMediaMcpServers.sln --verify-no-changes   # Check formatting + analyzers
python build.py test                                        # Unit/integration tests
python build.py start                                       # Build & start (port 5108)
curl http://localhost:5108/version                          # Verify version endpoint
python build.py mcp tools                                   # Verify MCP tool inventory
python build.py stop                                        # Stop when done
git add <files> && git commit -m "feat: ..."                # Commit after verification
```

## Release Protocol

**Prerequisites:** Must be on `develop` branch with a clean working tree.

**Steps:**

1. **Finalize CHANGELOG** — Change `## [X.Y.Z] — UNDER DEVELOPMENT` → `## [X.Y.Z] — YYYY-MM-DD` in `CHANGELOG.md`
   - Commit: `chore: release vX.Y.Z`

2. **Merge to main**
   ```bash
   git checkout main && git pull origin main
   git merge develop --no-ff -m "Merge branch 'develop' into main for release vX.Y.Z"
   ```

3. **Tag the release** (on main)
   ```bash
   git tag -a vX.Y.Z -m "Release vX.Y.Z - Brief description"
   ```

4. **Merge back to develop**
   ```bash
   git checkout develop && git merge main --no-ff -m "Merge branch 'main' into develop - sync Release version X.Y.Z"
   ```

5. **Post-release version bump** (on develop)
   - Bump `<Version>` in `Directory.Build.props` → next version
   - Add `## [X.Y+1.0] — UNDER DEVELOPMENT` to `CHANGELOG.md`
   - Commit: `chore: bump version for next development cycle`

6. **Push** (ASK USER FIRST)
   ```bash
   git push origin main && git push origin develop && git push origin vX.Y.Z
   ```

**Note:** CI matches BitbucketMcpServers: pushing a `v*` tag runs the GitHub Actions workflow that
publishes the `peakflames/peak-media-remote-mcp-server` container to Docker Hub (it logs in with
`DOCKERHUB_USERNAME` / `DOCKERHUB_TOKEN`, which the `peakflames` organization already provides as
organization secrets — the same ones BitbucketMcpServers uses; an org admin may need to grant this repo access). Branch pushes run no CI. The
walking-skeleton epic adds the workflow.

**Protection note:** steps 2–6 push directly to protected `main` and `develop` through the
administrator bypass; a maintainer without admin rights opens a pull request from `develop` to
`main` instead.
