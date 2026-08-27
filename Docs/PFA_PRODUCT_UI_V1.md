# PFA Product UI V1

The first product surface turns the research platform into a responsive application that can be used from a desktop or phone. It is served by the existing ASP.NET application and does not create a second source of truth.

## Current surfaces

- Market-intelligence overview with instrument, session, quality, and timeline context
- Responsive market chart with FVG geometry
- Research-pipeline progression and explicit execution lock
- Persistent-candidate table and interactive evidence detail
- Research, evidence, pattern, market, and system workspaces
- Desktop sidebar and touch-oriented mobile navigation
- Read-only operational overview API backed by legacy, canonical, and Phase 3 feature tables
- Interactive 1m, 5m, 15m, and 1h candlestick chart backed by stored one-minute bars
- Point inspection for OHLCV, preserved FVG observation count, and deterministic offline preview
- Exact stored-data coverage report
- Pattern-module inventory and recent FVG observation workspace
- Expanded research methodology and evidence-gate context

The visual research snapshot uses the supplied MESU6 evidence for 2026-08-19 through 2026-08-25: 203 FVG observations, 366 unique rules, 36 persistent candidates, and 24 persistent-negative rules. It is clearly research state; no UI control can activate or execute a strategy.

## Product boundaries

V1 is an application framework and meaningful command-center slice, not a trading terminal. Pattern, market, and evidence workspaces are intentionally framed for later API integration. Authentication, write workflows, sandbox trading, governance, alerts, and eventual execution controls must arrive only in their architectural phases.

The UI remains usable when its read-only operational endpoint is unavailable, then hydrates live canonical and feature counts when the API responds.

## Opening the application locally

In Visual Studio, select the `PFA Market Intelligence` launch profile and press the run button. The application opens in the default browser at `http://localhost:5188`. The ASP.NET process must remain running while the application is in use.

The refresh control re-reads the operational overview endpoint. Validation preparation remains visibly locked because Phase 3 does not authorize strategy activation or execution.

See `PFA_USER_MANUAL.md` for the complete operating guide and current product boundaries.
