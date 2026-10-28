# Adaptive risk sizing

Portfolio owns sizing from verified capital, quote/depth, venue precision, fees,
volatility and an observed stop anchor. A sizing result is evidence for an order;
it never grants approval or enables execution. See [trading](trading.md) for modes
and [architecture](architecture.md) for ownership.

## Separate policies

| Path                    | Scope                                                                                                                                     |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| ManualReview            | Disabled by default; optional Aggressive profile sizes marketable THB spot LIMIT proposals using completed five-minute stop/ATR evidence. |
| Adaptive AutoLive v1/v2 | Deterministic eligible THB-universe selection; size from verified account equity and available capital, including future deposits.        |
| Experimental v3         | Prospective adaptive-exit comparisons under its versioned policy.                                                                         |

- ManualReview requires the owner to approve each immutable proposal; final preflight
  revalidates it.
- Adaptive AutoLive v1/v2 requires one exact, unexpired owner-enabled policy mandate.
- The experimental v3 live gate remains closed until required observation and paired
  holdout evidence passes; no automatic upgrade.
- The ManualReview profile does not enable, disable or constrain a separate AutoLive policy.
- V2 selects verified native 5/15/60-minute history.
- A THB 1,000 hypothetical study allocation is not the live per-order cap.
- Legacy fixed-symbol rules and v1/v2 hashes remain unchanged; startup never widens
  a saved mandate.

## Inputs and limits

- Use available balances, not reserved funds. Include existing positions, open orders
  and outstanding claims when calculating exposure and cash capacity.
- Keep stop-risk scenarios, liquidity, spread, fees and slippage explicit.
- Reject stale, inconsistent, unsupported or missing evidence. Unknown broker outcomes
  block replacement; a missing cost basis never becomes zero profit.
- Check wallet and quote age after collection finishes. A slow quote read does not
  extend the freshness window or change the original observation timestamps.
- Preserve exact decimal amounts, market steps and base/quote scales. Portfolio checks
  fresh balances, policy/account identity and sizing again before a live submission.
- Adaptive sizing evaluates freshness after account collection. Immediately before
  sending an adaptive AutoLive buy, the engine evaluates the refreshed sizing again at the current
  time, including capital/quote/signal ages, account identity and the unchanged
  policy. It must still cover the original exact amount and limit; it never resizes
  an already claimed order. Legacy orders and owned sells keep their existing path.

### Account changes during a refresh

Order submission and broker-order reconciliation invalidate the cached account
evidence. Invalidation clears balances and valuation quotes immediately, even when
the clock has not advanced. A refresh that started before that invalidation cannot
restore the cache or return a usable result after its audit write completes.

| State                           | Sizing behavior                                     |
| ------------------------------- | --------------------------------------------------- |
| Fresh verified account evidence | Recheck local reservations and policy limits        |
| Account evidence invalidated    | Report that a refresh is pending; reject a new size |
| Earlier refresh still running   | Reject its result after invalidation                |
| New refresh completed           | Use the newly observed balances and quotes          |

Invalidating a cache never clears an uncertain order, reconciles a balance or grants
execution authority. The existing read-only refresh supplies replacement evidence.

Default adaptive bounds are policy limits, not loss guarantees:

| Bound                         | Default  |
| ----------------------------- | -------- |
| Exposure/concentration        | 100%     |
| Stop-risk scenario            | 5%       |
| Current-equity daily loss cap | 10%      |
| Positions                     | Up to 16 |

- Fresh owned quotes evaluate exits through the event dispatcher; research scans and
  broker reconciliation run separately. Wider market coverage does not imply all
  markets have fresh executable depth or a valid signal at the same instant.
- Account SQL retains fills, flows, sizing and audit history. Cash-flow history has
  bounded coverage.
- Net equity includes unrealized value; it is not realized profit. Exact TWR stays
  Unknown when flow-time valuations are absent.
- No automatic win-rate risk ramp, model-authored allocation or news-based execution
  authority is implied.

## Owner controls and maintenance

| Control                                                     | Effect                                                                                |
| ----------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| **Set up and review**                                       | Stages an adaptive policy without activating it.                                      |
| **Enable Auto Trading**                                     | Only the owner presses it for the exact account, policy and expiry (at most 30 days). |
| Saving material changes, expiry, Disable and Emergency stop | Disarm live trading.                                                                  |
| Individual manual approvals                                 | Remain separate from AutoLive.                                                        |

Maintenance requirements:

1. Disable AutoLive before deployment.
2. Verify canonical backups and restore.
3. Leave any later activation to the owner.

Development, tests and maintenance do not activate trading or place real orders.
Existing uncertain claims remain uncertain across restart; they are not automatically retried.
