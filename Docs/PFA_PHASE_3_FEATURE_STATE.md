# Phase 3 Market State and Universal Features

Phase 3 adds versioned feature definitions, independently timestamped feature values, and immutable market-state snapshots. Existing `FvgFeatureAnalysisService`, discovery inputs, session buckets, and API responses remain unchanged.

## Point-in-time contract

Every feature value has separate `AsOfUtc` and `KnownAtUtc`, engine version, data revision, quality flags, and source references. Consumers must check `KnownAtUtc` against their decision time. Feature roles explicitly distinguish market facts, predictors, execution facts, outcome labels, and post-entry diagnostics.

`MarketStateEngine` accepts an explicit as-of time and data revision. It excludes bars closing after the as-of instant and revisions not yet effective, selects the latest eligible revision per canonical identity, propagates quality, and creates deterministic snapshot/feature identities.

## Legacy FVG compatibility

`LegacyFvgFeatureAdapter` maps current FVG feature records without changing their values. Formation/strategy features become available at confirmation; execution facts become available at entry; realized R and diagnostics become available only when a stop or target timestamp exists. Outcome and diagnostic definitions cannot be returned by the predictor query.

The existing UTC session bucket is retained and marked `LegacySession`. This phase does not replace or reinterpret historical FVG learning records.

## Persistence and rollback

The migration adds `FeatureDefinitions`, `FeatureValues`, and `MarketStateSnapshots`, journaled as `PHASE3_FEATURE_STATE_1`. Initialization and writes are idempotent. No legacy tables are modified. Rollback is to stop using the new registry, adapter, state engine, repository, and endpoint.

## Open decisions

- Authoritative state dimensions and lookback windows
- Materialization versus on-demand feature policy
- Session-aware and multi-timeframe alignment after an authoritative calendar exists
- Cross-market clock and availability rules
- Feature correction/recomputation policy across data revisions
- Which legacy FVG fields should remain compatibility-only versus become universal definitions
