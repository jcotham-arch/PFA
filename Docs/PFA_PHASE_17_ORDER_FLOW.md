# Phase 17 — Order-Flow Subsystem

## Outcome

Phase 17 adds an isolated, source-neutral order-flow domain for trades and quotes. It does not alter candle ingestion, candle-derived features, market-pattern modules, replay, or any existing API behavior.

No production trade/quote source has been selected. Accordingly, this phase provides a normalized research-ingest contract and provider-adapter interface but installs no provider-specific adapter. It explicitly does not infer “order flow” from OHLCV candles.

## Event and provenance model

- Provider identity, provider event ID, provider symbol, instrument/contract identity, provider sequence, source version, raw reference, event time, and received/known time are retained.
- Events distinguish trades, quotes, originals, corrections, and cancels.
- Canonical IDs are stable per provider event identity; content hashes expose provider identity conflicts.
- Equivalent duplicates are counted and do not create duplicate canonical events.
- Corrections and cancels append new events with explicit supersession lineage. Prior rows are never rewritten.
- Cross-batch corrections can resolve targets already stored in the database.
- Quality flags cover invalid trades/quotes, crossed quotes, late arrival, out-of-sequence delivery, missing correction targets, corrections, and cancels.

## Classification and features

Aggressor classification uses only information known by the requested `asOfUtc`:

1. trades at/above the latest eligible ask are buys;
2. trades at/below the latest eligible bid are sells;
3. trades inside the spread use the prior eligible trade tick direction;
4. otherwise the side remains unknown.

Future-known and future-event-time quotes are not used. Cancelled/superseded events are removed from the effective view without erasing their audit history.

Versioned feature snapshots contain:

- total, buy, sell, and unknown volume;
- window delta and session-to-window-end cumulative delta;
- price-bucket volume profile and point of control;
- last eligible bid/ask size imbalance;
- session assignment, data revision, feature-set version, quality flags, exact source references, and content hash.

Profiles cannot cross a trading-session boundary. The current session assignment remains the documented legacy UTC compatibility model, not an authoritative CME calendar.

## Additive API

- `POST /api/order-flow/events` — ingest already-normalized provider trade/quote events.
- `POST /api/order-flow/snapshots` — create and persist a point-in-time feature snapshot.
- `GET /api/order-flow/snapshots/{snapshotId}` — retrieve immutable research features.
- `GET /api/order-flow/capabilities` — disclose installed source/adapters and safety limitations.

## Storage and retention

Order-flow uses separate tables:

- `OrderFlowEvents`
- `OrderFlowClassifiedTrades`
- `OrderFlowFeatureSnapshots`
- `OrderFlowRetentionPolicies`

Events are append-only and classifications/snapshots are immutable. The initial retention policy records planning horizons but enforces `AutomaticDeletionEnabled=0`; no automated deletion occurs before a source, cost model, legal entitlement, and operational retention decision are approved.

## Verification coverage

The Phase 17 tests cover received-time ordering, provider sequence regressions, equivalent duplicates, at-bid/at-ask and tick-rule classification, future-knowledge exclusion, corrections, cancels, cross-batch lineage, identity conflicts, price profiles, delta, quote imbalance, session boundaries, versioned/immutable persistence, and disabled automatic deletion.

## Unresolved decisions

Before enabling production capture, select and document:

1. the exact trades/quotes or depth-of-book source and its event semantics;
2. entitlement, redistribution, retention, volume, and cost constraints;
3. aggressor-side fields supplied by the provider versus locally inferred classification;
4. authoritative exchange sequencing, correction, cancel, and snapshot/recovery semantics;
5. whether full depth, market-by-order, or top-of-book data is required;
6. an authoritative exchange-session calendar and a storage/partition strategy beyond local SQLite scale.

Until those decisions are made, the subsystem is research infrastructure—not evidence that real historical order-flow coverage has been collected.
