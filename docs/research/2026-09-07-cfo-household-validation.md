# CFO household validation — 7 September 2026

Historical status: NOT_PROVEN for the experiments preserved below. A later independent-window validation correction gives 70% an 8/8 research-gate pass; see `2026-09-08-history-window-isolation.md` and `2026-09-08-cfo-70-percent-isolated-windows.json`. Do not use the earlier shared-validation results below as current evidence or attribute the new result to a trading-strategy improvement. No live execution or bank connection was added. The 80% candidate has not been rerun under the latest data contract.

## Household assumptions

- Take-home salary: THB 70,000 monthly; housing payment: THB 14,000.
- Essentials excluding housing: THB 24,400, retained from the previous user input.
- Opening reserve: zero (assumption). Minimum reserve: 3 × (24,400 + 14,000) = THB 115,200.
- Remaining after fixed needs: THB 31,600. Emergency allocation: 25%; investing rate applies after the base emergency allocation, subject to the opening-reserve safety gate.
- Below one month of reserves: no investment. One to below three months: half the proposed budget. Three months or more: full proposed budget. Withheld investment tops up reserves, capped at the effective target.
- Salary is constant throughout each simulated period. Job loss, inflation, home value, outstanding loan principal, and the split between mortgage principal and interest are not modeled.

## Recorded experiments

These are research results, not untouched out-of-sample proof. Normal costs are 0.1% fee and 0.1% slippage per fill; the second cost case doubles both. Existing PASS criteria were not relaxed: positive return, positive alpha against the executable passive benchmark, drawdown no greater than 25%, valid accounting, and verified historical cutoffs.

| 2024–2025 | 70% retained default | 80% candidate, not promoted |
|---|---:|---:|
| Salary received | 1,680,000 | 1,680,000 |
| Essentials | 585,600 | 585,600 |
| Housing payments | 336,000 | 336,000 |
| Emergency reserve | 115,200 | 115,200 |
| Released into portfolio | 419,776 | 492,960 |
| Discretionary allocation | 223,424 | 150,240 |
| Portfolio close | 559,590.05 | 671,944.50 |
| Profit after modeled costs | 139,814.05 | 178,984.50 |
| Reserve + portfolio | 674,790.05 | 787,144.50 |
| Return on deposited investment (not CAGR) | 33.31% | 36.31% |
| Portfolio maximum drawdown | 21.82% | 21.47% |
| Stress scenarios passed | 6 / 8 | 4 / 8 |

For the retained plan, once reserves are full: THB 22,120/month investment and THB 9,480/month discretionary, in addition to essentials and housing. Full budget first applies in August 2024. For the candidate: THB 25,280/month investment and THB 6,320/month discretionary, from July 2024.

| Market period | 70% normal / doubled cost | 80% normal / doubled cost |
|---|---|---|
| 2018–2019 | FAIL / FAIL: behind executable passive benchmark | FAIL / FAIL: behind executable passive benchmark |
| 2020–2021 | PASS / PASS | PASS / PASS |
| 2022–2023 | PASS / PASS | FAIL / FAIL: behind executable passive benchmark |
| 2024–2025 | PASS / PASS | PASS / PASS |

All recorded scenarios reconciled both treasury and investment accounting and reported no cutoff violations. All scenarios made positive profits; this alone was insufficient to pass. ETF comparisons are shown separately: PASS against the executable multi-position benchmark does not mean beating every ETF. For example, 70% in 2022–2023 ended at THB 496,601.03 versus QQQM DCA at THB 525,215.01 with the same funding.

## Evidence and limitations

