# Sandbox and Live Execution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Primary Codex owns high-risk implementation, review and acceptance; local workers only receive bounded, explicitly owned ordinary tasks.

**Goal:** Deliver an AI trading application with isolated Sandbox and Bitkub Live accounts, with Live disabled until owner setup, limits, reconciliation and explicit activation pass.

**Architecture:** Preserve the existing paper pipeline. Add a separate restricted Live execution host and durable PostgreSQL account/order state, reusing RiskEngine and the specialist meeting workflow. The AI process receives scoped evidence and proposals, never broker credentials or execution permissions.

**Tech Stack:** Existing .NET 10, ASP.NET Core, EF Core/Npgsql, PostgreSQL 16, Next.js/React/TypeScript, SignalR, xUnit and Node test runner. No local-AI runtime or broker SDK dependency.

**Spec:** `docs/superpowers/specs/2026-09-27-sandbox-live-execution-design.md` (owner-approved 2026-09-27).

## Global Constraints

- Sandbox remains default. Live development is authorized; connecting a private account and spending real money are not authorized by this plan approval.
- Initial broker/assets: Bitkub spot, BTC-THB, ETH-THB, SOL-THB. No withdrawals, transfers, leverage, borrowing, shorting or options.
- Owner inputs all monetary limits; missing/zero limits permit no real spending. Do not choose a real investment allocation in code.
- Initial activation requires no crypto holdings or open orders in the broker account. Do not sell existing holdings to satisfy this gate.
- Existing paper files and receipts are never migrated into Live funds or fills. Do not change legacy `ITradingExecutionGateway` registration to a real broker.
- RiskEngine remains final authority; executor revalidates the exact intent, snapshot and mandate immediately before dispatch.
- Analysis: once per Bangkok date after 09:00; reconciliation: every 30 seconds independently of AI. HOLD is valid.
- Unknown network outcome means no automatic placement retry. Restart reconciles, then stays disarmed. Stop persists across restarts.
- Distinct restricted executor runtime identity is required; environment filtering alone is not secret isolation.
- Release target 1 October 2026 is not a permission to waive tests or claim live readiness. Real canary requires separate owner authorization.
- Current workspace has no `.git`. Preserve files and existing data; record reviewed files/test output per task rather than inventing commits or initializing a repository.
- All task tests run with fake private broker transport and fixed time. Real private endpoints are outside automated test runs.

## Review Focus

1. Network timeout after broker acceptance: one send attempt, durable UNKNOWN, no resend after restart (Tasks 3–4).
2. Manual trade or extra deposit while AI is thinking: snapshot invalidation and halt, no implicit budget increase (Tasks 1, 4, 6).
3. Owner presses Stop during submission: serialize stop/dispatch authorization; report already-sent exposure and cancellation races honestly (Tasks 2, 4–5).
4. Model can read local files or Docker control: deny broker-secret/control access through runtime identity, not prompt text (Tasks 5, 8).
5. Partial fills/rounding/corrections/cursor pagination: correct units, no double application and no premature reservation release (Tasks 3–4).

## Files and boundaries

- Domain: new `src/TradingCompany.Domain/LiveTrading.cs` owns immutable account/mandate/intent types and pure gate policy. Existing `Trading.cs` RiskEngine remains shared.
- Application: new `LiveExecutionContracts.cs`, `LiveExecutionService.cs`, `LiveReconciliation.cs` own ports, dispatch and reconciliation. Existing AI contracts receive optional account context with safe paper defaults.
- Infrastructure: new `LiveTradingDbContext.cs`, `LiveTradingStore.cs`, `BitkubLiveBroker.cs`, `LiveMigrations/*` own persistence and signed HTTP, separate from `MarketDbContext.cs` and paper JSON files.
- New host `src/TradingCompany.LiveExecution/`: `Program.cs`, `OwnerSession.cs`, `LiveEndpoints.cs`, `LiveReconciliationWorker.cs`, project file. Only this host owns credentials and live database authority.
- Existing API: new `LiveCompanyCoordinator.cs`, `LiveExecutionClient.cs`; narrow registration changes in `Program.cs`, no replacement of paper services.
- Web: new `app/LiveAccountPanel.tsx`, `app/liveAccount.ts`; narrow changes to `page.tsx`, `types.ts`, `globals.css`. Protected Live events must not enter the existing public paper hub.
- Tests: named suites below in existing `tests/TradingCompany.Domain.Tests`; database-only tests in a new `tests/TradingCompany.LiveIntegration.Tests` project. Frontend tests follow existing `tests/*.test.cjs` conventions.
- Operations: new `start-company.ps1`, `docker-compose.live.yml`, `docs/live-operator-guide.md`, `tools/Test-LiveIsolation.ps1`; preserve `start-sandbox.ps1` compatibility.

