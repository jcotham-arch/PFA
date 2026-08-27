# Phase 12 — Generalized Cross-Day Evidence

Phase 12 stores immutable evidence for identical versioned signatures across explicit trading dates. It preserves the legacy FVG signature, classifications, gates, daily metrics, stability metrics, independent-event counts, and permanent non-activation behavior through an adapter; other pattern and sequence families use the same contracts.

Expected trading dates must be supplied by the session/dataset manifest. The adapter never manufactures trading dates from calendar days. Missing dates, positive/negative/flat days, regime identifiers, aggregate metrics, gates, and daily evidence remain explicit. Legacy evidence is labeled `legacy-regime-unclassified` because the existing service does not record regimes.

Persistent-negative evidence is durable and receives no automatic invalidation or deletion. Neither reports nor signatures can activate a strategy. Public APIs are read-only.

Additive tables: `GeneralCrossDayEvidenceReports`, `GeneralCrossDaySignatureEvidence`, and `GeneralCrossDayDailyEvidence`.

Additive endpoints:

- `GET /api/evidence/cross-day/{reportId}`
- `GET /api/evidence/cross-day/capabilities`

Existing FVG cross-day routes and gate logic remain unchanged. Rollback is to ignore the additive tables and use the legacy service.
