# Hybrid Agent Exit Policy — Phase 1 Implementation Plan

> **For Codex:** Execute task-by-task with TDD. Keep the deterministic champion active until every promotion gate passes. V1 remains paper-only.

**Goal:** Add a hosted Portfolio Agent + Risk Critic exit challenger to historical replay, preserve deterministic safety/accounting, compare it fairly against the current policy, and expose truthful evidence in Operations view.

**Architecture:** Domain owns market evidence, triggers, safety rules, replay decisions, metrics, and a provider-neutral `IHistoricalExitDecisionSource`. Application owns hosted-agent request/response contracts. AgentRuntime implements the bounded meeting. Infrastructure implements OpenAI Responses API and immutable artifact persistence. API composes dependencies; UI only renders returned facts. Domain never references Application, AgentRuntime, Infrastructure, or HTTP.

**Tech:** .NET 10, xUnit, ASP.NET Core `HttpClient`, `System.Text.Json`, Next.js/TypeScript, Docker Compose.

**Source spec:** `docs/superpowers/specs/2026-08-28-adaptive-exit-policy-design.md`

## Non-negotiable constraints

- Paper trading only; no broker/live execution path, leverage, Ollama, or local-AI wiring.
- Agent output is an untrusted proposal. `RiskEngine`, quantity ownership checks, next-open execution, and Controller reconciliation remain deterministic authorities.
- No configured hosted model means `MODEL_UNAVAILABLE`, not a fake Agent.
- Tests use fakes; no network or nondeterministic model calls.
- Persisted sandbox portfolio must remain unchanged by replay.
- 2024–2025 is confirmation evidence, not an unbiased promotion fold.

## Task 1: Cutoff-safe technical evidence

**Files**

- Create: `src/TradingCompany.Domain/ExitTechnicalEvidence.cs`
- Create: `tests/TradingCompany.Domain.Tests/ExitTechnicalEvidenceTests.cs`
- Create: `tests/TradingCompany.Domain.Tests/ExitTestFixtures.cs`

**Red:** Add hand-derived tests for ATR14, SMA20/50/200, momentum20/60, prior-close resistance20, prior-close support10, highest completed close since entry, volatility, unrealized return, and exclusion of bars after `DataCutoff`.

```csharp
[Fact]
public void Calculate_excludes_future_bars_and_hashes_canonical_evidence()
{
    var evidence = ExitEvidenceCalculator.Calculate(
        ExitTestFixtures.Position(), ExitTestFixtures.BarsThrough(new DateOnly(2024, 2, 1)),
        new DateOnly(2024, 1, 31), ExitTestFixtures.Portfolio());

    Assert.Equal(new DateOnly(2024, 1, 31), evidence.LatestObservation);
    Assert.DoesNotContain(evidence.ObservationIds, x => x.Contains("2024-02-01"));
    Assert.Equal(64, ExitEvidenceHasher.Sha256(evidence).Length);
}
```

Run: `dotnet test tests/TradingCompany.Domain.Tests/TradingCompany.Domain.Tests.csproj --filter ExitTechnicalEvidenceTests` — must fail because types do not exist.

**Green:** Define immutable `ExitTechnicalEvidence` and `ExitPositionSnapshot` records. Implement `ExitEvidenceCalculator.Calculate` with bars filtered to `Date <= cutoff` and sorted ascending; fail closed when required history is insufficient. Implement `ExitEvidenceHasher.Sha256` using invariant, ordered canonical JSON. Put reusable deterministic bars, position, portfolio, champion/hold fake decision sources, and a fake clock in `ExitTestFixtures.cs` so later tasks introduce no undefined helpers.

Run the focused test; expect pass.

## Task 2: Review triggers and deterministic safety envelope

**Files**

- Create: `src/TradingCompany.Domain/ExitReviewPolicy.cs`
- Create: `src/TradingCompany.Domain/ExitSafetyPolicy.cs`
- Create: `tests/TradingCompany.Domain.Tests/ExitReviewPolicyTests.cs`
- Create: `tests/TradingCompany.Domain.Tests/ExitSafetyPolicyTests.cs`

