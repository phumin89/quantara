# Continuous AI research

The automatic research scheduler works across nights, weekends and Bangkok midnight.
It selects a focused task from supplied evidence, runs its specialist, then runs the
Evidence Reviewer. The deterministic trading engine continues independently under
its existing owner mandate. Research does not approve, enable or submit trades.

## Responsibilities

| Role              | Assigned work                                                   | Trigger                                                                           |
| ----------------- | --------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| Intraday Analyst  | Completed bars, current setups, liquidity and costs             | Hourly eligibility; a fresh alert can shorten its cooldown to 15 minutes          |
| Position Reviewer | Actual holdings/fills and saved exit evidence                   | Holdings exist and review is due; fresh alerts can shorten cooldown to 15 minutes |
| News Verifier     | Source identity, event times, revisions and affected assets     | A supplied news revision has not been reviewed in retained recent task history    |
| Swing Analyst     | Descriptive multi-day thesis and supplied daily evidence        | Supplied daily candles exist and its 24-hour interval elapsed                     |
| Quant Researcher  | Supplied historical-study quality and limitations               | Study metrics exist, their digest changed and its 24-hour interval elapsed        |
| CEO               | Priorities and concise research summary                         | Its 12-hour interval elapsed                                                      |
| Evidence Reviewer | Validate the assigned specialist's claims and current positions | A specialist completes with valid account/policy/evidence lineage                 |

Each role loads its own `AGENTS.md` and `SKILL.md`. Specialists receive scoped evidence
and no full research backlog. Only CEO and Intraday may request up to three scanned
symbols for server-side read-only detail collection. Reviewers receive the full
current evidence needed for their typed assessment. They never receive broker credentials.

Role contracts define the goal, supplied evidence, method, completion criterion and
handoff, with correct/incorrect examples. Shared instructions cover evidence, authority
and concise Thai reports; market and proposal details are included only for relevant roles.
The schema supplies the research-symbol allowance so prompt and transport limits agree.

Focused specialists receive `specialistMemory`: at most two completed reviewed lessons
from the existing seven-day learning window, matching their role, account and exact policy
hash. Each lesson retains its report ID and timestamp. Future, incomplete, invalid and
unscoped reports are excluded. Memory is reported history, not verified performance,
model retraining, instructions or permission to change policy. No applicable lesson is
an ordinary empty result. CEO and Reviewer retain their broader learning context.

Memory stays within 4,000 characters and yields whole entries when the existing context
or wire limit requires it; omitted counts remain visible and current evidence is preserved.
These checks prove context construction and permission boundaries. They do not establish
that shorter prompts or recalled lessons improve forecasts or trading profit.

- One model task executes at a time per coordinator; a normal automatic run has two calls.
- Position work has a reserved lane. Ordinary work alternates intraday and due background
  specialties; oldest background work is selected first so alert traffic cannot starve it.
- After starting a focused task, the scheduler checks again after 15 minutes. This is
  an eligibility check, not a promise of a model call or a new chat message every 15 minutes.
- Empty or unchanged evidence does not create a full-team meeting. Missing daily/news/study
  capabilities leave the corresponding specialist available for future work.
- Legacy manually started CEO/Analyst/Reviewer runs and stored role ordinals remain supported.

## Market data quality

- Duplicate quotes agree when their exact decimal values match, including numeric
  strings and different decimal formatting. A conflicting field makes that market
  unusable in universe scans; other markets remain available. Missing fields stay
  unknown and cannot become zero.
- Portfolio quote imports reject the entire response if positive THB prices conflict
  for one normalized symbol. Coin imports also reject conflicting names or sources.
  Supported base symbols contain 1–30 ASCII letters or digits. These failures keep
  prior quotes; they do not repair balances or complete the remaining history import.
- Intraday candles must start on their native 5-, 15- or 60-minute boundaries.
  A misaligned candle invalidates the response; its timestamp is never rounded into place.
  Daily candles retain the provider's session boundaries.
- Research alerts also check alignment of retained 5-minute candles. If volume
  calculations exceed decimal limits, that window produces no alert and the feed
  continues. Normal breakout and breakdown rules remain unchanged.
- Optional depth/trade source failures leave those fields unavailable. Caller
  cancellation propagates through those reads instead of producing a partial result.

The market, signal, news and current-account readers request headers first and
bound response bytes before decoding text.
The byte budgets allow multibyte text within the existing character limits:

| Response               | Maximum buffered bytes | Text validation                                |
| ---------------------- | ---------------------: | ---------------------------------------------- |
| Base market research   |                160,000 | At most 40,000 characters                      |
| Native candle evidence |              1,000,000 | Schema validation and combined research budget |
| Market signals         |              4,000,000 | At most 1,000,000 characters                   |
| News evidence          |                 16,000 | At most 4,000 characters and five events       |
| Current account        |                 16,000 | Exactly 64 hexadecimal identity characters     |