## Task 1: Pure Live mandate, account and risk boundary

**Files:** Create `src/TradingCompany.Domain/LiveTrading.cs`, `src/TradingCompany.Application/LiveExecutionContracts.cs`, `tests/TradingCompany.Domain.Tests/LiveRiskTests.cs`.

**Interfaces:** Define `TradingMode { Sandbox, Live }`; `LiveAccountId(Guid Value)`; `LiveOrderState { Proposed, Rejected, Reserved, Dispatching, Unknown, Acknowledged, Open, PartiallyFilled, Filled, Cancelled }`.

Define records `LiveMandate` (account, version, capital allocation, max order THB, daily turnover THB, daily loss THB, drawdown fraction, exposure fraction, max positions, slippage fraction, fee reserve fraction, allowed symbols, optional expiry); `LiveIntent` (account, decision/intent IDs, mode, symbol, side, coin quantity, limit price, mandate/snapshot versions, evidence hash); `LiveSnapshot` (version, observed time, cash available/reserved, owned positions, open orders, equity, daily loss, drawdown, day turnover, reconciliation state); `LiveGateState` (armed, stopped, credential fingerprint, isolation verified); `LiveGateDecision` (allowed, violation codes).

Produce `LiveRiskPolicy.Evaluate(LiveIntent intent, LiveSnapshot snapshot, LiveMandate mandate, LiveGateState gate, DateTimeOffset now) -> LiveGateDecision`. It calls existing `RiskEngine.Evaluate`, plus account/mode/version/inventory/reservation/limit validation. Use decimal money/quantity throughout. A maximum 30-second snapshot age is an execution freshness ceiling, not a forecast horizon.

- [ ] Write `LiveRiskTests` assertions: Sandbox intent => `MODE_MISMATCH`; missing/zero mandate => not allowed; sell quantity above owned available => `INSUFFICIENT_OWNED_QUANTITY`; changed version => `STALE_APPROVAL`; stopped => denied. Test exact boundary at 30 seconds and >30 seconds.
- [ ] Add reservation test: allocated cash 1,000, already reserved 700, new order 400 plus fee => denied; capital above owner's allocation cannot be spent even if broker balance is higher. These are test inputs, not deployment defaults.
- [ ] Run `dotnet test tests/TradingCompany.Domain.Tests --filter FullyQualifiedName~LiveRiskTests`; observe RED.
- [ ] Implement the records and policy; validate finite supported ranges, positive monetary limits, fractional bounds and unknown enum values. Invalid policy must not throw into an allow path.
- [ ] Run the same filter; require all GREEN. Lead review confirms sell validation does not bypass Stop and pure policy alone cannot dispatch.

## Task 2: Durable Live ledger, reservations and account serialization

**Files:** Create Infrastructure `LiveTradingDbContext.cs`, `LiveTradingStore.cs`, `LiveMigrations/*`; create `tests/TradingCompany.LiveIntegration.Tests/{TradingCompany.LiveIntegration.Tests.csproj,LiveTradingStoreTests.cs}`; modify `TradingCompany.sln`.

**Interfaces:** In `LiveExecutionContracts.cs`, define `LiveOrderRecord` (intent, state, broker ID, reserved amount/quantity, immutable request bytes/hash, attempt timestamp, version) and `BrokerFill` (account, transaction ID, order ID, side, symbol, quantity, price, cash fee, evidence revision).

Produce `ILiveTradingStore.ReadAsync(LiveAccountId, CancellationToken) -> Task<LiveSnapshot>` and `WithAccountLockAsync<T>(LiveAccountId, Func<ILiveAccountTransaction, CancellationToken, Task<T>>, CancellationToken) -> Task<T>`. `ILiveAccountTransaction` exposes `ReadGate()`, `ReadMandate()`, `ReadOrder(Guid)`, `Reserve(LiveIntent)`, `RecordTransition(Guid, LiveOrderState, string?)`, `ApplyFill(BrokerFill)`, `SetStopped(bool)` and `CommitAsync(CancellationToken)`. Define read/status DTOs here rather than reuse paper account types. Transitions are append-only audited, versioned and validated.