**Red:** Test each trigger independently: new ISO week, +10%, +20%, 3×ATR trail crossing after arming, support break, forecast score ≤35, strategy demotion, and safety boundary. Prove a trigger returns a review reason but never an order. Test emergency loss defaults to 12%; portfolio contribution-adjusted drawdown defaults to 25%, blocks buys, and requests owned-position liquidation. Test deposits increase equity and high-water by the same amount.

```csharp
public sealed record ExitReviewContext(
    ExitTechnicalEvidence Evidence, DateOnly DecisionDate, bool ReviewedThisIsoWeek,
    bool AtrArmed, decimal PreviousClose, decimal PortfolioDrawdownPercent,
    bool StrategyPromoted, int ForecastScore);

public enum ExitReviewTrigger
{
    Weekly, ProfitMilestone10, ProfitMilestone20, AtrTrailCrossed,
    SupportBroken, BearishForecast, StrategyDemoted, SafetyBoundary
}
public enum ExitSafetyAction { None, ExitPosition, ExitPortfolio }
public sealed record ExitSafetyLimits(decimal EmergencyPositionLoss = .12m, decimal PortfolioDrawdown = .25m);
```

Run focused filters `ExitReviewPolicyTests|ExitSafetyPolicyTests`; expect fail.

**Green:** Implement pure `ExitReviewPolicy.Evaluate(ExitReviewContext)` returning ordered `ExitReviewTrigger` values and updated ATR-armed state. Implement pure `ExitSafetyPolicy.Evaluate`; safety outputs carry `SAFETY_EXIT` and owned quantity, never model prose. Keep thresholds configurable inputs, not hidden constants.

Run focused tests; expect pass.

## Task 3: Provider-neutral replay decision boundary

**Files**

- Create: `src/TradingCompany.Domain/HistoricalExitDecisions.cs`
- Create: `tests/TradingCompany.Domain.Tests/HistoricalExitDecisionTests.cs`

**Red:** Test allowed `HOLD/TRIM/SELL`, quantity ≤ owned quantity, exact symbol/hash/cutoff match, next-open requirement, and model-failure hold. Test that deterministic safety overrides unavailable/invalid discretionary output.

```csharp
public enum HistoricalExitAction { Hold, Trim, Sell }
public sealed record HistoricalExitRequest(
    DateOnly DecisionDate, ExitTechnicalEvidence Evidence, string EvidenceHash,
    ExitReviewContext Review, ExitSafetyAction SafetyAction);
public sealed record HistoricalExitDecision(
    HistoricalExitAction Action, decimal Quantity, string ReasonCode,
    string Thesis, string Critique, DateOnly DataCutoff, string EvidenceHash,
    bool IsSafetyExit, string ArtifactId);
public interface IHistoricalExitDecisionSource
{
    ValueTask<HistoricalExitDecision> DecideAsync(HistoricalExitRequest request, CancellationToken cancellationToken);
}
```

Run `dotnet test tests/TradingCompany.Domain.Tests/TradingCompany.Domain.Tests.csproj --filter HistoricalExitDecisionTests`; expect fail.

**Green:** Add `HistoricalExitDecisionValidator.Validate(request, decision, ownedQuantity)`. Define `DeterministicChampionExitDecisionSource` in Domain, matching current exit behavior, plus `SafetyFirstExitDecisionSource` decorator: safety exit is returned before calling the inner source. This boundary contains no Agent/OpenAI types.

Run focused tests; expect pass.

## Task 4: Hosted Agent contracts and strict validation

**Files**

- Modify: `src/TradingCompany.Application/Ports.cs`
- Create: `src/TradingCompany.Application/ExitAgentContracts.cs`
- Create: `tests/TradingCompany.Domain.Tests/ExitAgentContractTests.cs`