The configured HTTP deadline also covers body reads, and caller cancellation
propagates. Unavailable or oversized native evidence retains healthy base research
and reports native evidence as unavailable. News and account readers each use a
five-second deadline. A news timeout returns unavailable evidence; an account timeout
prevents starting account-specific work.

One portfolio collection has a shared 45-second deadline across account, wallet,
history, trading, market and open-order reads. Cancellation aborts the collection;
it does not turn unfinished work into an optional successful result. Wallet
evidence must still be at most 60 seconds old and no more than five seconds in the
future when the assembled snapshot is returned. Source timestamps and unknown
balances are preserved.

Research alerts check source ages after collection and again after resolving the
account. Market data keeps its original check time when combined with news. Fresh
news cannot extend a market signal's two-minute lifetime. If one component expires,
the other can still trigger research under its existing age limit. The scheduler
checks eligibility again after portfolio readiness; an expired alert leaves its key
unconsumed. If a combined alert expires during readiness, its fresh news also waits
for a later successful poll. A due scheduled round can still proceed without the alert.

## Assessment handoff

| Boundary            | Rule                                                                                                                                                                                                |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Account changes     | Exact account, policy and mode checks remain mandatory.                                                                                                                                             |
| Holding changes     | Compare exact decimal quantities and available balances, independent of trailing zeros or exponent formatting. Null, missing availability and zero remain distinct.                                 |
| Evidence references | Required Reviewer schemas restrict each ID to the supplied list and its count. The server still rejects invented or duplicate references; legacy inputs without the list keep their generic schema. |
| Review timing       | Copy the server's pinned review time and expiry. Review is scheduled no earlier than run start plus 15 minutes or context creation plus six minutes, whichever is later.                            |

The processing allowance includes the three-minute worker, final account collection
and normal role/audit persistence. It is not an extension of trading authority or a
guarantee against arbitrarily delayed storage. Existing future-time and expiry
validation still rejects genuinely late work; no report or confirmation is retried
automatically.

## Budget and availability

| Limit                | Current fallback policy                                                                    |
| -------------------- | ------------------------------------------------------------------------------------------ |
| Ordinary research    | At most two reservations in a rolling hour                                                 |
| Position research    | One additional reservation per rolling hour, only for an actual held-position task         |
| Shared total         | At most three reservations in a rolling hour                                               |
| Daily limit          | No calendar-day 24/32-round cutoff                                                         |
| Input/output/process | Existing bounded request/report sizes and worker timeout remain enforced                   |
| Token usage          | Actual SDK input/output/cached usage is retained per step when returned; otherwise Unknown |

This is **bounded request admission**, not a monetary or measured-token spending cap.
Temporary-workspace cleanup failures retain known usage and model audit fields.
If response validation also failed, its original diagnostic remains the primary
failure. Cleanup failure alone is a failed run, not a successful report.
An automatic run normally invokes two roles; the legacy manual run invokes three and
shares the ordinary admission lane. Provider subscription limits still apply. Future
token-based admission needs measured usage and an independently reviewed budget protocol.

SQL reservations are account-isolated and serializable. Existing claims are retained;
failed, cancelled or uncertain attempts are not refunded. Exact rolling-window release
times are reported. Future-dated claims cause an explicit clock-alignment wait.
Restart and midnight do not reset claims or create permission to replay uncertain work.

Source collection, deterministic execution and reconciliation do not depend on an AI
meeting. A provider outage or exhausted quota can defer model work; the UI reports the
actual failure or eligibility state rather than claiming that an agent is working.

MarketData news-source requests share a ten-second deadline for headers and body, with a 512,000-byte
body limit. Stalled reads become `Unavailable`; unrepresentable publication expiry
becomes `ParseFailed`. A failed source keeps cached events at their original age and
does not prevent the next configured source from being polled.

News publication dates use an invariant Gregorian calendar and an explicit
source timezone. Supported ISO and RSS timestamps retain their numeric offset;
RSS two-digit years use 2000–2049 for 00–49 and 1950–1999 for 50–99. Named RSS
zones map to their stated fixed offsets, not the machine's local timezone.
Incomplete, locale-ambiguous or unsupported dates stay unknown, retaining the
existing one-day expiry from fetch time. Known publication times keep the
seven-day expiry. Existing stored revisions are not rewritten. This avoids
[`DateTimeOffset.TryParse` defaults](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.tryparse)
that depend on local culture or fill in missing date/offset information.

