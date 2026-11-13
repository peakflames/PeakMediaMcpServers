Feature: 2.0 Sign-in and Access
    As a skill user inside the organization
    I want to connect Claude Code to the gateway with one command and one browser sign-in
    So that I get narration without holding an OpenRouter key, and nobody outside the organization can spend its credit


# --------------------------------------------------------------------------------------------------
# Discovery and client registration
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-02-8R5ouns] The gateway shall reject an unauthenticated request to /mcp with 401 and a WWW-Authenticate header pointing at its protected-resource metadata
    Given the gateway is running
    When the client sends an MCP initialize request to /mcp with no Authorization header
    Then the response status should be 401
    And the WWW-Authenticate header should contain a resource_metadata URL ending in "/.well-known/oauth-protected-resource"

Scenario: [TOR-02-eXSTt0Q] The gateway shall publish protected-resource metadata naming itself as the authorization server
    Given the gateway is running at the public URL G
    When the client sends GET /.well-known/oauth-protected-resource
    Then the response status should be 200
    And the JSON body's "resource" should be the gateway's /mcp URL under G
    And the JSON body's "authorization_servers" should contain G

Scenario: [TOR-02-VOsRKOw] The gateway shall publish authorization-server metadata listing its authorize, token and registration endpoints with S256 PKCE
    Given the gateway is running at the public URL G
    When the client sends GET /.well-known/oauth-authorization-server
    Then the response status should be 200
    And the JSON body should contain the following keys and values:
        | key                              | value         |
        | authorization_endpoint           | G/authorize   |
        | token_endpoint                   | G/token       |
        | registration_endpoint            | G/register    |
    And the JSON body's "code_challenge_methods_supported" should contain "S256"

Scenario: [TOR-02-Mb6PQo5] The gateway shall register an MCP client through dynamic client registration without any pre-shared client ID
    Given the gateway is running
    When the client sends POST /register with a JSON body naming a loopback redirect URI
    Then the response status should be 201
    And the JSON body should contain a non-empty "client_id"
    And the JSON body's "redirect_uris" should contain the redirect URI that was sent


# --------------------------------------------------------------------------------------------------
# Sign-in through the organization's identity provider
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-02-PosyPLa] The gateway shall redirect an authorization request to the organization's OIDC sign-in page
    Given a registered MCP client
    When the browser opens the gateway's /authorize with the client's ID, redirect URI, a state value and an S256 code challenge
    Then the response status should be 302
    And the Location header should start with the configured OIDC provider's authorization endpoint
    And the Location header's redirect_uri parameter should be the gateway's /oauth/callback URL

Scenario: [TOR-02-DU3EQVF] The gateway shall refuse an authorization request that carries no PKCE code challenge
    Given a registered MCP client
    When the browser opens the gateway's /authorize with the client's ID and redirect URI but no code_challenge
    Then the browser should not be redirected to the OIDC provider
    And the response should carry the OAuth error "invalid_request"

Scenario: [TOR-02-QFiwMY7] The gateway shall return an authorization code to the client's redirect URI after a successful sign-in and exchange it for an access token and a refresh token
    Given a registered MCP client has started an authorization request with state "xyz" and an S256 code challenge
    And the person signs in at the OIDC provider as "pat@example.com"
    When the OIDC provider redirects the browser to the gateway's /oauth/callback
    Then the browser should be redirected to the client's redirect URI with a "code" parameter and state "xyz"
    When the client sends POST /token with that code and the matching code verifier
    Then the response status should be 200
    And the JSON body should contain a non-empty "access_token" and a non-empty "refresh_token"
    And an MCP initialize request to /mcp bearing that access token should succeed

Scenario: [TOR-02-x3ci8yI] The gateway shall refuse a token request whose code verifier does not match the code challenge
    Given a registered MCP client holds an authorization code issued for an S256 code challenge
    When the client sends POST /token with that code and a different code verifier
    Then the response status should be 400
    And the JSON body's "error" should be "invalid_grant"
    And the JSON body should not contain "access_token"

Scenario: [TOR-02-kx8Nl0i] The gateway shall issue a new access token in exchange for a valid refresh token
    Given a signed-in client holds a refresh token
    When the client sends POST /token with grant_type "refresh_token" and that refresh token
    Then the response status should be 200
    And the JSON body should contain a non-empty "access_token"
    And an MCP initialize request to /mcp bearing the new access token should succeed

Scenario: [TOR-02-KmwGCjA] The gateway shall issue no authorization code and no token when the identity provider refuses the sign-in
    Given a registered MCP client has started an authorization request
    And the OIDC provider refuses the person because they are not assigned to the gateway's app
    When the OIDC provider redirects the browser to the gateway's /oauth/callback with error "access_denied"
    Then the browser should be redirected to the client's redirect URI with error "access_denied" and no "code" parameter
    And no access token should exist for that person

Scenario: [TOR-02-hKpbfCf] The gateway shall complete a real Okta sign-in started by "claude mcp add --transport http" with no client ID to paste
    # Verification: operator-observed
    Given the gateway is configured against an Okta developer org and the person is assigned to its OIDC app
    And the person has run 'claude mcp add --transport http peak-media https://<gateway-host>/mcp'
    When the person signs in on the Okta page that Claude Code opens in the browser
    Then '/mcp' in Claude Code should show "peak-media" as connected
    And the person should not have been asked to enter a client ID or an OpenRouter key


# --------------------------------------------------------------------------------------------------
# Access rule
# --------------------------------------------------------------------------------------------------

Scenario: [TOR-02-NwWTAqU] The gateway shall reject a request to /mcp whose access token is expired, malformed or not issued by the gateway
    Given the gateway is running
    When the client sends an MCP initialize request to /mcp with each of these Authorization headers:
        | token                                             |
        | Bearer <an expired gateway access token>          |
        | Bearer not-a-token                                |
        | Bearer <a well-formed token signed by another key> |
    Then every response status should be 401
    And every response should carry a WWW-Authenticate header

Scenario: [TOR-02-dj60B0R] The gateway shall offer every signed-in caller exactly the tools synthesize_narration, list_voices and my_usage, whether or not the caller is an administrator
    Given caller "pat@example.com" whose groups claim does not contain the configured Admin:Group
    And caller "ada@example.com" whose groups claim contains the configured Admin:Group
    When each caller requests the MCP tool list
    Then each tool list should contain exactly "synthesize_narration", "list_voices" and "my_usage"
    And the two tool lists should be identical, including every tool's input schema

Scenario: [TOR-02-Dc8Z0Ge] The gateway shall keep each caller's usage under that caller's own OIDC subject so that no caller sees another caller's records
    Given caller "pat@example.com" has synthesized 12 clips this month at a cost of $0.03
    And caller "sam@example.com" has made no calls
    When "sam@example.com" calls my_usage
    Then the result should show spent "$0.00" and clips synthesized "0"
    And the result should not contain the string "pat@example.com"

Scenario: [TOR-02-GIUxnmc] The gateway shall ignore any identity asserted by a request header or query parameter other than a gateway access token
    Given the gateway is running
    When the client sends an MCP initialize request to /mcp?user=pat@example.com with the header "X-User: pat@example.com" and no Authorization header
    Then the response status should be 401
