# ADR-0003: Run an actual Pixel Agents fork as the office

## Status

Accepted

## Context

Quantara V1 needs Pixel Agents itself, not a look-alike renderer. Quantara also must keep deterministic paper trading independent from visualization and must not install Claude or local-AI hooks.

## Decision

Vendor the MIT-licensed `pixel-agents-hq/pixel-agents` source at commit `3537e140c2094761beae748592aeb92ece8edfdd` under `integrations/pixel-agents`.

A Quantara bridge inside its standalone Node server subscribes to the ASP.NET Core SignalR `TradingEvent` stream and maps normalized lifecycle events into Pixel Agents' existing `AgentStateStore` and WebSocket messages. Its original React Canvas renderer, sprites, office layout, pathfinding, and character animation remain in use.

Docker runs Pixel Agents as a sibling service. The Quantara web shell embeds it for Office view and keeps Operations view and paper-run controls. The trading API has no dependency on either UI service.

## Non-goals

- No live broker or live execution.
- No Ollama, local AI, Claude hooks, or transcript scanning.
- No replacement pixel renderer.
- No LLM authority over risk or portfolio state.

## Consequences

- Upstream UI/assets/features are inherited directly.
- The small bridge is the only trading-specific fork seam.
- Pixel Office failure cannot stop backtests, `RiskEngine`, or paper execution.
- Upstream updates require replaying and testing the bridge against a new pinned commit.
