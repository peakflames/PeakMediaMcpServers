Feature: 8.0 Usage Dashboard
    As a skill user checking my own month, or a gateway admin accountable for everyone's
    I want a plain browser dashboard that shows spend against quota and lets an admin change a person's quota
    So that I can see where I stand without Claude Code, and an admin can steer the organization's spend without touching the server


# --------------------------------------------------------------------------------------------------
# Sign-in, roles and sign-out
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-08-7gPJfwq] The dashboard shall redirect a browser with no session to the organization's OIDC sign-in page
    Given a browser with no gateway session cookie
    When the browser opens the gateway's "/"
    Then the browser should be redirected to the configured OIDC provider's authorization endpoint
    And no usage figure should have been sent to the browser

Scenario: [TOR-08-RiNk9re] The dashboard shall land a person on My Usage (S-01) after a successful sign-in and show their email in the app header
    Given a browser with no gateway session cookie opens the gateway's "/"
    When the person signs in at the OIDC provider as "pat@example.com"
    Then the browser should show My Usage (S-01) with the heading "My usage"
    And the app header should contain the text "pat@example.com" and a "Sign out" button
    And the footer should contain text matching /PeakMediaRemoteMcpServer v\d+\.\d+\.\d+/

Scenario: [TOR-08-nLHs0pq] The dashboard shall land a member of the Okta admin group on My Usage (S-01) with the administrator navigation after a real Okta sign-in
    # Verification: operator-observed
    Given the gateway is configured against an Okta developer org whose ID token carries a groups claim
    And the person is a member of the group configured as Admin:Group
    When the person opens the gateway's "/" and signs in on the Okta page
    Then the browser should show My Usage (S-01)
    And the app header's navigation should show the "My usage", "Usage" and "Cache" links

Scenario: [TOR-08-XOxyf4r] The dashboard shall show a person outside the admin group the "My usage" navigation link only
    Given "pat@example.com" is signed in with a groups claim that does not contain the configured Admin:Group
    When the person views My Usage (S-01)
    Then the app header's navigation should contain the "My usage" link
    And the page should contain no "Usage" link and no "Cache" link, enabled or disabled

Scenario: [TOR-08-D1L4ilb] The dashboard shall show a member of the admin group the "My usage", "Usage" and "Cache" navigation links
    Given "ada@example.com" is signed in with a groups claim that contains the configured Admin:Group
    When the person views My Usage (S-01)
    Then the app header's navigation should contain exactly the links "My usage", "Usage" and "Cache"

Scenario: [TOR-08-iCZ63da] The dashboard shall answer every /admin address opened by a non-administrator with 403 and Not Authorized (S-05)
    Given "pat@example.com" is signed in without the admin role
    When the person opens each of "/admin", "/admin/cache" and the "/admin/users/{id}" address of another user
    Then every response status should be 403
    And every page should be Not Authorized (S-05) with the text "This page is for gateway administrators." and a "Go to My usage" link
    And no page should contain another user's email or any usage figure

Scenario: [TOR-08-Z8HzUrl] The dashboard shall take a person from Not Authorized (S-05) to My Usage (S-01) through the "Go to My usage" link
    Given "pat@example.com" is signed in without the admin role and is on Not Authorized (S-05)
    When the person clicks the "Go to My usage" link
    Then the browser should show My Usage (S-01)

Scenario: [TOR-08-F0Gau1p] The dashboard shall end the browser session and show Signed Out (S-06) when the "Sign out" button is used
    Given "pat@example.com" is signed in and is on My Usage (S-01)
    When the person clicks the "Sign out" button in the app header
    Then the browser should show Signed Out (S-06) with the text "You are signed out" and a "Sign in" link
    And opening the gateway's "/" afterwards should redirect to the OIDC provider's sign-in page

Scenario: [TOR-08-qmHrQZN] The dashboard shall start a new sign-in from the "Sign in" link on Signed Out (S-06)
    Given a person is on Signed Out (S-06)
    When the person clicks the "Sign in" link
    Then the browser should be redirected to the configured OIDC provider's authorization endpoint

Scenario: [TOR-08-leBAXoJ] The dashboard shall collapse the navigation, the signed-in email and the "Sign out" button into a sheet behind a menu button below 480 CSS px
    Given "ada@example.com" is signed in as an administrator and the viewport is 400 CSS px wide
    When the person views My Usage (S-01)
    Then the "Usage" and "Cache" links should not be visible and a menu button should be visible in the app header
    When the person activates the menu button
    Then a sheet should show the "My usage", "Usage" and "Cache" links, the text "ada@example.com" and the "Sign out" button