**Red:** Add contract tests for strict parsing and permission validation: unknown action, wrong symbol/cutoff/hash, unknown evidence ID, empty thesis, confidence outside 0–1, excess quantity, and unknown JSON fields all reject into `HOLD_MODEL_FAILURE`.

```csharp
public sealed record AgentModelRequest(
    string Role, string Model, string ReasoningEffort, string PromptVersion,
    string SystemInstructions, string InputJson, string JsonSchema,
    int MaxOutputTokens, TimeSpan Timeout);
public sealed record AgentModelResponse(
    string OutputJson, string ProviderRequestId, string Model,
    int InputTokens, int OutputTokens, TimeSpan Latency);
public interface ITradingAgentGateway
{
    Task<AgentModelResponse> CompleteStructuredAsync(AgentModelRequest request, CancellationToken cancellationToken);
}
```

**Green:** Add `ExitAgentProposal`, `ExitRiskCritique`, `ExitProposalValidation`, and `ExitAgentContract.ParseAndValidate`. Keep existing `ILanguageModel` for compatibility; do not route exit meetings through its unconstrained string response. Include explicit JSON schemas with `additionalProperties:false`.

Run focused tests; expect pass.

## Task 5: Immutable artifacts and hosted OpenAI gateway

**Files**

- Create: `src/TradingCompany.Application/AgentDecisionArtifacts.cs`
- Create: `src/TradingCompany.Infrastructure/JsonAgentDecisionArtifactStore.cs`
- Create: `src/TradingCompany.Infrastructure/OpenAiTradingAgentGateway.cs`
- Create: `tests/TradingCompany.Domain.Tests/AgentDecisionArtifactTests.cs`
- Create: `tests/TradingCompany.Domain.Tests/OpenAiTradingAgentGatewayTests.cs`

**Red:** Use a temporary file and fake HTTP handler. Test exact cache key `(model,promptVersion,role,evidenceHash,mandateVersion,dataCutoff)`, append-only versions, no overwrite on key mismatch, atomic concurrent append, strict-schema request body, `store:false`, response/usage parsing, timeout, malformed response, cancellation, and absence of API key in exceptions/loggable values.

**Green:** Define `AgentArtifactKey`, `AgentDecisionArtifact`, and `IAgentDecisionArtifactStore`. Store each immutable artifact as one canonical JSON file under the configured directory, named by its artifact ID; use `FileMode.CreateNew`, then flush before exposing it. Maintain no mutable truth file: exact-key lookup validates candidate file contents and chooses the earliest valid version; a new response gets a new ID/file. Serialize in-process writes with `SemaphoreSlim`; concurrent duplicate writes may create distinct immutable versions but lookup remains deterministic. Implement `OpenAiTradingAgentGateway` with injected named `HttpClient`, bearer key from configuration, `POST /v1/responses`, and strict `text.format` JSON schema. No model default in Domain; empty model fails as unavailable before HTTP.

Run both focused test classes; expect pass.

## Task 6: Bounded Portfolio Agent / Risk Critic meeting

**Files**

- Modify: `src/TradingCompany.AgentRuntime/AgentRuntime.cs`
- Create: `src/TradingCompany.AgentRuntime/HybridExitDecisionSource.cs`
- Modify: `src/TradingCompany.AgentRuntime/TradingCompany.AgentRuntime.csproj`
- Create: `tests/TradingCompany.Domain.Tests/HybridExitDecisionSourceTests.cs`

**Red:** With queued fake gateway responses, prove the exact sequence Portfolio proposal → Critic challenge → Portfolio final proposal. Assert separate role instructions, permissions, memory namespaces, shared immutable evidence, prior position decision artifact, maximum two Portfolio turns + one Critic turn, exact artifact cache reuse, per-meeting/token budgets, and these fail-closed results: model unavailable, timeout, malformed response, Critic failure, budget exhaustion. Prompts must explicitly state that news, sentiment, earnings, fundamentals, and macro are unavailable; invented evidence IDs reject. Safety exits must make zero model calls.