News identity uses the first nonblank provider GUID or ID, then the selected raw
article URL. Existing nonblank identifiers retain their exact case and whitespace;
empty identifiers cannot merge different articles. RSS keeps its GUID URL fallback.
Atom links use an absent `rel`, `alternate` or its equivalent registered relation
IRI, as defined by [RFC 4287](https://www.rfc-editor.org/rfc/rfc4287.html#section-4.2.7.2).
Relation comparison ignores case under [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288.html#section-2.1.1).
Self, enclosure and other non-article relations cannot replace an article link.
The existing HTTPS/host policy still applies. This changes new ingestion only;
previously stored identities and revisions are not rewritten or merged.

The news transport permits only its configured Federal Reserve/BLS HTTPS hosts
and rejects a DNS result if any address fails its public-address policy. This
defensive classifier retains conservative protocol-specific denials; it is not
a guarantee of routing or endpoint reachability. The IPv4 special/documentation
checks use the intended /24 prefixes rather than excluding all of 192.0/16.
IPv6 documentation prefix 3fff::/20 is denied as well. The IETF
protocol-assignment umbrella 2001::/23 is denied except for the already
permitted 2001:30::/28 DET allocation; existing stricter protocol denials stay
unchanged. The scope follows the
[IANA IPv4](https://www.iana.org/assignments/iana-ipv4-special-registry/)
and [IPv6](https://www.iana.org/assignments/iana-ipv6-special-registry/)
special-purpose registries; host allowlisting, certificate checks and connection
pinning remain in place.

## Evidence, recovery and status

- A persisted task records specialist, input digest, policy hash, evidence time, expiry
  and available news/study fingerprints. Each model step records its exact request digest,
  start/completion times and actual usage when available.
- New News tasks also retain their event/revision identities. Expiring or removing another
  event from a packet does not repeat a retained revision; coverage is scoped to the same
  account, including failed attempts. Routing reads up to 512 tasks from the preceding
  seven days plus one minute of clock tolerance, covering the source event lifetime
  under the current three-per-hour admission limit. This history is used for routing;
  it is not added to specialist prompts. Older tasks retain exact-packet fingerprint
  deduplication. Their missing per-event history is not inferred, so a changed legacy
  packet may need one new assessment before subset deduplication applies.
- Task evidence time, expiry, run creation and budget admission use the clock after
  source collection and history reads, so collection time does not shorten the task's
  review window.
- Before accepting a focused response, recheck account, policy, trading mode, holdings
  quantity/availability and task expiry. Rejected or late outputs cannot launch a dependent
  reviewer or become an assessment. Returned usage remains recorded even if claims fail validation.
- Requested market refreshes use the same account, policy, mode, balance and expiry checks
  before replacing the evidence passed to Reviewer. Market prices and source times may change.
- Specialist memory is scoped to its recipient. Reviewer receiving `specialistMemory=null`
  does not establish that the author received no memory. Compare reports at their stated
  source times; original facts absent from the refreshed snapshot remain unverified.
- `worker_evidence_changed` means that this recheck rejected stale research, not that
  the Codex runtime crashed. The failed report remains in history; later research uses
  fresh evidence under the existing schedule and budget. No immediate retry is added.
- A pending/running task recovered after restart is retained with a terminal interrupted
  status by the existing recovery path. It is not silently resumed or resent. Later eligible
  research is a new task, subject to the retained budget and cadence.
- Budget reason, next eligible role/time and Stop/Resume are persisted in Team-owned SQL.
  Daily Brief displays last completed time and actual active role. Unassigned members are
  shown as available rather than queued for a fictitious full-team meeting.
- Stop cancels portfolio refresh, readiness checks and pre-start evidence gathering before
  waiting to save the disabled schedule. Queued scheduler ticks cannot start another round
  while Stop is pending. An explicit Resume permits a new round; restart preserves Stop.
- If a market alert disappears before its data becomes ready, the scheduler returns to
  waiting for the next alert or scheduled round. It clears that temporary data-wait message
  without changing the deadline or consuming the alert. Actual start failures remain visible.
- News coverage remains the currently configured sources. This scheduler does not add
  project news feeds, longer history, a swing execution policy or profitable-strategy proof.

## Verification and rollout

| Proof                            | Scope                                                                                                      |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| Deterministic 30-hour simulation | All seven active roles, rolling limits, every elapsed hour and repeated coordinator restarts               |
| Evidence regressions             | Account/policy/balance/mode changes, expired responses and usage retention                                 |
| Worker fixtures                  | Approved legacy/focused handoffs, role-specific output permissions, no tools and SDK usage                 |
| Compatibility                    | Legacy reports, appended role ordinals, optional telemetry and additive schedule columns                   |
| Deployment                       | Team/Web updated with AutoLive stopped; canonical backups, isolated restores and retained records verified |
| Runtime pilot                    | Still requires observed provider latency/usage, source availability and actual uptime                      |

Deployment requires AutoLive disabled, canonical backups and verified restore as described
in [Docker runtime](docker-runtime.md). Maintenance never enables AutoLive or submits a
broker order. Existing financial rules/hashes, positions and history retain their ownership.

See the [research plan](aggressive-portfolio-plan.md) for strategy/evaluation work that
remains separate, and [architecture](architecture.md) for service and database boundaries.
