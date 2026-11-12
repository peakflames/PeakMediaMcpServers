# PeakMedia MCP Servers — Concept of Operations (ConOps)

**Document Version:** 1.1
**Date:** 2026-11-12
**Status:** Draft

Companion document: [Product Vision & Brief](product-vision.md)

---

## 1. Purpose & Scope

This document describes how people use PeakMedia MCP Servers day to day: how a Claude Code
session connects and signs in, how the peak-media skills obtain narration, how spend is metered
and limited, how admins see and steer usage, and how a solo user gets the same tools locally.

In scope: the gateway (`PeakMediaRemoteMcpServer`), the stdio server (`PeakMediaMcpServer`), the
shared tool library, and the client-integration contract the peak-media plugin adopts. Out of
scope: the plugin's own code changes, and image generation (a growth note).

## 2. Current State ("As-Is")

| Current method | Limitation |
|---|---|
| Each user exports `PEAK_MEDIA_OPENROUTER_API_KEY` (or legacy `TTS_OPENROUTER_API_KEY`) and `spine.mjs` / `sample-voice.mjs` call `POST https://openrouter.ai/api/v1/audio/speech` from `lib/tts/openrouter.mjs` | Every person needs an OpenRouter account, credit and a key on their laptop |
| Gemini voices sent with `provider.zdr: true` | Each account must enable "Allow Safety Retention" or Gemini requests fail with a 404 |
| No key set → `spine.mjs` writes `beatgrid.json` and a silent `narration.wav` | A user may not notice narration was skipped |
| Local clip cache `~/.cache/peak-media/tts/<sha256(model\0voice\0style\0text)>.wav` | Per laptop: identical clips are paid for once per person |
| Spend visible only on each person's OpenRouter dashboard | No organization view, no per-person limits |
| `sample-voice.mjs` measures wpm per voice by synthesizing a sample paragraph | Each sample is a paid call on the person's own key |

Core pain points:

1. Onboarding a narrating user means an OpenRouter signup, credit, a key and a privacy setting.
2. Nobody can see or cap what the organization spends on narration.
3. The same narration is bought repeatedly across people.
4. Keys are scattered across shell profiles.

## 3. Proposed System ("To-Be")

The organization runs one gateway container holding its OpenRouter key. Users add the gateway to
Claude Code once and sign in through the organization's Okta in the browser; Claude Code then
holds a token for the gateway. When a skill needs narration, Claude calls the gateway's
`synthesize_narration` tool with every clip of the spine. The gateway checks the caller's monthly
quota, serves each clip from its shared cache or synthesizes it through OpenRouter, reports
progress per clip, charges the caller the real cost, and returns per-clip metadata with
short-lived download URLs. Claude passes the result to `spine.mjs`, which downloads each clip into
the local cache under the same file name it would have computed itself and mixes the narration as
before. Audio never passes through the model's context.

Admins use a small browser dashboard on the gateway — built last, after narration and quotas —
to see every user's usage and change quotas. The admin role exists only there; every MCP tool
behaves the same for every signed-in person.

A person without a gateway registers the stdio server under the same `peak-media` name with their
own key. It exposes the same narration tools, writes clips straight into the local cache, and
returns their paths, so the skill's code path is identical.

## 4. User Roles & Profiles

| Role | Who | Questions they bring | Access |
|---|---|---|---|
| **Skill user** | Anyone in the organization assigned to the gateway's Okta app | "Can I narrate this video without a key?" "How much of my quota is left?" "Why did narration fail?" | `synthesize_narration`, `list_voices`, `my_usage`; **My usage** page on the dashboard |
| **Gateway admin** | Member of the configured admin group (`Admin:Group`, e.g. `peakmedia-admins`) | "Who spent what this month?" "Who is over quota?" "Is the cache paying off?" "Can I give this person more room?" | Everything a skill user has, plus the dashboard's **Usage**, **User detail** (quota edit) and **Cache** pages |
| **Operator** | Whoever deploys the container (often the admin) | "What do I configure? Does it survive a restart?" | Environment variables, `appsettings.json`, the data volume |
| **Solo user** | Anyone running the stdio server with their own key | "Can I use the same skills without a gateway?" | `synthesize_narration`, `list_voices` on their own machine |

