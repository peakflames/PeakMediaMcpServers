Feature: 4.0 Shared Audio Cache
    As a skill user who re-runs spines, and an admin who pays the bill
    I want every clip to be bought once for the whole organization and to stay available after a failure
    So that an unchanged script costs nothing, an interrupted run never costs twice, and the cache earns its keep


# --------------------------------------------------------------------------------------------------
# Cache key and hits
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-04-lRsfqBV] The gateway shall name every clip by the lowercase hex SHA-256 of model, voice, style and text joined by NUL bytes, followed by ".wav"
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with model "google/gemini-3.8-flash-lite-tts", voice "Rasalgethi", style "Plain." and one clip with text "Hello there."
    Then the clip entry's "cacheKey" should equal the lowercase hex SHA-256 of the UTF-8 bytes of "google/gemini-3.8-flash-lite-tts\0Rasalgethi\0Plain.\0Hello there." followed by ".wav"

Scenario: [TOR-04-csNgnyQ] The gateway shall serve a clip one caller has synthesized to any other caller from the cache at no cost
    Given caller "pat@example.com" has synthesized a clip with a given model, voice, style and text
    When caller "sam@example.com" calls synthesize_narration with the same model, voice, style and text
    Then the clip entry should have "cached": true and "costUsd": 0
    And the fake OpenRouter should have received zero requests during the second call
    And "sam@example.com"'s my_usage should show spent "$0.00" and cache hits "1"

Scenario: [TOR-04-2qCsalR] The gateway shall serve a repeated, unchanged spine entirely from the cache with zero OpenRouter calls
    Given a signed-in caller has synthesized a spine of 12 clips
    When the caller calls synthesize_narration again with the same model, voice, style and texts
    Then the tool result text should contain the line "12 clips · 12 cached · $0.00 this call"
    And every progress notification's message should match /^clip \d+\/12 cached$/
    And the fake OpenRouter should have received zero requests during the second call
    And the caller's month-to-date spend should be unchanged by the second call

Scenario: [TOR-04-TgW6vsE] The gateway shall treat a clip as uncached when its model, voice, style or text differs from every cached clip
    Given a signed-in caller has synthesized a clip with voice "Rasalgethi", style "Plain." and text "Hello there."
    When the caller calls synthesize_narration with the same model and these three clips:
        | n | voice      | style    | text          |
        | 1 | Kore       | Plain.   | Hello there.  |
        | 2 | Rasalgethi | Excited. | Hello there.  |
        | 3 | Rasalgethi | Plain.   | Hello there!  |
    Then every clip entry should have "cached": false
    And the three "cacheKey" values should differ from each other and from the first clip's

Scenario: [TOR-04-PXIafcT] The gateway shall cache each clip the moment it is synthesized so that a retry after a failed call pays only for the clips still missing
    Given a synthesize_narration call for 12 uncached clips failed at clip 5 after clips 1 to 4 were synthesized
    And the fake OpenRouter now answers every request with audio
    When the caller calls synthesize_narration again with the same 12 clips
    Then clip entries 1 to 4 should have "cached": true and "costUsd": 0
    And clip entries 5 to 12 should have "cached": false
    And the fake OpenRouter should have received exactly 8 requests during the second call


# --------------------------------------------------------------------------------------------------
# Clip downloads
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-04-DYooFQF] The gateway shall serve a clip's WAV at its download URL without a bearer token, repeatedly, for 15 minutes
    Given a signed-in caller has received a clip "url" from synthesize_narration
    When a client sends GET to that url with no Authorization header at 1 minute and again at 14 minutes after the result
    Then both response statuses should be 200
    And both responses should have Content-Type "audio/wav"
    And both bodies should be byte-identical

Scenario: [TOR-04-bIGlR3O] The gateway shall answer a download of an expired clip URL with 410 Gone and a problem-details body saying to call synthesize_narration again
    Given a signed-in caller has received a clip "url" from synthesize_narration
    When a client sends GET to that url 16 minutes after the result
    Then the response status should be 410
    And the response Content-Type should be "application/problem+json"
    And the response body's "detail" should be "This download link has expired. Call synthesize_narration again with the same request for a new link; cached clips are free."

Scenario: [TOR-04-xYMUNYV] The gateway shall renew expired download URLs through a repeated call that costs nothing
    Given a signed-in caller holds a synthesize_narration result for 12 clips whose URLs have expired
    When the caller calls synthesize_narration again with the same request
    Then every clip entry should have "cached": true and a "url" that differs from the expired one
    And a GET of every new "url" should return status 200
    And the caller's month-to-date spend should be unchanged

Scenario: [TOR-04-SqAxPKp] The gateway shall issue download URLs whose token cannot be derived from the clip
    Given two signed-in callers have each received a "url" for the same cached clip
    When the two URLs are compared
    Then the two URLs should differ
    And neither URL should contain the clip's "cacheKey"
    And each URL's token should be at least 32 URL-safe characters long


# --------------------------------------------------------------------------------------------------
# Size cap and eviction
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-04-SGh52Vj] The gateway shall remove least-recently-used clips once the cache exceeds Storage:CacheMaxGb
    Given Storage:CacheMaxGb is set so that the cache holds at most 3 clips of the test size
    And clips A, B and C were synthesized in that order
    When a caller synthesizes clip D and the eviction service has run
    Then a synthesize_narration call for clip A should report "cached": false
    And a synthesize_narration call for clips B, C and D, made before that call, should have reported "cached": true for each

Scenario: [TOR-04-rYFhU7O] The gateway shall refresh a clip's last-used time on every cache hit
    Given Storage:CacheMaxGb is set so that the cache holds at most 3 clips of the test size
    And clips A, B and C were synthesized in that order
    And a caller has since received clip A as a cache hit
    When a caller synthesizes clip D and the eviction service has run
    Then a synthesize_narration call for clip A should report "cached": true
    And a synthesize_narration call for clip B should report "cached": false
