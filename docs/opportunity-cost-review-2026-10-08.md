# Cash allocation and opportunity-cost review

The owner delegates allocation to the company and wants profit opportunities evaluated alongside downside. Keeping cash needs an investment rationale; inactivity does not establish optimal allocation.

## Verified failure

The 2026-10-08 14:00 Bangkok meeting completed as HOLD. Portfolio artifact `45889038-a91b-4713-8a51-47ad506cc4bb` and final artifact `90853426-c050-49d1-92c5-64a5181533fb` rejected ETH partly because the forecast direction was `0`. In the domain enum, `0` means **Bullish**, not an absent signal. The final report cited ETH's 2.797% out-of-sample return and 7.738% drawdown but said after-cost comparisons were missing. Its retained BTC balance occupied the single allowed position slot.

Read-only ledger inspection confirmed Armed=true, Stopped=false, Reconciled=true and an order ceiling equal to existing authorized capital, 5,000.0872 THB. There were no company orders or fills. This was a model decision and incomplete evidence problem, rather than the former 100 THB order ceiling.

The coordinator previously passed only the selected strategy's aggregate return and drawdown. If no strategy passed, it sent Cash and zero values while omitting tested alternatives, their losses, rejection reasons, validation dates and cost assumptions. The buy-and-hold reference was gross while strategy simulations were net.

## Change

- Candidate direction and strategy serialize as explicit names. Legacy numeric candidate JSON remains readable; domain enum values and historical artifacts are unchanged.
- Every Live role, including the three-role portfolio review, receives the actual execution limits and compact comparisons: cash, net buy-and-hold reference, all tested strategies, fold returns, drawdown, completed round trips, validation dates and rejection reasons.
- Historical cash opportunity cost is the largest positive net excess return over cash among validated alternatives in the same past windows. This is a retrospective comparison, not a forecast, promised gain or claim that maximum allocation is optimal.
- Buy-and-hold reference includes entry and exit fees and slippage in every validation fold. It assumes fractional full-capital exposure and ignores order lot rounding; it is a reference benchmark, not an executable simulation. Strategy simulations and promotion thresholds are unchanged.
- Instructions challenge entries and inactivity symmetrically. Cash-heavy/HOLD proposals must compare increasing an existing holding and rotating to an allowed alternative, respecting slots and minimum sizes, and give a measurable condition for reconsideration. A prior conclusion is not current evidence. Current spread remains separate from historical cost assumptions.

## Acceptance and limits

The lead reviewed all changed implementation and test files. Deterministic checks cover all three direction names, legacy numeric input, full and abbreviated meetings, changed-evidence/limit cache misses, rejected alternatives, best-return versus risk-adjusted selection, cost compounding and unchanged strategy outcomes.

`tools/test-trading.ps1` passed 852 tests, with zero failures and zero skips. The Release company Docker image built successfully. The local implementation bridge was unavailable because its configured Codex executable was missing; the bounded benchmark patch used the worker's direct fallback and was inspected by the lead before acceptance.

No account binding, RiskEngine, allowlist, promotion rule, owner limit or stored financial state was changed. No trade, cancellation, activation or financial-service restart was performed. The small initial manual purchase is not a performance result. Existing SOL history integrity failure remains visible and is not discarded to produce a signal.

The built company image is prepared for the owner to apply through **Enable Auto Trading.cmd**. The running financial services retain their current image until that owner action. The new meeting inputs are verified; improved decisions, future orders and profitability are not yet demonstrated.
