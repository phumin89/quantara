# Architecture

Quantara uses a modular .NET solution and a Next.js client. Domain owns deterministic financial invariants; Application owns contracts/accounting workflows and ports; Infrastructure implements storage/provider transport. Api composes Sandbox services, exposes HTTP/SignalR and currently contains background workflow orchestration.

## Main Sandbox path

`start-sandbox.ps1` composes the Windows API with Codex CLI and the Docker web/Pixel Office. The alternate Docker API profile uses an explicitly enabled OpenAI API gateway. Both select the single forward company. Sandbox JSON persistence needs no database; Live retains PostgreSQL.

`ForwardFundingService` advances the saved simulated treasury and synchronizes `JsonForwardPaperStore`. `ForwardPaperCompanyService` claims one Bangkok-date meeting in `JsonDailyMeetingJournal`, obtains completed historical evidence and runs `AiCompanyMeetingRunner`. Valid proposals become append-only paper intents. Later ticks obtain fresh quotes and pass them through `ForwardPaperLedger` and deterministic RiskEngine. The checksummed journal reconstructs positions, cash, receipts and pending orders.

Meeting summaries are durable observational evidence, separate from financial authority. Status reads do not run AI or mutate a ledger. Interrupted claims, corrupt journals and unreconciled funding fail closed. Old completed journals without a summary stay compatible and do not receive invented reports.

The duplicate company loop, replay/CFO simulation endpoints, option engine, research tournaments, obsolete request handlers and Mediator projects are removed. Saved legacy files remain untouched. The forward investment compatibility reader still reconciles existing credits; it is not a second trading loop.

## Live boundary

`TradingCompany.LiveExecution` is a separate HTTPS/loopback host for paired owner authentication, CSRF, owner controls, coordinator packets and recovery/reconciliation. `LiveExecutionService` owns durable preparation and one-attempt dispatch through `ILiveBroker`. `LiveReconciliation` consumes complete normalized evidence through `ILiveReconciliationSource`. `LiveTradingStore` uses separate PostgreSQL tables and immutable evidence.

The production composition root does not register a real broker, reconciliation evidence source, owner-control store or account provisioning. `BitkubLiveBroker` currently supplies raw fixed-host transport, not either normalized execution port. `LiveCompanyCoordinator` and its client are tested advisory components, not proof of a deployed Live company. Missing registration leaves Live unavailable.

Before registration, verify broker units, fees/credits, exact order/fill lineage and buy-budget semantics; implement bound-account evidence coverage and explicit performance baselines; provision restricted credentials and trusted TLS; then prove startup recovery, Stop, reconciliation and owner operation. Successful reconciliation never arms an account. Sandbox journals are never an input to Live authority.

## Scope

Preserve the deterministic layers, journals and account boundaries. Daily operation has one funding/meeting/execution path. Strategy validation remains because the Live adviser depends on it; adding a strategy or research surface is outside this cleanup. Api still owns background workflow orchestration. The Live completion gates remain open.

See [the review/completion gates](project-rework.md) and [Live evidence](live-release-evidence.md).
