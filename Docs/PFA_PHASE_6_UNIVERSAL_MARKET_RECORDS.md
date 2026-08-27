# Phase 6 — Universal MarketObservation and MarketOutcome

## Outcome

Phase 6 adds immutable, versioned, strategy-neutral observation and outcome records alongside the legacy FVG persistence path. Legacy tables, readers, routes, and calculation semantics remain authoritative during dual operation.

## Additive records

- `UniversalMarketObservations` stores module identity, revision, point-in-time timestamps, typed payload schema, lineage references, quality flags, and content hash.
- `UniversalObservationLifecycleEvents` preserves append-only lifecycle chronology.
- `UniversalMarketOutcomes` stores factual evaluation windows without treating them as trade results.
- `UniversalOutcomeMetrics` supports arbitrary named horizons rather than fixed FVG columns (`-1` is the explicit non-horizon sentinel for metrics such as MFE/MAE).
- `UniversalOutcomeEvents` preserves ordered first-event chronology.
- `UniversalObservationRelationships` is an inactive additive relationship surface for later sequence intelligence.

Existing FVG observation and outcome saves dual-write universal records. Existing FVG rows are additively backfilled during initialization. No legacy row is rewritten or removed.

## Explicit compatibility findings

The legacy observation identity is based on engine `1.0.0`, while `FvgOutcome.EngineVersion` defaults to `1.1.0`. Phase 6 preserves both values. It does not resolve or conceal the discrepancy.

Legacy `Outcomes.SetupId` points at `FvgOutcome.FvgId`, but the active observation identity is a deterministic `FVG-*` hash and the historical `Setups` relationship is not populated by replay. Universal outcomes therefore use the deterministic observation identity calculated from the factual FVG geometry. The universal schema intentionally does not enforce an observation foreign key until orphan inventory and migration reconciliation are complete.

## API

`GET /api/patterns/observations?moduleId=fvg&limit=100` and
`GET /api/patterns/outcomes?observationId={id}&limit=100` are additive. Existing FVG endpoints are unchanged.

## Rollback

Stop universal repository writes and ignore the six additive tables. Legacy observation/outcome tables remain intact and authoritative.