## 5. Operational Scenarios

### Scenario 1: First connection and sign-in

**Actor:** Skill user
**Trigger:** The user wants narrated videos and has been told the gateway's URL.
**Goal:** Connect Claude Code to the gateway without any OpenRouter key.

**Steps:**
1. The user runs `claude mcp add --transport http peak-media https://<gateway-host>/mcp`.
2. Claude Code connects to `/mcp`, receives `401` with a `WWW-Authenticate` header pointing at the
   gateway's protected-resource metadata, and discovers the gateway's authorization server
   metadata.
3. Claude Code registers itself through dynamic client registration (`POST /register`) and opens the
   browser at the gateway's `/authorize`.
4. The gateway redirects the browser to the organization's Okta sign-in page; the user signs in.
5. Okta redirects to `<gateway>/oauth/callback`; the gateway reads the ID token's subject, email
   and `groups` claim, records or updates the user, and returns an authorization code to Claude
   Code's local callback.
6. Claude Code exchanges the code for a gateway access token and refresh token.
7. The user runs `/mcp` in Claude Code and sees `peak-media` connected, with tools
   `synthesize_narration`, `list_voices`, `my_usage` — the same for every user.
8. A user not assigned to the Okta app is refused by Okta at step 4 and never receives a token.

**Outcome:** The user is connected and signed in; no key exists on their laptop.

### Scenario 2: Narrating a spine through the gateway

**Actor:** Skill user (through Claude running `/web2video`)
**Trigger:** `/web2video` reaches the spine stage in narrated mode with a 12-beat `script.md`.
**Goal:** Get `spine/narration.wav` and `spine/manifest.json` without a personal key.

**Steps:**
1. The skill sees a connected `peak-media` MCP server and chooses it over any personal key.
2. Claude calls `list_voices` once to confirm `google/gemini-3.8-flash-lite-tts` and voice
   `Rasalgethi` are allowed.
3. Claude calls `synthesize_narration` with `model`, `voice: "Rasalgethi"`, the house `style`, and
   `clips: [{n: 1, text: "…"}, … {n: 12, text: "…"}]`.
4. The gateway validates: model allowed, voice known, 1–40 clips, each 1–4,000 characters. It
   checks the caller's month-to-date spend is below their quota.
5. For each clip in order the gateway computes `cacheKey`; on a hit it records a $0 usage row; on a
   miss it calls OpenRouter with `response_format: "pcm"` and `provider.zdr: true`, wraps the PCM
   in a WAV header, stores it in the cache, reads the real cost, and records a usage row.
6. After each clip the gateway sends an MCP progress notification, e.g. `clip 4/12 synthesized
   (1.2 s audio, $0.0031)`; Claude Code shows it.
7. The result is markdown — `12 clips · 3 cached · $0.0284 this call · median 127 wpm` plus any
   slow-clip warning — and a fenced JSON block with, per clip, `n`, `cacheKey`, `durationMs`,
   `words`, `wpm`, `cached`, `costUsd`, `sampleRate`, `url` (valid 15 minutes).
8. Claude passes the JSON to `spine.mjs`, which downloads each `url` into
   `~/.cache/peak-media/tts/<cacheKey>` (skipping files already present), verifies each is a valid
   WAV, then lays out and mixes `narration.wav` as before.
9. A download of an expired URL gets `410 Gone` with a problem-details body telling the caller to
   call `synthesize_narration` again; the repeat call is all cache hits and costs nothing.

**Outcome:** The narrated spine exists locally; the caller's month shows the spend.

### Scenario 3: Re-running an unchanged spine

