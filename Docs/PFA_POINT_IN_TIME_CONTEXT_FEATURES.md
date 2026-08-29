# Point-in-time context features

Version: `point-in-time-context-features-1.3.0`

This layer converts market state known at a scenario's decision clock into research-only agent features. It is shared by the universal outcome dataset and the actionability outcome dataset so both training paths use the same missing-data and regime semantics.

## Available candle-derived evidence

- Latest completed one-minute bar: range fraction, close location, and log volume.
- Five-bar momentum: fractional return and positive, negative, or flat state.
- Twenty-bar volatility: current-range ratio and short-window-to-baseline range ratio.
- Twenty-bar participation: relative volume and five-to-twenty-bar volume acceleration.
- Auction state: balanced, transition, or directional based on body-to-range behavior.
- Interaction evidence: high-volume expansion, low-volume compression, directional expansion, direction-aligned momentum, and momentum-participation strength.

All canonical-bar queries use `CloseTimeUtc <= DecisionTimeUtc` (or the observation's earlier known-at clock). Outcome labels become available only after the decision time and remain separated from the feature clock.

## Availability contract

Every example has explicit availability gates for latest canonical bar, five-bar prior close, twenty-bar context, order flow, Level II, options positioning, and market breadth. When a source is unavailable, its gate is zero and its measurements are omitted. A missing feed is never represented as a neutral market measurement.

The bar-derived context snapshot likewise emits `SourceUnavailable` families for disconnected external sources, with a human-readable reason and no invented numeric values.

## Historical same-clock seasonality

Seasonality is now estimated only from earlier one-minute bars for the same instrument and UTC minute of day. The current decision bar is excluded (`CloseTimeUtc < DecisionTimeUtc`), the lookup is capped at the most recent 40 matching observations, and at least 10 prior observations are required. Until that minimum exists, `context.availability.canonical.seasonalityHistory` is zero and all historical-seasonality measurements are omitted.

Eligible examples expose historical sample count, mean and mean-absolute return fraction, positive-close rate, directional bias, and current range/volume relative to the same-clock baseline. The same implementation feeds both universal outcome examples and actionability examples. An indexed `(instrument, timeframe, minute-of-day, close-time)` lookup keeps the point-in-time query bounded.

Production dataset `AGDS-53BF02947F765FEB368D158107A5A1F9` contains 19,900 universal examples and 86 feature names, split chronologically into 13,928 train / 2,985 validation / 2,987 test. Historical same-clock measurements are genuinely available for 18,180 examples (91.3568%); the earlier 1,720 examples remain unavailable rather than being backfilled with future history. The dataset feature-audit route `/api/agent/research-datasets/{datasetId}/feature-coverage` reports present and non-zero coverage for any feature prefix, making source readiness measurable rather than inferred.

## First training result

Dataset `ARDS-F0259F12FDECD9AADF148224B30D6F9B` contains 149,884 scenarios, 83 features, and unchanged chronological partitions of 104,320 train / 22,575 validation / 22,989 test examples.

Hurdle run `AHR-4B38CFB490D52390906D144356D7185D` consumed the expanded dataset. Its untouched test selection produced -0.082173 mean net R and 0.580165 profit factor across 1,062 selected scenarios. This is a valid negative result: the expanded feature set is attached and trainable, but it does not pass an economic promotion gate and must remain research-only. The next research step is context-family ablation and regularization/feature selection, not sandbox activation.

## Context-family ablation result

Baseline run `ABR-AAAC375F4CF4BC68309B0051B668EF21` evaluated nine context families independently against the same structural-only control for failed breakouts, liquidity sweeps, and range breakouts. No family improved both mean absolute error and directional accuracy across the three modules. Regime state averaged -0.003884 directional-accuracy lift and +0.000928 MAE; regime interactions averaged -0.001735 lift and +0.000534 MAE. Session context slightly reduced MAE (-0.000025) but did not improve direction. Source availability had exactly zero effect because the external-source gates are uniformly unavailable in this historical dataset.

The structural base-only model selected 735 untouched-test scenarios at -0.041838 mean net R and 0.773543 profit factor. The all-feature linear model selected 1,335 at -0.093671 mean net R and 0.516308 profit factor. Both remain rejected. This evidence rules out indiscriminate linear context inclusion and supports the next step: module-specific nonlinear selection, train-only regularization, and regime-conditioned thresholds.

Version `research-promotion-gate-2.6.0` also places a deterministic 6,000-example cap on ridge and boosted-stump fitting. Full validation and test populations remain untouched; only model fitting is sampled. This makes repeated ablations reproducible and prevents research requests from monopolizing the local application.

## Regime-conditioned segment search

Version `actionability-segment-research-1.3.0` treats context as a selector rather than forcing every context feature into one linear model. It evaluates module and execution-policy combinations against explicit volatility, volume, auction, and momentum states; joint volatility-volume and auction-momentum states; and active regime interactions.

Report `ASR-75DD88CA05C4F754E38E0F38FD6E8160` evaluated 1,011 segments with at least 100 training and 100 validation examples. All 1,011 failed development economics, so none was permitted to inspect untouched test outcomes. The report retains each rejected segment's training/validation metrics and reasons, is stored immutably, and is available from `/api/research/actionability-segments/history`. This prevents negative evidence from disappearing while protecting the test set from repeated selection pressure.

## Module-specific nonlinear result

Baseline run `ABR-83149016A3560F50E913F4001817D434` freezes four 25-stump ensembles: a global control plus separate failed-breakout, liquidity-sweep, and range-breakout artifacts. The module-specific scoring path resolves the applicable artifact from the point-in-time `context.module.*` feature and remains research-only with no activation or broker-routing authority.

Neither the global nor module-specific nonlinear ensemble predicted any positive-expectancy scenarios in validation or untouched test. The structural ridge control remained the least-negative selectable model at -0.041838 mean net R and 0.773543 profit factor on 735 test scenarios. The combined ridge remained worse at -0.093671R and 0.516308 profit factor. This rejects shallow nonlinear rescue of the current policy labels; it does not prove that every possible entry/exit formulation is untradeable.

## Expanded policy-label campaign

Pattern run `PTR-06782AC8F111471E6BDC797B07D02814` tested true directional-close confirmation, two-bar progressing confirmation, break-even after 1R, a half-R trailing lock after 1R, and opposite-bar-close exits across 0.5R, 0.75R, 1R, and 1.5R targets and 5, 10, and 15 minute holding windows. The engine evaluated 576 hypotheses against 19,948 observations, retaining 3,523,104 samples including no-entry and ambiguous cases. Zero hypotheses achieved positive expectancy and profit factor above 1.0 in both training and validation, so no test-qualified policy or downstream agent-dataset rebuild was authorized.

The campaign exposed a scale constraint as well as a market result. Future requests now have a five-million-scenario preflight cap, and immutable sample writes reuse a prepared command inside one transaction. The completed 3.5-million-sample run remains preserved; the safety change prevents accidental combinatorial grids from expanding without an explicit narrower design.

The actionability dataset builder now also accepts an explicit pattern-run ID and defaults to a 500,000-source-sample cap. This prevents the latest broad exploratory campaign from silently replacing the narrower frozen training corpus. A larger run can still be selected intentionally by raising the cap after its storage and training cost is reviewed.

## Order Flow source readiness

The point-in-time encoder now consumes an immutable Order Flow feature snapshot only when its window ended by the decision clock, it was known by that clock, it is no more than five minutes stale, it matches the instrument/contract, and it contains non-empty source references and volume. Eligible snapshots emit buy/sell/unknown shares, delta fraction, cumulative delta normalized by window volume, quoted-size imbalance, point-of-control distance, and a positive availability gate. Missing or stale snapshots emit only an availability value of zero; they do not invent neutral Order Flow measurements.

The local production corpus currently reports `NoSourceData`: zero events, trades, quotes, and feature snapshots, with no production adapter selected. The Agent page now distinguishes an implemented Order Flow module from source readiness and labels it data-gated. `/api/order-flow/coverage` exposes the authoritative coverage state. Level II remains unavailable because no timestamped market-depth source exists.

## Cross-market confirmation result

Version `point-in-time-cross-market-1.0.0` resolves each related instrument's latest completed one-minute bar at or before the decision clock, requires that bar to be no more than two minutes stale, and computes its five-observed-bar return only when the lookback spans no more than ten minutes. The target instrument is excluded. Each snapshot retains the latest and prior canonical source IDs; future bars cannot enter a historical decision.

Dataset `AGDS-4D5B4D1F4119684DE4AA33084ACAC4DF` contains 19,900 examples and 101 feature names. Cross-market evidence is available for 19,806 examples (99.5276%), drawing from synchronized MES, currency, crude-oil, and gold bars already present in the canonical corpus. The bounded in-memory index builds this dataset in approximately 16 seconds; it replaced an unacceptably slow correlated-query prototype.

Baseline run `ABR-ADEAEA78AB1A0452FABFCF19ED266B84` remains globally rejected. The family ablation nevertheless identifies a module-specific research hypothesis: cross-market features improved untouched-test directional accuracy for failed breakouts by 3.1464 percentage points (45.4172% to 48.5636%) and range breakouts by 3.9525 points (43.7418% to 47.6943%), while slightly reducing MAE in both. They reduced liquidity-sweep accuracy by 3.0181 points. The FVG test slice contained only six examples and is not decision-grade. Cross-market confirmation must therefore be tested as a targeted breakout selector, not enabled globally.
