# TOR-08-nLHs0pq — An Okta admin-group member lands on My Usage with the administrator navigation

**Requirement:** The dashboard shall land a member of the Okta admin group on My Usage (S-01) with the
administrator navigation after a real Okta sign-in.
**Verification:** operator-observed (a person runs this against a real Okta org; tests never call Okta).
The automated counterpart with the stub issuer is `TOR_08_D1L4ilb_admin_sees_exactly_my_usage_usage_and_cache`
in `tests/PeakMediaRemoteMcpServer.E2E/AdminNavTests.cs`.

## One-time Okta setup

Use the Okta app, admin group and groups claim from `tests/manual/TOR-02-hKpbfCf.md`
("Okta administrator checklist"), with one addition for the dashboard:

- App → **General** → **Sign-in redirect URIs:** also add `http://localhost:5108/signin-oidc` (the
  dashboard's browser sign-in), next to the `/oauth/callback` URI already there.

## Given

- The gateway is configured against an Okta developer (Integrator) org whose ID token carries a groups
  claim, and you are a member of the group configured as `Admin:Group`. In your own terminal (secrets
  never leave it):

  ```bash
  set -a; source .env; set +a      # Broker__Upstream*, Admin__Group, OpenRouter__ApiKey
  .venv/bin/python build.py start
  ```

## When

1. Open a private browser window at `http://localhost:5108/`.
2. Sign in on the Okta page as the admin-group member.

## Then — what you should see

- [ ] The browser shows **My usage** (heading "My usage", address `http://localhost:5108/`).
- [ ] The app header's navigation shows the links **My usage**, **Usage** and **Cache**.
- [ ] (Contrast case) Signed in as the assigned non-admin test user in another private window, the header
      shows **My usage** only.

Stop the gateway afterwards: `.venv/bin/python build.py stop`.

## Result

| Date | Operator | Okta org | Result |
|------|----------|----------|--------|
|      |          |          |        |
