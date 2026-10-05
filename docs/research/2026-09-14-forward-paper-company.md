# Forward paper company acceptance evidence

Scope: approved sandbox integration, not live brokerage or proven investment performance.

## Delivered

- CFO month-end releases fund a separate version-2 event journal. Salary THB 70,000, housing THB 14,000 and existing essentials/reserve mandate remain unchanged.
- Immutable contributions, order intents, risk outcomes, fills and marks derive cash, holdings and PnL. Exclusive disk leases, checksum verification and append-only validation protect restart/retry behavior; corrupt or incomplete state fails closed.
- Backend scheduling invokes the existing eight-role meeting runner once per eligible business day. Proposals are untrusted; deterministic RiskEngine owns execution approval. HOLD is valid. Pending fills need fresh post-decision quotes and FX.
- Role events reach the pixel office. Office and Operations show forward balances independently of the clearly labeled legacy THB 100,000 demo.
- Hosted AI remains disabled. There is no scripted production fallback, local-AI runtime, bank connection, live broker order path or leverage.

## Verification

- `dotnet test tests/TradingCompany.Domain.Tests --no-build --verbosity quiet`: 695 passed, 0 failed, 0 skipped on 2026-09-14, using the previously rebuilt implementation.
- `node --test tests/*.test.cjs` in the web app: 10 passed, 0 failed during this resumed work.
- `npm run typecheck`: exit 0 on 2026-09-14.
- Production web build completed after Office/legacy-label changes in the preceding continuation. `docker compose config --quiet` passed; containers were not boot-tested.
- Desktop browser inspection showed the pixel office, connected event channel, separate legacy controls, zero-funded forward account and explicit model-disabled state.
- Prior backend restart left the initialized paper file SHA-256 unchanged: `FDEB94734FEF1B6B4E82F51C7E063A979CF715F43B66367614172890DA79F424`. Controlled-clock disk tests additionally cover funded buy/hold/sell restarts and duplicate fills.
- Hand calculation: THB 40,000, buy 2 at USD 100 with FX 35 and THB 10 fee gives cash 32,990 and equity 39,990. Sell at USD 110 with the same FX and another THB 10 fee gives closing cash 40,680, profit 680.
- Eight-role scripted-gateway test: approved contribution 8,295; buy/hold/sell sequence closes at 8,380 with net profit 85. This is an accounting fixture, NOT market performance or a prediction.

## Current preview and remaining gates

Observed API status: `MODEL_DISABLED`, no meetings or orders; contributions/cash/equity/PnL all zero. Next simulated salary date is 2026-09-30. Salary is simulated, not detected from a bank. The backend must remain running for scheduled work.

Forward execution currently supports only NVDA, AAPL, MSFT, VOO and QQQ. The legacy scanner's broader universe does not imply broader execution support. Crypto, SET and options execution are not part of this slice.

The read-only Alpaca latest-quote adapter models quoted spread; commissions, taxes, dividends and corporate-action reconciliation are not fully modeled. Hosted-model plus authenticated-provider operation has not been exercised end-to-end. Mobile viewport and Docker boot are unverified. Incomplete meeting journals require investigation rather than automatic retry. These limitations preclude a real-money readiness claim.

Quote source contract: [Alpaca latest stock quote](https://docs.alpaca.markets/us/reference/stocklatestquotesingle-1). No broker order endpoint is used.
