Feature: 5.0 Quotas and Usage
    As a skill user spending shared credit, and an admin accountable for it
    I want each person's spend metered at real cost against a monthly limit, with a clear answer when the limit is reached
    So that nobody is surprised by a refusal, nobody can run up the organization's bill unchecked, and I always know where I stand


# --------------------------------------------------------------------------------------------------
# Charging
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-05-MUV56KT] The gateway shall charge the caller the real cost OpenRouter reports for each synthesized clip
    Given a signed-in caller with no usage this month
    And the fake OpenRouter reports generation costs of $0.0031, $0.0042 and $0.0027 for three clips
    When the caller calls synthesize_narration with those three uncached clips
    Then the clip entries' "costUsd" should be 0.0031, 0.0042 and 0.0027
    And the tool result's summary should show "$0.0100 this call"
    And the caller's my_usage should show spent "$0.01" and clips synthesized "3"

Scenario: [TOR-05-dFazrDm] The gateway shall charge a clip from the configured model price and log a warning when OpenRouter's reported cost cannot be read
    Given a signed-in caller with no usage this month
    And the fake OpenRouter returns audio for a clip but fails every generation-statistics request
    When the caller calls synthesize_narration with that one uncached clip of 1,000 characters
    Then the clip entry's "costUsd" should equal the configured price of the model for 1,000 characters
    And the gateway's log should contain a "[WRN]" record saying the cost was estimated from the configured price

Scenario: [TOR-05-DIrxTPy] The gateway shall keep the charge for clips already synthesized when a call fails part-way
    Given a signed-in caller with no usage this month
    And the fake OpenRouter reports a cost of $0.0030 for each of clips 1 to 4 and never answers for clip 5
    When the caller calls synthesize_narration with 12 uncached clips and the call fails at clip 5
    Then the caller's my_usage should show spent "$0.01" and clips synthesized "4"


# --------------------------------------------------------------------------------------------------
# Monthly quota
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-05-3JuMxwL] The gateway shall give every caller without an override the configured default monthly quota of $25.00
    Given the configuration leaves the default monthly quota at 25
    And a signed-in caller for whom no quota override exists
    When the caller calls my_usage
    Then the result should show quota "$25.00"

Scenario: [TOR-05-3cXUiMQ] The gateway shall let a call that starts under quota run to completion and shall note in its result when the caller is then over quota
    Given a signed-in caller has spent $24.80 of a $25.00 quota this month
    And the fake OpenRouter reports a total cost of $0.52 for the clips of the call
    When the caller calls synthesize_narration with those uncached clips
    Then every clip should be synthesized and the tool result text should not start with "ERROR:"
    And the tool result text should contain "You are now over your monthly quota ($25.32 of $25.00)."

Scenario: [TOR-05-N0KTSFQ] The gateway shall refuse synthesize_narration before any synthesis when the caller's month-to-date spend exceeds their quota, naming the spend, the quota and the reset time
    Given the current time is 2026-11-12 10:00 UTC
    And a signed-in caller has spent $25.32 of a $25.00 quota this month
    When the caller calls synthesize_narration with one uncached clip
    Then the tool result text should be "ERROR: monthly quota of $25.00 reached ($25.32 spent this month). Try again after 2026-12-01 00:00 UTC or ask an administrator to raise your quota."
    And the fake OpenRouter should have received zero requests

Scenario: [TOR-05-HqJn98D] The gateway shall refuse synthesize_narration when the caller's month-to-date spend equals their quota exactly
    Given the current time is 2026-11-12 10:00 UTC
    And a signed-in caller has spent exactly $25.00 of a $25.00 quota this month
    When the caller calls synthesize_narration with one uncached clip
    Then the tool result text should be "ERROR: monthly quota of $25.00 reached ($25.00 spent this month). Try again after 2026-12-01 00:00 UTC or ask an administrator to raise your quota."

Scenario: [TOR-05-zi52cgw] The gateway shall refuse an over-quota caller even when every clip of the call is already cached
    Given a signed-in caller has spent $25.32 of a $25.00 quota this month
    And every clip of the call is in the gateway cache
    When the caller calls synthesize_narration with those clips
    Then the tool result text should start with "ERROR: monthly quota of $25.00 reached"
    And the tool result should contain no clip URL

Scenario: [TOR-05-efm0UOd] The gateway shall count month-to-date spend from 00:00 UTC on the first day of the calendar month
    Given a signed-in caller spent $25.32 of a $25.00 quota between 2026-11-01 and 2026-11-30 UTC
    And the current time is 2026-12-01 00:00 UTC
    When the caller calls synthesize_narration with one uncached clip
    Then the tool result text should not start with "ERROR:"
    And the caller's my_usage should show month "2026-12" and a spent amount equal to the cost of that one clip

Scenario: [TOR-05-babYeNE] The gateway shall apply an administrator's quota override to the caller's next call
    Given a signed-in caller has spent $25.32 of the default $25.00 quota this month
    And an administrator has since set that caller's monthly quota to $60.00
    When the caller calls synthesize_narration with one uncached clip
    Then the tool result text should not start with "ERROR:"
    And the caller's my_usage should show quota "$60.00"


# --------------------------------------------------------------------------------------------------
# my_usage
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-05-bLVMjT4] The gateway shall report, through my_usage, the caller's month, spend, quota, remaining amount, clips synthesized, cache hits and reset time
    Given the current time is 2026-11-12 10:00 UTC
    And a signed-in caller with a $25.00 quota has this month 418 synthesized clips costing $25.32 in total and 95 cache hits
    When the caller calls my_usage
    Then the result should show the following values:
        | field             | value                 |
        | month             | 2026-11               |
        | spent             | $25.32                |
        | quota             | $25.00                |
        | remaining         | $0.00                 |
        | clips synthesized | 418                   |
        | cache hits        | 95                    |
        | resets            | 2026-12-01 00:00 UTC  |

Scenario: [TOR-05-0pHv4Fb] The gateway shall report the remaining amount as quota minus spend, in US dollars to the cent, when the caller is under quota
    Given a signed-in caller has spent $12.34 of a $25.00 quota this month
    When the caller calls my_usage
    Then the result should show spent "$12.34", quota "$25.00" and remaining "$12.66"

Scenario: [TOR-05-WmxtWmi] The gateway shall answer my_usage for a caller who is over quota
    Given a signed-in caller has spent $25.32 of a $25.00 quota this month
    When the caller calls my_usage
    Then the tool result text should not start with "ERROR:"
    And the result should show spent "$25.32"


# --------------------------------------------------------------------------------------------------
# Privacy of narration text
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-05-ryNGNXA] The gateway shall never store or log narration text
    Given a signed-in caller under quota
    When the caller calls synthesize_narration with one clip whose text is "Zebra-quartz-lantern-7391 is the marker sentence."
    Then the SQLite database file at Storage:DatabasePath should not contain the string "Zebra-quartz-lantern-7391"
    And neither the standard error nor any file under 'logs/' should contain the string "Zebra-quartz-lantern-7391"
