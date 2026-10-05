# ADR-0002: Use Mediator for application dispatch

## Status

Accepted

## Decision

Quantara uses martinothamar's `Mediator` for in-process command and query
dispatch. Application commands implement focused contracts in
`TradingCompany.Application.Abstractions`; handler implementations live in
`TradingCompany.Application.Handlers` and depend on the Application feature
assembly. Both assemblies are registered by the API composition root.
The executable API owns `Mediator.SourceGenerator` and `AddMediator`
configuration, and endpoints dispatch through `ISender`.

Mediator is not an event bus and does not replace explicit trading workflows or
domain services. The deterministic `RiskEngine` remains the final authority and
is invoked directly from application orchestration.

## Consequences

- API endpoints remain thin and decoupled from handler construction.
- Commands and queries have one discoverable application entry point.
- Source-generated dispatch avoids reflection-based scanning at runtime.
- A small framework dependency is accepted at the application boundary.
