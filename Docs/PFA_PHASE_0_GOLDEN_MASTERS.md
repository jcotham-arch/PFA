# Phase 0 Golden Masters

This Phase 0 test suite records current behavior; it does not declare that behavior ideal and does not authorize production changes.

## Curated replay fixtures

`PFA FVG Scanner.Tests/Fixtures/known-replay-days.json` contains deterministic replay cases dated 2025-01-06, 2025-02-11, and 2025-03-19. They protect full mitigation, partial mitigation, no retracement, post-confirmation filtering, and chronological minute processing. These are curated synthetic known-day fixtures because the repository contains no checked-in provider payloads or historical database that can be safely copied into tests.

## Known version inconsistency

Current persisted FVG observations, candidate discovery, cross-day evidence, and out-of-sample validation report engine version `1.0.0`. Historical replay and MES scenario evaluation report `1.1.0`. The golden master at `PFA FVG Scanner.Tests/GoldenMasters/legacy-engine-versions.json` documents both values without selecting or changing the authoritative legacy version.

## Current behavior intentionally captured

- Candle idempotency includes provider in its unique identity. Equal candles from Massive and Tradovate coexist; there is no conflict reconciliation.
- Raw market events are append-only and do not have an idempotency key.
- Five-minute aggregation completes when it has five distinct timestamps in a bucket; it does not verify that those timestamps are all five expected minute offsets.
- OHLC data cannot establish within-candle chronology. An entry candle that also touches an exit, or a later candle touching both stop and target, produces an ambiguous scenario with no realized P&L.
- Cross-day grouping uses UTC calendar dates and immutable rule fields, not an exchange trading-session identity.
- Passing validation gates never activates a strategy; `CanActivateStrategy` remains false.

Generated GUIDs and wall-clock creation timestamps are excluded from golden comparisons. Stable natural keys, versions, calculations, classifications, chronology, and response-shaped model properties remain asserted.
