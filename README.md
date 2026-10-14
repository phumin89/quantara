# Quantara

Next.js/TypeScript/Material UI workspace with independently runnable C# services.
Crypto tracks Bitkub balances and retained history. CEO, Analyst and Reviewer
research through Codex SDK and may propose spot LIMIT orders for explicit human
approval. Background research never approves or submits orders.

## Structure

- `apps/web`: routes and API proxy, class components, feature services/contracts,
  shared navigation/theme and scoped DI.
- `services/market-data`: public Bitkub coins, THB prices and intraday candles in its own SQL database. No broker credentials.
- `services/portfolio`: signed account reads, retained history, portfolio projection and its SQL Server database.
- `services/team`: controller, Mediator CQRS, orchestration and its own SQL Server
  database. `services/ai-worker` is the Node/Codex SDK process adapter.
- Each service has separate Api, Application, Domain and Infrastructure projects. Controllers delegate to Mediator CQRS handlers; Domain has no framework dependencies.
- `tests`: Portfolio, MarketData and architecture checks. One C# type or React component per named file.

Open `Quantara.sln` in Visual Studio, or this root folder in VS Code. Editor settings, analyzers, typed ESLint and strict compiler flags are committed alongside CI checks.

## Run

Install frontend dependencies once with `npm ci`, then use three terminals:

```powershell
npm run dev:market-data
npm run dev:api
npm run dev
```

Web: http://127.0.0.1:3000. Portfolio: port 5050. MarketData: port 5051. Production frontend: `npm run build`, then `npm start`. Both services expose `/health`; Portfolio checks SQL connectivity and persisted ledger readability.

For continuous operation on this Windows PC, see
[local runtime setup](docs/local-runtime.md). It installs hidden per-user service
watchers, restart recovery and a temporary idle-sleep request. Real orders still
require individual human approval.

Portfolio reads only BITKUB_API_KEY and BITKUB_API_SECRET from environment variables or the repository-root `.env`. MarketData never receives credentials. Configure `ConnectionStrings__Quantara` for another SQL Server; local default is the existing MSSQLLocalDB Quantara database. Set `Services__MarketDataUrl` on Portfolio and `PORTFOLIO_SERVICE_URL` on the Next.js server when services run elsewhere.

The Portfolio database preserves the original `CryptoPortfolioLedger` table, account-key fingerprint and JSON fields. Its idempotent SQL migration creates missing schema and never drops data. API-key rotation creates a separate partition. Old source is archived locally under ignored `work/prestructure`; `.env` remains outside Git.

## Behavior

Home remains empty; Crypto shows actual balances, retained closed coins and trade fills. Available THB excludes reserved funds. Total assets means held crypto only; a missing price makes the total unknown. Filters do not alter the summary. Snapshot errors retain saved records and offer Retry. Status details expose separate wallet/price/history update times.

Bitkub exchange history is limited to about 90 days and excludes broker conversions.
Cost and profit remain unknown until complete accounting is verified. Real spot
LIMIT proposals require individual human review; withdrawals, transfers and
leverage are not supported. Saans is not bundled; the documented system font
fallback is used.

## Verification

```powershell
npm run format:check
npm run check
npm run build
dotnet build Quantara.sln
npm run test:architecture
npm run test:editor
dotnet run --project tests/Portfolio.Checks/Portfolio.Checks.csproj
dotnet run --project tests/MarketData.Checks/MarketData.Checks.csproj
```

Opt into real SQL verification with `-- --sql` on Portfolio.Checks. It uses a separate deterministic test account, not the owner's partition. Stop running services before rebuilding their assemblies on Windows.

Browser checks with frontend running: `npm run test:ui`, `npm run test:contracts`. Real-data `npm run test:crypto` also needs both services and configured credentials. Install Chromium with `npx playwright install chromium`, or use installed Edge via `$env:PLAYWRIGHT_CHANNEL = 'msedge'`. Test fixtures/failure injection remain confined to the isolated test browser.

Use `npm run test:crypto -- --fixtures` for deterministic responsive Crypto
checks without broker credentials; it intercepts requests only inside the test
browser. CI uses this mode, never simulated portfolio data in the application.

CI runs formatting, lint/type checks, builds, deterministic checks, architecture checks, dependency audit, isolated browser checks and Gitleaks scanning. It never needs broker credentials or starts real account imports. GitHub branch protection must require these checks to enforce merge gates; the workflow alone does not change remote branch policy.

Project-owned Roslyn diagnostics QNT001/QNT002 enforce one type per matching
file in C# editors/builds. QNT003 rejects null-forgiving suppression and QNT004
requires explicit sealed/abstract/static class intent. ESLint's
`quantara/file-ownership` reports multiple components/types and mismatched names
while typing. `npm run test:editor` proves these rules reject invalid probes and
accept valid source without adding bad code to the application.

