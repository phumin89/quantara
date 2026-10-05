# Frozen CFO replay verification

Completed through the local Operations UI. Paper only; no strategy changes.

- Mandate: January 2024–December 2025; salary THB 70,000/month;
  essentials 24,400; housing 14,000; reserve target 115,200; opening reserve 0;
  emergency rate 25%; investment rate 70%.
- Snapshot ID: `6e910a4ce3f424ee4e55f6f2726ec292f15c9d114f8850930ad54e7ff29b27b5`.
- Result fingerprint, capture AND replay:
  `a9cccd4c186592aa190988104f705656ac7a91abac63a339082f0284df4f1917`.
- File: `src/TradingCompany.Api/data/simulation-snapshots/<ID>.json`,
  171,938,945 bytes, four independent historical windows, 161 requested symbols.
- Saved sources include Bitkub daily bars, Yahoo native/adjusted bars and
  Frankfurter historical FX. Exclusions remain in the evidence.
- Build: `frozen-replay-v1:10.0.11:en-US:77098fd5-40a3-4dc5-a539-31f5ac2b83ed:f28ab57c-4dd9-46e8-858c-221d4b654142:53e30c17-cef1-4bdb-a4c4-99326fc14b9d`.

## Observed results

Investment contributions: THB 419,776.00. Closing portfolio: 559,590.04.
Historical paper profit: 139,814.04. Separate reserve: 115,200.00.
Combined liquid assets: 674,790.04. Housing payments: 336,000.00.

The capture showed `Frozen inputs saved`. The rerun showed
`Offline reproduction verified` with the identical ID and result fingerprint.
The editable housing field was temporarily changed to 14,001 before pressing
the frozen rerun button. The result correctly retained the original 14,000
mandate and 336,000 total housing payments. The field was restored to 14,000.

## Verification

- .NET suite: 616 passed, zero failed.
- UI render tests: four passed. Typecheck and production Next build passed.
- Docker Compose configuration validated; snapshot path uses the existing
  persistent `/data` volume. Docker deployment itself was not exercised.
- Handler tests disable the market provider after capture; replay makes no
  provider calls and preserves trades, cash flows, curve and exclusions.
- Tampering, changed build/request/result fingerprint, missing windows and
  invalid IDs fail closed. CFO API tests verify shared portfolio unchanged,
  all eight stress outcomes reproduced and zero-budget mandate changes rejected.

This is reproducibility evidence, not proof of future returns or absence of
historical selection/adjusted-price bias. The previously inspected 2018–2025
periods are research evidence, not untouched holdouts. Snapshot files contain
household assumptions and need private storage and backups. Preserve this exact
application build to replay after upgrades.

## Next slice, not activated

Create the separate persisted forward-paper funding/decision ledger. Do not
seed it with these historical profits or activate the unrelated shared wallet.
Salary postings must be idempotent and decisions recorded before later prices
arrive. This frozen-replay slice does not claim the full AI company is finished.