**Actor:** Skill user
**Trigger:** The user re-runs the spine stage after changing plates but not `script.md`.
**Goal:** Narration at no cost.

**Steps:**
1. Claude calls `synthesize_narration` with the same model, voice, style and texts.
2. Every clip's `cacheKey` is found in the gateway cache; each is recorded as a $0 hit and its
   last-used time refreshed.
3. Progress notifications read `clip N/12 cached`.
4. The result reads `12 clips · 12 cached · $0.00 this call`.
5. `spine.mjs` finds every file already in the local cache and downloads nothing.

**Outcome:** Identical narration, $0.00 charged, zero OpenRouter calls.

### Scenario 4: Hitting the monthly quota

**Actor:** Skill user
**Trigger:** The user has spent $24.80 of a $25.00 quota this month and narrates a long spine.
**Goal:** Understand why narration stops and what to do.

**Steps:**
1. Claude calls `synthesize_narration`; month-to-date spend ($24.80) is below the quota, so the call
   proceeds.
2. All clips are synthesized; the call costs $0.52 and the user's month reaches $25.32. The result
   notes `You are now over your monthly quota ($25.32 of $25.00).`
3. Later Claude calls `synthesize_narration` again; the gateway refuses before any synthesis with
   `ERROR: monthly quota of $25.00 reached ($25.32 spent this month). Try again after 2026-12-01
   00:00 UTC or ask an administrator to raise your quota.`
4. A call whose every clip would be a cache hit is refused the same way (the quota gate is per
   call).
5. The user calls `my_usage` and sees: month `2026-11`, spent `$25.32`, quota `$25.00`, remaining
   `$0.00`, clips synthesized `418`, cache hits `95`, resets `2026-12-01 00:00 UTC`.
6. The skill reports the gateway error to the user and does not fall back to any personal key.

**Outcome:** The user knows exactly why and what to do next; spend never runs on unchecked.

### Scenario 5: Admin reviews and adjusts usage

**Actor:** Gateway admin
**Trigger:** Month-end review, or a user asks for more quota.
**Goal:** See spend per person and raise one person's quota.

**Steps:**
1. The admin opens `https://<gateway-host>/` and signs in on the organization's Okta page; the
   gateway sees `Admin:Group` (e.g. `peakmedia-admins`) in the ID token's `groups` claim, marks the
   session as admin, and shows My Usage (S-01).
2. On My Usage (S-01), the app header's navigation shows the "My usage", "Usage" and "Cache" links.
3. The admin clicks the "Usage" link; on Usage (S-02) they pick `2026-11` in the "Month" select.
   The table lists one row per user with the columns Email, Spent, Quota, Characters, Clips,
   Cache-hit rate and Status — an `OVER` badge where spent ≥ quota — sorted by Spent (highest
   first), with a "Total" footer row for the month.
4. On Usage (S-02), the admin clicks the "pat@example.com" link; User Detail (S-03) opens with
   Pat's usage rows for the month (Time (UTC), Model, Voice, Characters, Cost, Source `cached` or
   `synthesized`) — never the narration text.
5. On User Detail (S-03), the admin types `60` in the "Monthly quota (USD)" input and clicks the
   "Save" button. The gateway records the override with the admin's subject and time; the page
   shows `Quota set to $60.00/month (was $25.00, the default). Applies to Pat's next call.`
6. Pat's next `synthesize_narration` call proceeds.
7. Later, on Pat's User Detail (S-03), the admin clicks the "Use default…" button and then "Use
   default" in the confirmation dialog (its "Cancel" button is first and the default); the override
   is cleared and the page shows `Quota reset to the default ($25.00/month).`
8. The admin clicks the "Cache" link; Cache (S-04) shows Size against Size cap
   (`Storage:CacheMaxGb`), Clips, Hit rate this month, and Evicted this month.
9. The admin clicks the "Sign out" button in the app header; the browser session ends and Signed
   Out (S-06) shows `You are signed out` with a "Sign in" link.

