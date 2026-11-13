Feature: 1.0 Service Foundation
    As an operator who deploys the gateway, or a solo user who runs the stdio server
    I want each server to say what it is, which version it is, and exactly what is wrong when it cannot run
    So that I can trust a deployment at a glance and fix a bad one without reading source code


# --------------------------------------------------------------------------------------------------
# Tool Hygiene & Operability
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-01-9TZEFBD] The gateway shall expose its name and semantic version at GET /version as JSON {"name", "version"} without requiring sign-in
    Given the gateway is running
    And the request carries no Authorization header and no session cookie
    When the client sends GET /version
    Then the response status should be 200
    And the response body should be valid JSON whose "name" is "PeakMediaRemoteMcpServer"
    And the response body's "version" should match /^\d+\.\d+\.\d+$/

Scenario: [TOR-01-p6jE1nT] The gateway shall report in the MCP initialize handshake the same version that GET /version returns
    Given the gateway is running
    And a signed-in caller holds a gateway access token
    When the caller sends the MCP initialize request to /mcp
    Then the result's serverInfo.name should be "PeakMediaRemoteMcpServer"
    And the result's serverInfo.version should equal the "version" returned by GET /version

Scenario: [TOR-01-zQT2lRH] The stdio server shall print its name and semantic version to standard output when invoked with --version, exiting with code 0
    Given the user passes the commandline args '--version'
    When the stdio server is run
    Then the standard output should be exactly one line matching /^PeakMediaMcpServer v\d+\.\d+\.\d+$/
    And the exit code should be 0

Scenario: [TOR-01-p3K2fH3] The stdio server shall report in the MCP initialize handshake the same version that --version prints
    Given the environment variable PEAK_MEDIA_OPENROUTER_API_KEY is set
    And the stdio server is started with no commandline args
    When the client sends the MCP initialize request over standard input
    Then the result's serverInfo.name should be "PeakMediaMcpServer"
    And the result's serverInfo.version should equal the version printed by '--version'

Scenario: [TOR-01-0wIkgxQ] The gateway shall emit, as its first log record on startup, an Information record containing its name and semantic version
    Given the gateway is configured with sign-in and an OpenRouter key
    When the gateway is started
    Then the first record written to standard error should match /\[INF\] PeakMediaRemoteMcpServer v\d+\.\d+\.\d+ starting$/
    And the first record of the day's log file under 'logs/' should match the same pattern

Scenario: [TOR-01-7VjhchC] The stdio server shall emit, as its first log record on startup, an Information record containing its name and semantic version
    Given the environment variable PEAK_MEDIA_OPENROUTER_API_KEY is set
    When the stdio server is started with no commandline args
    Then the first record written to standard error should match /\[INF\] PeakMediaMcpServer v\d+\.\d+\.\d+ starting$/

Scenario: [TOR-01-VoM1MJ7] The gateway shall write every log record as plain text in the form "<timestamp with milliseconds and UTC offset> [<three-letter level>] <message>"
    Given the gateway is running
    When the client sends GET /version
    Then every record written to standard error should match /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[(VRB|DBG|INF|WRN|ERR|FTL)\] .+/

Scenario: [TOR-01-mDo59qy] The gateway shall write its log records to a daily rolling file under 'logs/' next to the binary as well as to standard error
    Given the gateway is started on the UTC date 2026-11-12
    When the client sends GET /version
    Then a file whose name contains "20261112" should exist under the 'logs/' directory next to the gateway binary
    And that file should contain a record matching /\[INF\] PeakMediaRemoteMcpServer v\d+\.\d+\.\d+ starting$/

Scenario: [TOR-01-6Smkepz] The stdio server shall write every log record as plain text in the form "<timestamp with milliseconds and UTC offset> [<three-letter level>] <message>" to standard error and to a daily rolling file under 'logs/'
    Given the environment variable PEAK_MEDIA_OPENROUTER_API_KEY is set
    When the stdio server is started with no commandline args
    Then every record written to standard error should match /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[(VRB|DBG|INF|WRN|ERR|FTL)\] .+/
    And a log file should exist under the 'logs/' directory next to the stdio server binary

Scenario: [TOR-01-spPaZpz] The stdio server shall exit with code 2 on an invalid invocation and name the next action on standard error
    Given the user passes the commandline args '--bogus-flag'
    When the stdio server is run
    Then the exit code should be 2
    And the standard error should contain the string "Error: unknown option '--bogus-flag'. Run PeakMediaMcpServer --version, or start it with no arguments to serve MCP over stdio."
    And the standard output should be empty

