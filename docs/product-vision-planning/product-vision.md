# PeakMedia MCP Servers — Product Vision & Brief

**Document Version:** 1.0
**Date:** 2026-11-12
**Status:** Draft

Companion document: [Concept of Operations](concept-of-operations.md)

---

## 1. Product Name

**PeakMedia MCP Servers** — *one sign-in, one shared voice key: narration for every peak-media user.*

Two servers share one tool library:

- **PeakMedia Remote MCP Server** (the *gateway*) — an HTTP MCP server an organization runs as one
  container. It holds the organization's OpenRouter key, signs callers in through the
  organization's identity provider (Okta), meters spend per person, and shares an audio cache.
- **PeakMedia MCP Server** (the *stdio server*) — a local console MCP server for a single person
  who brings their own OpenRouter key. Same tools, no sign-in, no database.

## 2. Problem Statement

The peak-media Claude Code skills (`/web2video`, `/plate-factory`, and the `video-spine` stage they
share) narrate videos by calling OpenRouter text-to-speech directly from Node scripts
(`video-spine/scripts/spine.mjs`, `sample-voice.mjs`, through `lib/tts/`). Every person who wants a
narrated video must hold their own OpenRouter key in `PEAK_MEDIA_OPENROUTER_API_KEY` (legacy
`TTS_OPENROUTER_API_KEY`). Without one, the skill quietly writes a beat grid and a silent track
instead of narration.

Inside an organization this does not scale. Each person signs up for OpenRouter, buys credit,
and — to use the default Gemini voices with zero data retention — finds and enables "Allow Safety
Retention" in their own OpenRouter privacy settings. Spend is scattered across personal accounts
that nobody can see in one place, and nothing limits how much one person spends.

Each person also keeps their own clip cache in `~/.cache/peak-media/tts/`. When two people narrate
the same paragraph with the same model, voice and style, the organization pays twice.

Pain points today:

- Every narrating user needs a personal OpenRouter key, credit, and the right privacy setting.
- No missing-key error: a user without a key gets a silent beat grid and may not notice why.
- No view of who spent what, and no per-person spending limit.
- Identical clips are synthesized and paid for once per person.
- Keys live in shell profiles on many laptops.

## 3. Target Users

| User group | Primary needs |
|---|---|
| **Skill user** — a person running `/web2video`, `/plate-factory` or `video-spine` in Claude Code inside the organization | Narration that just works after one sign-in; clear errors naming what to do; progress while a long spine renders |
| **Gateway admin** — the person who runs the container and owns the OpenRouter account | One key, one bill; see each person's monthly spend; raise or lower a person's quota; know the cache is earning its keep |
| **Solo user** — a person outside any organization's gateway, with their own OpenRouter key | The same tools over stdio, so the skills have one code path, with no sign-in or server to run |
| **Skill author** — maintainer of the peak-media plugin | A stable, documented tool contract to code `spine.mjs` and `sample-voice.mjs` against |

## 4. Vision Statement

Every peak-media skill gets narration by calling one MCP tool, `synthesize_narration`. Inside an
organization that tool runs on a gateway: people sign in once with the organization's Okta, spend
the organization's shared OpenRouter credit within a monthly quota, and never pay twice for the
same clip. Outside one, the identical tool runs locally over stdio on the person's own key. Either
way the audio lands in the skill's local clip cache without ever passing through the model's
context, and the skill author writes one code path.

## 5. Goals & Success Criteria (MVP)

| # | Goal | Success criterion |
|---|---|---|
| G1 | No personal keys inside the organization | A signed-in user with no OpenRouter key set calls `synthesize_narration` for a 12-clip spine and receives 12 downloadable WAV clips |
| G2 | The cache saves money | Re-running an unchanged spine makes zero OpenRouter calls, adds $0.00 to the caller's month, and reports 12/12 cache hits |
| G3 | Quotas are enforced | A user whose month-to-date spend is at or above their quota is refused with `ERROR: monthly quota of $25.00 reached …`, and the dashboard's **Usage** page flags them `OVER` |
| G4 | Long narration shows progress | A 12-clip spine call emits one MCP progress notification per clip, visible in Claude Code |
| G5 | Audio never enters the model's context | Tool results carry metadata and URLs (gateway) or file paths (stdio) only — no audio bytes |
| G6 | One code path for skills | The gateway and the stdio server expose `synthesize_narration` and `list_voices` with identical arguments and result fields (URL vs. path aside) |
| G7 | Sign-in works with a real Okta | A user adds the gateway with `claude mcp add --transport http peak-media <url>` and completes a browser sign-in against an Okta developer org, with no client ID to paste |
| G8 | Admins can see and steer usage | An admin signs in to the dashboard, sees every user's month-to-date spend on **Usage**, and changes one user's quota on **User detail**; the change applies to that user's next call |

## 6. MVP Scope Summary

### Narration tools (gateway and stdio)

- `synthesize_narration` — one call per spine: a list of clips (`n`, `text`, optional `style`) plus
  `model`, `voice`, `style`. Synthesizes clip by clip, cache first.
