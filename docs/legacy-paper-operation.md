# Archived paper operation — superseded 2026-10-05

Historical reference only. The shipped application is now Live only; these old Sandbox UI/startup instructions are not the current product. Use the root README and headless-verification.md. Preserve historical data; do not start metered AI rehearsals automatically.

Quantara is an autonomous AI trading company for its owner. Prove the company in **Sandbox**, then complete a separately authorized **Live** executor. The pixel office shows application activity; deterministic risk and accounting decide whether proposals can execute.

The current operating product is the forward paper company. Live has tested accounting, persistence, owner controls, recovery and advisory components, but **no production broker/reconciliation integration**. It is not ready to trade real funds.

## Start the main Sandbox

On Windows, start Docker Desktop and sign in with `codex login`, then run:

```powershell
.\start-sandbox.ps1
.\start-sandbox.ps1 -Action Status
.\start-sandbox.ps1 -Action Restart
.\start-sandbox.ps1 -Action Stop
```

Open [Quantara](http://localhost:3000). The API is at http://localhost:8080 and the Pixel Office at http://localhost:4242.

The launcher uses the signed-in Codex allowance for eight specialist stages. It runs the API on Windows and the web/office in Docker, without copying ChatGPT credentials into containers. Closing the browser does not stop the company; Windows must stay awake. After reboot, start the launcher again. This is not an installed Windows service.

A new Sandbox starts with **100,000 THB of simulated initial capital**. This is separate from simulated salary. The seed is recorded once; restarts do not top up the account. `-InitialPaperCapital 0` creates a new salary-only Sandbox. For an existing account, use its original seed; a changed seed fails closed. Do not delete accounts or alter saved journals to change it.

Saved accounts remain under `src/TradingCompany.Api/data`; runtime binaries and logs are in `.runtime`. Backtest profits never enter this account.

## Daily workflow

1. Treasury credits eligible simulated salary allocations and preserves the protected reserve.
2. The company researches completed public market history. Crypto is eligible daily after 09:00 Bangkok time; stocks are eligible on weekdays when their quote source is configured.
3. Eight bounded AI stages produce non-binding proposals. HOLD is a valid decision.
4. Orders wait for fresh prices observed after the decision. Deterministic RiskEngine checks cash, ownership, exposure, position size, drawdown and quote validity.
5. Paper execution records fills or risk rejections in an append-only, checksummed ledger. Once-per-date meeting claims prevent duplicate meetings after restart.

**Company today** shows this account and its saved daily reports. **Money and portfolio** shows its funding, positions, pending orders and receipts.

New completed meetings save role reports and model usage. Failed meetings save reported usage without presenting rejected artifacts as approved reports. Old journals without summaries remain valid and explicitly lack reports; no historical reports are fabricated. The event channel shows activity observed while the page is open; the saved journal provides durable daily evidence.

Crypto paper quotes use public Bitkub BTC/ETH/SOL THB order books without credentials. Paper costs include observed spread and an assumed 0.25% fee per side, limited by observed top-of-book depth. HTTP Date establishes response observation time, not an exchange trade timestamp. Full depth, queue position, taxes and actual broker fees/fills are not modeled. Missing, stale or invalid quotes create no fill.

Optional stocks require read-only `APCA_API_KEY_ID` and `APCA_API_SECRET_KEY` in the API environment. They are not Live authorization. Stale held-stock valuations can block new buys, including crypto buys; the whole-portfolio freshness guard remains authoritative.

## Alternative metered API profile

`docker compose up --build` runs the same forward account workflow with an explicitly enabled OpenAI API provider. Supply secrets through the environment or your deployment secret store. `.env.example` documents safe settings; AI defaults off. Sandbox funding defaults on with a one-time simulated seed. The Sandbox profile no longer requires PostgreSQL.

Do not run this profile alongside the Windows launcher: they share ports and use different saved storage. Docker ports bind to loopback. The Windows launcher selects Codex CLI; the Docker profile selects the separately billed API gateway.

## Verification

```powershell
dotnet test TradingCompany.sln
cd apps/trading-company-web
node --test tests/*.test.cjs
npm run typecheck
npm run build
```

Live integration tests use disposable PostgreSQL containers and require a working Docker engine. A failed or unexecuted database suite is not a release pass.

The isolated `tools/ForwardSandboxRehearsal` runner accepts `--live-codex` for an opt-in real-provider/public-data paper rehearsal. It creates a new account under `.runtime/rehearsals`, consumes Codex allowance, leaves the saved treasury untouched and accepts HOLD. It proves plumbing, not investment performance. Offline tests cover buy/sell, costs, depth rejection, invalid proposals and restart safety.

## Live completion

The isolated `TradingCompany.LiveExecution` host uses owner authentication, CSRF and HTTPS. It is unconfigured and disarmed by default. It still needs a verified Bitkub execution/evidence adapter, account provisioning and performance-baseline lifecycle, restricted credential isolation, a production composition root and complete owner operation/deployment evidence.

No broker credentials belong in browser code, prompts, logs or model processes. Owner account binding, positive limits, successful reconciliation and explicit activation are mandatory. Installing, restarting, pairing or changing the mode cannot activate Live. Withdrawals, bank transfers, margin, leverage and short selling are outside scope.

Read [the rework review and completion gates](docs/project-rework.md), [architecture](docs/architecture.md), [Live release evidence](docs/live-release-evidence.md) and [approved Live design](docs/superpowers/specs/2026-09-27-sandbox-live-execution-design.md).

The obsolete company loop, replay/CFO simulators, exit tournaments, options experiments and legacy launcher were removed. Existing archived data is retained; the application does not operate those accounts.

## Data compatibility

Preserve saved journals and matching application versions. Fee/depth-bearing paper journals and Live buy-budget orders require compatible readers; never strip fields, rewrite evidence or reset an account after checksum/reconciliation failure. Live rollback stops/disarms execution and retains the ledger until a compatible executor can reconcile it.

## License

Pixel Agents is adapted under its existing license. See [third-party notices](THIRD_PARTY_NOTICES.md).