Scenario: [TOR-01-6Q14afY] The stdio server shall write only MCP protocol messages to standard output and shall write every log and diagnostic line to standard error
    Given the environment variable PEAK_MEDIA_OPENROUTER_API_KEY is set
    And the stdio server is started with no commandline args
    When the client completes the MCP initialize handshake and requests the tool list
    Then every line on the standard output should be a valid JSON-RPC 2.0 message
    And the standard error should contain the startup log record

Scenario: [TOR-01-9QIaBsB] The gateway shall write nothing to standard output
    Given the gateway is started
    When the client sends GET /version and then sends GET /mcp with no Authorization header
    Then the gateway's standard output should be empty
    And the gateway's standard error should contain the startup log record

Scenario: [TOR-01-Ji4u6Ta] The gateway shall return every MCP tool failure as a result whose text starts with "ERROR:" and names the problem and the next action
    Given a signed-in caller holds a gateway access token
    When the caller calls synthesize_narration with an empty 'clips' list
    Then the tool result text should be "ERROR: no clips were given. Send between 1 and 40 clips."
    And the tool result text should not contain an exception type name or a stack trace

Scenario: [TOR-01-B0JJ3Jt] The gateway shall answer a failed request to a non-MCP HTTP endpoint with an RFC 9457 problem-details body whose detail names the problem and the next action
    Given the gateway is running
    When the client sends GET /clips/not-a-real-token
    Then the response status should be 404
    And the response Content-Type should be "application/problem+json"
    And the response body should be valid JSON containing the following keys and values:
        | key    | value                                                                          |
        | title  | Not Found                                                                      |
        | status | 404                                                                            |
        | detail | No clip matches this download link. Call synthesize_narration again for a new link. |


# --------------------------------------------------------------------------------------------------
# Gateway start-up and deployment
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-01-2lmaDWE] The gateway shall refuse to start when McpAuth:Enabled is false
    Given the configuration sets McpAuth:Enabled to false
    And the environment variable OpenRouter__ApiKey is set
    When the gateway is started
    Then the process should exit with a non-zero exit code
    And the standard error should contain the string "McpAuth:Enabled is false. The gateway has no anonymous mode: set McpAuth:Enabled to true and configure the Broker settings, then restart."
    And no request to GET /version should be answered

Scenario: [TOR-01-EzMqmjt] The gateway shall refuse to start when the upstream sign-in provider is not configured
    Given the configuration sets McpAuth:Enabled to true
    And the environment variable Broker__UpstreamClientSecret is not set
    And the environment variable OpenRouter__ApiKey is set
    When the gateway is started
    Then the process should exit with a non-zero exit code
    And the standard error should contain the string "Broker__UpstreamClientSecret is not set. Set the OIDC client secret of the organization's sign-in app in the environment and restart."

Scenario: [TOR-01-a1AeUH3] The gateway shall refuse to start when the OpenRouter key is not set
    Given the configuration sets McpAuth:Enabled to true with the Broker settings present
    And the environment variable OpenRouter__ApiKey is not set
    When the gateway is started
    Then the process should exit with a non-zero exit code
    And the standard error should contain the string "OpenRouter__ApiKey is not set. Set the organization's OpenRouter key in the environment and restart."

Scenario: [TOR-01-x5DXvRT] The gateway shall never write the OpenRouter key or the OIDC client secret to its log
    Given the environment variable OpenRouter__ApiKey is "sk-or-test-SECRET-KEY-123"
    And the environment variable Broker__UpstreamClientSecret is "oidc-test-SECRET-456"
    When the gateway is started and a signed-in caller completes one synthesize_narration call
    Then neither the standard error nor any file under 'logs/' should contain the string "sk-or-test-SECRET-KEY-123"
    And neither the standard error nor any file under 'logs/' should contain the string "oidc-test-SECRET-456"

Scenario: [TOR-01-65SaYiJ] The gateway shall keep its usage records, quota overrides and cached audio under the configured data directory so that a restart on the same volume preserves them
    Given a caller has spent $0.52 this month and an administrator has set that caller's quota to $60.00
    And one clip with cacheKey K is in the gateway cache
    When the gateway is stopped and started again with the same Storage:DatabasePath
    Then the caller's my_usage result should show spent "$0.52" and quota "$60.00"
    And a synthesize_narration call for the clip with cacheKey K should report "cached": true
