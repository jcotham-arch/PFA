# Phase 16 — Walk-Forward Validation

## Outcome

Phase 16 adds generalized, immutable walk-forward evidence for frozen hypotheses. It does not replace or change the legacy FVG out-of-sample endpoint; that validator remains the single-fold compatibility reference and can be mapped into the new evidence model.

No Phase 16 result can activate a strategy. The domain records default to `CanActivateStrategy=false`, the repository rejects unsafe records, and SQLite constrains the value to zero while preventing updates or deletion of plans, folds, reports, and fold results.

## Validation model

- A plan freezes one hypothesis signature, parameter hash, dataset ID, and data revision.
- Rolling folds use half-open UTC ranges: validation includes its start and excludes its end.
- Training and validation are separated by a configurable embargo.
- Validation folds cannot overlap; `StepDays` must be at least `ValidationDays`.
- The frozen hypothesis is never rediscovered or re-optimized between folds.
- Observations from another correction revision are rejected instead of mixed into the report.
- A changed parameter hash is retained as drift and makes the aggregate result unstable.
- Fold gates use independent-event counts to prevent duplicate observations inflating sample sufficiency.
- Each fold records sample size, independent events, expectancy, win rate, profit factor, drawdown, and an observation-content hash.
- Aggregation reports weighted and worst-fold expectancy, passed/failed folds, and first-to-last expectancy degradation.
- A legacy FVG validation report can be represented as one conservative fold, but never as activation authority.

## Additive API

- `POST /api/walk-forward/evaluate` — create a deterministic plan, evaluate observations, and durably store immutable evidence.
- `GET /api/walk-forward/plans/{planId}` — retrieve the frozen fold plan.
- `GET /api/walk-forward/reports/{reportId}` — retrieve the immutable aggregate and fold results.
- `GET /api/walk-forward/capabilities` — disclose fold, revision, drift, compatibility, and non-activation guarantees.

The evaluation API accepts research evidence only. It does not submit orders, promote strategies, or start sandbox/live execution.

## Database records

- `WalkForwardPlans`
- `WalkForwardFolds`
- `WalkForwardReports`
- `WalkForwardFoldResults`

All four tables are additive. Existing market, research, validation, and strategy-registry data is untouched.

## Verification coverage

The Phase 16 tests cover rolling folds, embargo enforcement, train/validation separation, non-overlapping validation windows, correction-revision plan isolation, future end-boundary exclusion, weighted aggregation, performance degradation, parameter drift, legacy single-fold compatibility, idempotent persistence, immutable database records, and strategy non-activation.

## Explicit limitations and next decisions

- A stable report is evidence for later governed review only; it is not proof of live profitability.
- Fold duration, embargo, sample threshold, and acceptable degradation remain campaign-level research choices.
- Commission, slippage, latency, and ambiguity methodology must be encoded in the upstream frozen observation dataset and its revision.
- The current engine evaluates already-frozen signatures. Automated selection of a different parameter set inside a fold is intentionally absent because it would reintroduce hidden optimization.
