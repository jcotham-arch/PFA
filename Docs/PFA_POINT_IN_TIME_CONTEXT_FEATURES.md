# Point-in-time context features

Version: `point-in-time-context-features-1.0.0`

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

## First training result

Dataset `ARDS-F0259F12FDECD9AADF148224B30D6F9B` contains 149,884 scenarios, 83 features, and unchanged chronological partitions of 104,320 train / 22,575 validation / 22,989 test examples.

Hurdle run `AHR-4B38CFB490D52390906D144356D7185D` consumed the expanded dataset. Its untouched test selection produced -0.082173 mean net R and 0.580165 profit factor across 1,062 selected scenarios. This is a valid negative result: the expanded feature set is attached and trainable, but it does not pass an economic promotion gate and must remain research-only. The next research step is context-family ablation and regularization/feature selection, not sandbox activation.

## Context-family ablation result

Baseline run `ABR-AAAC375F4CF4BC68309B0051B668EF21` evaluated nine context families independently against the same structural-only control for failed breakouts, liquidity sweeps, and range breakouts. No family improved both mean absolute error and directional accuracy across the three modules. Regime state averaged -0.003884 directional-accuracy lift and +0.000928 MAE; regime interactions averaged -0.001735 lift and +0.000534 MAE. Session context slightly reduced MAE (-0.000025) but did not improve direction. Source availability had exactly zero effect because the external-source gates are uniformly unavailable in this historical dataset.

The structural base-only model selected 735 untouched-test scenarios at -0.041838 mean net R and 0.773543 profit factor. The all-feature linear model selected 1,335 at -0.093671 mean net R and 0.516308 profit factor. Both remain rejected. This evidence rules out indiscriminate linear context inclusion and supports the next step: module-specific nonlinear selection, train-only regularization, and regime-conditioned thresholds.

Version `research-promotion-gate-2.6.0` also places a deterministic 6,000-example cap on ridge and boosted-stump fitting. Full validation and test populations remain untouched; only model fitting is sampled. This makes repeated ablations reproducible and prevents research requests from monopolizing the local application.
