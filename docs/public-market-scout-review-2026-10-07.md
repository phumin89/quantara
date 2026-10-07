# Public market scanner review — 2026-10-07

The owner's follow-up asked whether coin research is retained, whether the app
searches beyond its watchlist, and how chart analysis becomes entry/exit prices.
Inspection found a real scanner failure alongside the office display issue.

## Failure and accepted patch

Bitkub's public metadata includes one-character bases such as G, W, S and C.
The research symbol validator required two characters. `ObserveMarketsAsync`
therefore rejected the entire valid public feed before shortlist ranking.
The saved 12:00 and 13:00 reports had an empty shortlist and a public-feed gap.

The additive correction accepts 1–15 uppercase letters/digits before `-THB`.
Codex reviewed the bounded worker proposal, applied it, and verified all callers.
Duplicate identities, unsupported syntax, invalid numbers, malformed responses,
source provenance and bounded document sizes still fail validation.

The shared validator is used by the company writer, local journal and execution
host's report reader. The browser had the same restrictive pattern and was
updated too. Regression coverage exercises a single-letter ticker, account-bound
report storage with unchanged money/mandate/gate, and browser report parsing and
rendering with `executionAllowed: false`.

No execution allowlist changed. The scanner can suggest research candidates;
those candidates do not gain order authority. No activation, order submission,
cancellation or broker credential change was performed.

## Verification and runtime evidence

- Before the correction, the deterministic fixture failed with
  `Invalid public market identities`: 796 passed, one failed.
- After the patch and storage regression, headless trading checks passed:
  797 passed, zero failed. Windows Application Control blocked a native test
  assembly; the existing isolated Docker verification workflow was used without
  changing Windows protections or mounting credentials / AI sign-in.
- Browser suite: 92 passed; TypeScript passed. This second round changes data
  validation only. The office's desktop, tablet and phone visual review remains
  recorded in `live-office-roster-review-2026-10-07.md`.
- A public-only smoke probe invokes the actual `ObserveMarketsAsync` parser:
  247 usable spot observations, including C/G/S/W and BTC/ETH/SOL. This is a
  timestamp-specific observation, not a fixed market count or tradeable universe.
- Company, owner and frontend production images built successfully. Research
  service was recreated with its existing sign-in and durable data mounts.
  Owner/frontend report readers were then updated; research and ledger start
  times were preserved during that update. The completed round's shortlist did
  not contain one-letter symbols, so it was also accepted by the older reader
  during the brief deployment overlap.
- The actual 16:00 local review completed READ_ONLY and persisted five reports.
  Its shortlist is BTC, ETH, SOL, USDT, NEAR, XRP, DOGE and ADA. The prior
  public-feed failure is absent; missing-document gaps for the five new coins
  and truncated ETH source text remain explicit.
- A read-only PostgreSQL query independently confirms the 16:00 account audit:
  READ_ONLY, five reports and eight shortlist entries. After updating the owner
  reader, the account remains reconciled with `Armed=false`, the execution
  allowlist remains BTC/ETH/SOL, and the HTTPS app returns 200.

Logs and the public-only probe are in ignored `work/single-letter-market-*`.

## Storage and analysis audit

The durable company volume contains six extracted document versions and their
original-byte snapshots. Source retrieval refreshes after 24 hours. AI credibility
findings are cached by document content, instructions and model, so unchanged
documents reuse analysis across days and application restarts. The public scan
brief itself is an in-memory 15-minute cache; completed meeting snapshots preserve
its shortlist and evidence. Public scanning covers Bitkub THB spot metadata, but
the fixed paper catalog still covers only BTC, ETH and SOL. It does not provide
a complete paper/incident/unlock dossier for every shortlisted coin.

The worker checks every 30 seconds, with backoff and conditional model meetings.
The measured research-host CPU sample was 0.00%, with roughly 98 MiB memory.
Static idle sprites do not mean continuous model inference. The office animates
only roles with actual fresh working events.

Chart evidence uses completed daily THB bars, SMA20/50/200, momentum and
volatility, plus cost-aware walk-forward strategy validation. The meeting receives
forecast/strategy results, not an intraday entry/stop/take-profit price plan.
Current order sizing derives a limit price from refreshed bid/ask and the broker
price step. Explicit structured entry/exit prices are still a product gap.

The owner regards the initial THB30 purchase as a test. It is not proof of an
autonomous profitable strategy. Its real position and broker history remain in
account reconciliation; they cannot be discarded from money accounting.

## Compatibility and rollback

No schema or stored record was rewritten. Legacy two-character symbols remain
valid. Keep the additive readers in company, owner and browser when rolling back
unrelated changes: an older restrictive validator could reject newly saved
single-letter research records. Any rollback image must retain the widened
public-research validator. Do not remove legitimate historical evidence to make
an old reader pass. Trading authority remains separately gated and read-only.