**Outcome:** The admin sees the organization's spend and steers it without touching the server.
**Screens:** S-01 My Usage, S-02 Usage, S-03 User Detail, S-04 Cache, S-06 Signed Out

### Scenario 6: Solo user on the stdio server

**Actor:** Solo user
**Trigger:** A person outside any organization wants narrated videos.
**Goal:** Use the same skills with their own key.

**Steps:**
1. The user exports `PEAK_MEDIA_OPENROUTER_API_KEY` and runs `claude mcp add peak-media --
   <path-to>/PeakMediaMcpServer` (or the published tool command).
2. On start with no key set, the server writes `Error: PEAK_MEDIA_OPENROUTER_API_KEY is not set.
   Export your OpenRouter key and restart.` to stderr and exits 1.
3. With the key set, `/mcp` lists `synthesize_narration` and `list_voices` only.
4. The skill calls `synthesize_narration` exactly as in Scenario 2.
5. The server writes each clip into `~/.cache/peak-media/tts/<cacheKey>` (or `PEAK_MEDIA_TTS_CACHE`)
   and returns `path` in place of `url`; `costUsd` is reported, no quota applies.
6. `spine.mjs` finds the files already in place and mixes as usual.
7. `--tts-model fake/tone` style testing: `model: "fake/tone"` is accepted here and refused on the
   gateway.

**Outcome:** Same skill behavior, user's own key, no server to run.

### Scenario 7: OpenRouter fails mid-spine

**Actor:** Skill user
**Trigger:** OpenRouter times out on clip 5 of 12.
**Goal:** Recover without paying twice.

**Steps:**
1. Clips 1–4 are synthesized, cached and charged; progress shows `clip 4/12 synthesized`.
2. Clip 5 times out after 120 s; the gateway retries once; the retry times out too.
3. The call returns `ERROR: OpenRouter did not answer for clip 5 of 12 (timeout, retried once).
   Clips 1–4 are saved; run the same request again to continue.`
4. The user retries; clips 1–4 are cache hits ($0), clips 5–12 are synthesized.
5. A 402 from OpenRouter (the shared account is out of credit) is not retried and returns `ERROR:
   the gateway's OpenRouter account is out of credit. Ask an administrator to add credit.`
6. A 401 from OpenRouter returns `ERROR: the gateway's OpenRouter key was rejected. Ask an
   administrator to check the gateway's key.` — the key never appears in the message or logs.

**Outcome:** Failures name the clip and the next action; finished work is never lost or re-billed.

### Scenario 8: Checking my usage in the browser

**Actor:** Skill user
**Trigger:** A user wants to look at their usage without Claude Code.
**Goal:** Read their own month's usage in a browser.

**Steps:**
1. The user opens `https://<gateway-host>/` and is redirected through the same Okta sign-in; the
   gateway sets a session cookie and shows My Usage (S-01).
2. On My Usage (S-01), the app header's navigation shows the "My usage" link only — no admin links.
3. My Usage (S-01) shows, for the month in the "Month" select: Spent, Quota, Remaining, Clips
   synthesized, Cache hits, and Resets — the same figures `my_usage` returns.
4. Opening `/admin` directly returns a 403 and shows Not Authorized (S-05): `This page is for
   gateway administrators.` with a "Go to My usage" link.
5. The user clicks the "Sign out" button in the app header; the browser session ends and Signed
   Out (S-06) shows `You are signed out` with a "Sign in" link.

**Outcome:** Users see their own usage in the browser under the same access rule as the tools.
**Screens:** S-01 My Usage, S-05 Not Authorized, S-06 Signed Out

## 6. System Interfaces & Data Flows