- [ ] Write actual PostgreSQL tests: concurrent same intent produces one reservation; two different 700-THB orders against 1,000 THB cannot both reserve; duplicate fill does not change balances twice; process/session lock loss prevents subsequent dispatch authorization.
- [ ] Add transactional Stop race test: after Stop obtains the account dispatch lock, no new `Dispatching` transition may commit. A previous Dispatching order stays visible until reconciled.
- [ ] Add failure tests for database outage, missing schema and transaction rollback; no substitute in-memory success. Live numeric storage uses exact decimal with precision sufficient for validated exchange scale; never existing integer `TradeProposalRow.Quantity`.
- [ ] Run `dotnet test tests/TradingCompany.LiveIntegration.Tests --filter FullyQualifiedName~LiveTradingStoreTests` with a dedicated disposable test database; RED before implementation. Missing database is a failed prerequisite, never an invisible skip in release checks.
- [ ] Implement separate `live` schema with unique account/intent and account/transaction keys, account row/version checks, session advisory lock for dispatch ownership, and audited corrections rather than replacing history. Never hold an uncommitted database transaction across broker I/O; maintain exclusive dispatch ownership through the external call, then persist acknowledgement in a new transaction.
- [ ] Re-run GREEN; verify migration leaves paper tables and JSON files unchanged. Test schema down/up only in disposable database; production rollback disables Live and preserves all orders.

## Task 3: Private Bitkub HTTP contract, no execution wiring

**Files:** Create `src/TradingCompany.Infrastructure/BitkubLiveBroker.cs`, `tests/TradingCompany.Domain.Tests/BitkubLiveBrokerTests.cs`; extend `LiveExecutionContracts.cs` with broker DTOs.

**Interfaces:** `ILiveBroker.GetSnapshotAsync(CancellationToken) -> Task<BrokerSnapshot>`; `GetSymbolRulesAsync(string, CancellationToken) -> Task<BrokerSymbolRules>`; `PlaceLimitAsync(LiveIntent, CancellationToken) -> Task<BrokerSubmission>`; `GetOrderAsync(string symbol, string id, OrderSide side, CancellationToken) -> Task<BrokerOrder>`; `ReadHistoryAsync(string symbol, string? cursor, CancellationToken) -> Task<BrokerHistoryPage>`; `CancelAsync(string symbol, string id, OrderSide side, CancellationToken) -> Task<BrokerCancellation>`.

DTOs retain available/reserved balances, opaque IDs, client reference, raw status, observation times, per-fill records and pagination completeness. Submission is `Accepted`, `Rejected` or `Unknown`; none means `Filled`. Credentials are a constructor dependency supplied only by the restricted executor host.

- [ ] Write fake-HTTP contract tests using official V3/V4 response fixtures. Pin signature bytes to a fixed timestamp/body; verify buy amount is THB, sell amount is coin quantity, invariant decimal serialization and opaque string IDs.
- [ ] Assert malformed success, HTTP 5xx or timeout after send => Unknown; HTTP 200 with known terminal exchange error => Rejected; response redirect => failure without forwarding credentials; no automatic HTTP placement retry.
- [ ] Test fee credit separately from paid fees; paginated history must follow cursors, detect repeated cursors and refuse to call incomplete history reconciled. Unknown fee/fill semantics => blocked result, never inferred units.
- [ ] Run `dotnet test tests/TradingCompany.Domain.Tests --filter FullyQualifiedName~BitkubLiveBrokerTests`; observe RED.
- [ ] Implement fixed `https://api.bitkub.com` allowlisted paths, current V3 order/metadata APIs and V4 wallet API from the spec references. Validate current pair names, scale/minimum/freeze flags; conservatively round down quantities and spending, never round a buy above the budget. Get fresh server time for signing. No redirects, withdraw methods or configurable public base URL in production.
- [ ] Run GREEN and review captured fake requests for absent secret logging. Real endpoint compatibility remains unverified until owner-authorized preflight; fixtures alone do not prove it.

## Task 4: Dispatch, unknown outcomes and reconciliation

**Files:** Create `src/TradingCompany.Application/LiveExecutionService.cs`, `LiveReconciliation.cs`; create `tests/TradingCompany.Domain.Tests/LiveExecutionTests.cs`, `LiveReconciliationTests.cs`; extend integration tests for restart.

