# Phase 15 — Automated Multi-Instrument Historical Pipeline

## Outcome

Phase 15 adds a durable, resumable wrapper around the existing Massive one-minute backfill and strict five-minute rebuild services. The compatibility endpoints and their current behavior are unchanged.

Submitting a plan does **not** contact Massive or consume provider quota. Execution requires a separate, explicit `run` request. This is intentional because the requested twelve-month, multi-market campaign has unresolved provider-cost, contract-roll, and authoritative-session inputs.

## Protected behavior and boundaries

- Plans are deterministic and immutable for the same provider, time range, resolutions, instruments, provider symbols, window size, and concurrency limit.
- Every futures instrument requires an explicit dated provider symbol such as `MESU6`; root symbols are not silently converted into contracts.
- Instruments must exist in the versioned research universe, and resolutions must be approved by their instrument definition.
- Large ranges are divided into deterministic windows (default seven days), keeping each one-minute request safely below the legacy 50,000-row limit under normal complete-minute coverage.
- Window boundaries have explicit trading-session assignments and retain the instrument-definition version.
- Jobs, execution runs, checkpoints, coverage, and manifests are durable SQLite records.
- Re-submitting the same plan is idempotent and does not execute work.
- Completed windows are skipped on resume; failed windows are retried.
- A failure for one window/instrument is isolated from the others.
- Concurrency is bounded per plan (1–8; default 2) to limit provider pressure and SQLite contention.
- Dataset manifests include completed/failed window counts, saved/rebuilt totals, quality-issue counts, instruments, session assignment version, and a content hash.
- Existing manual Massive backfill and historical rebuild APIs remain available and unchanged.

## Additive API

- `POST /api/historical-pipeline/jobs` — validate, plan, and durably submit without executing.
- `GET /api/historical-pipeline/jobs/{jobId}` — job, checkpoint, and manifest status.
- `POST /api/historical-pipeline/jobs/{jobId}/run` — explicitly execute or resume the installed Massive adapter.
- `GET /api/historical-pipeline/capabilities` — safety constraints and unresolved execution inputs.

Example instrument input:

```json
{
  "name": "MES research slice",
  "provider": "Massive",
  "startUtc": "2026-08-01T00:00:00Z",
  "endUtc": "2026-08-08T00:00:00Z",
  "instruments": [
    { "instrumentId": "MES", "providerSymbol": "MESU6" }
  ],
  "windowDays": 7,
  "maxConcurrency": 2,
  "sourceResolution": "1m",
  "rebuildResolution": "5m"
}
```

## Verification coverage

The Phase 15 tests cover deterministic pagination, complete contiguous windows, explicit provider symbols, duplicate/implicit rollover rejection, submission idempotency, no execution on submission, durable checkpoints, failure isolation, retry/resume, completed-window skipping, bounded multi-instrument concurrency, session-aware coverage, instrument-definition lineage, quality manifests, and durable run/coverage records.

## Current data status and unresolved decisions

This phase builds the campaign machinery; it does not claim that a twelve-month campaign has run. The existing local data observed before this phase contains real coverage only for `MESU6` and is far shorter than twelve months. The other registered research markets still require explicit dated-contract schedules and provider-symbol mappings.

Before starting a paid long-range campaign, decide and version:

1. contract rollover rules and whether research uses individual or continuous contracts;
2. an authoritative CME/CBOT/COMEX/NYMEX holiday, early-close, and maintenance calendar (the current assignment is the documented legacy UTC compatibility model);
3. provider entitlements, pagination/rate limits, expected cost, and retry/backoff policy;
4. campaign-specific quality thresholds for missing bars, provider conflicts, and rebuild acceptance;
5. whether research should begin after each instrument completes or only after a frozen whole-universe manifest is available.

No database schema was destructively migrated, no configuration or secret was changed, and no provider download was started by this implementation.
