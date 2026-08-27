# Prop Firm Assassins — User Manual

## Purpose

PFA is a research-first market-intelligence application. It preserves market data, detects versioned patterns, studies entry/stop/target scenarios, discovers hypotheses, and measures independent evidence. It does not currently place trades or authorize a strategy.

## Open the application

In Visual Studio, select the `PFA Market Intelligence` profile and run the project. The application opens at `http://localhost:5188`. The application process must remain running while using the local site.

## Navigation

- **Overview** — current research snapshot, interactive market chart, evidence readiness, candidate summary, and operational activity.
- **Market** — exact stored candle coverage by symbol and timeframe. A warning remains visible until a rollover-aware long-range campaign exists.
- **Patterns** — registered pattern modules and recent preserved observations. Phase 4 supplies universal module contracts; Phase 5 will adapt the existing FVG system into the universal runtime.
- **Research** — candidate definitions, population, search-space context, leakage controls, and the next required gate.
- **Evidence** — persistent candidates, watchlist, persistent-negative behavior, insufficient evidence, and the exact promotion gates.
- **System** — ingestion, canonical timeline, feature engine, and execution-lock status.

On a phone-sized screen the sidebar becomes a bottom navigation bar.

## Interactive chart

Select `1m`, `5m`, `15m`, or `1h` above the chart. The application requests the selected resolution from the local read-only chart API. Higher timeframes are derived from stored one-minute candles. Move the pointer across a bar to inspect its open, high, low, close, and volume. When the API is unavailable, deterministic preview bars keep the interface usable but are not research evidence.

The full drawing and indicator workbench—rectangles, lines, custom indicators, saved layouts, and replay controls—is a later product increment. It must remain separate from pattern evidence and strategy authorization.

## Pattern modules

Every universal detector declares a stable module ID, version, and supported resolution. Detection receives an explicit point-in-time canonical context. The contract rejects future bars, unsupported resolutions, incomplete/invalid data, unresolved instruments, and provider conflicts.

FVG is Pattern Module #1. Phase 5 maps canonical bars into the preserved legacy detector and maps its result back into a source-linked universal observation. Its algorithms are not rewritten.

## Research and evidence

Candidate discovery is not strategy approval. A candidate must have independent observations and meet the displayed persistence gates before it may enter frozen out-of-sample validation. Negative, flat, incomplete, and insufficient results remain part of the evidence record.

Current cross-day evidence covers the supplied five-trading-day MESU6 window. It is not a six- or twelve-month campaign. The Market screen reports the exact data physically present in the local database.

## Safety and interpretation

- No screen can activate a strategy or place an order.
- Historical results do not guarantee future profitability.
- Outcome fields are not available to predictors before their known time.
- Incomplete data and unresolved chronology must remain explicit.
- Provider credentials and local databases are never displayed by the UI.

## Planned capabilities

The roadmap adds the FVG universal adapter, lifecycle engine, market-sequence research, additional pattern modules, rollover-aware long-range campaigns, walk-forward validation, prop-account intelligence, interactive chart tools, prospective sandboxing, governance, and only later separately authorized execution infrastructure.
