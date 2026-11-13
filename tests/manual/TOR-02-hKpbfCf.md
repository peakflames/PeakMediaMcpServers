# TOR-02-hKpbfCf — Real Okta sign-in started by `claude mcp add --transport http`

**Requirement:** The gateway shall complete a real Okta sign-in started by
"claude mcp add --transport http" with no client ID to paste.
**Verification:** operator-observed (a person runs this against a real Okta org; tests never call Okta).

## Okta administrator checklist (one-time setup)

1. Admin Console → **Applications → Applications → Create App Integration** → **OIDC – OpenID Connect**
   → **Web Application**. Grant type: **Authorization Code** only.
2. **Sign-in redirect URIs:** `http://localhost:5108/oauth/callback` (local) and
   `https://<gateway-host>/oauth/callback` (production). No sign-out URI is needed.
3. **Assignments:** *Limit access to selected groups* (or assign people). Only assigned people can sign
   in; anyone else is refused with `access_denied`.
4. **Directory → Groups:** create the admin group (e.g. `PeakMedia Admins`) and add the administrators.
   Set `Admin__Group` to that exact group name.
5. App → **Sign On** tab → **Token claims (OIDC)** → **Add expression** (federated claims):
   Name `peakmedia_groups` (Okta reserves `groups` here), Expression
   `user.getGroups({'group.profile.name': 'org.peakmedia'}).![profile.name]` (the name filter is
   *starts with*; use a prefix all the app's groups share), then **Save**. Set
   `Broker__UpstreamGroupsClaim=peakmedia_groups`.
   On orgs that still show the legacy screen: *OpenID Connect ID Token* → **Groups claim type: Filter**,
   claim name `groups`, filter *Matches regex* `.*` (the default `Broker:UpstreamGroupsClaim`).
6. Copy the **Client ID** → `Broker__UpstreamClientId`; the **Client secret** →
   `Broker__UpstreamClientSecret` (environment only — a gitignored `.env` sourced into the shell is fine; never the repo or a chat);
   the org URL `https://<org>.okta.com` → `Broker__UpstreamAuthority` (the org authorization server).
7. Start the gateway with those variables plus `OpenRouter__ApiKey`, then connect Claude Code (steps below).

For this run, also create: the admin group with yourself in it, one assigned **non-admin** test user,
and one **unassigned** test user.

## Given

- The gateway is configured against an Okta developer (Integrator) org, and the person is assigned to
  its OIDC app. In your own terminal (secrets never leave it):

  ```bash
  export Broker__UpstreamAuthority=https://<org>.okta.com
  export Broker__UpstreamClientId=<client id>
  export Broker__UpstreamClientSecret=<client secret>
  export Admin__Group="PeakMedia Admins"
  export OpenRouter__ApiKey=<key>
  .venv/bin/python build.py run
  ```

- The person has run:

  ```bash
  claude mcp add --transport http peak-media http://localhost:5108/mcp
  ```

## When

1. In Claude Code, run `/mcp`, select **peak-media**, choose **Authenticate**.
2. The browser opens the Okta sign-in page; sign in as the admin user.

## Then — what you should see

- [ ] `/mcp` in Claude Code shows **peak-media** as **connected**.
- [ ] At no point were you asked to enter a client ID or an OpenRouter key.
- [ ] `.venv/bin/python build.py log --tail 50` shows `Sign-in completed with role admin.`
- [ ] The log holds no access token, refresh token, authorization code, client secret or OpenRouter key.

Repeat for the **non-admin** user (expect `Sign-in completed with role user.`) and the **unassigned**
user (expect Okta to refuse: Okta shows its own "400 Bad Request — User is not assigned to the client
application" page, error code `access_denied`, and does not redirect back to the gateway; the log shows no
`Sign-in completed` record for the attempt, and Claude Code does not connect).

## Observation record

| Date | Observer | Okta org | Admin user | Non-admin user | Unassigned user | Secrets in log? | Result |
|---|---|---|---|---|---|---|---|
| 2026-11-12 | Todd Schavey (implementer session; log checked by Claude) | Okta Integrator org (org authorization server) | tizzo@ in `org.peakmedia.admins` + `org.peakmedia.users`: Claude Code connected, `Sign-in completed with role admin.`, 2 group claims, Admin:Group among them (22:39:59) | same account removed from `org.peakmedia.admins`: connected, `role user`, 1 group claim, not among them (22:43:22) | account not assigned to the app: Okta's own "400 — User is not assigned to the client application" (`access_denied`), no gateway sign-in recorded, not connected (22:24) | none (log scanned for JWTs, codes, refresh tokens, client secrets, bearer values: 0 hits) | Observed as expected; no client ID or OpenRouter key asked |

Notes from this run: Okta's federated-claims screen reserves the claim name `groups`, so the claim was
named `peakmedia_groups` with `Broker__UpstreamGroupsClaim=peakmedia_groups` (setting added in this
epic). The app's group assignments were needed before any sign-in succeeded.
`/peak-workflow:wrapup-epic` re-collects this observation independently.