| Interface | Direction | Protocol | Purpose |
|---|---|---|---|
| `/mcp` | Claude Code → gateway | MCP Streamable HTTP, Bearer token | Tool calls, progress notifications |
| `/.well-known/oauth-protected-resource`, `/.well-known/oauth-authorization-server`, `/register`, `/authorize`, `/token`, `/oauth/callback` | Claude Code / browser ↔ gateway | OAuth 2.1 (PKCE, DCR) | Broker sign-in |
| Okta OIDC (`/authorize`, `/token`, JWKS) | Gateway ↔ Okta | OIDC authorization code | Upstream sign-in; subject, email, groups |
| `/clips/{token}` | `spine.mjs` → gateway | HTTPS GET, token in path | Download one WAV (15-minute, reusable) |
| `/version` | Anyone → gateway | HTTPS GET | `{name, version}` |
| `/`, `/admin/*` | Browser → gateway | HTTPS, cookie session | Dashboard (built last) |
| OpenRouter `POST /api/v1/audio/speech`, generation stats | Gateway / stdio → OpenRouter | HTTPS, Bearer key | Synthesis and real cost |
| stdio | Claude Code ↔ stdio server | MCP stdio | Same narration tools, local files |

| Data store | Owner | Contents |
|---|---|---|
| SQLite `Storage:DatabasePath` | Gateway | Users, quota overrides, usage log, cache index, download tokens, broker state |
| Audio directory on the data volume | Gateway | Cached WAVs named `<cacheKey>` |
| `~/.cache/peak-media/tts/` | User's machine | Local clip cache shared by the skills and the stdio server |

Data flow (gateway narration):

```
Claude Code ──synthesize_narration(clips)──▶ Gateway ──quota check──▶ SQLite
                                               │
                    per clip: cache hit? ──yes─┤
                                       no ─────┼──▶ OpenRouter /audio/speech (zdr)
                                               │◀── PCM + generation id → cost
                                               ├──▶ cache WAV + usage row (subject, cost, key)
           ◀── progress "clip n/N" ────────────┤
           ◀── markdown + JSON {cacheKey, url…}┘
Claude ──JSON──▶ spine.mjs ──GET /clips/{token}──▶ Gateway ──▶ WAV ──▶ ~/.cache/peak-media/tts/<cacheKey>
spine.mjs ──ffmpeg mix──▶ spine/narration.wav + manifest.json
```

## 7. Functional Summary

### Narration tools

| Feature | Gateway | Stdio |
|---|---|---|
| `synthesize_narration` — whole spine, cache first, progress per clip | ✔ (URLs) | ✔ (paths) |
| `list_voices` — allowed models, voices, defaults, style support | ✔ | ✔ |
| Input validation before spend (model, voice, ≤ 40 clips, ≤ 4,000 chars) | ✔ | ✔ |
| `provider.zdr: true` on every request | ✔ | ✔ |
| One retry on timeout / 5xx | ✔ | ✔ |
| `fake/tone` model | refused | ✔ |

### Usage and quotas (gateway)

| Feature | Who |
|---|---|
| Monthly USD quota, default $25, calendar month UTC | every user |
| Real cost charged per synthesized clip; hits $0 | every user |
| Refuse calls once month-to-date spend ≥ quota | every user |
| `my_usage` | every user |
| Quota override set / clear (dashboard **User detail**) | admin |

### Sign-in and access (gateway)

| Feature | Notes |
|---|---|
| OAuth 2.1 broker delegating to OIDC (Okta) | DCR on; PKCE |
| Dashboard admin by `groups` claim membership | `Admin:Group`; MCP tools have no roles |
| One access rule for tools and pages | own data for users; all data for admins |
| Refuse to start without sign-in config or OpenRouter key | no anonymous mode |

### Cache (gateway)

| Feature | Notes |
|---|---|
| Shared cache keyed by `sha256(model\0voice\0style\0text)` | byte-compatible with the skills |
| LRU eviction at `Storage:CacheMaxGb` (20 GB) | hit refreshes last-used |
| Download tokens, 15 minutes, reusable | expired → 410 |

