# Hybrid Agent Exit Policy — Phase 1 Design

## Goal

Make exit decisions behave like an AI trading company without giving a model direct control of money. A Portfolio Agent decides `HOLD`, `TRIM`, or `SELL` from dated technical evidence; a Risk Critic Agent challenges the proposal; deterministic validation, RiskEngine, Paper Trader, and Controller retain final operational authority.

ATR, momentum, trend, support/resistance, and profit milestones are evidence and meeting triggers. They are not the sole trading brain. Exact numbers remain only where deterministic safety needs enforceable boundaries.

Phase 1 remains paper-only. It adds no live broker execution, leverage, Ollama, local AI, or model path that can bypass RiskEngine.

## Scope

Phase 1 adds a hosted-model exit challenger to historical company replay and Operations-view evidence. It does not silently replace the continuously running sandbox company's current policy. A challenger becomes eligible for a later sandbox-runtime rollout only after the promotion evidence in this design is visible and valid.

The current deterministic historical policy remains the champion. The hybrid challenger uses identical entries, universe, THB 12,000 monthly funding, costs, position limits, market histories, next-bar execution, and accounting. Only exit deliberation differs.

When no hosted model is configured, the UI reports `MODEL UNAVAILABLE`; the challenger cannot be promoted. The champion remains usable. Quantara must never label deterministic placeholder text as an Agent decision.

## Company Decision Flow

1. **Technical Monitor** calculates evidence from completed bars only.
2. A weekly review or material technical trigger creates an exit-review task.
3. **Portfolio Agent** receives the mandate, owned positions, dated evidence, costs, and prior position memory. It returns a schema-constrained proposal.
4. **Risk Critic Agent** receives the same dated evidence and the proposal. It returns objections and a non-binding recommendation.
5. **Portfolio Agent** receives the critique and returns one final proposal. The meeting is capped at two Portfolio Agent turns and one Risk Critic turn.
6. Deterministic schema and permission validation rejects malformed or unauthorized output.
7. **RiskEngine** approves or rejects the validated proposal. An Agent cannot override it.
8. **Paper Trader** schedules an approved order for the next eligible bar's open.
9. **Controller** reconciles cash, quantity, fees, slippage, and realized PnL immediately after each fill.
10. **Audit Agent** may summarize immutable results but cannot alter decisions or accounting.

Model output is always a proposal, never a fill or ledger mutation.

## Evidence Available to Agents

Each review contains only information whose observation date is at or before the review cutoff:

- symbol, quantity, average cost, current exposure, and portfolio cash;
- completed OHLCV bars and latest completed close;
- SMA20, SMA50, and SMA200;
- 20-day and 60-day momentum;
- ATR14 and volatility;
- highest completed close since entry;
- unrealized return and maximum observed unrealized profit;
- 20-day resistance and 10-day support based on prior completed closes;
- selected walk-forward strategy and its validation evidence;
- current deterministic forecast score and direction;
- fees, slippage, portfolio drawdown, and mandate limits;
- prior Agent decision artifact for that position.

Phase 1 supplies no news, sentiment, earnings, fundamentals, or macro data. Prompts must say those datasets are unavailable; Agents cannot invent them.

## Review Triggers

The Technical Monitor opens one batched meeting for all affected held positions when any condition occurs:

- first eligible review of a new ISO week;
- completed close reaches 10% above average cost, arming ATR evidence;
- completed close reaches 20% above average cost;
- completed close crosses below `highest completed close - (3 × ATR14)` after ATR evidence is armed;
- completed close crosses below 20-day support;
- forecast becomes bearish with score 35 or lower;
- selected strategy is no longer promoted;
- position or portfolio reaches a deterministic safety boundary.

Triggers mean “Agent review required,” not “sell.” Identical evidence hashes do not create duplicate meetings.

## Agent Output Contract

The final Portfolio Agent response is strict structured data containing:

