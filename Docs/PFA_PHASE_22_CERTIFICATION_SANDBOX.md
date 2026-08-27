# Phase 22 — Certification Sandbox

Phase 22 adds a self-contained, simulation-only certification environment. It does not contain a live broker route, live credentials, or any mechanism that can activate a strategy for real trading.

## Application access

- Main application: `http://127.0.0.1:5214/`
- Certification sandbox: `http://127.0.0.1:5214/sandbox.html`

The local server must be running for either address to work. Port `5214` is the fixed development preview port used for this phase.

## Certification controls

The execution engine freezes a deterministic realism profile and models latency, jitter, stale quotes, venue outages, bid/ask fills, queue uncertainty, participation-limited partial fills, adverse slippage, volatility and size impact, commissions, and stop activation chronology. Reconciliation compares the internal ledger with a simulated venue ledger and reports mismatches.

The initial conservative 50K rule pack enforces trailing drawdown, daily loss, contract limits, news and session restrictions, automation permissions, consistency, minimum trading days, profit targets, and payout gates. A failed operational or evidence gate remains a failure; the UI cannot promote a strategy.

## Evidence admission

A strategy enters certification only when its exact frozen version is both `ValidationComplete` and linked to a `Stable` walk-forward report. At the end of this phase no strategy satisfies both conditions, so the sandbox correctly displays zero eligible strategies and does not create fantasy trades.

## Pattern and sequence replay

In Development on the loopback interface only, `POST /api/patterns/replay` replays the registered active detectors against stored market bars and persists universal observations and sequence instances idempotently. The current MES 5-minute replay evaluated 1,481 complete aggregate bars and detected 877 distinct observations: 237 FVG, 320 liquidity sweeps, 160 range breakouts, and 160 failed breakouts. Existing legacy FVG captures remain preserved, so the dashboard's total FVG row may be higher than the replay-only count.

## Known data limitation

The local database currently contains 8,903 real one-minute bars for MESU6, covering August 19–26, 2026. Other registered instruments have no stored real bars. The configured Massive provider reports an error because its API key is missing. The application reports those instruments as `NO REAL DATA`; it does not synthesize or mislabel coverage.