Scenario: [TOR-08-fX9XW5F] The dashboard shall offer a light, dark and system theme choice and apply the dark theme through the "dark" class on the html element
    Given a signed-in person is on My Usage (S-01) in the light theme
    When the person chooses the dark theme with the theme toggle in the app header
    Then the html element should have the class "dark"
    And the page background's computed color should differ from its light-theme color
    And the choice should still be applied after the page is reloaded


# --------------------------------------------------------------------------------------------------
# My Usage (S-01)
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-08-v8yv7CR] The dashboard shall show on My Usage (S-01) the same figures my_usage returns for the signed-in person
    Given the current time is 2026-11-12 10:00 UTC
    And "pat@example.com" with a $25.00 quota has this month 40 synthesized clips costing $12.34 in total and 9 cache hits
    And another user has spent $3.00 this month
    When "pat@example.com" views My Usage (S-01)
    Then the stat cards should show the following values:
        | card              | value                 |
        | Spent             | $12.34                |
        | Quota             | $25.00                |
        | Remaining         | $12.66                |
        | Clips synthesized | 40                    |
        | Cache hits        | 9                     |
        | Resets            | 2026-12-01 00:00 UTC  |
    And the page should not contain the other user's email

Scenario: [TOR-08-TZrQMGu] The dashboard shall show on My Usage (S-01) the figures of the month chosen in the "Month" select
    Given "pat@example.com" spent $4.00 in 2026-10 and $12.34 in 2026-11
    And the person is on My Usage (S-01) showing 2026-11
    When the person picks "2026-10" in the "Month" select
    Then the Spent card should show "$4.00"

Scenario: [TOR-08-0OVqiet] The dashboard shall show a quota notice on My Usage (S-01) when the person's spend has reached their quota
    Given the current time is 2026-11-12 10:00 UTC
    And "pat@example.com" has spent $25.32 of a $25.00 quota this month
    When the person views My Usage (S-01)
    Then the page should contain the text "You have reached your monthly quota ($25.32 of $25.00). Narration is paused until 2026-12-01 00:00 UTC. Ask an administrator to raise your quota."

Scenario: [TOR-08-6kh4CnB] The dashboard shall return My Usage (S-01) to the current month when the "Go to current month" button is used
    Given the current month is 2026-11 and "pat@example.com" has usage in 2026-11 and none in 2026-09
    And the person has picked "2026-09" in the "Month" select and sees "No usage in 2026-09"
    When the person clicks the "Go to current month" button
    Then the "Month" select should show "2026-11"
    And the stat cards should be visible

Scenario: [TOR-08-rxZtbxT] The dashboard shall reload My Usage (S-01) when the "Retry" button is used after a failed load
    Given the test fault switch forces the data source to fail
    And "pat@example.com" is on My Usage (S-01) showing "Couldn't load your usage. Check your connection and try again."
    When the test fault switch is cleared and the person clicks the "Retry" button
    Then the stat cards should be visible
    And the text "Couldn't load your usage." should no longer be visible


# --------------------------------------------------------------------------------------------------
# Usage (S-02)
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-08-kX399h8] The dashboard shall list on Usage (S-02) one row per user for the chosen month, sorted by Spent with the highest first
    Given an administrator is signed in
    And in 2026-11 "pat@example.com" spent $25.32, "sam@example.com" spent $12.34 and "lee@example.com" spent $0.40
    When the administrator clicks the "Usage" link and picks "2026-11" in the "Month" select
    Then the table on Usage (S-02) should have the column headers "Email", "Spent", "Quota", "Characters", "Clips", "Cache-hit rate" and "Status"
    And the table should have exactly 3 user rows in the order "pat@example.com", "sam@example.com", "lee@example.com"
    And the subtitle should read "2026-11 · 3 users · 1 over quota · sorted by Spent, highest first"

Scenario: [TOR-08-nzR0l2p] The dashboard shall mark on Usage (S-02) every user whose spend has reached their quota with an "OVER" badge
    Given an administrator is signed in
    And in 2026-11 "pat@example.com" spent $25.32 of $25.00, "kim@example.com" spent $25.00 of $25.00 and "sam@example.com" spent $12.34 of $25.00
    When the administrator views Usage (S-02) for "2026-11"
    Then the Status cells of "pat@example.com" and "kim@example.com" should contain the text "OVER"
    And the Status cell of "sam@example.com" should not contain the text "OVER"

