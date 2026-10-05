# Thai bank API feasibility for Quantara Personal CFO

Checked: 2026-09-01

## Decision

Quantara must not assume that a normal personal bank account exposes a general-purpose API for unattended balance access and fund transfers.

- Preferred operating model: salary lands in a dedicated `Salary Inbox` account. Quantara receives authority only over that account, not every personal account.
- On salary detection, a deterministic allocation policy reserves bills, emergency savings, debt payments, investing capital, and discretionary cash before any AI recommendation runs.
- Bills, emergency reserves, and other protected envelopes are unavailable to the trading company. AI may reason only about the investing allocation and genuinely uncommitted surplus.
- Build the first Personal CFO phase without bank credentials: manual/statement import, deterministic allocation envelopes, recommendations, and paper transfers.
- Add consent-based read-only bank data when an eligible provider connection under Thailand's Your Data framework is available.
- Add real payment initiation only through a bank-approved corporate or partner integration. Keep transaction limits, idempotency, reconciliation, dual control, and a kill switch outside AI authority.
- Never automate a retail banking website or mobile application by screen scraping, password storage, or OTP interception.

## Evidence

- Bank of Thailand's Your Data rules cover consent-based sharing of deposit, credit, and payment-account information. Providers are scheduled to phase in personal deposit data sharing from late 2026, followed by other data through 2027–2028. This is data sharing; it does not by itself grant payment-initiation authority.
  - https://www.bot.or.th/th/news-and-media/news/news-20251110.html
  - https://www.bot.or.th/en/financial-innovation/digital-finance/open-data/Your_Data_Project.html
- Krungsri documents a real fund-transfer API, but describes it as a corporate service for companies and partners. It requires an account relationship, security onboarding, customer eligibility checks, consent/account linking when debiting customer funds, and supports authorization/dual-control flows.
  - https://developers.krungsri.com/usecases/financial-services/fund-transfer
- Krungthai has sandbox and public application concepts and fund-transfer APIs. Production use remains subject to a service request and approval; its FAQ allows transaction limits to be specified during that process.
  - https://developers.krungthai.com/
  - https://developers.krungthai.com/faq/api-product-1
- Bangkok Bank's public developer catalog currently emphasizes business collection/payment products such as bill payment, app-to-app payment, and QR payment rather than a universal personal-account aggregation API.
  - https://apiportal.bangkokbank.com/en/apis
- BOT API is a public economic/statistical data service, not an API for accessing a user's commercial-bank account.
  - https://portal.api.bot.or.th/

## Safe rollout order

1. Paper Personal CFO: simulated salary events and allocations.
2. Read-only: imported statements, then consent-based account feeds when accessible.
3. Human-confirmed payments: bank-app deep links or maker/reviewer/approver flow.
4. Bounded automation: bank-approved API, small limits, protected reserves, reconciliation, and automatic shutdown on ambiguity.
