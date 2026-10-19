# Adaptive exit candidate — frozen protocol

| Protocol detail | Value                                                   |
| --------------- | ------------------------------------------------------- |
| Exit candidate  | `adaptive-exit-v1`                                      |
| Paired study    | `paired-exit-v1`                                        |
| Frozen          | 19 October 2026, before collecting prospective returns. |

- This is an experimental exit candidate, not a claim of profitability.
- Entry selection, initial protective stop and sizing remain those of v2.
- No owner mandate is upgraded.
- Live activation of v3 is unavailable in this release.

| Item                                       | Frozen definition / default                                                                                                                                                                                                      |
| ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Required history                           | The entry's native 5/15/60-minute timeframe, at least 51 contiguous completed candles ending at the latest completed interval. Never switch to a longer timeframe to avoid a loss.                                               |
| ATR                                        | Arithmetic mean of the last 20 true ranges; true range is max(high − low, abs(high − previous close), abs(low − previous close)). No Wilder smoothing.                                                                           |
| Trend                                      | EMA20 > EMA50 + 0.5 ATR and last completed close ≥ EMA50. EMA seeds use the first 20/50 closes of the supplied window.                                                                                                           |
| Range / unknown                            | abs(EMA20 − EMA50) ≤ 0.5 ATR is Range; otherwise Unknown. Unknown uses the bounded Range timing/trailing profile; it is never assumed to be a trend.                                                                             |
| Regime transition                          | Two distinct scheduled completed-candle reviews must confirm a different known regime. A transition may shorten the original deadline; it never lengthens it.                                                                    |
| Review cadence                             | Trend: 4 entry-timeframe candles. Range/Unknown: 2. Interval is capped by the profile's maximum holding time. Repeated quotes cannot consume reviews.                                                                            |
| Minimum hold for discretionary review exit | Trend: 120 minutes. Range/Unknown: 60. This never delays protective stop or hard deadline.                                                                                                                                       |
| Maximum hold                               | Trend: 480 minutes. Range/Unknown: 240. Anchored once to first confirmed fill. Additional fills and restart never reset it. Authority expiry/Disable still blocks broker submission.                                             |
| Review allowance                           | Maximum 24 reviews; exit after 3 consecutive reviews without close − fee-inclusive average cost ≥ 0.25 current ATR, once minimum hold is reached.                                                                                |
| Structural invalidation                    | On a scheduled completed-candle review after minimum hold: close < EMA50 and EMA20 < EMA50.                                                                                                                                      |
| Target                                     | Fixed at entry limit + max(2 entry ATR, entry limit × saved take-profit bps / 10,000). No moving target. Range exits at target; Trend follows trailing protection.                                                               |
| Trailing activation                        | Peak executable bid must cover fee-inclusive entry cost, estimated exit fee and slippage, and exceed average cost by at least 0.5 entry ATR.                                                                                     |
| Trailing distance                          | Trend: 2 current ATR. Range/Unknown: 1.5 current ATR. Effective stop is max(previous stop, peak bid − distance); an armed trail stays armed.                                                                                     |
| Priority                                   | Valid account/mandate and no uncertain active order before any side effect. Fresh protective stop, hard deadline, scheduled structural/review invalidation, armed trailing, regime target. At most one active intent per symbol. |
| Missing evidence                           | Stale/future quote cannot trigger submission. Missing/mismatched candle history cannot postpone review or deadline. Original protection and last verified ATR remain; no manufactured candles.                                   |
| Costs                                      | Entry uses confirmed quantity, gross and fee. Cost basis already includes entry fee. Estimated hypothetical exit applies slippage to bid then rounds one exit fee upward to satang; entry fee is not subtracted twice.           |

| Retained state       | Evidence                                                                                                                    |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Immutable entry plan | Proposal/evidence timestamps, entry candle, timeframe, entry ATR, policy/rules hash, fees/slippage and initial stop/target. |
| Holding              | First-fill anchor, current stop, pending/confirmed regime, review counts, revision and last reason.                         |
| Sell intent          | Plan revision used to create it.                                                                                            |

## Evidence and limitations

- Only new v2 entries with a valid prospective seed participate. Existing holdings are not backfilled with invented entry evidence.
- Paper and live samples are separate; paper outcomes never count toward the live review threshold. Results are account/rules-hash isolated.
- A hypothetical exit needs a fresh bid and sufficient visible bid quantity. Active entry orders, invalid seeds and uncertain costs remain Unknown; there is no assumed cancellation or guaranteed LIMIT fill.
- The comparison is a bid/slippage liquidation scenario, not proof that a real resting LIMIT would fill. It does not simulate portfolio capital competition. Historical paired replay, portfolio replay and out-of-sample evaluation remain release gates before a live candidate is offered.
- Actual v2 realized net and hypothetical exit net remain separate. Completed hypothetical outcomes are not rewritten by later quotes. Later entry fills invalidate a premature comparison rather than manufacturing a result.
- Collect at least 14 calendar days and 30 paired closed live outcomes across regimes. Even then, `readyForReview` means review is due; it never activates v3 or proves positive expectancy. Reject or retain Insufficient evidence if safety, costs, drawdown or uncertainty fail the reviewed plan.
- Do not retune this protocol after seeing holdout returns and report it as the same experiment. Change the candidate version and restart its evidence window.

See [delivery and rollout plan](background-jobs-and-adaptive-exit-plan.md) for deployment and compatible recovery requirements.
