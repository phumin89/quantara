# Quantara rework and completion gates

Reviewed 2026-10-01. Goal: one autonomous trading company, proved in Sandbox before separately authorized Live operation.

## Completed scope cleanup

The earlier cleanup only hid competing features. This pass removes their implementation:

- Duplicate autonomous company loop, demo portfolio, NVDA cycle and legacy controls.
- Historical company/CFO simulation endpoints, exit challengers/tournaments, snapshot replays and option experiments.
- Obsolete command handlers, Mediator projects/packages, EF foundation and backtest tools.
- Research forms, legacy panels and associated unused types/styles/tests.

One Sandbox path remains: funding → completed-market evidence → bounded AI meeting → deterministic approval → fresh-quote paper execution → reconciled owner evidence. Company today and Money/portfolio are the only Sandbox views. Live remains a separate surface.

The snapshot comparison removes 47 obsolete source files and approximately 8,500 lines across backend, tools and frontend. Counts include blank lines and exclude generated files, dependencies and tests. The new historical-evidence contract is included in the estimate. Details are in `work/scope-cleanup/source-metrics.json`.

Kept deliberately: the paper journals, salary allocation/reserve rules, forward-credit compatibility reader, real market data/FX validation, AI budgets/artifacts, deterministic RiskEngine, Pixel Office and Live accounting/security/persistence. Strategy validation remains because the Live adviser uses it. Historical documents and saved account files are retained as evidence; they do not represent active endpoints.

## Sandbox acceptance evidence

- Main domain/application/API suite: **509 passed**.
- Live PostgreSQL integration suite: **38 passed**.
- Frontend suite: **26 passed**.
- Production solution build, web build and TypeScript check passed.
- Docker configuration validation and supported Windows launcher restart passed.
- Browser checked desktop, 375-pixel mobile portfolio without overflow, durable daily status and separate Live readiness without the paper iframe.
- All six existing forward account/journal/lock files kept identical hashes through restart.
- Runtime: forward-paper, AI enabled through Codex CLI, existing daily meeting COMPLETED, 100,000 THB simulated equity and no holdings/fills.

The smaller test count follows removal of obsolete feature tests. Core market-data, forecast, financial, restart, provider and Live tests remain. No new paid model meeting, private broker call or real order was performed. Today's older journal has no saved summary; the UI explicitly states that rather than creating a report.

Logs: `work/scope-cleanup/backend-test.log`, `live-test.log`, `release-build.log`, `web-build.log`, `restart.log`. Screenshot: `workspace.jpg`.

## Owner workspace improvements — 2026-10-01

The current web build uses a quieter monochrome shell, self-hosted IBM Plex Sans Thai and the original colored Pixel Office. Today shows company status, financial summary and accepted daily evidence before office/team. Money separates portfolio, pending orders, salary/reserve and transactions; Thai labels and Bangkok dates replace financial/debug jargon. Technical details and identifiers remain in disclosures. Position/order summaries fit phones; allocation tables retain a keyboard-focusable scroll region, visible cue and sticky first column.

Account and company reads have independent polling and an eight-second whole-request deadline, including the JSON body. Runtime shape/finite-number/date validation rejects malformed responses before render. The read timestamp means the browser retrieved the saved account; it does not certify fresh market prices. Unavailable responses hide unverified balances.

Meeting stages now publish stable employee IDs. Both portfolio stages update one employee; the office bridge and UI keep the same eight-person configured roster. Bounded received activity survives switching Live and back within the page session. Saved accepted reports can restore staff summaries without inventing historical events. Live unmounts the Sandbox office and stays separate.

Sandbox has a persisted device-local pause/resume control. Access requires loopback, the exact configured local web origin/host and a process capability on mutations. This is device-local Sandbox control, not production owner authentication or Live authorization. Pause affects subsequent company ticks; started work may finish and scheduled simulated salary continues. Resume cannot clear failed/interrupted reconciliation. Missing/corrupt saved control state fails closed. Mutations are never automatically retried and the UI requires a matching server confirmation.

Fresh verification: **516 backend tests passed in the existing official .NET SDK Docker environment; 38 frontend tests; 10 Pixel bridge tests; TypeScript and web/API production builds passed.** Windows blocked loading the freshly built test assembly through Application Control; the actual published API ran successfully on Windows. A new API integration test initially exposed eager configuration capture, corrected to resolve the control store from finalized configuration. Its temporary control files were archived; existing financial files were not rewritten.

Browser verified 1280px desktop, 375px mobile, actual pause/resume confirmation and no Sandbox iframe on Live. All six pre-existing account/journal/lock files retained identical hashes through deployment and control checks. Sandbox was restored to its prior running state. No new paid meeting or real order was run. Logs, screenshots, runtime backup and account hash evidence: `work/rework-ui/`.

## Live completion, in order

Owner requested review after every implementation round on 2026-10-01. The follow-up compact workspace and private Live read slice are documented in `work/owner-rounds/review.md`. The private account UI accepts only matching account/snapshot/mandate versions and reconciled observations less than 30 seconds old; synthetic tests do not prove production connectivity. No activation/order action was added. Owner has no broker contract evidence yet and requested `docs/bitkub-live-verification.md`; production adapter acceptance remains blocked pending that evidence. Current follow-up web tests: 43 passed; Pixel camera: 3 passed; existing company bridge: 10 passed.

1. **Broker normalization:** verify symbol precision/step units, minimums, buy spending versus sell quantity, fee/credit conversion and actual fill lineage. Implement preparation and complete bound-account evidence on the existing ports. Unknown placements remain blocked and are never automatically resent.
2. **Provisioning/baselines:** bind actual broker/account/credential identity and permitted inventory. Persist explicit Bangkok-day starting equity and flow-adjusted high-water equity. Test rollover, manual flows, fees and credential replacement. Deposits do not silently enlarge authority.
3. **Restricted runtime:** register the verified broker/evidence source, PostgreSQL store, owner controls and workers in the isolated executor. Use a restricted identity, protected secrets and trusted TLS. AI cannot access credentials/storage. Startup reconciles and disarms; missing dependencies fail closed.
4. **Owner operation:** show verified balances, pending/unknown/partial orders, actual fees, saved mandate/preflight, explicit activation, Stop, cancellation and private events. Actions require authoritative outcomes.
5. **Release proof:** pass broker fault, persistence/concurrency/restart, private UI and deployment-isolation checks. A real canary requires separate owner authorization afterward.

The official Bitkub V3/V4 documents inspected during the earlier review did not resolve side-specific step units or fee-credit normalization. Broker-confirmed semantics or redacted contract evidence is needed before accepting the production adapter. Raw signed transport and isolated tests do not complete Live.

## Preservation and limitations

This workspace has no Git repository. The pre-cleanup source/configuration snapshot and hash manifest are in `work/scope-cleanup/before`; restore selected files from there if needed. Existing financial journals and archives were not rewritten or deleted. No local-AI runtime was added.

The local worker bridge could not launch its configured executable. Its fallback produced a partial patch. Lead review restored required market/FX and rehearsal tests, retained the strategy dependency used by Live, corrected the solution, added account-event filtering and restored durable summaries on the daily screen before running acceptance checks.

Sandbox is running. Live remains unfinished at the gates above; this cleanup does not prove investment performance or authorize real-money execution.