Scenario: [TOR-08-nFpaAG7] The dashboard shall show on Usage (S-02) a "Total" footer row summing the month
    Given an administrator is signed in
    And in 2026-11 three users spent $25.32, $12.34 and $0.40 on 300, 120 and 6 clips
    When the administrator views Usage (S-02) for "2026-11"
    Then the table's footer row should be labelled "Total"
    And the footer row should show Spent "$38.06" and Clips "426"

Scenario: [TOR-08-rnr0ApF] The dashboard shall show an empty state on Usage (S-02) for a month in which nobody requested narration
    Given an administrator is signed in and no user has usage in 2026-09
    When the administrator views Usage (S-02) and picks "2026-09" in the "Month" select
    Then the main content region should contain the text "No usage in 2026-09" and "Nobody requested narration in this month."
    And the main content region should contain a "Go to current month" button
    When the administrator clicks the "Go to current month" button
    Then the "Month" select should show the current month

Scenario: [TOR-08-S8MYBHC] The dashboard shall show an error state with a working "Retry" button on Usage (S-02) when the usage data cannot be loaded
    Given an administrator is signed in
    And the test fault switch forces the data source to fail
    When the administrator views Usage (S-02) for "2026-11"
    Then the main content region should contain the text "Couldn't load usage for 2026-11. Check your connection and try again."
    And the main content region should contain a "Retry" button
    When the test fault switch is cleared and the administrator clicks the "Retry" button
    Then the users table should be visible

Scenario: [TOR-08-sfJ3IWh] The dashboard shall open User Detail (S-03) for a user when their email link on Usage (S-02) is used
    Given an administrator is on Usage (S-02) for "2026-11" and the table lists "pat@example.com"
    When the administrator clicks the "pat@example.com" link
    Then the browser should show User Detail (S-03) with the heading "pat@example.com"
    And the "Month" select on User Detail (S-03) should show "2026-11"


# --------------------------------------------------------------------------------------------------
# User Detail (S-03)
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-08-JeFRxDI] The dashboard shall list on User Detail (S-03) one row per clip of the user's month, newest first, without the narration text
    Given an administrator is signed in
    And "pat@example.com" requested in 2026-11 a clip with the text "Zebra-quartz-lantern-7391 is the marker sentence." and later the same clip again as a cache hit
    When the administrator views User Detail (S-03) for "pat@example.com" and "2026-11"
    Then the table should have the column headers "Time (UTC)", "Model", "Voice", "Characters", "Cost" and "Source"
    And the first row's Source should be "cached" with Cost "$0.00" and the second row's Source should be "synthesized"
    And the page should not contain the string "Zebra-quartz-lantern-7391"

Scenario: [TOR-08-i1syL2k] The dashboard shall summarize on User Detail (S-03) the user's month: spend against quota, an "OVER" badge when reached, clips and cache-hit rate
    Given an administrator is signed in
    And "pat@example.com" has in 2026-11 spent $25.32 of a $25.00 quota on 300 synthesized clips and 100 cache hits
    When the administrator views User Detail (S-03) for "pat@example.com" and "2026-11"
    Then the subtitle should contain "2026-11", "$25.32 of $25.00", "OVER", "400 clips" and "25% cache hits"

Scenario: [TOR-08-iUG0WAC] The dashboard shall show on User Detail (S-03) the user's current monthly quota and whether it is the default
    Given an administrator is signed in and no quota override exists for "pat@example.com"
    When the administrator views User Detail (S-03) for "pat@example.com"
    Then the "Monthly quota (USD)" input should hold the value "25.00"
    And the quota form should contain the text "the default"

Scenario: [TOR-08-QNNE1fW] The dashboard shall save a quota override when an administrator enters an amount in "Monthly quota (USD)" and uses the "Save" button, and shall confirm it in text
    Given an administrator is on User Detail (S-03) for "pat@example.com", whose quota is the default $25.00
    When the administrator types "60" in the "Monthly quota (USD)" input and clicks the "Save" button
    Then the page should contain the text "Quota set to $60.00/month (was $25.00, the default). Applies to Pat's next call."
    And "pat@example.com"'s my_usage result should show quota "$60.00"