**Interfaces:** `LiveExecutionService.SubmitAsync(LiveIntent, CancellationToken) -> Task<LiveOrderRecord>` consumes Tasks 1–3. `LiveReconciliation.ReconcileAsync(LiveAccountId, CancellationToken) -> Task<LiveReconciliationResult>` returns complete/blocked, broker/local snapshot versions and violation codes. `CancelOwnedAsync(LiveAccountId, Guid intentId, CancellationToken) -> Task<LiveOrderRecord>` only targets stored broker IDs. Never accept a frontend-provided arbitrary broker ID.

- [ ] Write deterministic sequence test: reserve, persist Dispatching, send once, persist Accepted; balance stays reserved until confirmed fills. Duplicate submit returns the existing intent without a send.
- [ ] Inject crash at each boundary: after reserve; after Dispatching commit before send; after broker acceptance before local acknowledgement; during acknowledgement commit. Recreated service must not retransmit an ambiguous intent (`broker.PlaceCalls == 1` after an actual send).
- [ ] Test partial fill then cancellation with a racing additional fill, duplicate transaction, fee correction, changed snapshot during AI work, unexplained balance delta and incomplete history. No reservation is released merely because an order vanished from open orders.
- [ ] Test credential/account changes and tampered intent after approval; all blocked. Test Stop during send reports pending exposure and does not promise cancellation.
- [ ] Run `dotnet test tests/TradingCompany.Domain.Tests --filter "FullyQualifiedName~LiveExecutionTests|FullyQualifiedName~LiveReconciliationTests"`; RED.
- [ ] Implement state machine with account serialization, version revalidation and explicit Unknown handling. Compare exchange observations across stable reads; inconsistent/non-atomic observations mean retry read/reconcile only, never retry placement. Bound polling and rate-limit backoff; keep UNKNOWN blocked without conclusive evidence.
- [ ] Re-run GREEN plus database restart tests. Lead review accounts for every placement and cancellation side effect and proves no paper-ledger fill path is invoked.

## Task 5: Restricted executor host and owner controls

**Files:** Create `src/TradingCompany.LiveExecution/{TradingCompany.LiveExecution.csproj,Program.cs,OwnerSession.cs,LiveEndpoints.cs,LiveReconciliationWorker.cs}`; create `tests/TradingCompany.Domain.Tests/LiveOwnerAccessTests.cs`, `LiveActivationTests.cs`; update solution/test references.

**Interfaces:** Owner endpoints: `GET /live/status`, `GET /live/account`, `POST /live/mandate`, `POST /live/preflight`, `POST /live/arm`, `POST /live/stop`, `POST /live/orders/{intentId}/cancel`. Use separate `POST /owner/pair`, `POST /owner/logout` and anti-forgery token endpoint. No browser placement endpoint. Internal scoped research input/proposal endpoints require a distinct coordinator identity that cannot arm, edit limits, stop-clear or read secrets.

- [ ] Write WebApplicationFactory tests: unauthenticated account read/mutation denied; expired session, wrong host/origin, missing CSRF and replayed pairing code denied; coordinator identity cannot call owner controls.
- [ ] Assert selecting Live never arms it; zero limits, missing isolation proof, missing credentials, non-empty initial inventory/open orders and unreconciled snapshot block arming. Broker-account binding uses owner-confirmed identity plus credential fingerprint; do not invent an account ID from balances.
- [ ] Test restart/credential/mandate change disarms; Stop persists; owner session expiry does not silently reset Stop. No auth failure response leaks balances or key values.
- [ ] Run `dotnet test tests/TradingCompany.Domain.Tests --filter "FullyQualifiedName~LiveOwnerAccessTests|FullyQualifiedName~LiveActivationTests"`; RED.
- [ ] Implement ASP.NET Core cookie authentication/data protection and antiforgery rather than custom cryptography. Pair using a one-time 256-bit code generated by the restricted host, five-minute expiry, single use and rate limiting; show locally, not in normal logs. Session cookie is HttpOnly/SameSite=Strict, eight-hour expiry; use HTTPS/Secure for Live local endpoints. Issue local certificate setup instructions, never suppress browser TLS warnings.
- [ ] Implement read-only preflight, saved mandate version, explicit arm and persistent stop. Run reconciliation every 30 seconds even if disarmed or model unavailable. No withdrawal permission is requested; owner verification is explicit if broker cannot attest permissions.
- [ ] Run GREEN. Host startup without owner pairing, credentials or positive mandate must return DISABLED and make zero placement calls.

