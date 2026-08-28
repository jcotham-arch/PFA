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

## Immutable certification campaigns

Certification campaigns bind an exact strategy ID/version and stable walk-forward evidence revision to one or more frozen, officially verified rule-pack snapshots. Each rule pack is evaluated independently against the same closed trading-day evidence. Campaigns, rule-pack snapshots, and results are append-only and content-addressed; replaying the same campaign is idempotent, while reusing its identity with different content is rejected.

`POST /api/certification/campaigns` is protected by the separate sandbox control token. The service rechecks the exact `ValidationComplete` strategy version and its linked stable walk-forward report before evaluation. Unverified or duplicate rule packs fail closed. Database constraints permanently set both `CanPromoteStrategy` and `CanRouteToRealBroker` to false.

The sandbox dashboard reports campaign and payout-eligible result totals. A payout-eligible simulation is evidence for review only—it cannot activate a strategy or authorize live infrastructure.

## Readiness projection

The sandbox also projects the existing mandatory live-pilot design gate. It searches for an exact frozen strategy version with a stable immutable walk-forward report and a stable, nonzero Phase 20 forward comparison derived from that same report. It separately displays the eleven accountable design decisions required by the migration plan. Missing evidence or decisions remain visible instead of being inferred or auto-approved. Even `ReadyForInfrastructureBuild` can authorize only an inert infrastructure build; the auditor always returns false for live routing and strategy activation.

## Pattern and sequence replay

In Development on the loopback interface only, `POST /api/patterns/replay` replays the registered active detectors against stored market bars and persists universal observations and sequence instances idempotently. The current MES 5-minute replay evaluated 1,481 complete aggregate bars and detected 877 distinct observations: 237 FVG, 320 liquidity sweeps, 160 range breakouts, and 160 failed breakouts. Existing legacy FVG captures remain preserved, so the dashboard's total FVG row may be higher than the replay-only count.

## Known data limitation

The local database currently contains 8,903 real one-minute bars for MESU6, covering August 19–26, 2026. Other registered instruments have no stored real bars. The configured Massive provider reports an error because its API key is missing. The application reports those instruments as `NO REAL DATA`; it does not synthesize or mislabel coverage.
