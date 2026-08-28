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