- `action`: `HOLD`, `TRIM`, or `SELL`;
- `symbol`;
- `quantity` for `TRIM` or `SELL`;
- `confidence` from 0 to 1;
- `thesis` and `invalidationReason`;
- evidence identifiers used;
- `dataCutoff`;
- expected risk and opportunity cost;
- Critic objections accepted or rejected with reasons.

Validation requires an owned symbol, allowed action, available quantity, matching cutoff, known evidence identifiers, and non-empty reasons. Unknown fields do not grant permissions. Invalid, missing, timed-out, or budget-exceeded output becomes `HOLD_MODEL_FAILURE`; no discretionary order is created.

## Deterministic Safety Envelope

The Agent may sell or trim earlier, but it cannot weaken these boundaries:

- no leverage or negative cash;
- no naked or short options;
- no order larger than the owned quantity;
- existing maximum order, exposure, and open-position limits;
- configurable aggressive-sandbox emergency position loss limit, default 12% from average cost;
- configurable portfolio drawdown halt, default 25% of contribution-adjusted high-water equity;
- kill switch blocks new exposure;
- next-bar execution only;
- malformed or unavailable market evidence creates no new discretionary trade.

Crossing the emergency position-loss boundary creates a deterministic full-exit proposal even when the model is unavailable. Crossing the portfolio drawdown boundary blocks new buys and queues full exits for all owned positions at their next eligible opens. These paths are labeled `SAFETY_EXIT`, not Agent judgment.

Stops are decision boundaries, not guaranteed fill prices. Overnight gaps may fill worse at the next eligible open and must remain visible.

## Hosted Agent Runtime

Add an Application-layer agent port with a hosted OpenAI implementation. Model, reasoning effort, prompt version, per-role input/output budgets, timeout, and maximum turns are configuration. No model name is hard-coded into Domain logic.

Each role has separate instructions, context, memory namespace, permissions, and budget. Phase 1 roles are `PortfolioExitAgent` and `RiskCriticAgent`. They cannot access portfolio stores, market providers, HTTP clients, order execution, or Controller state directly.

Historical decisions are persisted as immutable artifacts keyed by:

- model and prompt version;
- role;
- evidence hash;
- mandate version;
- data cutoff.

A replay reuses an exact cached artifact. A new model call creates a new artifact version instead of overwriting evidence. This makes a completed historical run reproducible even though first-time model inference itself is not deterministic.

Automated tests use deterministic fake agent responses and never call a hosted model. A separately invoked smoke test may use the configured hosted provider and must report its cost and model metadata.

## No-Lookahead Controls

- Prompt construction accepts an explicit `dataCutoff` and rejects any later observation.
- Agent tools expose only the immutable evidence artifact, not raw unrestricted providers.
- Every proposal repeats the cutoff and evidence identifiers.
- Execution must occur after the cutoff on the next eligible bar.
- A future observation, future cutoff, or early execution invalidates the entire run.
- Cached decisions are reusable only when their evidence hash and cutoff match exactly.

## Champion/Challenger Evaluation

The API loads history from 400 calendar days before 2018-01-01 through the requested replay end. The tournament compares champion and hybrid challenger over three isolated folds: 2018-2019, 2020-2021, and 2022-2023.

Each fold starts THB 12,000, adds THB 12,000 monthly, uses preceding history only as indicator warmup, and carries no cash or positions into another fold. Both policies receive identical market and funding inputs. Symbols without sufficient fold evidence are excluded from both sides of that fold.

Promotion requires:

- zero lookahead violations in every fold;
- valid Controller reconciliation in every fold;
- complete model, prompt, budget, and artifact provenance;
- positive aggregate profit after fees and slippage;
- strictly positive alpha over the contribution-aware equal-weight buy-and-hold benchmark in at least two of three folds;
- positive median alpha across the three folds;
- median annualized-return-to-maximum-drawdown score at least 10% above champion;
- challenger score above champion in at least two folds;
- median return on deposits no more than 2 percentage points below champion;
- total cash-flow-adjusted peak-profit giveback at least 20% below champion;
- maximum drawdown no greater than 25% in every fold;
- no unauthorized attempt, unhandled safety boundary, permission violation, schema violation, or model-budget overrun.