- Full numerical summaries: `2026-09-07-cfo-70-percent.json` and `2026-09-07-cfo-80-percent.json` beside this report.
- Each stress fold independently reconstructs the household ledger and uses its actual dated investment schedule. It does not substitute fixed THB 12,000 deposits or replay future household balances.
- 39 symbols were skipped in these broad-history runs. The selected universe is not reconstructed from historical index membership; survivorship and unavailable-market coverage remain limitations.
- The stress runner loads a shared history beginning 400 days before January 2018. The expanding-window selector can therefore have more prior training history in later stress folds than in a standalone two-year run, which loads 400 days before its first investment. Do not treat those two modes as identical experiments. A fixed input-window/data-snapshot contract is a required next validation improvement.
- The data cutoff audit checks available timestamps, not every possible source of bias. Repeated research on 2018–2025 is not fresh validation. Reserve protection is a budget rule, not a guarantee against losing investment capital.
- Increasing funding changed trades as well as amounts; the 80% candidate's worse 2022–2023 result is evidence of cash-flow/position-sizing sensitivity, not proof of its cause. Inspect dated holdings and whole-unit sizing before modifying the strategy.
- Neither run spent hosted AI tokens. These results use the deterministic research company, not independent hosted-agent investment decisions.

## Engineering acceptance

- Entire domain/application/API suite: 598 passed, zero failed.
- UI input-snapshot regression: reproduced missing snapshot first, then passed after the fix; TypeScript check and production Docker builds passed.
- The page exposes the completed result's actual input mandate independently of editable form values and shows all eight stress outcomes with ETF references.
- Remaining investment acceptance: not met. Preserve failed experiments and existing gates; next work is repeatable data-window validation, sizing-sensitivity diagnosis, then a separately frozen future paper-evaluation period. Do not optimize until a chosen historical score turns green and call that proof.

## 8 September: historical-window correction

Reproduced an evaluation-isolation defect with a calendar-anchored deterministic data provider: enabling stress changed the primary replay's exit evidence hashes because optional loading supplied extra historical bars. This was not future-price leakage, but it broke consistency between otherwise identical experiments.

The primary handler and each stress fold now slice the loaded union to 400 calendar days before the actual first investment through the replay end. A CFO fold uses its first released investment date, not the earlier salary date. No strategy thresholds or pass criteria changed. Two regression cases (fixed contributions and CFO funding) compare full primary results, trades, curves, timelines and evidence, and verify that the matching stress fold agrees with the standalone result.

Fresh verification: 603 .NET tests passed; 3 CFO UI rendering tests passed; TypeScript check passed. Original JSON summaries are preserved, not relabeled as corrected results. Market-data reruns under the corrected contract are tracked separately.

The first market rerun also exposed later-listed instruments with empty histories in earlier folds (including ABNB and QQQI). Such instruments are now explicitly excluded from that fold, with exclusion metadata returned and displayed. When all instruments are unavailable, the fold retains its cash contributions and fails with `MARKET_DATA_UNAVAILABLE`; it never borrows future bars. Regression coverage includes both mixed and entirely unavailable histories, and the primary replay's all-unavailable cash path.

Docker Desktop failed to initialize on 8 September (`dockerInference` listener/file-access error). No factory reset or Docker-volume deletion was performed. Validation temporarily uses a local API on port 8080 and web on 3000; the local company's auto-start and hosted AI are disabled, with isolated temporary portfolio state. The existing Docker portfolio is not replaced by this diagnostic portfolio.

## Corrected market reruns completed — 8 September

| Metric | 70% retained default | 80% candidate, not promoted |
|---|---:|---:|
| 2024–2025 portfolio close | 559,590.04 | 671,944.50 |
| Profit after modeled costs | 139,814.04 | 178,984.50 |
| Reserve + portfolio | 674,790.04 | 787,144.50 |
| Stress scenarios passed | 6 / 8 | 4 / 8 |
| Matching primary / stress equity equal | Yes | Yes |

Both corrected reruns completed with valid accounting and historical cutoffs. Failure periods are unchanged: 2018–2019 for 70%; 2018–2019 and 2022–2023 for 80%, at both cost levels. Both remain NOT_PROVEN; no strategy or promotion gate was weakened. Larger deposits do not establish a better strategy.

Corrected evidence: `2026-09-08-cfo-70-percent-corrected.json` and `2026-09-08-cfo-80-percent-corrected.json`. Each contains the requested mandate, funding totals, all eight results and `matchingFoldEqual: true`. Provider-level failures excluded 39 symbols before replay. Additional fold-level exclusions were 11, 2, 1 and 0 for the successive market periods. The per-fold list is additional to provider-level failures, not a claim that the full universe was available.
