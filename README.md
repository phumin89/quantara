# Quantara 🚀

A trading workspace for Bitkub THB spot markets, with portfolio tracking,
real-time market monitoring, and an AI research team.

**Check your portfolio. Follow the team's research. See what the trading engine is doing.**

[Get started](#-get-started) · [Trading modes](#-trading-modes) ·
[Development](#-development) · [Documentation](#-documentation)

## ✨ What you'll find

- **💰 Crypto** — actual balances, available THB, retained closed coins, and trade fills.
- **📋 Daily Brief** — trading status first, with team chat and reports one tab away.
  Choose a Bangkok date to revisit a day's research and summary.
- **⚡ Real-time monitoring** — fresh quotes evaluate owned-position exits;
  new entries still require completed five-minute candles.
- **🧠 AI research** — CEO, Analyst, and Reviewer collaborate through Codex SDK.
- **📱 Telegram updates** — concise Thai confirmed-fill notifications and
  critical alerts; individual review buttons belong to the ManualReview workflow.

Market subscriptions are bounded: owned positions and open orders stay in the
watch set, with up to 16 rotating candidates and 64 watched symbols in total.
The broader market scan runs separately.

Portfolio totals never invent missing prices, costs, or profit. Incomplete
accounting stays **Unknown**, and saved history survives a failed refresh.

## 🧰 The stack

| Area             | Technology                                                    |
| ---------------- | ------------------------------------------------------------- |
| Frontend         | Next.js, TypeScript, React class components, Material UI      |
| Backend          | C# / .NET 10, thin controllers, Mediator CQRS                 |
| Data             | SQL Server, with a separate database for each service         |
| Market events    | Bitkub WebSockets → MarketData → private HTTP SSE → Portfolio |
| AI research      | Node.js, Codex SDK, existing Codex ChatGPT login              |
| Local deployment | Docker Compose                                                |

## 🚀 Get started

Use **Node.js 24** for the workspace tooling and the **.NET 10 SDK** for native
C# development. Docker Desktop with Linux containers runs the continuous local
deployment, including SQL Server.

### 🐳 Docker — the continuous runtime

For the **first verified setup**, follow the
[Docker runtime guide](docs/docker-runtime.md):

```powershell
.\tools\Start-QuantaraDocker.ps1 -Action Prepare
.\tools\Start-QuantaraDocker.ps1 -Action Migrate
.\tools\Start-QuantaraDocker.ps1 -Action Start
.\tools\Start-QuantaraDocker.ps1 -Action Status
```

Then open **[http://127.0.0.1:3000](http://127.0.0.1:3000)**.

Already have an active Docker deployment?

- **Start** preserves existing containers.
- **Prepare** prepares configuration and images; it does not deploy updates.
- Image updates need reviewed maintenance, current database backups, and
  **Auto Trading disabled first**.
- Keep Docker as the canonical data owner. Do not run a native runtime against
  a stale copy of its databases.

Only the web app is published to the host. APIs and SQL stay on private Compose
networks. Continuous operation still needs the PC awake, Docker running, and
network access.

### 💻 Native development

Install workspace dependencies:

```powershell
npm ci
```

Run each process in its own terminal:

| Command                   | Starts         | Native address                          |
| ------------------------- | -------------- | --------------------------------------- |
| `npm run dev`             | Web app        | [localhost:3000](http://127.0.0.1:3000) |
| `npm run dev:api`         | Portfolio API  | `127.0.0.1:5050`                        |
| `npm run dev:market-data` | MarketData API | `127.0.0.1:5051`                        |
| `npm run dev:team`        | AI team API    | `127.0.0.1:5052`                        |

The APIs expose `/health`; Portfolio also checks SQL connectivity and retained
ledger readability. For a production frontend build, run `npm run build`, then
`npm start`.

See [local runtime setup](docs/local-runtime.md) for native SQL and process
ownership. Native development and the canonical Docker runtime must not run as
competing hosts.

## 🎛️ Trading modes

| Mode                | What happens                                           | Who authorizes real orders?                        |
| ------------------- | ------------------------------------------------------ | -------------------------------------------------- |
| 🧪 **Paper**        | Simulated funds, fees, and limit fills                 | No real orders                                     |
| 👀 **ManualReview** | Individual spot LIMIT proposals await review           | The owner approves each proposal                   |
| ⚡ **AutoLive**     | The deterministic engine trades within a valid mandate | The owner enables the exact policy and expiry once |
| ⏸️ **Stopped**      | Trading is disabled                                    | A restart does not reactivate it                   |

> New trading state starts in **Paper**. Setup, deployment, research, and tests
> never enable AutoLive.

- AutoLive's mandate binds the account, strategy/rules hash, market scope,
  capital/risk limits, and expiry. It has no per-trade approval step.
- Material changes, expiry, **Disable**, and **Emergency stop** disarm live
  authority. Re-enabling requires an explicit owner action.
- AI research cannot enable AutoLive, choose its orders, or submit transactions.
- Trading is **THB spot LIMIT only**. Withdrawals, transfers, and leverage are
  not supported. A limit intent does not guarantee a fill or an exit price.

For activation, accounting, recovery, and real-time behavior, read
[trading controls](docs/trading.md) and [adaptive risk](docs/adaptive-risk.md).

## 🗂️ Project map

| Path                   | Responsibility                                                           | Database             |
| ---------------------- | ------------------------------------------------------------------------ | -------------------- |
| `apps/web`             | Pages, API proxy, feature services, navigation, theme, and scoped DI     | —                    |
| `services/portfolio`   | Account reads, retained history, risk, trading, and order reconciliation | `Quantara`           |
| `services/market-data` | Public coins, prices, order books, and completed intraday candles        | `QuantaraMarketData` |
| `services/team`        | AI orchestration, research rounds, role reports, and memory              | `QuantaraTeam`       |
| `services/ai-worker`   | Isolated Node/Codex SDK process adapter                                  | —                    |
| `tests`                | Deterministic service, architecture, and editor checks                   | Test fixtures        |
| `tools` / `docker`     | Formatting, verification, runtime helpers, and deployment                | —                    |

Each C# service separates **Api → Application → Domain** responsibilities, with
Infrastructure implementing application interfaces. Services communicate through
HTTP and do not share database ownership or another service's assemblies.

A few conventions keep the code predictable:

- One C# type or React component/named type per matching file.
- Class-based React components, typed Context, and a lazy ServiceContainer.
- Thin controllers and Mediator CQRS; framework-free Domain projects.
- Strict compiler, analyzer, ESLint, and transport-validation rules.

Open **`Quantara.sln` in Visual Studio**, or the **repository folder in VS Code**.
The editor and CI use the checked-in rules.

## 🔐 Configuration

Broker credentials stay in Portfolio's server environment or the root
`.env`. Never commit or expose them to the browser, MarketData, Team, or model
workers.

| Setting                                     | Used by                  | Purpose                                                    |
| ------------------------------------------- | ------------------------ | ---------------------------------------------------------- |
| `BITKUB_API_KEY`, `BITKUB_API_SECRET`       | Portfolio                | Signed account access                                      |
| `ConnectionStrings__Quantara`               | Portfolio                | Portfolio SQL connection                                   |
| `ConnectionStrings__MarketData`             | MarketData               | MarketData SQL connection                                  |
| `ConnectionStrings__Team`                   | Team                     | Team SQL connection                                        |
| `Services__MarketDataUrl`                   | Portfolio                | MarketData service address                                 |
| `Services__PortfolioUrl`                    | Team                     | Portfolio service address                                  |
| `PORTFOLIO_SERVICE_URL`, `TEAM_SERVICE_URL` | Next.js server           | Backend proxy addresses                                    |
| `MarketEvents__Token`                       | MarketData and Portfolio | Matching private stream/watch token for native development |

Docker preparation generates and preserves private owner-control and market-event
tokens in `.runtime/docker/runtime.env`; it does not change the root `.env`.
The market-event token goes only to MarketData and Portfolio. See
[private server configuration](docs/trading.md#private-server-configuration).

Existing Portfolio tables, JSON fields, and account partitions are retained.
Schema initialization creates missing structures without dropping data; API-key
rotation creates a separate account partition.

Bitkub history imports cover its available window, about 90 days, and exclude
broker conversions. Cost and profit stay Unknown until accounting is verified.

## 🧠 Meet the AI team

| Role     | Focus                                                 |
| -------- | ----------------------------------------------------- |
| CEO      | Chooses research priorities and coordinates the round |
| Analyst  | Investigates the plan using supplied evidence         |
| Reviewer | Checks conclusions and writes the summary             |

Team uses the existing **Codex ChatGPT login**, with no separate API key.
Sign in locally with Codex if needed. Team never reads broker credentials.

- Hourly reviews and deduplicated signal assessments run without overlapping
  rounds or replaying a missed-round backlog.
- **Stop company** persists the disabled state and cancels active research;
  **Resume** makes the next round due.
- Closing the browser does not stop the server.
- Research prerequisites must be ready before model work begins.
- Run only one Team host per database; interrupted tasks are marked failed
  rather than replayed at startup.

<details>
<summary>AI setup and isolation details</summary>

- `Codex__AuthHome` selects the credential directory; otherwise Team uses
  `CODEX_HOME` or the owner's `~/.codex`.
- `Codex__NodePath` can select an absolute Node executable. `npm ci` installs
  the pinned SDK/runtime.
- The worker uses a temporary workspace, allowlisted environment/data, and
  `--ignore-user-config --ephemeral`.
- Personal configuration, MCP servers, plugins, shell, web search, and tool
  network access are not imported or enabled. Model inference still contacts
  OpenAI.
- Each role has a 180-second timeout. Tokens and raw worker errors never enter
  prompts, browser code, or Git.
- Real SQL and authenticated model checks are opt-in; fixture success alone
  does not prove those integrations are available.

</details>

## 🛠️ Development

### Format once, let the tools handle the layout

| Command                     | Purpose                                  |
| --------------------------- | ---------------------------------------- |
| `npm run format`            | Fix frontend and C# formatting           |
| `npm run format:check`      | Check formatting without rewriting files |
| `npm run check`             | Run frontend lint and type checks        |
| `npm run build`             | Build the production frontend            |
| `dotnet build Quantara.sln` | Build the C# solution                    |

Frontend builds run ESLint fixes and Prettier automatically. C# builds run
CSharpier and Roslyn formatting fixes before compilation. Formatter failures
stop the build; CI checks formatting before building.

- **TypeScript:** width 80, semicolons, single-quoted strings.
- **JSX / JSON:** double-quoted attributes / strings.
- **C#:** width 120, four spaces, LF, and double-quoted strings.
- Widths guide wrapping; long string literals are not hard line-length failures.
- The C# member-spacing Quick Fix is also available in the editor.

Stop native services before rebuilding their loaded assemblies on Windows.
To skip C# autoformat explicitly, use
`dotnet build Quantara.sln -p:QuantaraAutoFormat=false`.

### ✅ Useful checks

| Command                     | Checks                                                      |
| --------------------------- | ----------------------------------------------------------- |
| `npm run test:architecture` | Service/layer boundaries and file ownership                 |
| `npm run test:editor`       | Compiler/analyzer and editor rule enforcement               |
| `npm run test:trading`      | Portfolio, API, MarketData, and trading transport fixtures  |
| `npm run test:ai-worker`    | Isolated worker/process boundaries without a real AI call   |
| `npm run test:team`         | Team orchestration and research rules                       |
| `npm run test:docker`       | Runtime helpers, configuration, and private token isolation |

<details>
<summary>Browser checks and optional integrations</summary>

With an isolated frontend running:

```powershell
npm run test:ui
npm run test:contracts
npm run test:crypto -- --fixtures
node tools/verify-trading-ui.cjs --browser
```

Use `npx playwright install chromium`, or select installed Edge with
`$env:PLAYWRIGHT_CHANNEL = 'msedge'`. Fixtures intercept requests only inside the
test browser; the application never switches to fake portfolio data.

- `npm run test:team-ui` checks the Daily Brief research UI.
- `npm run test:presentation` checks state priority and filters without a browser.
- Portfolio and MarketData checks can also run directly:
  `dotnet run --project tests/Portfolio.Checks/Portfolio.Checks.csproj` and
  `dotnet run --project tests/MarketData.Checks/MarketData.Checks.csproj`.
- Real SQL verification is opt-in with `-- --sql` on Portfolio.Checks and uses
  a separate test account.
- Real-data `npm run test:crypto` needs running services and configured
  credentials. Keep it separate from isolated fixture checks.

</details>

CI runs formatting, lint/type checks, builds, deterministic and browser checks,
architecture checks, dependency audit, and secret scanning. It needs no broker
credentials. GitHub branch protection must separately require the workflow's
checks; the workflow does not configure branch policy.

## 📚 Documentation

| Looking for…                                    | Start here                               |
| ----------------------------------------------- | ---------------------------------------- |
| Continuous local deployment and maintenance     | [Docker runtime](docs/docker-runtime.md) |
| Native development and data ownership           | [Local runtime](docs/local-runtime.md)   |
| Trading modes, real-time behavior, and recovery | [Trading](docs/trading.md)               |
| Adaptive capital and risk sizing                | [Adaptive risk](docs/adaptive-risk.md)   |
| Service boundaries and frontend structure       | [Architecture](docs/architecture.md)     |
| UI conventions and visual direction             | [Design](docs/design.md)                 |