## Task 6: Account-scoped AI company integration

**Files:** Create API `LiveCompanyCoordinator.cs`, `LiveExecutionClient.cs`; modify `Program.cs`, Application `AiCompanyContracts.cs`, `Ports.cs`, AgentRuntime `AiCompanyMeetingRunner.cs`, Infrastructure `CodexCliTradingAgentGateway.cs`; add `LiveCompanyCoordinatorTests.cs` and extend existing meeting/gateway tests.

**Interfaces:** Append optional `TradingAccountContext(TradingMode Mode, string AccountId)` to meeting input and realtime envelopes with Sandbox-compatible default. Include it in artifact cache keys and daily journal claims. `LiveCompanyCoordinator.TickAsync(CancellationToken) -> Task` consumes a scoped executor research snapshot and `IAiCompanyMeetingRunner`; it never receives credentials or direct broker instance. `LiveExecutionClient` submits final structured intents with mandate/snapshot/evidence binding; executor still revalidates.

- [ ] Write tests: same evidence for Sandbox and Live never shares an actionable proposal cache/journal; all eight roles use the real reconciled account snapshot; invalid model output, model outage, stale version or HOLD makes zero placement calls.
- [ ] Test Bangkok 08:59/no meeting, 09:00/eligible, second same-day tick/no extra meeting; reconciliation continues regardless. Invalid live state prevents spending model quota on a meeting.
- [ ] Assert paper salary/legacy NVDA endpoint cannot access Live executor, and model prompt/output contains no broker/session/internal transport secret. Existing paper tests retain their behavior.
- [ ] Run filtered `LiveCompanyCoordinatorTests`, `AiCompanyMeetingRunnerTests`, `CodexCliTradingAgentGatewayTests`; RED for new behavior.
- [ ] Implement coordinator and account-scoped packet validation. Replace gateway's hardcoded “paper-only trading company” with mode-neutral advisory wording; explicit execution context remains supplied by the validated meeting. Preserve environment allowlist and read-only ephemeral execution; do not treat those alone as runtime isolation proof.
- [ ] Run GREEN. Do not add a provider fallback or change trading strategy merely to make the model choose BUY.

## Task 7: Usable Sandbox / Live workspace and private activity

**Files:** Create web `app/LiveAccountPanel.tsx`, `app/liveAccount.ts`, `tests/live-account-panel.test.cjs`; modify `app/page.tsx`, `app/types.ts`, `app/globals.css`, `src/TradingCompany.LiveExecution/LiveEndpoints.cs`; add `LiveActivityHub.cs` in that host. Create Pixel `webview-ui/src/transport/parentMessageTransport.ts` and its test; modify `webview-ui/src/transport/index.ts` and `server/src/quantaraBridge.ts` for explicit mode filtering. Preserve the existing Sandbox WebSocket transport.

**Interfaces:** `LiveAccountPanel` consumes only the authenticated host status/account DTOs; typed fetch helpers send credentials and antiforgery tokens only to the fixed configured Live origin. `LiveActivityHub` requires owner auth and explicit account grouping. No real account data goes to `/realtime/trading`, the legacy unauthenticated paper hub. For the Live office iframe, `ParentMessageTransport` implements existing `MessageTransport`, receiving only account-scoped sanitized role/activity messages from the authenticated parent page, not from the public Pixel server. Validate exact configured parent origin, `event.source === window.parent`, per-mount channel nonce and message schema; nonce is a channel binding, not owner authentication.

- [ ] Write UI tests: default Sandbox; selecting Live shows disabled/readiness state; no key input stored in localStorage; stale/inaccessible API shows unavailable, not zero balances or armed; armed UI identifies real money and bound account.
- [ ] Test partial fills/Unknown/Stop/cancel-pending copy, separate paper and real P&L, and account switch discards old subscriptions/results. Test unauthenticated/other-account live event subscriptions denied. Live office must ignore wrong origin/source/account/nonce, never connect its public WebSocket, and clear runtime state on session/account switch.
- [ ] Run `node --test tests/live-account-panel.test.cjs` from web directory and the Live hub access tests; RED.
- [ ] Implement checklist, owner pairing, limits, explicit activation confirmation, Stop and cancel-owned-order controls using existing styles and accessible labels. Never include actual trade authorization in the mode selector.
- [ ] Implement Live office as a read-only presentation of parent-delivered role events using the existing Pixel assets/engine. Do not send balances, credentials or free-form private reports to the iframe or public Pixel backend. Disable editing/session/terminal commands in this transport; static roster and layout are presentation configuration, never evidence of work. Until transport verification passes, display office unavailable in Live rather than substitute Sandbox animation.
- [ ] Run web `node --test tests/*.test.cjs`, `npm run typecheck`, `npm run build` and live hub tests; GREEN. Use browser checks for mode selection, keyboard access, error state and Stop semantics without private credentials or real orders.

