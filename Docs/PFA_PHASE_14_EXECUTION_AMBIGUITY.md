# Phase 14 — Execution Ambiguity Resolution Escalation

Phase 14 preserves every legacy one-minute ambiguous scenario and may resolve chronology only with higher-resolution evidence. The fixed hierarchy is one-second evidence followed by tick evidence. If stop and target remain inside the same smallest evidence event, the outcome remains `StillAmbiguous`; missing or quality-rejected evidence produces `NoEvidence`. There is no optimistic fallback.

Each immutable request records subject, instrument, direction, exact time window, stop/target prices, original resolution, execution-model version, and data revision. Results retain every attempted resolution, reason, exact source references, resolved resolution/time when proven, and resolver version. Incomplete, invalid-OHLC, provider-conflicted, and unresolved-instrument events cannot resolve chronology.

The legacy MES adapter creates requests only for scenarios already marked `IntrabarSequenceUnknown`; it does not change their existing realized-P&L behavior.

Additive tables: `ExecutionEvidenceRequests`, `ExecutionAmbiguityResults`, and `ExecutionResolutionAttempts`. Read-only endpoints:

- `GET /api/execution/ambiguity/results/{resultId}`
- `GET /api/execution/ambiguity/capabilities`

Public reprocessing is disabled until a higher-resolution provider and authorization boundary are selected. Rollback is to ignore these additive records and retain legacy ambiguity unchanged.
