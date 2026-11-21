# Contributing

How to set up a machine for PeakMedia MCP Servers and how work is done in this repository.
[README.md](README.md) covers what the product is and the full settings reference;
[CLAUDE.md](CLAUDE.md) holds the conventions Claude Code follows.

## Set up a new machine

### 1. Tools

```bash
brew install --cask dotnet-sdk          # .NET 11 SDK
python3 --version                       # 3.11 or newer
node --version                          # Node.js, for the Blazor Blueprint MCP server (npx)
```

Optional, for dashboard work: `playwright-cli` (`npm install -g @playwright/cli@latest`).

### 2. Code and project venv

```bash
git clone https://github.com/peakflames/PeakMediaMcpServers.git
cd PeakMediaMcpServers
git checkout develop
python3 -m venv .venv && .venv/bin/pip install psutil fastmcp
```

Homebrew's Python refuses a global `pip install`, so everything goes in `.venv/`. Always call
`.venv/bin/python build.py …` (or `source .venv/bin/activate` once per terminal).

### 3. Secrets: `.env` in the repo root

`.env` is git-ignored and never committed. Copy it from your old machine through a password
manager or another secure channel — never git, chat or email. It holds:

| Variable | Secret? | What |
|---|---|---|
| `OpenRouter__ApiKey` | yes | The shared OpenRouter key |
| `Broker__UpstreamClientSecret` | yes | Okta sign-in app's client secret |
| `Broker__UpstreamClientId` | no | Okta sign-in app's client ID |
| `Broker__UpstreamAuthority` | no | Okta org URL, e.g. `https://<org>.okta.com` |
| `Broker__UpstreamGroupsClaim` | no | ID-token claim holding group names (e.g. `peakmedia_groups`) |
| `Admin__Group` | no | Okta group whose members are dashboard admins |

### 4. Okta (once per Okta app, not per machine)

The Okta app's redirect addresses are all `localhost:5108`, so a new machine needs no Okta change.
For a new Okta app, follow README → *Okta setup*, and register **both** sign-in redirect URIs:

- `http://localhost:5108/oauth/callback` — MCP clients (Claude Code)
- `http://localhost:5108/signin-oidc` — the usage dashboard in a browser

Optional sign-out redirect URI: `http://localhost:5108/signout-callback-oidc`.
A missing `/signin-oidc` shows up as Okta's error *"The 'redirect_uri' parameter must be a Login
redirect URI in the client app settings"* when opening the dashboard.

### 5. Build, test, run

```bash
.venv/bin/python build.py build
.venv/bin/python build.py test
.venv/bin/python build.py start        # start/run load .env themselves; an already-exported variable wins
curl http://localhost:5108/version     # {"name":"PeakMediaRemoteMcpServer","version":"0.1.0"}
```

- Dashboard: open <http://localhost:5108/> → Okta sign-in → **My usage** (admins also see **Usage** and **Cache**).
- Stop: `.venv/bin/python build.py stop`. Status: `build.py status`. Logs: `build.py log --tail 50`.
- The running gateway keeps its SQLite database and logs next to the binary
  (`src/PeakMediaRemoteMcpServer/bin/Debug/net11.0/data/` and `…/logs/`). Local usage history is test
  data and is not carried between machines.

### 6. Connect Claude Code

```bash
claude mcp add --transport http peak-media http://localhost:5108/mcp
# in Claude Code: /mcp → peak-media → Authenticate → sign in through Okta
```

Add `--scope user` to use it in every project. Check with `claude mcp get peak-media`
(expect `✔ Connected` while the gateway runs). From the terminal,
`.venv/bin/python build.py mcp tools --oauth` signs in through Okta and lists the tools.

For dashboard work, also approve the project's `blazorblueprint` MCP server (in `.mcp.json`) the
first time Claude Code asks; check with `claude mcp get blazorblueprint`.

### 7. What does not move between machines

| Item | Why | Action |
|---|---|---|
| `.env` | secrets | copy securely (step 3) |
| `<repo>.autopilot/` workspace | autopilot's private clone; only holds work until it is merged and fetched | nothing, once `develop` is pushed |
| Local database and logs | test data | nothing |
| Claude Code memory (`~/.claude/projects/…/memory/`) | per machine | working preferences are in CLAUDE.md → *Working with the owner* |

## How work is done

- Branches, merges and releases: CLAUDE.md → *Git Workflow* and *Release Protocol*
  (base branch `develop`, `--no-ff` merges, `main` for releases).
- Work is planned and built with the peak-workflow commands (`/peak-workflow:status`,
  `/peak-workflow:triage <issue>`, `/peak-workflow:quick-fix`, `/peak-workflow:start-epic`,
  `/peak-workflow:autopilot`). Open work is tracked in GitHub issues; the pinned issue holds the
  suggested order.
- Every change passes the gates in CLAUDE.md → *Verification & Quality Gates* before it is committed.
- Autopilot builds in its own private clone. Its merged work reaches your repo only when you fetch it:

  ```bash
  git fetch "../PeakMediaMcpServers.autopilot/clone" develop:autopilot/develop && git log develop..autopilot/develop
  git merge --ff-only autopilot/develop
  git branch -d autopilot/develop
  ```

  `--ff-only` keeps each epic's `--no-ff` merge commit as it is.