```csharp
public sealed record ExitAgentRuntimeOptions(
    bool Enabled, string Model, string ReasoningEffort, string PromptVersion,
    string MandateVersion, int MaxOutputTokens, int MaxMeetingsPerFold,
    int MaxModelCallsPerMeeting, int MaxTotalTokensPerFold, TimeSpan Timeout);

public sealed record ExitMeetingResult(
    HistoricalExitDecision Decision, int ModelCalls, int InputTokens,
    int OutputTokens, int CacheHits, string Status);
```

**Green:** Implement `HybridExitDecisionSource : IHistoricalExitDecisionSource`. It builds role-specific prompts only from serialized `HistoricalExitRequest` plus the exact prior artifact for that position namespace, checks cache before each call, persists every valid provider response as a new immutable artifact, validates the final proposal, and returns provider-neutral Domain decisions. Roles have no portfolio-store, provider, HTTP, order, or Controller access. Catch only known provider/format/budget failures and map them to `HOLD_MODEL_FAILURE`; propagate cancellation. Update test project reference to AgentRuntime if absent.

Run `dotnet test tests/TradingCompany.Domain.Tests/TradingCompany.Domain.Tests.csproj --filter HybridExitDecisionSourceTests`; expect pass.

## Task 7: Async historical replay integration

**Files**

- Modify: `src/TradingCompany.Domain/HistoricalCompanySimulation.cs`
- Modify: `tests/TradingCompany.Domain.Tests/HistoricalCompanySimulationTests.cs`
- Modify: `tests/TradingCompany.Domain.Tests/ExitTestFixtures.cs`

**Red:** Add deterministic fixtures proving: review trigger calls the decision source once per unique evidence hash; HOLD creates no trade; TRIM/SELL still pass `RiskEngine`; order executes only at next eligible open; overnight gap uses that open; missing next bar keeps position; safety exits work when source fails; audit captures evidence cutoff/hash, thesis, critique, validation, risk outcome, and fill as separate stages; persisted sandbox state is untouched.

**Green:** Add `RunAsync(CompanySimulationInput, IHistoricalExitDecisionSource, Action<DateOnly>?, CancellationToken)` and keep `Run` as a compatibility wrapper using `DeterministicChampionExitDecisionSource` only. Never synchronously block a hosted source. Extend `SimulationTrade`, timeline, and result with typed exit provenance/meeting counters while preserving existing JSON fields. Deduplicate by evidence hash. Queue decisions for the next eligible bar and audit `ExecutionDate > DataCutoff`. Route every non-HOLD proposal through existing `RiskEngine` and current accounting functions.

Run `HistoricalCompanySimulationTests`; then full .NET tests.

## Task 8: Money-weighted metrics and promotion tournament

**Files**

- Create: `src/TradingCompany.Domain/ExitPolicyTournament.cs`
- Modify: `src/TradingCompany.Application/Ports.cs`
- Create: `tests/TradingCompany.Domain.Tests/ExitPolicyTournamentTests.cs`

**Red:** Hand-calculate fixtures for XIRR, contribution-adjusted high-water/drawdown, peak profit/giveback, annualized-return-to-drawdown score, median across three folds, two-of-three wins, and every failure gate. Explicitly test tie, missing XIRR root, missing provenance, model failure, accounting failure, lookahead violation, >25% fold drawdown, budget overrun, and unauthorized attempt all keep champion.