## Task 8: Launch profile, isolation proof and recovery drill

**Files:** Create `start-company.ps1`, `docker-compose.live.yml`, `tools/Test-LiveIsolation.ps1`, `docs/live-operator-guide.md`; modify README and `.env.example` with names/placeholders only. Add `tests/TradingCompany.LiveIntegration.Tests/LiveRecoveryTests.cs`.

**Interfaces:** `start-company.ps1 -Action Start|Status|Stop|Restart` starts/checks web, paper API and optional separately provisioned Live host; it never arms Live. `Test-LiveIsolation.ps1` runs non-secret probe files/processes under the model runtime identity and proves inability to read executor credential storage, owner session keys, Live DB/control credentials and Docker/administrator interfaces.

- [ ] Write recovery drill with fake broker: kill dispatcher after accepted send, restart, reconcile existing order without resubmission and remain disarmed. API-down/web-up must report degraded, not ready. Broker/model/database outages produce distinct blocked reasons.
- [ ] Use a dedicated test DB/volume, never the real paper data directory. Run `dotnet test tests/TradingCompany.LiveIntegration.Tests`; RED for absent lifecycle implementation.
- [ ] Implement localhost bindings and non-default randomly provisioned database credentials via restricted local secret files, no secrets in compose/source/command lines. Do not reuse the legacy compose sample password for Live. Keep database volumes durable and owned by executor identity.
- [ ] Document provisioning a dedicated non-administrator executor/model isolation boundary, trusted local TLS and owner key setup. Do not create OS users, install services/certificates or change permissions without separate owner approval. If isolation proof cannot run under the actual deployment identities, mark Live blocked; do not substitute a mocked green test.
- [ ] Run full deterministic suites, UI tests/typecheck/build, restart drill and paper-file before/after hashes. Capture evidence in a dated local report with no credentials or private balances. Document pending account-specific checks explicitly.
- [ ] Update launcher/readme status explanations: browser need not stay open, background services and awake host do; restart never activates Live. No scheduled tasks or sleep-setting changes in this plan.

## Task 9: Release review and owner handoff (no implicit transactions)

**Files:** Create `docs/live-release-evidence.md`; update implementation checklist only from fresh observed results.

- [ ] Lead reviews all changed money/security paths directly; run `dotnet test TradingCompany.sln`, the isolated PostgreSQL integration suite, web tests/typecheck/build and relevant Pixel tests. Confirm tests do not contact private production APIs.
- [ ] Compare each spec gate to evidence. If task tests cannot prove security under actual runtime identities, or host/DB/auth setup is absent, publish BLOCKED rather than “live ready.”
- [ ] Owner supplies credentials locally and separately authorizes read-only preflight. Verify actual permissions/balances/symbol contracts without a placement. Until supplied, mark NOT RUN, not pass.
- [ ] Owner specifies actual money limits and separately authorizes a bounded real canary, then performs activation. No agent/tool places an actual financial order under this development request. Observe canary receipts and reconciliation only after that authority exists.
- [ ] Until actual canary evidence exists, release label is “implemented, live execution unverified.” Do not promise profitability or the 1 October date. No product-wide “complete” claim while required gates remain.

## Self-review and execution handoff

Spec coverage: isolation/accounts (1–2,5–7); broker units/API (3); state/recovery (2,4,8); capital/risk/Stop (1,4–5); AI workflow (6); UI/status (7); operator lifecycle/security (5,8); release evidence (9). All five Review Focus cases have explicit task assertions.

Separate OS identity/TLS/account setup needs owner action. This dependency is not hidden behind simulated success. Live account/order/risk visibility must work even when Pixel presentation is unavailable, and full release requires Task 7's private presentation transport tests.

Recommended execution: Native in this chat, primary Codex owning all money/security code and final acceptance; bounded ordinary local-worker assistance only under the repository routing rules. No implementation starts until the owner reviews this plan and confirms the execution approach. Written-spec approval alone is not real-money activation.