Scenario: [TOR-08-bjHma3b] The dashboard shall tell the administrator in text when a quota cannot be saved and keep the entered amount
    Given an administrator is on User Detail (S-03) for "pat@example.com"
    And the test fault switch forces the data source to fail
    When the administrator types "60" in the "Monthly quota (USD)" input and clicks the "Save" button
    Then the page should contain the text "Couldn't save the quota. Check your connection and press Save again."
    And the "Monthly quota (USD)" input should still hold the value "60"

Scenario: [TOR-08-6Vzjnrg] The dashboard shall clear a user's quota override when an administrator confirms "Use default" on User Detail (S-03)
    Given an administrator is on User Detail (S-03) for "pat@example.com", whose quota override is $60.00
    When the administrator clicks the "Use default…" button and then the "Use default" button in the confirmation dialog
    Then the page should contain the text "Quota reset to the default ($25.00/month)."
    And "pat@example.com"'s my_usage result should show quota "$25.00"

Scenario: [TOR-08-MCIZVXn] The dashboard shall show on User Detail (S-03) the rows of the month chosen in the "Month" select
    Given an administrator is on User Detail (S-03) for "pat@example.com", who has 2 usage rows in 2026-10 and 5 in 2026-11
    When the administrator picks "2026-10" in the "Month" select
    Then the table should have exactly 2 rows

Scenario: [TOR-08-wX5QWUB] The dashboard shall return an administrator from User Detail (S-03) to Usage (S-02) through the "Usage" breadcrumb link and, in the empty state, the "Back to Usage" link
    Given an administrator is on User Detail (S-03) for "pat@example.com" showing "No usage for pat@example.com in 2026-09"
    When the administrator clicks the "Back to Usage" link
    Then the browser should show Usage (S-02)
    When the administrator opens User Detail (S-03) for "pat@example.com" again and clicks the "Usage" breadcrumb link
    Then the browser should show Usage (S-02)

Scenario: [TOR-08-bydYtvm] The dashboard shall reload User Detail (S-03) when the "Retry" button is used after a failed load
    Given the test fault switch forces the data source to fail
    And an administrator is on User Detail (S-03) showing "Couldn't load usage for pat@example.com. Check your connection and try again."
    When the test fault switch is cleared and the administrator clicks the "Retry" button
    Then the requests table should be visible


# --------------------------------------------------------------------------------------------------
# Cache (S-04)
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-08-VHkMu93] The dashboard shall show on Cache (S-04) the cache's size, size cap, clip count, hit rate this month and evictions this month
    Given an administrator is signed in
    And the cache holds 12.4 GB in 5,210 clips under a 20 GB Storage:CacheMaxGb, with a 62% hit rate and 37 evictions this month
    When the administrator clicks the "Cache" link
    Then the stat cards on Cache (S-04) should show the following values:
        | card               | value   |
        | Size               | 12.4 GB |
        | Size cap           | 20 GB   |
        | Clips              | 5,210   |
        | Hit rate this month | 62%    |
        | Evicted this month | 37      |
    And the subtitle should read "12.4 GB of 20 GB used (62%) · least-recently-used clips are removed first when full"

Scenario: [TOR-08-5ZEXDBu] The dashboard shall show an empty state on Cache (S-04) when the cache holds no clips
    Given an administrator is signed in and the cache holds no clips
    When the administrator views Cache (S-04)
    Then the main content region should contain the text "The cache is empty" and "Clips appear here after the first narration request."
    And the main content region should contain a "Refresh" button

Scenario: [TOR-08-NeOMGCI] The dashboard shall show an error state with a working "Retry" button on Cache (S-04) when the cache statistics cannot be loaded
    Given an administrator is signed in
    And the test fault switch forces the data source to fail
    When the administrator views Cache (S-04)
    Then the main content region should contain the text "Couldn't load cache statistics. Check your connection and try again."
    And the main content region should contain a "Retry" button
    When the test fault switch is cleared and the administrator clicks the "Retry" button
    Then the stat cards should be visible

Scenario: [TOR-08-irZrgRw] The dashboard shall reload the figures on Cache (S-04) when the "Refresh" button is used
    Given an administrator is on Cache (S-04) and the Clips card shows "0"
    And a caller has since synthesized one clip
    When the administrator clicks the "Refresh" button
    Then the Clips card should show "1"
