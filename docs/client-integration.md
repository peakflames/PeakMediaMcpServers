# Client integration contract

This is the contract a client — in practice the peak-media skills (`spine.mjs`, `sample-voice.mjs`)
— follows to obtain narration from either PeakMedia server. Both servers expose the same tools with
the same arguments and the same result fields; the only difference is where a clip lives:

| Server | Transport | OpenRouter key | Clip location field |
|---|---|---|---|
| Gateway (`PeakMediaRemoteMcpServer`) | MCP Streamable HTTP at `/mcp`, Okta sign-in | the organization's shared key, held by the gateway | `url` — a short-lived download link |
| Stdio server (`PeakMediaMcpServer`) | MCP stdio, no sign-in, no database | the user's own key | `path` — the WAV already in the local cache |

Register whichever server you use under the MCP server name **`peak-media`**, so a skill finds it the
same way in both cases.

## Connecting

### Gateway

```bash
claude mcp add --transport http peak-media https://<gateway-host>/mcp
```

Then in Claude Code run `/mcp` → `peak-media` → **Authenticate** and sign in through the
organization's Okta page once. No OpenRouter key and no client ID are needed.

### Stdio server (solo use, your own OpenRouter key)

Build or publish `src/PeakMediaMcpServer`, then:

```bash
export PEAK_MEDIA_OPENROUTER_API_KEY=sk-or-...    # in the shell Claude Code starts from
claude mcp add peak-media -- dotnet /path/to/PeakMediaMcpServer.dll
```

For a self-contained publish use `claude mcp add peak-media -- /path/to/PeakMediaMcpServer`. To store
the key in the MCP registration instead of the shell, pass it before the name:
`claude mcp add -e PEAK_MEDIA_OPENROUTER_API_KEY=sk-or-... peak-media -- dotnet /path/to/PeakMediaMcpServer.dll`.

Environment variables read by the stdio server:

| Variable | Meaning |
|---|---|
| `PEAK_MEDIA_OPENROUTER_API_KEY` | Your OpenRouter key. Required (unless the legacy variable is set). |
| `TTS_OPENROUTER_API_KEY` | Legacy name for the same key, used only when `PEAK_MEDIA_OPENROUTER_API_KEY` is not set. |
| `PEAK_MEDIA_TTS_CACHE` | The local cache directory. Default `~/.cache/peak-media/tts`. |

With no key set the server exits with code 1 and prints
`Error: PEAK_MEDIA_OPENROUTER_API_KEY is not set. Export your OpenRouter key and restart.` on standard
error. `PeakMediaMcpServer --version` prints `PeakMediaMcpServer v<semver>`.

The stdio server also accepts the offline model `fake/tone`: a sine tone per clip, no network call,
no cost — for testing a pipeline end to end.

## Tools

### `synthesize_narration`

Turns a script of 1 to 40 clips into 16-bit mono 24 kHz WAV files, in order, sending one MCP progress
notification per clip when the request carries a progress token.

| Argument | Type | Required | Meaning |
|---|---|---|---|
| `clips` | array of clip objects | yes | The clips in order, 1 to 40. |
| `clips[].n` | integer | no | The clip's number; defaults to its 1-based position. |
| `clips[].text` | string | yes | The words to speak, 1 to 4,000 characters. |
| `clips[].style` | string | no | Delivery style for this clip; overrides the call's `style`. |
| `clips[].voice` | string | no | Voice for this clip; overrides the call's `voice`. |
| `model` | string | no | A model id from `list_voices`; defaults to the server's default model. |
| `voice` | string | no | A voice of that model (case-insensitive); defaults to the model's default voice. |
| `style` | string | no | Delivery style sent as instructions to models that take one (Gemini). |

The result is markdown: a summary line, a warning per suspiciously slow clip, and exactly one fenced
`json` block holding an array with one entry per clip, in order. Each clip entry has these fields:

