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

## Evidence, recovery and status

- A persisted task records specialist, input digest, policy hash, evidence time, expiry
  and available news/study fingerprints. Each model step records its exact request digest,
  start/completion times and actual usage when available.
- Before accepting a focused response, recheck account, policy, trading mode, holdings
  quantity/availability and task expiry. Rejected or late outputs cannot launch a dependent
  reviewer or become an assessment. Returned usage remains recorded even if claims fail validation.
- `worker_evidence_changed` means that this recheck rejected stale research, not that
  the Codex runtime crashed. The failed report remains in history; later research uses
  fresh evidence under the existing schedule and budget. No immediate retry is added.
- A pending/running task recovered after restart is retained with a terminal interrupted
  status by the existing recovery path. It is not silently resumed or resent. Later eligible
  research is a new task, subject to the retained budget and cadence.
- Budget reason, next eligible role/time and Stop/Resume are persisted in Team-owned SQL.
  Daily Brief displays last completed time and actual active role. Unassigned members are
  shown as available rather than queued for a fictitious full-team meeting.
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
