Feature: 3.0 Narration
    As a skill user narrating a video from Claude Code
    I want one tool call to turn my whole script into speech, showing progress clip by clip and telling me plainly when something is wrong
    So that narration just works, I never wonder whether it silently failed, and no audio is spent on a request that could not succeed


# --------------------------------------------------------------------------------------------------
# list_voices
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-03-Vaata1w] The gateway shall list, through list_voices, every allowed model with its voices, its default voice and whether style is sent, and name the default model
    Given the gateway runs with the default model allow-list
    And a signed-in caller
    When the caller calls list_voices
    Then the result should list exactly these models:
        | model                             | default voice | style sent |
        | google/gemini-3.8-flash-lite-tts  | Rasalgethi    | yes        |
        | google/gemini-3.8-flash-tts       | Rasalgethi    | yes        |
        | microsoft/mai-voice-2.1-flash     | en-GB-Emily   | no         |
        | microsoft/mai-voice-2.1           | en-GB-Emily   | no         |
    And the result should name "google/gemini-3.8-flash-lite-tts" as the default model
    And each model should list at least one voice, including its default voice
    And the result should not list the model "fake/tone"


# --------------------------------------------------------------------------------------------------
# Validation before any spend
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-03-nt9zDad] The gateway shall refuse a synthesize_narration call naming a model that is not allowed, before any synthesis
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with model "acme/unknown-tts" and one clip
    Then the tool result text should be "ERROR: unknown model 'acme/unknown-tts'. Call list_voices and pick one of its models."
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-03-mPpT1yR] The gateway shall refuse a synthesize_narration call naming a voice the model does not have, before any synthesis
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with model "google/gemini-3.8-flash-lite-tts", voice "Rasalgeti" and one clip
    Then the tool result text should be "ERROR: unknown voice 'Rasalgeti' for google/gemini-3.8-flash-lite-tts. Call list_voices and pick one of its Gemini voices."
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-03-HLJlVSo] The gateway shall match voice names case-insensitively
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with model "google/gemini-3.8-flash-lite-tts", voice "rasalgethi" and one clip
    Then the tool result text should not start with "ERROR:"
    And the request received by the fake OpenRouter should carry the voice "Rasalgethi"

Scenario: [TOR-03-IcVxy00] The gateway shall refuse a synthesize_narration call containing a clip with empty text, before any synthesis
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with 3 clips where clip n=2 has the text "   "
    Then the tool result text should be "ERROR: clip 2 has no text. Give every clip between 1 and 4,000 characters of text."
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-03-eyAaGVV] The gateway shall refuse a synthesize_narration call with more than 40 clips, before any synthesis
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with 41 clips
    Then the tool result text should be "ERROR: 41 clips were given and the limit is 40 per call. Split the script into calls of at most 40 clips."
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-03-LeqCLZu] The gateway shall refuse a synthesize_narration call containing a clip longer than 4,000 characters, before any synthesis
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with 3 clips where clip n=3 has 4,001 characters of text
    Then the tool result text should be "ERROR: clip 3 has 4,001 characters and the limit is 4,000. Split it into shorter clips."
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-03-9ruVWBA] The gateway shall accept a synthesize_narration call at the limits of 40 clips and 4,000 characters per clip
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with 40 clips where clip n=1 has exactly 4,000 characters of text
    Then the tool result text should not start with "ERROR:"
    And the result's JSON block should contain 40 clip entries

Scenario: [TOR-03-uMFP7Oc] The gateway shall refuse the model "fake/tone"
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with model "fake/tone" and one clip
    Then the tool result text should be "ERROR: unknown model 'fake/tone'. Call list_voices and pick one of its models."
    And the fake OpenRouter should have received zero requests


# --------------------------------------------------------------------------------------------------
# Synthesis and result
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-03-IyOofVh] The gateway shall synthesize a spine of clips in one synthesize_narration call for a signed-in caller who has no OpenRouter key
    Given a signed-in caller under quota with no OpenRouter key in their environment
    And none of the 12 clips is in the gateway cache
    When the caller calls synthesize_narration with model "google/gemini-3.8-flash-lite-tts", voice "Rasalgethi", a style and clips n=1 to n=12
    Then the result's JSON block should contain 12 clip entries in the order n=1 to n=12
    And every clip entry should have "cached": false and a "url"
    And a GET of every "url" should return a WAV file

Scenario: [TOR-03-E04hqrD] The gateway shall return, for every clip, the fields n, cacheKey, durationMs, words, wpm, cached, costUsd, sampleRate and url in one fenced JSON block
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with 2 clips
    Then the tool result text should contain exactly one fenced JSON block
    And every clip entry in that block should have exactly the keys "n", "cacheKey", "durationMs", "words", "wpm", "cached", "costUsd", "sampleRate" and "url"
    And every "cacheKey" should match /^[0-9a-f]{64}\.wav$/

Scenario: [TOR-03-RevSWV4] The gateway shall summarize each synthesize_narration result in markdown with the clip count, cached count, cost of the call and median words per minute
    Given a signed-in caller under quota
    And 3 of the 12 clips are in the gateway cache
    And the fake OpenRouter reports a total cost of $0.0284 for the other 9 clips at a median of 127 wpm across the call
    When the caller calls synthesize_narration with the 12 clips
    Then the tool result text should contain the line "12 clips · 3 cached · $0.0284 this call · median 127 wpm"

Scenario: [TOR-03-J5FXU3j] The gateway shall return no audio bytes in a synthesize_narration result
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with 12 clips
    Then every content item of the tool result should be of type "text"
    And the tool result text should be shorter than 20,000 characters
    And the tool result text should not contain a base64 run of 200 or more characters