| Field | Type | Meaning |
|---|---|---|
| `n` | integer | The clip's number. |
| `cacheKey` | string | The clip's name in every cache (see below). |
| `durationMs` | integer | Audio length in milliseconds (WAV data bytes ÷ 48, rounded). |
| `words` | integer | Spoken words (mood tags such as `<Warm>` are not counted). |
| `wpm` | integer | Words per minute. |
| `cached` | boolean | True when the clip came from the cache instead of being synthesized. |
| `costUsd` | number | What this clip cost in US dollars; `0` for a cached clip. |
| `sampleRate` | integer | Always `24000`. |
| `url` | string | **Gateway only** — a download link for the WAV, valid for 15 minutes. |
| `path` | string | **Stdio server only** — the full path of the WAV in the local cache. |

Example entry (gateway; the stdio server has `path` in place of `url`):

```json
{"n":1,"cacheKey":"3f…a9.wav","durationMs":4120,"words":11,"wpm":160,"cached":false,"costUsd":0.0008,"sampleRate":24000,"url":"https://<gateway-host>/clips/<token>"}
```

### `list_voices`

No arguments. Lists the allowed models, each with its voices, its default voice and whether a style
is sent to it, and names the default model. The stdio server lists the gateway's models plus
`fake/tone`.

### `my_usage`

No arguments. **Gateway only.** Shows the signed-in caller's usage for the current calendar month
(UTC): month, amount spent, monthly quota, remaining amount, clips synthesized, cache hits and when
the quota resets.

## The cache key

A clip's `cacheKey` is the lowercase hex **SHA-256** of the UTF-8 bytes of **model, voice, style and
text joined by NUL bytes** (`model\0voice\0style\0text`), followed by **`.wav`**. The style is the
resolved one (the clip's `style`, else the call's, else empty) and the voice is spelled as
`list_voices` spells it. Any difference in the four inputs is a different clip, so a client may
compute the key itself to check its local cache before calling.

## Selection order (which source a skill uses)

A skill obtains narration from the first source that is available, in this order:

1. **A connected `peak-media` MCP server** (gateway or stdio) — call `synthesize_narration`.
2. **A personal OpenRouter key** (`PEAK_MEDIA_OPENROUTER_API_KEY`, or legacy `TTS_OPENROUTER_API_KEY`)
   — the skill's own direct OpenRouter call.
3. **The beat grid** — no narration audio; timing from the script alone.

A tool result whose text starts with **`ERROR:`** is reported to the user as it stands — it names the
problem and the next action (for example a reached quota or an unknown voice). It is **never followed
by a fallback to a personal key**: an organization that routes narration through the gateway decided
where its spend goes, and a silent fallback would bypass that decision. The fallback order applies
only when no `peak-media` server is connected.

## Download into the cache (gateway results)

The local cache is the directory `~/.cache/peak-media/tts/` (or `PEAK_MEDIA_TTS_CACHE`); each clip is
the file `~/.cache/peak-media/tts/<cacheKey>`.

For a gateway result, for each clip entry in order:

1. If `~/.cache/peak-media/tts/<cacheKey>` already exists (and is a valid WAV), **skip** it — no
   download.
2. Otherwise download its `url` (a plain `GET`, no Authorization header — the link is the credential)
   to a temporary file and move it to `~/.cache/peak-media/tts/<cacheKey>`.
3. **Verify** every file is a valid WAV — `RIFF`/`WAVE`, PCM, 16-bit, mono, 24 kHz, data length
   matching the file — **before mixing**. A file that fails is deleted and downloaded again.

A download answered **410** means the **link expired** (links last 15 minutes). Call
`synthesize_narration` again with the same request: the clips are served from the gateway's cache, so
the new call returns **new links at no cost**. A 404 means the link is unknown.

For a stdio result there is nothing to download: each `path` is **already inside the local cache**
(`<cache directory>/<cacheKey>`), so it **needs no download** — verify it as in step 3 and mix it.

## Errors

Every failure is a tool result starting `ERROR:`, of the form `ERROR: <problem>. <next action>`,
identical on both servers for the same input — for example
`ERROR: unknown voice 'Rasalgeti' for google/gemini-3.8-flash-lite-tts. Call list_voices and pick one of its Gemini voices.`
A failure part-way through a call names the failing clip and keeps every earlier clip cached, e.g.
`ERROR: OpenRouter did not answer for clip 2 of 3 (HTTP 503, retried once). Clip 1 is saved; run the same request again to continue.`
Only messages about whose key or storage failed differ: the gateway points at an administrator, the
stdio server at your own key or cache directory.
