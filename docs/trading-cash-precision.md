# Cash precision during reactivation

The strategy ledger retains exact execution amounts. Bitkub's statement can settle a different THB amount because it rounds buy debits upward and sell proceeds downward to two decimal places. The [official Thai API FAQ, question 22](https://support.bitkub.com/th/solutions/articles?id=151000199019) describes this distinction from `order-info` arithmetic.

## Accepted evidence

| Check                | Requirement                                                                                                                |
| -------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| Normal comparison    | Fresh broker cash exactly equals retained ledger cash                                                                      |
| Settlement fallback  | A reviewed cash baseline exists and retained funding is unchanged                                                          |
| Replay               | Baseline plus exact subsequent execution movements reproduces retained cash exactly                                        |
| Execution identity   | One execution per order, unique fill identity, known fee/credit and precision, matching terminal filled or cancelled order |
| Cancellation         | No claimed or unknown cancellation                                                                                         |
| Additional movements | No external sale after the baseline cutoff                                                                                 |
| Final comparison     | Fresh broker cash exactly equals the supported rounded settlement replay                                                   |

There is no numerical tolerance. Unsupported multiple-execution orders, missing evidence and unexplained differences still require review. Executions already covered by the cash baseline are not counted again.

## Preserved behavior

- Reactivation preserves the existing live ledger, funding, opening time, fills, holdings, external sales and cash baselines.
- Rounding residuals are not deposits, profit or new capital.
- `settledCashThb` is nullable, derived comparison evidence. It is not a current wallet balance or an authorization.
- Activation still checks fresh broker capital, account identity, holdings, orders, mandate terms and expiry. Order sizing uses verified available capital.
- Initial unfunded setup keeps its existing initialization behavior. Maintenance never enables trading.
