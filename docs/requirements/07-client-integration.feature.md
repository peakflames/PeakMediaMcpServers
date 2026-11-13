Feature: 7.0 Client Integration Contract
    As the author of the peak-media skills
    I want one stable, written contract for obtaining narration from either server
    So that I can change spine.mjs and sample-voice.mjs once, with confidence, and keep a single code path


# --------------------------------------------------------------------------------------------------
# docs/client-integration.md
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-07-LaYp8xj] The client integration document shall state the arguments and result fields of synthesize_narration, list_voices and my_usage as the running servers expose them
    Given the repository at the released version
    When a reader opens 'docs/client-integration.md'
    Then it should name every argument in the gateway's synthesize_narration, list_voices and my_usage input schemas
    And it should name every clip result field: "n", "cacheKey", "durationMs", "words", "wpm", "cached", "costUsd", "sampleRate", and "url" (gateway) or "path" (stdio)
    And it should state that the cacheKey is the SHA-256 of model, voice, style and text joined by NUL bytes, followed by ".wav"

Scenario: [TOR-07-1XYMIbC] The client integration document shall state the skill's selection order and that a gateway error never falls back to a personal key
    Given the repository at the released version
    When a reader opens 'docs/client-integration.md'
    Then it should state the selection order: a connected "peak-media" MCP server first, then a personal OpenRouter key, then the beat grid
    And it should state that a tool result starting with "ERROR:" is reported to the user and is never followed by a fallback to a personal key

Scenario: [TOR-07-8MvsuuM] The client integration document shall describe the download-into-cache step for gateway results
    Given the repository at the released version
    When a reader opens 'docs/client-integration.md'
    Then it should state that each clip "url" is downloaded to "~/.cache/peak-media/tts/<cacheKey>", skipping files already present
    And it should state that each downloaded file is verified to be a valid WAV before mixing
    And it should state that a 410 response means the link expired and that calling synthesize_narration again returns new links at no cost
    And it should state that a stdio result's "path" is already inside the local cache and needs no download

Scenario: [TOR-07-akAqnvh] The client integration document shall give the "claude mcp add" command for the gateway and for the stdio server, both under the name "peak-media"
    Given the repository at the released version
    When a reader opens 'docs/client-integration.md'
    Then it should contain the command "claude mcp add --transport http peak-media https://<gateway-host>/mcp"
    And it should contain a "claude mcp add peak-media --" command that starts PeakMediaMcpServer
    And it should name the environment variables PEAK_MEDIA_OPENROUTER_API_KEY, TTS_OPENROUTER_API_KEY and PEAK_MEDIA_TTS_CACHE for the stdio server
