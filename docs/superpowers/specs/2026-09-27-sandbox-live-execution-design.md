# Quantara: Sandbox and owner-authorized Live execution

Date: 2026-09-27
Status: approved by the owner in chat on 2026-09-27; implementation plan pending review. No live implementation or activation claimed.

## Intent and boundary

The owner wants an AI company that can actually trade real money and also has a Sandbox mode. A paper-only product does not satisfy that goal. After explicit activation, specialist agents work automatically; the owner is not required to approve every order within the saved mandate. RiskEngine remains the deterministic final authority.

The requested target is 1 October 2026. This is a delivery target, not permission to bypass verification or a promise that the release will be ready then. Developing the Live path is authorized. Connecting a private account, choosing a monetary risk budget and enabling actual transactions are separate owner actions.

First broker: Bitkub spot, initially BTC-THB, ETH-THB and SOL-THB. This reuses the working public-market integration. It is not a statistical claim that crypto outperforms stocks. Webull and additional assets are later integrations, not fake selectable options.

## Current repository evidence

- `ForwardPaperCompanyService` advances simulated payroll and writes directly to `JsonForwardPaperStore`. It is not a broker coordinator.
- `ForwardPaperLedger` models fills, costs and accounting. Its synthetic fills must never become Live execution evidence.
- `AiCompanyMeetingRunner` already produces bounded structured specialist artifacts and final non-binding proposals. Reuse its eight-role workflow.
- `RiskEngine` validates proposal limits. Live also needs confirmed holdings, reserved cash, order states and broker reconciliation; those are not currently provided by the generic `ITradingExecutionGateway`.
- `Program.cs` exposes local endpoints without owner authentication. CORS is not authentication and cannot authorize live mutations.
- The existing launcher starts the web and office in Docker and the Codex-enabled API on Windows. No Windows-service auto-start is installed. Latest runtime check found web/office alive but the API unreachable.
- Existing PostgreSQL/Npgsql dependencies are available. Paper account files must stay untouched by the new storage path.

## Architecture decision

Three alternatives were considered:

1. Swap the existing paper gateway globally: rejected. Legacy demo endpoints and simulated capital could reach a real broker.
2. Add a separate guarded Live coordinator and broker executor: selected. Reuse analysis and domain validation while isolating money and secrets.
3. Build a multi-broker platform immediately: deferred. Adds integration and failure cases before the first complete broker path works.

The owner-facing application has two explicit account workspaces. Selecting a workspace changes the view, not authorization. Opening Live initially shows disabled/readiness state. Sandbox routes never resolve a Live executor. Every persisted decision, event, receipt, meeting and artifact carries a mode/account identity; UI and Pixel Office filter by that identity.

The Live coordinator takes a reconciled broker-backed portfolio snapshot and current approved mandate, obtains fresh market evidence, calls the existing specialist meeting workflow, validates its structured proposals, and sizes permitted intents. It then passes intents to the deterministic Live executor. No frontend endpoint accepts an arbitrary broker order as a shortcut around this flow.

The executor independently rechecks account identity, arming, stop state, risk, balances, open orders, symbol constraints and freshness immediately before dispatch. Risk approvals are bound to the exact intent, mandate version and snapshot version; an old approval cannot authorize a modified order.

## Money and mandates

- Sandbox keeps its current salary simulation, reserve gate, paper ledger and backtests.
- Live funding is actual broker-held money explicitly allocated by the owner. The salary simulator is never a source of Live buying power.
- Initial Live capital, maximum order value, daily turnover cap, maximum daily loss, maximum drawdown, exposure cap and allowed symbols require owner input. Defaults allow zero real spending.
- Budget checks reserve outstanding orders and fees; concurrent proposals cannot each spend the same available cash.
- Owned coin quantity bounds every sell. No borrowing, leverage, shorting, options, deposits, withdrawals or bank integrations in this slice.
- Pre-existing/manual positions are displayed separately and cannot be sold by AI. For initial activation, require the selected broker account to have no crypto positions or open orders; do not liquidate anything to meet this gate. Non-empty accounts remain blocked until an explicit inventory-adoption workflow exists.
- Extra deposits do not enlarge the mandate automatically. Unexplained deposits, withdrawals or manual trades halt execution until reconciled and acknowledged.
- Daily loss uses start-of-day equity adjusted for recorded external flows; drawdown uses flow-adjusted high-water equity. Missing valuations block trading. These are risk controls, not guarantees against loss or gaps.

## Broker contract (verified against official docs)

Use fixed HTTPS Bitkub endpoints, no configurable arbitrary destination or redirect forwarding. Trading remains V3; wallet balances use V4. Private requests use a fresh server timestamp and HMAC headers. Buy amount is THB spending; sell amount is coin quantity. Persist `client_id`, but do not assume it guarantees broker-side idempotency. Read status, paginated history and balances to reconcile actual fills and fees; an accepted request is not a fill. Treat exchange order and transaction IDs as opaque strings. Validate current symbol metadata, precision, minimum size and trading restrictions before sending.

Sources checked 2026-09-27:

- https://github.com/bitkub/bitkub-official-api-docs
- https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v3.md
- https://github.com/bitkub/bitkub-official-api-docs/blob/master/rest-v4.md

Contract ambiguities or changed schemas fail closed and require reconciliation; never infer units from a numeric magnitude. Initial execution uses limit orders with an owner-approved price/slippage bound; no uncapped market-order fallback.

## Durable execution and recovery

Use a separate Live PostgreSQL schema and repository, using the existing Npgsql stack. This adds a required durable database to the Live launch profile; Sandbox continues to work without it. A unique account/decision/intent key and serialized account transaction protect budget reservations and durable state. Only one dispatcher may own an account at a time, including across processes.