```csharp
public sealed record DatedCashFlow(DateOnly Date, decimal Amount);
public sealed record ExitPolicyFold(DateOnly Start, DateOnly End);
public sealed record ExitPolicySummary(
    decimal ReturnOnDeposits, decimal? Xirr, decimal MaxDrawdownPercent,
    decimal ReturnToDrawdown, decimal PeakGiveback, int Trades,
    decimal Fees, decimal Slippage, AgentRunStatistics AgentStatistics,
    bool AccountingValid, LookaheadVerification Lookahead);
public sealed record AgentRunStatistics(
    int Meetings, int CacheHits, int Failures, int SafetyExits,
    int Holds, int Trims, int Sells, int InputTokens, int OutputTokens,
    bool BudgetExceeded, bool UnauthorizedAttempt, bool ProvenanceComplete);
public sealed record ExitPolicyFoldResult(
    ExitPolicyFold Fold, ExitPolicySummary Champion, ExitPolicySummary Challenger,
    bool ChallengerWon);
public sealed record ExitPolicyTournamentInput(
    IReadOnlyDictionary<Symbol, IReadOnlyList<HistoricalBar>> Histories,
    StrategyCostModel Costs, decimal InitialCapital, decimal MonthlyContribution);
public sealed record PromotionGate(string Code, bool Passed, string Reason);
public sealed record ExitPolicyTournamentResult(
    IReadOnlyList<ExitPolicyFoldResult> Folds, IReadOnlyList<PromotionGate> Gates,
    bool Promoted, string ActivePolicy, string Status);
public interface IExitPolicyTournamentRunner
{
    Task<ExitPolicyTournamentResult> RunAsync(
        ExitPolicyTournamentInput input, CancellationToken cancellationToken);
}
```

**Green:** Implement the Domain records shown above, bounded deterministic `MoneyWeightedReturn.TryCalculateXirr` (bracket root, bisection, explicit no-root), pure metrics, and `ExitPolicyPromotionPolicy`. Add `IExitPolicyTournamentRunner` to Application after Domain types compile. Tournament folds are 2018–2019, 2020–2021, 2022–2023, each fresh THB 12,000 + THB 12,000 monthly and 400 calendar-day warmup. Both policies receive identical entries/data/costs; only exit source changes. Gates exactly match the design spec. Champion wins all ties/incomplete evidence. `Promoted` means “eligible for a later sandbox-runtime rollout”; it must not mutate the continuously running company's active policy.

Run focused tests; expect pass.

## Task 9: Handler, API, configuration, and dependency composition

**Files**

- Modify: `src/TradingCompany.Application/RunCompanySimulationCommand.cs`
- Modify: `src/TradingCompany.Application.Handlers/RunCompanySimulationHandler.cs`
- Create: `src/TradingCompany.AgentRuntime/ExitPolicyTournamentRunner.cs`
- Modify: `src/TradingCompany.Api/Program.cs`
- Modify: `src/TradingCompany.Api/CompanySimulationRequest.cs`
- Modify: `src/TradingCompany.Api/appsettings.json`
- Modify: `src/TradingCompany.Api/TradingCompany.Api.csproj`
- Modify: `src/TradingCompany.Infrastructure/TradingCompany.Infrastructure.csproj`
- Modify: `tests/TradingCompany.Domain.Tests/CompanySimulationHandlerTests.cs`
- Modify: `tests/TradingCompany.Domain.Tests/CompanySimulationApiTests.cs`

**Red:** Test default request runs champion without an Agent call. A tournament request with disabled/empty model returns `MODEL_UNAVAILABLE` and never promotes. With fake gateway/artifacts, verify three isolated folds, correct 400-day history request, cancellation/progress, truthful model/prompt/mandate metadata, and unchanged champion result. Reject attempts to request live mode or bypass safety.

**Green:** Implement `ExitPolicyTournamentRunner : IExitPolicyTournamentRunner` as the Application-facing orchestrator/factory that creates fresh champion/challenger sources and fresh Agent budgets per fold. Add an optional `evaluateExitChallenger` request flag; keep default backward-compatible. Handler loads fold data once and passes typed `ExitPolicyTournamentInput` to injected runner; it never constructs AgentRuntime classes. Do not reference AgentRuntime from Domain, Application, or Application.Handlers. API adds a reference to AgentRuntime and registers the runner against the Application port; Infrastructure remains the only hosted HTTP/artifact implementation. Never write the tournament recommendation into `AutonomousCompanyService`, `PaperPortfolio`, or its scheduler configuration.

