# Code instructions

These are the repository's engineering defaults, adapted from the owner's Clean Code,
SOLID and Clean Architecture guidance. Use judgment: improve ownership and clarity
without adding speculative abstractions. [AGENTS.md](AGENTS.md) defines financial
authority and maintenance requirements; this document never expands that authority.

## Ownership and dependencies

- Give each module one clear responsibility. Separate transport, validation, business
  decisions, persistence and presentation when they change for different reasons.
- Keep one C# class, record, interface or enum in a matching file. Keep one React
  component or named type in a matching file. Next.js route filenames are exceptions.
- Organize frontend code by feature and component. Colocate component props and state;
  keep contracts, API adapters, services and presentation models in their own folders.
- Keep Domain free of HTTP, JSON, SQL, UI frameworks and dependency injection.
  Application depends on Domain; Infrastructure implements Application interfaces;
  API is the composition root. Dependencies point inward.
- Keep controllers thin. Dispatch commands and queries through Mediator; queries use
  read-only interfaces. Keep writes and side effects out of query handlers.
- Services communicate through HTTP. Each service owns its database, contracts and
  migrations; do not reference another service's assemblies or query its tables.
- Reuse existing code, the standard library and installed dependencies before adding
  a package, service, interface or abstraction. Extract a real responsibility, not a
  forwarding wrapper or a generic helper collection.

## Readability and types

- Use meaningful domain names. Prefer a short, direct implementation over a clever
  expression. Replace unexplained literals with named domain concepts where useful.
- Keep methods focused and flatten nested control flow with guards. Review methods
  over 20 lines, nesting beyond three levels and files over 500 lines; these are
  review signals, not reasons to split cohesive code mechanically.
- Use explicit access modifiers, `this` for instance members, braces and nullable
  modeling. Seal C# classes unless inheritance is intentional or the type is static.
- Use enums or validated discriminated values for closed sets. Preserve existing
  wire values, stored values and hash inputs when introducing internal types.
- Do not use TypeScript `any`, non-null assertions, compiler suppressions or lint
  disables to conceal a modeling or validation problem.
- Model unknown values explicitly. Unknown cost, fills or profit must remain unknown;
  zero and a guessed default are not substitutes for missing evidence.
- Plain transport DTOs are appropriate. Put domain behavior beside the concepts that
  own it; do not create inheritance hierarchies for data containers.
- Use class-based React components and services. Resolve feature services through
  Context and the lazy `ServiceContainer`. Application functions belong on classes;
  callbacks inside methods are allowed. Do not introduce hooks or function components.

## Boundaries and failures

- Validate external HTTP data, persisted documents, configuration and model output at
  trust boundaries. Reject ambiguous identity, unsupported values and invalid ranges.
- Keep credentials outside browser bundles, research prompts, worker environments and
  logs. Preserve existing redaction; do not expose raw exception bodies or secrets
  merely to retain an inner exception.
- Keep account-specific trades, financial results, personal objectives, risk
  preferences and runtime snapshots out of public documentation and static prompts.
  Store private operational evidence in ignored local storage; research reads the
  current objective and risk settings from its account-scoped runtime context.
- Make failure states explicit. Preserve useful causes where safe; do not swallow a
  failure and report success. A deliberate best-effort path must explain its limit.
- Persist financial claims before side effects. Preserve compare-and-swap revisions,
  account partitions, exact order amounts, expiry, rules hashes and idempotency keys.
- Never automatically retry an uncertain broker submission, cancellation or message
  send. Background execution does not grant financial authority.
- Propagate cancellation through ordinary I/O. After a durable external-action claim,
  preserve the existing bounded completion and unknown-outcome recovery semantics.
- Keep shared mutable state small, synchronize ownership and prevent overlapping
  rounds. Avoid unobserved fire-and-forget work and unbounded queues or payloads.

## Formatting and comments

- Let the checked formatter and analyzer configuration decide whitespace, line width,
  semicolons and quotes. Run the repository scripts instead of hand-formatting files.
- Use single quotes in TypeScript and double quotes for JSX attributes and C# strings.
  Preserve language syntax and configured escaping rules.
- Separate declarations and logical steps with the configured blank-line rules.
  Avoid dense one-line constructors, unrelated operations and nested ternaries.
- Comments explain a constraint or a non-obvious reason. Remove stale explanations,
  commented-out code and vague TODOs. Version control owns the change history.

## Tests and refactoring

- Understand callers and establish a relevant baseline before restructuring code.
  Preserve behavior, public interfaces, exception semantics, ordering and serialized
  bytes unless the task explicitly requires a behavior or compatibility change.
- Write a failing regression check before fixing non-trivial business, parser,
  concurrency, financial or security behavior. Keep tests deterministic and isolated.
- Use fake brokers in development and CI. Tests must not place real orders, activate
  AutoLive, mutate the owner's account or require production credentials.
- Prefer observable behavior checks over tests that duplicate private implementation.
  Mechanical moves use the existing behavior checks plus architecture checks.
- Review the actual diff independently. Run frontend check/build, format checks,
  solution build, deterministic and architecture checks. Inspect changed UI at bounded
  desktop, tablet and phone sizes. Keep opt-in SQL/broker checks clearly identified.
- Preserve data and existing work. Do not wipe databases, reset changes, regenerate
  authority or silently upgrade legacy mandates during cleanup.

## Documentation

- Keep [docs/architecture.md](docs/architecture.md) about current ownership and flows.
  Put operational procedures in runtime guides and feature details in focused docs.
- Keep [DESIGN.md](DESIGN.md) as the visual source of truth. App labels are English;
  owner-facing progress reports and Telegram messages use concise, readable Thai.
- Distinguish implemented behavior, configured behavior and verified running state.
  Do not mark observation periods or live strategy gates complete without evidence.
- Update examples and links alongside code. Document compatibility limits and recovery
  steps where they matter; avoid duplicating the same policy across many files.

## Enforcement

The checked `.editorconfig`, compiler analyzers, ESLint, Prettier and CSharpier rules
are executable policy. `npm run test:architecture` checks type ownership and layer
boundaries; `npm run test:docs` checks local Markdown links. Passing these checks does
not replace review of cohesion, behavior or financial authority.