Scenario: [TOR-03-ehw1xk6] The gateway shall send one MCP progress notification per clip while a synthesize_narration call runs
    Given a signed-in caller under quota whose request carries a progress token
    And clips n=1 to n=3 of the 12 clips are in the gateway cache
    When the caller calls synthesize_narration with the 12 clips
    Then the caller should receive exactly 12 progress notifications, with progress 1 to 12 and total 12, before the result
    And the notification for clip 2 should have the message "clip 2/12 cached"
    And the notification for clip 4 should have a message matching /^clip 4\/12 synthesized \(\d+\.\d s audio, \$\d+\.\d{4}\)$/

Scenario: [TOR-03-eKeTzs6] The gateway shall deliver each clip as a 16-bit mono 24 kHz WAV file
    Given a signed-in caller has received a synthesize_narration result for one clip
    When the caller downloads the clip's "url"
    Then the body should start with a RIFF/WAVE header declaring PCM, 1 channel, 24000 Hz and 16 bits per sample
    And the clip entry's "sampleRate" should be 24000
    And the clip entry's "durationMs" should equal the WAV data length divided by 48 bytes per millisecond, rounded

Scenario: [TOR-03-0gjBH7R] The gateway shall count a clip's words without its mood tags when reporting words and wpm
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with one clip whose text is "<Warm> Welcome back to the show."
    Then the clip entry's "words" should be 5

Scenario: [TOR-03-SVUzauN] The gateway shall warn about every clip whose words per minute are below 70% of the median of its call
    Given a signed-in caller under quota
    And the fake OpenRouter returns audio at 130 wpm for clips 1 to 4 and at 80 wpm for clip 5
    When the caller calls synthesize_narration with the 5 clips
    Then the tool result's markdown should contain a warning naming clip 5, its 80 wpm and the median 130 wpm
    And the warning should say a mood tag may have been read aloud
    And the markdown should contain no warning naming clips 1 to 4


# --------------------------------------------------------------------------------------------------
# Requests to OpenRouter
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-03-UYCubxo] The gateway shall request each uncached clip from OpenRouter's /api/v1/audio/speech with PCM output and zero data retention
    Given a signed-in caller under quota
    And the clip is not in the gateway cache
    When the caller calls synthesize_narration with model "google/gemini-3.8-flash-lite-tts", voice "Rasalgethi" and one clip
    Then the fake OpenRouter should have received exactly one POST to /api/v1/audio/speech
    And that request's JSON body should contain the following keys and values:
        | key             | value                            |
        | model           | google/gemini-3.8-flash-lite-tts |
        | voice           | Rasalgethi                       |
        | response_format | pcm                              |
        | provider.zdr    | true                             |
    And that request should carry the gateway's OpenRouter key as a Bearer token

Scenario: [TOR-03-Ah5Slpu] The gateway shall send the style to Gemini models as "instructions" and shall send no style to MAI models
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with style "Plain, measured delivery." once with model "google/gemini-3.8-flash-lite-tts" and once with model "microsoft/mai-voice-2.1-flash"
    Then the Gemini request received by the fake OpenRouter should have "instructions" equal to "Plain, measured delivery."
    And the MAI request received by the fake OpenRouter should have no "instructions" key

Scenario: [TOR-03-JKfDgpt] The gateway shall use a clip's own style in place of the call's style when the clip gives one
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with model "google/gemini-3.8-flash-lite-tts", style "Plain." and clips n=1 with no style and n=2 with style "Excited."
    Then the request for clip 1 received by the fake OpenRouter should have "instructions" equal to "Plain."
    And the request for clip 2 received by the fake OpenRouter should have "instructions" equal to "Excited."


# --------------------------------------------------------------------------------------------------
# Upstream failures
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-03-u0NLjMV] The gateway shall retry a clip once when OpenRouter times out or answers with a 5xx status
    Given a signed-in caller under quota
    And the fake OpenRouter answers the first request for clip 2 with status 503 and every other request with audio
    When the caller calls synthesize_narration with 3 uncached clips
    Then the tool result text should not start with "ERROR:"
    And the fake OpenRouter should have received exactly 2 requests for clip 2 and 4 requests in total

Scenario: [TOR-03-5607RO5] The gateway shall fail a synthesize_narration call with an error naming the clip when OpenRouter does not answer for that clip after one retry
    Given a signed-in caller under quota
    And the fake OpenRouter answers clips 1 to 4 with audio and never answers for clip 5 within the per-clip timeout
    When the caller calls synthesize_narration with 12 uncached clips
    Then the tool result text should be "ERROR: OpenRouter did not answer for clip 5 of 12 (timeout, retried once). Clips 1–4 are saved; run the same request again to continue."
    And the fake OpenRouter should have received exactly 2 requests for clip 5 and zero requests for clips 6 to 12

Scenario: [TOR-03-FQcysYo] The gateway shall report an out-of-credit OpenRouter account without retrying
    Given a signed-in caller under quota
    And the fake OpenRouter answers every request with status 402
    When the caller calls synthesize_narration with one uncached clip
    Then the tool result text should be "ERROR: the gateway's OpenRouter account is out of credit. Ask an administrator to add credit."
    And the fake OpenRouter should have received exactly 1 request

Scenario: [TOR-03-A312w2t] The gateway shall report a rejected OpenRouter key without revealing the key
    Given the gateway's OpenRouter key is "sk-or-test-SECRET-KEY-123"
    And the fake OpenRouter answers every request with status 401
    When a signed-in caller under quota calls synthesize_narration with one uncached clip
    Then the tool result text should be "ERROR: the gateway's OpenRouter key was rejected. Ask an administrator to check the gateway's key."
    And neither the tool result nor the gateway's log should contain the string "sk-or-test-SECRET-KEY-123"
