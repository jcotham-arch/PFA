# PFA Agent Research Dataset V1

## Status

Implemented and materialized on 2026-08-28 as research-only infrastructure. Dataset and model-run records are immutable and cannot activate a strategy or route to a real broker.

## Dataset

- Dataset ID: `AGDS-62D1265E9BCC7537B20A9501E87B5902`
- Version: `generic-outcome-dataset-1.0.0`
- Examples: 19,900
- Chronological split: 13,930 train / 2,985 validation / 2,985 test
- Instruments: MES, 6A, 6B, 6E, 6J
- Event range: 2026-04-30 through 2026-08-27 UTC
- Target horizon: 15 minutes
- Content hash: `62D1265E9BCC7537B20A9501E87B5902EC38D6F36C28AC08650C32C80FDF4AD3`

Each example retains observation/outcome identity, instrument, explicit contract, timeframe, module and pattern identity, direction, event time, feature-known time, decision time, outcome-known time, split, source revision, extracted numeric geometry features, and content hash.

Targets are directional close change, maximum favorable excursion, and maximum adverse excursion in instrument-native ticks. They are generic market-response labels, not fills, realized R, strategy returns, or authorization to trade.

## Metric identity correction

The original `UniversalOutcomeMetrics` primary key omitted `Unit`, causing points, ticks, and USD rows with the same outcome/name/horizon to collide. The first inserted unit survived and later units were ignored. Migration `UNIVERSAL_OUTCOME_METRIC_UNIT_IDENTITY_1` adds `Unit` to the immutable metric identity. Existing outcome IDs were preserved and replay was used to restore tick and USD metrics.

## Baseline run

- Run ID: `ABR-2B430C289E973EACBFDD556F8EFBB423`
- Model: `grouped-mean-baseline-1.0.0`
- Training samples: 13,930
- Groups: instrument + module + direction
- Target: 15-minute directional close change in ticks

| Split | Samples | MAE ticks | RMSE ticks | Directional accuracy |
|---|---:|---:|---:|---:|
| Train | 13,930 | 4.621 | 8.652 | 46.6% |
| Validation | 2,985 | 3.157 | 4.482 | 46.2% |
| Test | 2,985 | 6.008 | 10.675 | 37.3% |

The baseline does not generalize and is not a promotable model. Its purpose is to establish a deterministic, leakage-controlled benchmark that future models must beat on untouched validation and test populations.

## Next research gates

1. Add embargoed and per-instrument temporal folds rather than relying only on one global split.
2. Compare against zero, global-mean, per-instrument, and per-module baselines.
3. Add calibrated linear and tree baselines using only training-split feature selection.
4. Report per-instrument, per-module, direction, regime, and date stability.
5. Freeze a separate execution hypothesis before producing strategy-specific R labels.
6. Keep all validation/test data outside subsequent fitting and tuning.

## V1.1 split correction

Segmented evaluation revealed that a single global chronological split could place a late-arriving instrument entirely in the test population. Dataset version `generic-outcome-dataset-1.1.0` therefore assigns chronological 70/15/15 splits independently inside each instrument before combining the populations. This preserves temporal ordering while ensuring each included instrument has its own training, validation, and test evidence.

The corrected materialization is `AGDS-50E5F28574E991C0266092D02ED6A515`: 19,900 examples split into 13,928 train, 2,985 validation, and 2,987 test rows. Baseline run `ABR-72FB67B5CAD16F21CF29BE876474D607` evaluates every instrument in both held-out populations. Its aggregate validation/test directional accuracy is 45.9%/45.9%, so it remains a research benchmark rather than a promotable model.

## Baseline comparison V1.2

Run `ABR-4F2ED9C0A3AD4C98C7536B7A8D132090` compares zero, global-mean, instrument-mean, module-mean, and instrument/module/direction-grouped predictors fitted only on training rows. On test data, the grouped predictor reaches 45.9% directional accuracy but 4.031 ticks MAE; the global-mean predictor reaches 45.8% and 3.998 ticks. The added grouping therefore does not yet provide a meaningful edge over the trivial baseline. This negative result remains visible by design and blocks promotion.

## Embargoed walk-forward V1.3

Run `ABR-6A6E552BFA3ACC1112EF5C00AB52EF43` adds three deterministic expanding folds inside the development population. Each instrument is sliced chronologically, each fold trains only on its earlier history, labels must be known before a 15-minute pre-validation embargo, and the final test split remains untouched. Fold directional accuracy progresses from 43.9% to 45.3% to 47.6%; MAE moves from 6.389 to 4.816 to 3.350 ticks. The instability across time is explicit evidence that stronger modeling and stability gates are still required.

## Ridge-linear baseline V1.4

Run `ABR-F28EE81A440C0F552BEAB28C04BDA1B9` fits a deterministic L2-regularized linear model using numeric geometry features standardized from training rows only. It records 44.4% validation and 45.1% test directional accuracy with 3.779/4.016 tick MAE. It does not beat the global-mean or grouped predictors decisively, so the feature set has not yet demonstrated sufficient predictive signal and promotion remains blocked.

## Nonlinear baseline and enforced promotion gate

The deterministic 25-stage boosted-stump model records 44.3% validation and 44.7% test directional accuracy with 3.766/4.014 tick MAE. Run `ABR-FBF66C6BAC4076300FDEB76E9ED8C2C9` selects candidates by validation MAE, then applies a machine-readable research gate against untouched test and walk-forward evidence. The selected boosted-stump candidate is rejected because it does not beat global-mean test MAE, lacks the required two-percentage-point directional lift, and has walk-forward folds below 50%. Instrument coverage passes. This gate is advisory for further research only and never grants strategy activation or broker-routing authority.

## Context feature contract V1.2

Dataset `AGDS-06C06A8F1557340C69FE765B7E2664AC` expands the point-in-time vector from 15 to 32 numeric features with deterministic instrument, module, pattern, UTC session-cycle, and weekday-cycle context. The 19,900-example population and per-instrument splits are unchanged. Run `ABR-9DB205AFF809011E271E0B0DD20C0E59` shows that the expanded ridge and boosted-stump models still fail the promotion gate. The negative result indicates that categorical and calendar context alone are insufficient; future work must add genuine pre-decision market-state and regime features rather than tune against test outcomes.

## Canonical timeline repair and market-state features V1.3

The canonical coverage audit found 579,449 historical bars recorded as `UNRESOLVED` because instrument definitions were incorrectly effective-dated from 2026-08-27 and dated contract symbols such as `MESU6` and `6EU6` were not recognized as roots. The fix makes reviewed definitions historically applicable, resolves dated symbols, preserves original canonical rows, and adds an additive versioned root-resolution mapping plus an indexed revision-one research lookup. Coverage now resolves MES, 6A, 6B, 6E, 6J, CL, GC, and HG histories.

Dataset `AGDS-382EC60CAA9E155503AC45B44467613E` contains 19,900 examples and 36 features, now including pre-decision five-minute return, one-minute range fraction, close location, and log volume from immutable revision-one canonical bars whose close time is at or before the observation clock. Run `ABR-2562C9EDC41B7EB455386DAADE15C730` still rejects the boosted-stump candidate. This proves the data is now reaching the engine correctly, while also showing that this initial market-state window is not sufficient predictive evidence.