### Dashboard (built last)

| Page | Who |
|---|---|
| My usage | every user |
| Usage (all users by month) | admin |
| User detail + quota edit | admin |
| Cache | admin |

## 8. Operational Constraints & Assumptions

| Constraint / assumption | Detail |
|---|---|
| Deployment | One Linux container per organization (`peakflames/peak-media-remote-mcp-server`), single replica, data volume at `/app/data` |
| Identity provider | Any OIDC provider; Okta in practice. The gateway's broker needs only a standard OIDC web app in Okta (no custom authorization server) |
| Okta app setup | Redirect URI `<gateway>/oauth/callback`; people assigned to the app may sign in; a `groups` claim in the ID token carrying the admin group |
| Development identity | A free Okta developer (Integrator) org with a test OIDC app and a `peakmedia-admins` group for hand verification; automated tests use a stub issuer |
| Secrets | `OpenRouter__ApiKey` and the OIDC client secret come only from environment variables |
| OpenRouter account | One organization account; "Allow Safety Retention" enabled once so Gemini works with ZDR; a credit limit on the key acts as the organization-wide ceiling |
| Cost | Real cost per clip comes from OpenRouter's generation statistics; if it cannot be read, the clip is charged from the configured model price and a warning is logged |
| Quota timing | Checked at call start; a call that starts under quota completes even if it ends over |
| Models | Allow-list and prices in `appsettings.json`, default: `google/gemini-3.8-flash-lite-tts`, `google/gemini-3.8-flash-tts`, `microsoft/mai-voice-2.1-flash`, `microsoft/mai-voice-2.1` |
| Limits per call | ≤ 40 clips, ≤ 4,000 characters per clip; OpenRouter timeout 120 s per clip |
| Privacy | Narration text is never stored or logged; usage rows hold counts and the cache key only |
| Client changes | The peak-media plugin adopts `docs/client-integration.md` in its own repository |
| Priority | Narration, cache and quotas first; the dashboard is planned last |

## 9. Glossary

| Term | Definition |
|---|---|
| **Gateway** | `PeakMediaRemoteMcpServer`, the organization's HTTP MCP server holding the shared key |
| **Stdio server** | `PeakMediaMcpServer`, the local MCP server using the user's own key |
| **Spine** | The timed narration track of a video (`spine/narration.wav` + `manifest.json`), built by `video-spine` |
| **Beat** | One section of `script.md`; one narration paragraph and one clip |
| **Clip** | One synthesized paragraph of speech; one WAV |
| **Beat grid** | The silent fallback timing the skills write when there is no narration |
| **Cache key** | `<sha256(model \0 voice \0 style \0 text)>.wav`, the clip's file name in every cache |
| **Cache hit** | A clip served from the cache without calling OpenRouter; costs $0 |
| **Style** | Delivery instructions sent to Gemini models as `instructions`; ignored by MAI |
| **wpm** | Words per minute of a clip; mood tags are not counted as words |
| **Slow clip** | A clip below 70% of the median wpm of its call — usually a mood tag read aloud |
| **ZDR** | Zero data retention, requested on every OpenRouter call with `provider.zdr: true` |
| **Quota** | A user's monthly spend limit in US dollars (default $25) |
| **Month-to-date spend** | The sum of a user's usage-row costs since 00:00 UTC on the 1st |
| **Broker** | The gateway acting as an OAuth 2.1 authorization server that delegates sign-in to Okta |
| **DCR** | Dynamic client registration (RFC 7591): MCP clients register themselves, no client ID to paste |
| **Admin group** | The OIDC `groups` value (`Admin:Group`) that marks a user as a gateway admin |
| **Download token** | The unguessable, 15-minute, reusable token in a clip's download URL |
| **LRU eviction** | Removing least-recently-used clips once the cache exceeds its size cap |
| **OpenRouter** | The model router whose `/api/v1/audio/speech` endpoint synthesizes speech |