Order lifecycle:

`PROPOSED -> REJECTED` or `PROPOSED -> RESERVED -> DISPATCHING -> ACKNOWLEDGED -> OPEN/PARTIALLY_FILLED -> FILLED/CANCELLED`.

Before network transmission, durably record `DISPATCHING` and the exact request identity. Any timeout, connection loss, invalid response, process crash in that state, or failure to persist the acknowledgement becomes `UNKNOWN`. Never retry an unknown placement automatically, including after restart. Stop new account orders and reconcile using broker evidence. If absence cannot be proved, remain blocked for owner investigation; a missing open order alone does not prove rejection.

Recognized terminal rejection releases a reservation. Acknowledgement keeps funds reserved. Apply actual fills exactly once using transaction identity; support partial fills, multiple fills and fee corrections without guessing. Release the remainder only after confirmed terminal status and reconciled funds. Store raw broker evidence with redacted authentication, audit timestamps and normalized records.

Cancellation is also a reconciled operation. A timeout does not mean cancellation succeeded; fills may race cancellation. Poll order/history/balances and retain reservations until resolved. Cancel requests use only the app's known orders, never every order on the account.

## Owner control and security

- Default state `DISABLED`; successful read-only checks yield `READY_TO_ARM`, never automatic activation.
- Arming binds an owner session to a broker identity, configuration fingerprint and positive saved mandate. Key/account/limit changes require re-arming. Restart returns to disarmed state after reconciling outstanding orders.
- Add owner authentication before any Live account view or mutation. Use a locally paired owner session with HttpOnly, SameSite cookies, CSRF protection and exact origin/host validation. Loopback binding is required in the initial local deployment; remote deployment requires a separate security design and TLS.
- Keep broker credentials in a restricted executor service, not the Codex-enabled process. That service owns signing and revalidation. Model processes must not inherit secrets or have filesystem/container-administration access to the credential store. Use a distinct restricted runtime identity; sharing an unrestricted desktop identity is not a credential-isolation boundary.
- Keys are entered locally, never pasted into this chat, browser source, prompts or checked-in files. Credential displays and logs are redacted. Never enable withdrawals; require the owner to verify permissions in the broker portal if the API cannot attest them.
- Persistent Stop prevents new placements. Already transmitted orders may fill; show them explicitly and offer a distinct cancel-owned-orders action. Stop does not sell all holdings or guarantee losses stop.
- A broker-read failure, reconciliation mismatch, stale price, invalid AI output, unavailable model, expired mandate or exhausted usage quota results in no new order. Reconciliation and stop controls remain available even without AI.

## UI and operation

Keep the existing company, money and research views. Add an unmistakable Sandbox / Live account selector and separate account balances, receipts, P&L and evidence. Live has a readiness checklist: backend, model, restricted executor, owner authentication, credentials, actual funds, mandate, reconciliation, arming. Show precise blocked reasons rather than an empty office or fake successful activity.

Live UI must distinguish disabled, connecting, ready-to-arm, armed, researching, holding, order pending, partially filled, stopped and reconciliation required. “HOLD” is a valid completed AI decision, not an execution test. Display actual fees and actual broker fills separately from marked-to-market estimates and Sandbox costs.

Run analysis at most once per Bangkok date after 09:00 initially; reconcile open orders independently every 30 seconds. Daily scheduling is not a requirement to trade daily. All scheduled work is account-scoped. A single launcher checks and starts required components and reports backend readiness; web availability alone is not system readiness. Do not install recurring OS startup or change sleep settings silently.

## Verification and release gates

1. Deterministic contract fixtures cover signature bytes, numeric serialization, buy/sell units, IDs, API error bodies, stale metadata, rejected orders and redirected requests.
2. Sandbox cannot construct/resolve the Live executor; wrong-mode funds, artifacts and proposals are rejected. Existing saved Sandbox data and regression tests remain intact.
3. Tests cover zero/unset limits, overspending with concurrent intents, insufficient coin inventory, fees, open-order reserves, stop-before-dispatch and tampered approvals.
4. Fault injection covers crashes before/after transmission, unknown placement, acknowledgement persistence failure, duplicate fills, partial fills, cancellation races, stale data and paginated reconciliation. Exactly one placement attempt is allowed for an ambiguous intent.
5. Auth tests deny unauthenticated state access/mutations, cross-origin requests, stale sessions and unapproved account changes. Verify secrets are absent from model processes, prompts, UI and logs.
6. UI tests verify mode/account segregation and truthful blocked states. Full stack restart proof confirms reconciliation first and no silent re-arming.
7. After credentials are supplied locally by the owner, perform read-only account preflight. No account-specific success is claimed before this evidence exists.
8. The owner separately authorizes a bounded real-money canary; owner-controlled activation must happen before that test. Verify an actual order, fill or cancellation and account reconciliation. Without it, label the release “implemented, live execution unverified,” not live-ready.

No profitability or capital-preservation guarantee follows from these tests. This design does not authorize changing strategy parameters until a historical test wins.

## Delivery sequence and stop condition

Implement in slices: isolated account/authorization and storage; authenticated broker contract and deterministic state machine; AI coordinator and reconciliation; UI/readiness and lifecycle; failure-injection tests and deployment proof. Every unfinished slice leaves Live disabled. Never ship a switch that labels paper receipts as live.

The implementation can be completed and tested with mock private broker responses without spending real money. Account-specific preflight and the real canary remain explicitly pending owner setup/authorization, rather than concealed behind a “completed” label.

The owner explicitly approved this written design in chat on 2026-09-27. The next artifact is the implementation plan, whose review precedes product-code changes. The workspace has no Git metadata, so this document is saved locally, not committed.