- One MCP progress notification per clip (`clip 4/12 synthesized` / `cached`).
- Each clip cached the moment it is made, so a retry after a timeout or cancel only pays for clips
  still missing.
- Result per clip: `n`, `cacheKey` (`<sha256(model\0voice\0style\0text)>.wav`, byte-compatible with
  the skills' cache), `durationMs`, `words`, `wpm`, `cached`, `costUsd`, `sampleRate`, plus
  `url` (gateway, valid 15 minutes, reusable) or `path` (stdio, inside the local cache).
- Result summary: total cost of this call, cached count, median wpm, slow-clip warnings (a clip
  below 70% of the median wpm — a mood tag likely read aloud).
- `list_voices` — the allowed models and their voices, defaults, and whether style is sent.
- Validation before any spend: unknown model, unknown voice, empty clip, too many clips (40) or too
  long a clip (4,000 characters) is refused with an `ERROR:` naming the fix.
- Every OpenRouter request sends `provider: { "zdr": true }`.
- One automatic retry per clip on timeout or 5xx; then the call fails with an `ERROR:` naming the
  clip. Clips already made stay cached and charged.

### Gateway: sign-in and access

- OAuth 2.1 broker (ported from BitbucketMcpServers): the gateway is the authorization server MCP
  clients talk to, and delegates sign-in to the organization's OIDC provider (Okta).
- Dynamic client registration on, so `claude mcp add --transport http peak-media <url>` needs no
  client ID.
- Every caller identified by OIDC subject and email; dashboard admins identified by membership
  in a configured group (`Admin:Group`, e.g. `peakmedia-admins`) in the ID token's `groups` claim.
- The gateway refuses to start when sign-in is not configured or the OpenRouter key is missing.

### Gateway: quotas and usage

- Per-user monthly quota in US dollars, calendar month in UTC, default **$25** from configuration,
  per-user overrides stored in the database.
- Charged at the real cost OpenRouter reports for each synthesized clip; cache hits cost $0.
- A call is allowed if the caller is under quota when it starts and runs to completion; once
  month-to-date spend reaches the quota, later calls are refused until 00:00 UTC on the 1st.
- Usage log row per clip: caller, time, model, voice, characters, cost, cache key, hit or miss.
  The narration text itself is never stored or logged.
- `my_usage` — the caller's month-to-date spend, quota, remaining, clips synthesized vs. cache
  hits, reset date.

### Gateway: shared audio cache

- One cache for all users, keyed by the same sha256 as the skills' local cache.
- Size cap `Storage:CacheMaxGb` (default 20 GB), least-recently-used eviction; a hit refreshes
  last-used.
- Download URLs are unguessable per-clip tokens, valid 15 minutes, fetchable repeatedly; an expired
  URL is renewed by calling the tool again (a free cache hit).

### Stdio server

- Same `synthesize_narration` and `list_voices` contract; reads `PEAK_MEDIA_OPENROUTER_API_KEY`
  (legacy `TTS_OPENROUTER_API_KEY`); writes WAVs straight into `~/.cache/peak-media/tts/`
  (`PEAK_MEDIA_TTS_CACHE` overrides) and returns their paths.
- Accepts `fake/tone` for offline testing; no usage tracking, quotas or admin tools.

### Client integration contract

- `docs/client-integration.md`: the tool contract, the skill's selection order (connected
  `peak-media` MCP server → personal key → beat grid; a gateway error never falls back to a
  personal key), the download-into-cache step, and the `claude mcp add` commands for both
  servers. The skill code changes themselves are made in the peak-media plugin repository.

### Usage dashboard (built last, after narration and quotas)

- Blazor pages (Interactive Server) in the gateway, built with the Blazor Blueprint UI component
  library, signed in through the same OIDC provider (browser cookie session).
- Two roles: **user** (every signed-in person) and **admin** (member of `Admin:Group`). The admin
  role exists only in the dashboard; every MCP tool behaves the same for everyone.
