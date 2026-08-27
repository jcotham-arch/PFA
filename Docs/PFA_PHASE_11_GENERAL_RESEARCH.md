# Phase 11 — Generalized Research and Candidate Discovery

Phase 11 stores pattern-neutral, reproducible research runs and hypotheses while preserving the legacy FVG candidate matrix, order, statuses, metrics, sample threshold, and independent-FVG counts through a compatibility adapter.

Every run records an immutable dataset manifest, data revision/hash, complete search-space declaration, candidate count, multiple-comparison method, random seed where applicable, population counts, independent-event key, exclusions, input manifest, engine version, timestamps, and every tested hypothesis. Empty, insufficient, unstable, and negative results are retained. A completed run is rejected unless its stored hypothesis count exactly matches its declared search-space count.

Research records and database constraints permanently set `CanActivateStrategy=false`. This layer cannot alter the Phase 10 registry or activate sandbox/live behavior.

The legacy adapter records `legacy-none-recorded` for multiple-comparison handling because the existing discovery service does not provide that metadata. It records `legacy-exclusions-not-itemized` because exclusions occur before the current discovery report and are not separately counted. These are documented gaps, not silently invented evidence.

Additive tables: `GeneralResearchRuns` and `GeneralResearchHypotheses`.

Read-only additive endpoints:

- `GET /api/research/runs?limit=50`
- `GET /api/research/runs/{researchRunId}`
- `GET /api/research/runs/capabilities`

Existing research summary and candidate APIs remain unchanged. Rollback is to ignore the additive generalized tables and continue using the legacy discovery outputs.
