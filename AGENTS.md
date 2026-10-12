# Engineering

- Lead owns scope, architecture, review, acceptance and verification. Local workers receive bounded file ownership; inspect their actual output before accepting it.
- Follow docs/architecture.md, DESIGN.md and the checked compiler/analyzer/lint configurations.
- One C# class/record/interface/enum per matching file. One React component or named type per matching file; Next.js route conventions are exceptions for filenames.
- Keep controllers thin and use Mediator CQRS. Queries receive read-only interfaces. Domain is framework-free; dependencies point inward. Services communicate through HTTP and never share database ownership or another service's assemblies.
- Components are class based and obtain feature services through Context and the lazy ServiceContainer. Transport data is validated. Keep broker credentials outside browser code, prompts, model workers and logs.
- Use explicit access modifiers, this-qualified instance members, braces, nullable modeling, meaningful names and sealed classes unless intentionally abstract/static. No suppressions to hide warnings or nullability problems.
- Preserve the existing SQL schema, account partitions, retained history and unknown cost/profit semantics. No database wipes or authority migrations during refactors.
- Owner authorized the AI team on 2026-10-12: CEO, Analyst and Reviewer collaborate on explicitly started report tasks through Codex SDK. No scheduled AI work, trading, withdrawals, transfers, leverage or authorization expansion is part of this slice.
- Run npm check/build, format checks, solution build, deterministic and architecture checks. UI changes require bounded desktop/tablet/phone inspection. Real Bitkub/SQL checks are opt-in; CI needs no broker secrets.
- Thai progress reports should stay concise; app labels remain English. Complete authorized work and report material limitations honestly.
