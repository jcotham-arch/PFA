# Phase 2 Canonical Timeline

Phase 2 adds a provider-neutral canonical bar timeline beside the legacy `Candles` and `RawMarketEvents` tables. Legacy writes and APIs remain unchanged and occur before additive canonical writes.

During compatibility operation a failed canonical write is returned as a failed ingestion attempt and does not roll back or block the successful legacy path. Canonical failures therefore need operational observability before canonical storage can become authoritative.

## Safety and migration

Before implementation, `Data/Database/pfa-market-data.db` was copied to `pfa-market-data.pre-phase2-20260827.db`. Both files are runtime data and remain ignored by Git. The schema migration is additive and journaled as `PHASE2_CANONICAL_TIMELINE_1`; repeated initialization is idempotent.

No existing table or column is deleted, renamed, or rewritten. Rollback is to stop canonical service registration/dual writes and continue using legacy tables. The additive canonical tables can remain unused.

## Identity and revisions

A canonical bar identity uses instrument, dated contract (or explicit unresolved identity), resolution, and open time. Provider and live/backfill mode are lineage—not canonical identity—so equivalent inputs converge. Content changes create preserved revisions. Same-provider changes are corrections; cross-provider differences are explicit provider conflicts. Neither overwrites an earlier revision.

## Provenance and quality

Every source records provider, original symbol, source event type/resolution/timestamp/version, received time, ingestion run, and optional raw reference. Quality flags cover incomplete and invalid OHLC, unresolved instrument/contract, legacy session assignment, corrections, and provider conflicts.

The current session assignment is still the Phase 1 legacy UTC compatibility adapter. The current DI contract resolver contains no production mappings, so dated provider symbols remain explicitly unresolved until reviewed mappings are configured. This is intentional and prevents guessed contract identity.

## Open decisions

- Correction authority and when a revision becomes authoritative
- Provider precedence/reconciliation
- Durable ingestion-run lifecycle/checkpoints
- Backfill pagination beyond the legacy 50,000-row request
- Session-aware gaps and expected bars
- Reviewed dated-contract mapping source
- Backfill of existing legacy rows into canonical tables