## Automatic formatting

Run `npm run format` to fix the entire repository's frontend and C# formatting.
It applies ESLint fixes and Prettier to the frontend, then pinned CSharpier and
Roslyn code fixes to C#. This includes QNT005 member spacing and readable C#
blocks. Run `npm run format:check` for a non-writing CI check; both commands use
the same rules. Formatting does not resolve architecture or business errors.
The QNT005 code fix is also available from the C# editor's Quick Fix action.

Frontend builds automatically run `format:web`, including direct workspace
builds. C# project and solution builds format each application's project before
compilation; editor design-time checks and formatter bootstrap projects are
excluded. Formatter failures stop the build. To explicitly skip C# autoformat,
use `dotnet build Quantara.sln -p:QuantaraAutoFormat=false`. CI still runs the
non-writing `format:check` gate before any build.

Prettier uses width 80, semicolons, single-quoted TypeScript strings and
double-quoted JSX attributes. CSharpier uses width 120, four spaces and LF.
Widths guide wrapping rather than imposing a hard limit on string literals.
JSON keeps double quotes; C# uses double quotes for strings and single quotes
for characters. The CSharpier VS Code extension is the C# formatter.

React UI uses class components and lifecycle methods. Named Props define component
contracts; ConnectionStatusPresenter encapsulates connection-state decisions
and appearance. Enums represent portfolio filters, position status, trade side
and cost status. C# domain enums remain independent of JSON; API/SQL converters
retain historical string spellings and reject unknown or numeric values.
`npm run test:presentation` checks state priority and filtering without a browser.

## AI team

Start `npm run dev:team` alongside the frontend and Portfolio service. Team listens
on loopback port 5052; `TEAM_SERVICE_URL` configures the server-side web proxy.
Team uses the existing Codex ChatGPT login. Sign in locally with Codex if needed;
`Codex__AuthHome` selects its credential directory (otherwise `CODEX_HOME` or
the owner's `~/.codex`). Tokens never enter prompts, logs, browser code or Git.
No API key is needed. Team does not read broker credentials from `.env`.

Team uses a separate local `QuantaraTeam` SQL Server database and creates missing
schema only. Set `ConnectionStrings__Team` for an existing remote database and
`Services__PortfolioUrl` for Portfolio's address. `Codex__NodePath` may select an
absolute Node executable; otherwise Node is resolved from PATH. `npm ci` installs
the pinned Codex SDK/runtime. Do not run multiple Team hosts against the same
database; startup recovery marks interrupted tasks failed rather than replaying them.

At `/daily-brief` (`/team` remains an alias), choose a Bangkok date to read recorded
role reports and Reviewer summaries. A new company's research loop is enabled by
the owner's 2026-10-13 direction. When login, SQL and portfolio data are ready,
CEO chooses analysis priorities, Analyst follows the plan, and Reviewer checks
the evidence. The next round uses the previous review as bounded untrusted context.
Hourly reviews and deduplicated signal assessments run without overlapping
rounds or a missed-round backlog. Each role has a 180-second process timeout.
Research runs overnight; only an explicit human approval can submit a real order.

CompanySchedule is a new table in Team's database; existing TeamRuns and Portfolio
schema/history stay intact. The next hour is reserved before starting a round,
so uncertain failures/restarts do not immediately repeat model calls. Dependency
readiness is checked every 30 seconds. Due rounds request the existing Portfolio
read-only sync and wait for completion; failed readiness can request another sync
after five minutes. Stop company persists the disabled state and cancels active
work; Resume makes the next round due. Saved stops survive restarts. Closing the
web page does not stop the server. No model work occurs when prerequisites fail.

The worker receives only explicitly allowed environment variables and a temporary
working directory. A narrow launch mode in Team.Api forwards SDK execution to the
pinned Codex CLI with `--ignore-user-config --ephemeral`, sharing only the existing
auth cache for normal token refresh. Personal config, MCP servers and plugins are
not imported. Shell, app tools, web search and sandbox network are disabled; the
model receives only whitelisted portfolio fields. Raw worker errors never reach
prompts, logs or the browser. Model inference still contacts OpenAI.
Windows policy failures stay visible; this app does not bypass Application Control.

Run `npm run test:ai-worker`, `npm run test:team` and `npm run test:team-ui` for
deterministic process-boundary, orchestration and isolated browser checks. These
checks do not spend AI credits, call Bitkub or require SQL secrets. Real SQL and
authenticated model execution are separate opt-in verification; fixture success
does not prove those integrations are available on a particular machine.