- Every user sees **My usage** for a chosen month.
- Admins also see **Usage** (all users by month: spend, quota, characters, clips, cache-hit rate,
  `OVER` flag), **User detail** (one user's usage rows, no text) with **Monthly quota** editing,
  and **Cache** (size vs. cap, clip count, hit rate, evictions).
- This is the only place admins see organization-wide usage or change quotas.

### Cross-cutting Concerns

- Version exposure: `GET /version` and `--version`; version in the first log line.
- Serilog to stderr and a daily file; never narration text, tokens or keys.
- Errors in the `ERROR: <problem>. <next action>` form for tools, RFC 9457 problem details for HTTP.
- One container per organization, single replica, SQLite on a mounted volume.
- Tests use a fake OpenRouter and a stub OIDC issuer; never the real services.

## 7. Out of Scope for MVP

- **Image generation** — a growth note (see §11); not designed or built now.
- **Organization-wide spend ceiling** — the credit limit on the shared OpenRouter key covers it.
- **Multi-tenant hosting** — one container per organization; no tenants inside one gateway.
- **Code changes in the peak-media plugin** — this project defines the contract; the plugin
  repository adopts it.
- **Pre-call cost estimates** — quotas are enforced on real, reported cost after the fact.
- **Admin MCP tools** (`usage_report`, `set_quota`) — admin work happens in the dashboard.
- **Storing or reviewing narration text** — only hashes and counts are kept.
- **Async jobs / polling** — one synchronous call per spine with progress notifications.
- **Music, SFX, or anything other than speech.**
- **Usage tracking on the stdio server** — the user's OpenRouter dashboard covers it.

## 8. Key Business Scenarios

1. **First connection and sign-in.** A skill user adds the gateway to Claude Code with one
   `claude mcp add` command, signs in through Okta in the browser, and sees the `peak-media` tools
   listed — no OpenRouter key on their laptop.
2. **Narrating a spine through the gateway.** During `/web2video`, Claude calls
   `synthesize_narration` with the script's 12 clips. Claude Code shows progress per clip; the
   result's URLs are handed to `spine.mjs`, which downloads the clips into the local cache and mixes
   `narration.wav`.
3. **Re-running an unchanged spine.** The user tweaks plates but not the script and re-runs the
   spine. Every clip is a cache hit — locally or on the gateway — and nothing is charged.
4. **Hitting the monthly quota.** A heavy user's finished call pushes them past $25. Their next call
   is refused with an error naming the spend, the quota and the reset date, and suggesting they ask
   an admin. They check `my_usage`.
5. **Admin reviews and adjusts usage.** The admin signs in to the dashboard, sees one user flagged
   `OVER` on the month's **Usage** page during a deadline project, and raises that user's quota on
   **User detail**.
6. **Solo user on stdio.** A person outside the organization registers the stdio server under the
   same `peak-media` name with their own key; the skill narrates through the same tool and receives
   local file paths.
7. **OpenRouter fails mid-spine.** Clip 5 of 12 times out twice; the call returns an error naming
   clip 5. A retry a minute later finds clips 1–4 cached and pays only for 5–12.
8. **Checking my usage in the browser.** A user opens the gateway's dashboard, signs in, and sees
   their own month on **My usage**; admin pages are not offered to them.

## 9. Design Direction

- **Tool results are markdown for Claude, with one fenced JSON block for scripts.** The JSON holds
  the per-clip fields the skill script consumes; the markdown around it is a short human summary
  (cost, cached count, median wpm, warnings).
- **Field names mirror the skills.** `n`, `text`, `style`, `durationMs`, `words`, `wpm`, `cached`
  and the `cacheKey` file name match `lib/tts` results so `spine.mjs` changes stay small.
- **Errors name the problem and the next action**, in the `ERROR: …. …` form — e.g. `ERROR: unknown
  voice 'Rasalgeti' for google/gemini-3.8-flash-lite-tts. Call list_voices and pick one of its
  Gemini voices.`
- **Money is shown in US dollars to the cent** (`$12.34 of $25.00`); dates in UTC.
- **Tool names are snake_case** (`synthesize_narration`, `list_voices`, `my_usage`).
- **Dashboard:** plain, dense tables over decoration; readable in light and dark; money to the cent,
  dates in UTC; no narration text anywhere; admin-only pages absent from a non-admin's navigation.

## 10. Data Strategy

- **Source of audio:** OpenRouter `POST /api/v1/audio/speech`, `response_format: "pcm"` (16-bit
  mono, 24 kHz), wrapped in a WAV header before caching. Real per-clip cost is read from
  OpenRouter's generation statistics for the returned generation ID.
- **Gateway database (SQLite at `Storage:DatabasePath`):**
  - users (OIDC subject, email, last seen),
  - quota overrides (subject, monthly USD, set by, set at),
  - usage log (subject, time, model, voice, characters, cost, cache key, hit/miss),
  - cache index (cache key, size, created by subject, created at, last used, hit count),
  - download tokens (token hash, cache key, subject, expires at),
  - broker state (clients, sign-in transactions, issued tokens, signing key).
- **Audio files:** on the mounted volume next to the database; the index enforces the size cap.
- **Freshness:** month-to-date spend is computed from the usage log at call time; nothing is batched.
- **Background work:** cache eviction and expired-token cleanup run in a lightweight background
  service; nothing else runs unattended.
- **Stdio server:** no database; the local cache directory is its only state.

## 11. Backlog / Future Vision

- **Image generation** through the same gateway, sign-in, quota and cache model.
- Organization-wide monthly spend ceiling.
- Pre-call cost estimates from a model price table, shown before a long spine runs.
- Usage export (CSV) for finance.
- Admin MCP tools (`usage_report`, `set_quota`) for admins who prefer Claude Code to the browser.
- Quota alerts at 80% of a user's monthly quota.
- Per-model allow-lists per user or group.
- Resource-server mode for organizations whose Okta has a custom authorization server.
- Usage tracking on the stdio server.