Bind configuration under `TradingAgents:ExitPolicy` with safe defaults:

```json
{
  "Enabled": false,
  "Model": "",
  "ReasoningEffort": "medium",
  "PromptVersion": "exit-v1",
  "MandateVersion": "aggressive-sandbox-v1",
  "TimeoutSeconds": 60,
  "MaxOutputTokens": 800,
  "MaxMeetingsPerFold": 120,
  "MaxModelCallsPerMeeting": 3,
  "MaxTotalTokensPerFold": 120000,
  "ArtifactPath": "/data/agent-exit-artifacts-v1",
  "EmergencyPositionLoss": 0.12,
  "PortfolioDrawdown": 0.25
}
```

Read `OPENAI_API_KEY` only from environment/user secrets. Add startup validation only when enabled. Run handler/API tests; expect pass.

## Task 10: Operations-view evidence

**Files**

- Modify: `apps/trading-company-web/app/types.ts`
- Modify: `apps/trading-company-web/app/page.tsx`
- Modify: `apps/trading-company-web/app/globals.css`
- Modify: `tests/TradingCompany.Domain.Tests/CompanySimulationApiTests.cs`

**Red:** Extend API snapshot assertions first: tournament status, side-by-side fold metrics, gate codes/reasons, model/prompt/mandate versions, meetings/cache/failures/token usage, and per-trade exit stages. Missing model must serialize `MODEL_UNAVAILABLE`; safety exits must serialize `SAFETY_EXIT`.

**Green:** Add typed `ExitPolicyTournament` structures. Render one compact panel with champion/challenger columns, fold summaries, promotion badge, explicit pass/fail gates, and truthful unavailable/invalid states. Timeline displays Portfolio thesis, Critic objection, deterministic validation, RiskEngine result, and fill separately. Do not alter Pixel office behavior and do not label deterministic roles as hosted Agents.

Run:

```powershell
npm --prefix apps/trading-company-web run typecheck
npm --prefix apps/trading-company-web run build
```

## Task 11: Documentation, Docker, and final proof

**Files**

- Modify: `.env.example`
- Modify: `docker-compose.yml`
- Modify: `README.md`
- Modify: `PRODUCT.md`
- Modify: `DESIGN.md`

**Red:** Add/extend health/API test proving mode stays `paper` whether Agent is enabled or unavailable. Confirm no live route is registered.

**Green:** Document champion/challenger meaning, how to opt into hosted evaluation, costs/budgets, immutable artifacts, failure behavior, and why promotion is not a profit guarantee. Compose passes `OPENAI_API_KEY: ${OPENAI_API_KEY:-}` and persists the artifact path; default remains disabled.

Run complete verification:

```powershell
dotnet test TradingCompany.sln -c Release
dotnet build TradingCompany.sln -c Release --no-restore
npm --prefix apps/trading-company-web run typecheck
npm --prefix apps/trading-company-web run build
docker compose config
docker compose up -d --build
Invoke-RestMethod http://localhost:8080/health
Invoke-WebRequest http://localhost:3000/ -UseBasicParsing
```

Then run one real public-data champion replay to prove existing behavior. If an API key/model are explicitly configured, run one separately invoked bounded challenger smoke replay and report provider/model, request count, token usage, artifact IDs, lookahead/accounting results, and promotion gates. Never require that smoke call for deterministic CI.

## Completion checklist

- All tests/builds pass without network-dependent CI tests.
- Domain dependency direction remains clean.
- No Agent can mutate cash, portfolio, fills, RiskEngine, or Controller.
- Every discretionary decision is cutoff-safe, schema-valid, cached, and auditable.
- Every safety exit remains available during model failure.
- Tournament compares identical inputs and keeps champion on any incomplete/failed gate.
- UI distinguishes hosted Agent judgment, deterministic validation, safety, and paper fill.
- Default deployment is paper-only, champion-active, hosted Agent disabled.