Tie, missing model evidence, missing valid XIRR root, invalid accounting, or any failed gate keeps the champion. Neither Agent can promote itself.

The already-inspected 2024-2025 result is confirmation evidence, not an unbiased holdout. The observed giveback influenced this feature. A future unseen period or separately frozen universe remains necessary before any live-trading discussion.

## Metrics

Results add:

- active exit policy and promotion decision;
- model, prompt, mandate, evidence, and artifact versions;
- Agent review count, cached review count, failures, latency, and token budget use;
- HOLD, TRIM, SELL, and deterministic safety-exit counts;
- annualized return, maximum drawdown percentage, and return-to-drawdown score;
- cash-flow-adjusted peak profit and peak-to-final giveback;
- per-trade Agent thesis, Critic objection summary, exit reason, threshold evidence, and RiskEngine decision.

Cash-flow-adjusted profit is `equity - cumulative contributed capital`. Peak giveback is maximum cash-flow-adjusted profit minus final cash-flow-adjusted profit.

Annualized return is money-weighted XIRR: deposits are negative dated cash flows and final equity is positive on fold end. A deterministic bounded solver is required; no valid root fails promotion. Each deposit increases both equity and the prior high-water value by the same amount before marking drawdown, so deposits cannot erase losses.

## UI

Operations view adds an **Exit Policy Tournament** with champion and challenger side by side:

- return, annualized return, drawdown, giveback, trades, and costs;
- Agent meetings, cache hits, failures, and budget use;
- promotion gates with explicit pass/fail reasons;
- active model/prompt/mandate versions;
- a clear `MODEL UNAVAILABLE` or `INVALID AGENT EVIDENCE` state.

Trade and timeline rows show the Portfolio Agent thesis, Risk Critic objection, deterministic validation, RiskEngine outcome, and paper fill as separate stages. Safety exits are visually distinct. The UI never claims that a deterministic role is a hosted Agent.

## Failure Handling

- Hosted model unavailable, timeout, malformed output, or exhausted budget: discretionary `HOLD_MODEL_FAILURE`; safety remains active.
- Risk Critic unavailable: no discretionary order; safety remains active.
- Provider or evidence failure: no Agent meeting and no discretionary order.
- Missing next bar: position remains open; no synthetic fill or final liquidation.
- Invalid accounting or lookahead evidence: invalidate the run and block promotion.
- Persisted sandbox portfolio remains byte-equivalent before and after replay.

## Testing

Use test-driven development with hand-derived fixtures:

1. ATR14, support/resistance, momentum, and evidence cutoff calculations.
2. Triggers open meetings but never directly create discretionary orders.
3. Portfolio Agent and Critic receive only cutoff-safe immutable evidence.
4. Schema, symbol, quantity, permission, evidence-hash, and cutoff validation.
5. Invalid model output becomes HOLD; emergency safety still exits.
6. Agent SELL and TRIM require deterministic RiskEngine approval.
7. Decision date precedes next-open execution, including overnight gaps.
8. Cached artifacts require exact model, prompt, mandate, hash, and cutoff matches.
9. Deposits cannot inflate profit or erase drawdown/giveback.
10. Champion wins ties and every incomplete or failed gate.
11. Controller accounting, no leverage, portfolio isolation, and deterministic test execution remain intact.
12. API contract, TypeScript typecheck, production build, full .NET tests, Docker health, and real public-data replay pass.

## Acceptance

Phase 1 is complete when the app can create cutoff-safe Agent exit meetings, validate and risk-check their proposals, replay immutable decision artifacts, compare hybrid challenger with champion, display all evidence truthfully, and promote only a gate-passing challenger. A failed or unavailable Agent must create no discretionary trade, while deterministic safety remains operational.
