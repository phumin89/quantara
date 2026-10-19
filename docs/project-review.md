# Project review — 19 October 2026

| Review detail         | Scope                                                                                                                                                   |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Areas                 | Repository structure, the owner's code instructions, large production classes, dependency boundaries, worker lifecycle, documentation and verification. |
| Preservation          | Existing feature work and retained data were preserved.                                                                                                 |
| Runtime and authority | Source and fixture verification does not establish deployment state or trading authority.                                                               |

## Completed fixes

- [x] Added the repository's [code instructions](../code-instructions.md), adapted
      from the owner's source document, and linked them from AGENTS, PRODUCT and README.
- [x] Split trading orchestration, signal processing, live execution, read-only
      reconciliation, Paper fills, valuation and revision-based persistence. Execution
      claims, final authority checks, precision, exceptions and ordering remain intact.
- [x] Split TradingPanel into focused class views, settings, mandate review and
      controls. The parent retains DI, polling, cancellation, validation and actions.
- [x] Split DailyBriefPage into news, conversation, summary, research status and
      member views. Lifecycle/date/control methods retain their original syntax.
- [x] Removed the unused TeamPage implementation; `/team` still aliases Daily Brief.
- [x] Separated HTTP snapshot collection from bounded JSON, trading and research
      projections. All moved projection expressions retain their original behavior.
- [x] Separated the AI output schema from worker lifecycle. Tool restrictions,
      field bounds, role permissions and response validation remain unchanged.
- [x] Removed JSON serialization attributes from Domain. Local stopwatch timing is
      still retained during event coalescing and excluded from wire serialization.
- [x] Fixed worker cleanup after cancellation or invalid output. Pipe failures are
      observed immediately; termination completes before the worker task returns.
- [x] Strengthened architecture checks for Domain transport/persistence dependencies
      and read-only ports across all Mediator query handlers, including flat folders.
- [x] Separated read-only portfolio sync status from the interface that can request
      a sync. Query and command DI resolve the same worker through different capabilities.
- [x] Rewrote the architecture guide around current ownership and flows. Corrected
      adaptive timeframe, objective, activation and dashboard-route documentation.
- [x] Added Markdown link checks and missing dashboard/assessment checks to CI.
      Markdown and workflow formatting now run through the same checked formatter.

## Responsibility sizes

Line counts describe the reviewed snapshot, not permanent size limits. Extracted
classes own real responsibilities; they do not use partial classes or forwarding bags.

| Container               | Before | After |
| ----------------------- | -----: | ----: |
| TradingEngine           |  1,533 |   339 |
| TradingPanel            |  2,141 |   617 |
| DailyBriefPage          |  1,253 |   396 |
| PortfolioSnapshotReader |  1,040 |   438 |
| CodexTeamWorker         |    618 |   411 |
| Architecture guide      |    538 |   160 |

Large cohesive venue adapters, schema scripts and composition roots were reviewed
without splitting them solely to satisfy a line-count threshold. No new dependency,
generic framework, service boundary or database migration was introduced.

## Verification

| Check                                                                              | Result                                                                            |
| ---------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- |
| Frontend lint/typecheck and production build                                       | Passed                                                                            |
| Solution build with configured autoformat/analyzers                                | Passed; zero compiler warnings/errors                                             |
| Prettier, CSharpier, analyzer and blank-line checks                                | Passed                                                                            |
| Architecture ownership/boundaries                                                  | 440 service C# files and 169 frontend files passed                                |
| Portfolio, Portfolio API, MarketData and Team deterministic checks                 | Passed                                                                            |
| Editor/analyzers, DI, services, presentation, runtime and Docker helper checks     | Passed                                                                            |
| AI worker/process, output schema, safe errors and assessment fixtures              | Passed without real model calls                                                   |
| Dashboard proxy and staged activation checks                                       | Passed with fake transports                                                       |
| Shell, Crypto, contracts, lifecycle, orders, risk, Trading and Team browser checks | Passed against isolated APIs                                                      |
| Desktop/tablet/phone inspection                                                    | Passed; preserved layout, focus behavior and no horizontal page overflow          |
| Daily Brief before/after comparison                                                | 19 byte-identical render fixtures; eight unchanged lifecycle/control/date methods |
| Dependency audit                                                                   | Zero reported vulnerabilities                                                     |

The new worker-output regression failed before the fix by timing out while a child
process remained alive. It now rejects oversized output promptly and confirms that
the process has exited. Cancellation checks cover repeated supervised shutdowns.
Event checks also preserve local latency timing without adding a serialized field.

## Verification limits

- Default checks use fake brokers and isolated browser fixtures. They do not activate
  AutoLive, submit orders, send Telegram messages or call a real model.
- SQL integration checks remain opt-in and were skipped in this cleanup run. These
  results do not establish canonical database or account state.
- Per-project automatic formatting emits workspace-loading notices. The final format
  checks succeed with no pending changes; diagnostic solution checks are retained
  alongside raw build/test logs under ignored `work/project-cleanup`.
- The existing 72-hour delivery observation and experimental exit comparison/holdout
  requirements remain open. A source cleanup does not prove an execution edge or
  complete those observation gates.
- Deployment is separate reviewed maintenance under [the runtime guide](docker-runtime.md).
