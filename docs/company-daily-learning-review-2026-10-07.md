# Daily company learning review — 2026-10-07

## Delivered behavior

Research follows the public-market shortlist instead of always reading BTC, ETH and SOL. The initial verified catalog now covers BTC, ETH, SOL, DOGE, XRP, ADA, USDT and NEAR. Unsupported shortlisted coins remain explicit evidence gaps. The daily Codex oversight automation checks that queue and can add a narrowly reviewed official source; the trading model cannot authorize arbitrary URLs or expand the execution allowlist.

The Live company has an independent daily learning worker. It checks at startup and every five minutes, runs at or after 08:30 Bangkok, and catches up when the company starts later. A completed daily attempt is reused across process restarts. The scheduled job requires the computer and company container to be running. The Codex oversight heartbeat is active daily at 08:00 local time and requires Codex to be available.

All eight roles receive their own retained lesson and next practice, supported by an exact quotation and saved report identity. The review uses up to seven preceding days plus the current day, the latest own report, a shared risk-critic report, and the previous recorded practice. The shared critic is supplied once rather than repeated in every role input. One structured review produces the eight profiles; this is retained role memory, not model weight training.

Validated memory is injected only into the matching role's next meeting input, marked as untrusted historical opinion. A changed daily profile requests the full eight-role sequence in the next available meeting slot. Account scope, mandate, limits, evidence and memory affect decision cache identity. Existing one-attempt-per-hour meeting rules remain in place.

The saved meeting includes the profile used in that round. The team room keeps conclusions first and exposes dated lessons, next practices and quotations behind a closed disclosure. Old reports without learning remain readable. Today's new profile was stored after the 17:00 meeting; it is available for the next eligible meeting, rather than being retroactively attached to an earlier decision.

## Evidence and limits

An accepted lesson means its report identity and exact quotation were checked. It does not establish improved forecasting, strategy edge or profitability. Each future practice needs a reproducible comparison against real subsequent work. The oversight automation is instructed to avoid self-rating claims and to make only evidence-supported, tested improvements.

Daily profiles, original model drafts and content-addressed source research persist in the existing company volume. Profiles copied into completed meetings use the existing PostgreSQL-backed account audit. Originals remain available when the meeting projection must be shortened.

Automatic learning is limited to one attempt per account, model, prompt version and business date. A durable marker is written before inference. Cancellation, failure or restart cannot silently retry that attempt. Missing own history spends no model quota. An invalid role is withheld while valid roles remain usable. Structurally invalid envelopes are rejected entirely; cached accepted citations are rechecked against the journal.

The first development run rejected its profile. That failed evidence was retained, and a corrected version was deliberately exercised once during this implementation round. The second real run accepted all eight roles. This development day therefore contains two review calls across two prompt versions; it is not evidence that retries are free. Usage is recorded once on the original draft, without counting the validated projection again.

## Review findings and fixes

- Fixed source coverage was replaced with the current shortlist and explicit unsupported-source gaps.
- Browser and server readers were updated together for eight findings and up to sixteen source references. The 24 KiB research bound and 60 KiB meeting bound remain enforced.
- The original ADA HTML route lacked readable main content. The original NEAR PDF URL returned 404. Both were replaced with verified files in the projects' official documentation repositories.
- Existing HTML extraction still strips scripts/styles and requires main content. Raw Markdown and PDF extraction follow their own formats; response headers cannot cause known HTML to be treated as raw text.
- Real eight-coin Thai research exceeded the prior meeting packet budget. The meeting projection now shortens summaries, statements and exact quote excerpts when needed; full reports remain archived. A regression test uses eight 600-character Thai summaries.
- The initial learning implementation discarded rejected drafts and rejected all roles when one citation failed. Draft retention and individual citation validation now make failures reviewable without promoting a bad lesson.
- Tests exposed a fixture clock accidentally constructed from ticks and a down-level iterator problem in the component test loader. The clock and reader loop were corrected; assertions now exercise accepted memory rather than merely observing a gateway call.

## Verification

- Headless deterministic checks: **824 passed, zero failed**. Includes learning citation pairing, canonical role order, missing history, account isolation, cutoff, next-day behavior, file-store restart, cancellation/failure markers, draft usage, journal preservation, role-scoped injection/cache changes, expanded source coverage and unchanged financial authority.
- Browser component checks: **95 passed, zero failed**. TypeScript checking passed. The production frontend build passed.
- Impeccable detector: no findings. Source-component visual review at 1440, 768 and 390 px showed all eight roles, a scrollable reader and no horizontal page overflow. These screenshots use explicitly labelled synthetic UI data, not fabricated production meetings.
- Public retrieval probe, with no broker or AI credentials: all five new official sources read successfully. NEAR's archived paper exceeds 60,000 extracted characters and is explicitly partial; the existing Ethereum whitepaper is also partial.
- Read-only probe over actual persisted research: **eight findings, eleven sources, 17,903 serialized bytes, valid, zero model attempts**. The archive contains eleven document versions and all eight full credibility reports.
- Fresh learning-service instance over the actual read-only company volume: **eight accepted roles restored, zero model attempts**. Independent inspection matched every accepted quotation to the cited saved own/risk-critic report.
- Reader services were deployed before the company writer. Existing owner, ledger, company data and authentication mounts were preserved. Web returned HTTP 200. Execution remained **Armed=false, Stopped=false, Reconciled=true**, with BTC/ETH/SOL as the unchanged execution allowlist.

Local evidence is retained in ignored `work/daily-learning-tests-final.log`, `work/daily-learning-ui-tests-final.log`, `work/daily-public-sources-proof.log`, `work/daily-research-brief-proof.log` and `work/daily-learning-ui/`.

## Official source additions

- [Dogecoin Core FAQ](https://raw.githubusercontent.com/dogecoin/dogecoin/master/doc/FAQ.md)
- [XRP Ledger consensus](https://xrpl.org/docs/concepts/consensus-protocol)
- [Cardano Foundation consensus and staking](https://raw.githubusercontent.com/cardano-foundation/developer-portal/main/docs/operators/basics/consensus-staking.md)
- [Tether historical whitepaper notice](https://tether.to/en/whitepaper/)
- [NEAR Nightshade documentation archive](https://raw.githubusercontent.com/near/docs/master/papers/Nightshade.pdf)

Project documentation is a source of project claims, not independent confirmation. Tether's notice is historical; the NEAR archive alone does not establish the current protocol state. Unchanged source content reuses validated research rather than spending model quota to reread it daily.

## Remaining Live boundaries

This research and memory change does not activate trading or demonstrate autonomous real-money execution. Existing live activation, reconciliation, current-market evidence and RiskEngine checks remain required. Existing real positions and the initial 30 THB test purchase remain in accounting; that purchase is not presented as a company performance result.

Rollback requires reverting the matching reader/writer code together and retaining the existing data volumes. Once learning or expanded research has been saved, an old reader may reject those records. Do not delete history, reset pairing or restore old contract readers over new records as a shortcut.
