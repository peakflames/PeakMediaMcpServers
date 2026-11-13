Feature: 6.0 Stdio Server
    As a solo user with my own OpenRouter key
    I want the same narration tools on my own machine, with no sign-in and no server to run
    So that the peak-media skills work for me exactly as they do inside an organization


# --------------------------------------------------------------------------------------------------
# Start-up and key
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-06-WYyXusz] The stdio server shall exit with code 1 and name the fix on standard error when no OpenRouter key is set
    Given neither PEAK_MEDIA_OPENROUTER_API_KEY nor TTS_OPENROUTER_API_KEY is set in the environment
    When the stdio server is started with no commandline args
    Then the standard error should contain the string "Error: PEAK_MEDIA_OPENROUTER_API_KEY is not set. Export your OpenRouter key and restart."
    And the exit code should be 1
    And the standard output should be empty

Scenario: [TOR-06-dX6DFFO] The stdio server shall accept the legacy TTS_OPENROUTER_API_KEY when PEAK_MEDIA_OPENROUTER_API_KEY is not set
    Given PEAK_MEDIA_OPENROUTER_API_KEY is not set and TTS_OPENROUTER_API_KEY is "sk-or-legacy-key"
    And the stdio server is started with no commandline args
    When the client calls synthesize_narration with one uncached clip
    Then the request received by the fake OpenRouter should carry the Bearer token "sk-or-legacy-key"

Scenario: [TOR-06-ej048Ep] The stdio server shall use PEAK_MEDIA_OPENROUTER_API_KEY in preference to TTS_OPENROUTER_API_KEY when both are set
    Given PEAK_MEDIA_OPENROUTER_API_KEY is "sk-or-new-key" and TTS_OPENROUTER_API_KEY is "sk-or-legacy-key"
    And the stdio server is started with no commandline args
    When the client calls synthesize_narration with one uncached clip
    Then the request received by the fake OpenRouter should carry the Bearer token "sk-or-new-key"

Scenario: [TOR-06-emXCHe2] The stdio server shall offer exactly the tools synthesize_narration and list_voices
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set
    And the stdio server is started with no commandline args
    When the client requests the MCP tool list
    Then the tool list should contain exactly "synthesize_narration" and "list_voices"


# --------------------------------------------------------------------------------------------------
# Narration into the local cache
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-06-heFgF6C] The stdio server shall write each clip into the local cache directory under its cacheKey and return its path in place of a url
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set and PEAK_MEDIA_TTS_CACHE is not set
    And the user's home directory is H
    When the client calls synthesize_narration with 2 uncached clips
    Then every clip entry should have a "path" equal to "H/.cache/peak-media/tts/" followed by its "cacheKey"
    And no clip entry should have a "url"
    And a valid 16-bit mono 24 kHz WAV file should exist at every "path"
    And every clip entry should have a "costUsd" equal to the cost the fake OpenRouter reported

Scenario: [TOR-06-vtaQIaK] The stdio server shall write clips into the directory named by PEAK_MEDIA_TTS_CACHE when it is set
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set and PEAK_MEDIA_TTS_CACHE is "/tmp/pm-cache"
    When the client calls synthesize_narration with one uncached clip
    Then the clip entry's "path" should be "/tmp/pm-cache/" followed by its "cacheKey"
    And a WAV file should exist at that path

Scenario: [TOR-06-D8tj7pH] The stdio server shall serve a clip already present in the local cache directory without calling OpenRouter
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set
    And the local cache directory already holds a valid WAV file named by the clip's cacheKey
    When the client calls synthesize_narration with that clip
    Then the clip entry should have "cached": true and "costUsd": 0
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-06-g9QibOG] The stdio server shall accept the model "fake/tone" and synthesize its clips without calling OpenRouter
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set
    When the client calls synthesize_narration with model "fake/tone" and 2 clips
    Then the tool result text should not start with "ERROR:"
    And a valid WAV file should exist at every clip entry's "path"
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-06-UeA7aSw] The stdio server shall request each uncached clip from OpenRouter with PCM output and zero data retention
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set
    When the client calls synthesize_narration with model "google/gemini-3.8-flash-lite-tts", voice "Rasalgethi" and one uncached clip
    Then the request received by the fake OpenRouter should have "response_format" equal to "pcm" and "provider.zdr" equal to true

Scenario: [TOR-06-i2lj5iF] The stdio server shall send one MCP progress notification per clip while a synthesize_narration call runs
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set
    And the client's request carries a progress token
    When the client calls synthesize_narration with 3 uncached clips
    Then the client should receive exactly 3 progress notifications, with progress 1 to 3 and total 3, before the result

Scenario: [TOR-06-fTgpl4m] The stdio server shall retry a clip once on a timeout or 5xx answer and then fail with an error naming the clip
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set
    And the fake OpenRouter answers clip 1 with audio and every request for clip 2 with status 503
    When the client calls synthesize_narration with 3 uncached clips
    Then the tool result text should be "ERROR: OpenRouter did not answer for clip 2 of 3 (HTTP 503, retried once). Clip 1 is saved; run the same request again to continue."
    And the fake OpenRouter should have received exactly 2 requests for clip 2
    And a WAV file for clip 1 should exist in the local cache directory


# --------------------------------------------------------------------------------------------------
# One contract with the gateway
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-06-PY6kH1r] The stdio server shall expose synthesize_narration and list_voices with the same arguments and result fields as the gateway, differing only in path for url
    Given the gateway's tool list obtained by a signed-in caller
    And the stdio server's tool list
    When the input schemas of synthesize_narration and of list_voices are compared between the two servers
    Then the input schemas should be identical
    And the keys of a clip entry returned by the stdio server should equal the keys of a clip entry returned by the gateway with "url" replaced by "path"

Scenario: [TOR-06-bPXmWnj] The stdio server shall refuse invalid synthesize_narration input with the same error text as the gateway
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set
    When the client calls synthesize_narration with model "google/gemini-3.8-flash-lite-tts", voice "Rasalgeti" and one clip
    Then the tool result text should be "ERROR: unknown voice 'Rasalgeti' for google/gemini-3.8-flash-lite-tts. Call list_voices and pick one of its Gemini voices."
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-06-FXNq0Rx] The stdio server shall list the model "fake/tone" in list_voices alongside the models the gateway allows
    Given PEAK_MEDIA_OPENROUTER_API_KEY is set
    When the client calls list_voices
    Then the result should list the models "google/gemini-3.8-flash-lite-tts", "google/gemini-3.8-flash-tts", "microsoft/mai-voice-2.1-flash", "microsoft/mai-voice-2.1" and "fake/tone"

Scenario: [TOR-06-GC17sBD] The stdio server shall never write narration text or the user's OpenRouter key to its log
    Given PEAK_MEDIA_OPENROUTER_API_KEY is "sk-or-test-SOLO-KEY-789"
    When the client calls synthesize_narration with one clip whose text is "Zebra-quartz-lantern-7391 is the marker sentence."
    Then neither the standard error nor any file under 'logs/' should contain the string "Zebra-quartz-lantern-7391"
    And neither the standard error nor any file under 'logs/' should contain the string "sk-or-test-SOLO-KEY-789"
