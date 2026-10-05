# Historical data validation must be isolated by replay window

## Cause and observed evidence

The earlier 400-day slicing fix aligned the engine's training history, but validation still happened against a shared 2016–2025 provider request. Any invalid OHLC bar anywhere in that union caused the entire symbol to be excluded from every fold.

Read-only Yahoo chart inspection on 8 September 2026 reproduced this for BGRIM.BK:

| Requested range | Bars | Invalid OHLC |
|---|---:|---|
| 2016-11-27 through 2025-12-31 | 2,052 | 2021-03-01: open 46.75, high 47.50, low 46.00, close 48.00 |
| 2017-01-25 through 2019-12-31 | 599 | None |

The close exceeded the recorded high. Rejecting that source range is appropriate; using the rejection to remove a stock from an earlier 2018–2019 experiment is not. This was cross-window data-availability contamination, not a changed trade signal. The existing decision-cutoff check did not detect this upstream selection problem.

A standalone 2018–2019 CFO replay with the existing 70% mandate produced THB 62,426.26 profit versus the prior shared-validation THB 44,706.62. Do **not** attribute that difference to strategy improvement: the standalone replay loaded a different valid universe (20 provider exclusions instead of the union's 39). BGRIM alone contributed THB 15,567.06 unrealized PnL. The detailed diagnostic artifact is `2026-09-08-cfo-2018-diagnostic.json`.

## Bounded correction

- Primary replay loads and validates only its own first-investment-minus-400-days through end-date range.
- Every stress period independently loads and validates that same window definition using its own CFO funding calendar.
- Identical windows reuse the same in-request data, including primary/matching-fold and normal/double-cost scenarios. There is no cross-request snapshot cache.
- Provider failures now appear in the relevant stress row's exclusions, alongside engine exclusions.
- Optional hosted tournament loading is separate from the primary replay. Internal tournament fold loading remains outside this correction's scope.
- No signal, exit threshold, allocation rate, risk limit or pass criterion changed. Paper-only execution and household reserve allocation are unchanged.

## Verification

The new deterministic regression failed in both fixed-funding and CFO cases before the fix. Afterward both preserve the complete standalone result when a later window's provider fails, and the matching stress fold agrees. Failed later windows remain explicitly unavailable, retain cash and report their excluded symbol.

Full suite: 605 passing .NET tests. UI: 3 passing render tests and TypeScript check. UI copy now explains that exclusions and data coverage are period-specific.

Final API build also passed with zero warnings/errors. Reloaded the in-app browser after the API restart and ran the CFO form with salary 70,000 / housing 14,000. Browser verification showed the actual 2024–2025 mandate, `COMPLETED`, 8/8 scenarios and THB 139,814.04 profit (a one-satang source revision versus the saved API run). The API remains paper-only, with hosted challengers disabled for these runs.

The lead inspected the actual handler, runner and regression code after implementation. An optional local reviewer produced no review: its bridge referred to a missing versioned `codex.exe`. No local review claims are used as evidence; lead review and the executed tests remain the acceptance basis.

## Limits / next validation work

### Updated 70% experiment

Full fresh API replay completed with 8/8 existing research gates passing (four periods, normal/double costs). All salary, reserve and investment accounting checks passed. Primary 2024–2025 equity exactly matches the same-window normal-cost stress row, which reuses its input data.

| Period | Normal-cost profit THB | Double-cost profit THB | Normal max drawdown | Exclusions |
|---|---:|---:|---:|---:|
| 2018–2019 | 62,426.28 | 61,829.47 | 23.94% | 20 |
| 2020–2021 | 125,627.56 | 123,714.21 | 11.33% | 12 |
| 2022–2023 | 79,205.45 | 78,243.04 | 22.05% | 36 |
| 2024–2025 | 139,814.05 | 138,117.32 | 21.82% | 36 |

Each period releases THB 419,776 from the same household mandate and retains THB 115,200 emergency savings. The 2024–2025 paper portfolio closes at THB 559,590.05; portfolio plus reserve is THB 674,790.05. Salary is THB 70,000/month, essentials THB 24,400 and housing/debt THB 14,000. Discretionary allocation across 24 months is THB 223,424 and is excluded from liquid assets.

Evidence: `2026-09-08-cfo-70-percent-isolated-windows.json`. Source-price revisions mean the separate diagnostic 2018 run differs by a few satang; the identical-window comparison within the new request uses the same data. 80% was not rerun or promoted. Passing alpha is against the custom executable four-position hold reference, **not** every available ETF.

This fixes cross-window contamination, not all historical-data bias. Current-universe membership, whole-request OHLC rejection within an individual period, provider revisions, adjusted-price history and incomplete market coverage still need point-in-time dataset controls. No claim of a fully bias-free backtest or guaranteed profit is justified. Freeze raw input snapshots and their hashes before comparing strategy candidates.

The CFO top-level handler still hides a standalone replay when a required VOO/QQQM benchmark is unavailable; this particularly affects periods before QQQM existed. Stress scenarios already expose whichever ETF results are available. That separate reporting contract was not weakened merely to turn an experiment green.
