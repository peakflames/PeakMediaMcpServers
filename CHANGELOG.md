# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

### Added
### Changed
- `build.py smoke` runs the Run-the-tool gate unattended: it starts its own gateway against the test sign-in server, signs in through the gateway's real flow (no browser, no bypass), lists the tools and stops everything. The real Okta sign-in (`build.py mcp tools --oauth`) moves to a by-hand release check.
### Fixed

---

## [0.1.0] — UNDER DEVELOPMENT

### Added
- Solution skeleton: `PeakMediaRemoteMcpServer` (HTTP gateway), `PeakMediaMcpServer` (stdio server),
  `PeakMediaMcpTools` (shared library), test projects, `build.py`, and a tag-triggered container publish
  to Docker Hub (`peakflames/peak-media-remote-mcp-server`).
- Gateway `GET /version` returning `{name, version}` without sign-in; `/mcp` behind authorization.
- Stdio server `--version` flag (exit 0); invalid invocations exit 2 naming the next action; a missing
  `PEAK_MEDIA_OPENROUTER_API_KEY` exits 1.
- MCP handshake `serverInfo` reports the same name and version on both servers.
- Startup log line `<Name> v<version> starting` as the first record; plain-text log format to stderr and
  a daily file under `logs/` named with the UTC date; nothing written to stdout except the MCP protocol.
- The gateway refuses to start when `McpAuth:Enabled` is false, `Broker__UpstreamClientSecret` is unset,
  or `OpenRouter__ApiKey` is unset.
- SQLite storage bootstrap (`Storage:DatabasePath`, temp-path fallback with a warning, schema versioning).
- Okta sign-in through the gateway's own OAuth 2.1 authorization server (broker): protected-resource and
  authorization-server metadata, dynamic client registration (loopback redirect URIs only, plus an
  allowlist), `/authorize` with PKCE S256, `/oauth/callback` validating the OIDC ID token, `/token` with
  authorization-code and rotating refresh-token grants. Connect Claude Code with
  `claude mcp add --transport http peak-media <gateway>/mcp` and one browser sign-in — no client ID to paste.
- `/mcp` accepts the gateway's RS256 access tokens (`sub`, `email`, `role`); a 401 points at
  `/.well-known/oauth-protected-resource`. The `admin` role comes from the Okta `groups` claim (`Admin:Group`).
- The access rule (owner or admin) in one place, `users` table and schema v1 for sign-in state.
- The gateway refuses to start without `Broker:UpstreamAuthority` / `Broker:UpstreamClientId`, or when
  `McpAuth:Resource` is not `Broker:IssuerUri` + `/mcp`.
- `build.py mcp` accepts a token in `PEAK_MEDIA_ACCESS_TOKEN` or signs in with `--oauth`.
- Okta administrator checklist in the README; stub OIDC issuer and `TestSignIn` helper for tests.
- `docs/architecture.md` and `docs/design-notes.md` describe the as-built walking skeleton, its design
  decisions, and known issues.
- `list_voices` MCP tool: the allowed TTS models, the default model, and each model's voices (from the
  `Tts` section of `appsettings.json`).
- `synthesize_narration` MCP tool: narrates 1–40 clips in one call through the gateway's shared
  OpenRouter key, validating the whole request before any spend; clips run in order, cache first, with
  one MCP progress notification per clip; the result is a markdown summary (clips, cached, cost, median
  wpm, slow-clip warnings) plus one JSON block with a download URL per clip. Gateway only — the stdio
  server does not offer it.
- Shared audio cache keyed by model, voice, style and text: schema v2 adds `audio_cache` and
  `clip_downloads`; WAV files under `Storage:AudioDirectory` (default `audio/` beside the database).
- `GET /clips/{token}` downloads a clip without sign-in through a random token valid for 15 minutes;
  unknown tokens return 404 and expired ones 410 as RFC 9457 problem details.
- Per-clip cost from OpenRouter's generation lookup; when it cannot be read, the clip is charged the
  model's configured price (`Tts:Models[].PriceUsdPerThousandCharacters`, estimates) with a `[WRN]`
  log record saying the cost was estimated.
- A clip that times out, cannot reach OpenRouter or gets a 5xx answer is retried once
  (`OpenRouter:RetryDelayMs`, default 1000); the per-request timeout is `OpenRouter:ClipTimeout`
  (default 2 minutes). A clip that still fails ends the call with an error naming the clip and the
  clips already saved, e.g. `ERROR: OpenRouter did not answer for clip 5 of 12 (timeout, retried once).
  Clips 1–4 are saved; run the same request again to continue.` — running it again pays only for the
  missing clips.
- An out-of-credit (402) or rejected-key (401) OpenRouter answer is not retried and is reported as such,
  asking the caller to contact an administrator; the key never appears in the result or the log.
- LRU cache eviction: every `Storage:EvictionIntervalMinutes` (default 10) the gateway removes the
  least-recently-used clips until the cache fits `Storage:CacheMaxGb` (default 10; the gateway refuses
  to start when it is 0 or less), and removes download links that expired more than 24 hours ago.
- Usage log and monthly quotas: schema v3 adds `usage_log` (subject, time, model, voice, characters,
  cost, cache key, hit — never the narration text) and `quota_overrides` (set by, set at). Every
  synthesized clip is charged at its real cost, cache hits cost nothing, and clips finished before a
  failure or cancellation stay charged. Default quota `Quota:DefaultMonthlyUsd` (25), counted per calendar
  month in UTC; administrators' per-user overrides go through `QuotaStore.SetOverrideAsync`.
- `synthesize_narration` refuses a caller whose month-to-date spend has reached their quota, before any
  synthesis or cache lookup: `ERROR: monthly quota of $25.00 reached ($25.32 spent this month). Try again
  after 2026-12-01 00:00 UTC or ask an administrator to raise your quota.` A call that starts under quota
  runs to completion and notes `You are now over your monthly quota ($25.32 of $25.00).`
- `my_usage` MCP tool (gateway only): the caller's own month, spent, quota, remaining, clips synthesized,
  cache hits and reset time, in US dollars to the cent and UTC.

### Changed
- The narration summary prints a free call's cost as `$0.00` (other costs keep four decimals, e.g. `$0.0100`).
