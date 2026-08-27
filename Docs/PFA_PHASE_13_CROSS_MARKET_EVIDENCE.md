# Phase 13 — Cross-Market Evidence

Phase 13 evaluates one frozen, versioned signature across explicitly planned instruments. Instrument economics normalize movement into points, ticks, and dollars per contract while R metrics remain dimensionless.

Comparability is explicit. Missing instrument definitions, unavailable evidence, definition-version mismatches, or unavailable required features are non-comparable. Session-version differences are partially comparable and remain in notes. No comparison silently treats different market microstructures as equivalent.

Results classify evidence as Robust, MarketSpecific, Mixed, or Inconclusive. Weak or negative performance on another market is retained as evidence and never automatically invalidates the source hypothesis. Cross-market results cannot activate strategies.

Additive tables: `CrossMarketEvidenceResults` and `CrossMarketInstrumentEvidence`. Read-only endpoints:

- `GET /api/evidence/cross-market/{resultId}`
- `GET /api/evidence/cross-market/capabilities`

Existing single-market and cross-day evidence remain unchanged. Rollback is to omit cross-market evidence from progression and ignore the additive tables.
