# PFA Non-FVG Trade-Hypothesis Lab V1

## Purpose

This research layer tests explicit entry, stop, target, exit, cost, and direction interpretations for liquidity sweeps, range breakouts, failed breakouts, and later sequence stages. A pattern remains a market fact; a hypothesis is a separate, immutable interpretation of how someone might act on that fact.

## Initial execution contract

- Entry: first one-minute bar open at or after the observation's point-in-time `KnownAtUtc`.
- Stop: module-specific structural invalidation plus a configurable tick buffer.
- Target: configurable R multiple of entry-to-stop risk.
- Exit: first stop or target, otherwise the final close inside the maximum holding window.
- Costs: configurable round-trip ticks deducted from gross R.
- Ambiguity: if stop and target occur in the same one-minute bar, the sample is `Ambiguous`; no favorable ordering is assumed.
- Direction: hypotheses may follow or oppose the stored pattern direction. This lets failed-breakout continuation and reversal interpretations coexist without mutating the observation.
- Splits: chronological 70/15/15 train, validation, and untouched test populations inside each instrument/module group.

Every sample retains observation identity, hypothesis identity, decision/entry/exit clocks, prices, outcome, gross/net R, MFE/MAE in R, reason, and content hash. Runs and samples are immutable and cannot activate a strategy or route to a broker.

## First five-market grid

- Run: `PTR-323D1B7D1E38176F6C07E79EA74A21EB`
- Instruments: MES, 6A, 6B, 6E, 6J
- Observations: 19,948
- Hypotheses: 36
- Samples: 220,194
- Modules: liquidity sweep, range breakout, failed breakout
- Targets: 1R, 2R, 3R
- Maximum holds: 15, 30, 60 minutes
- Stop buffer: one tick
- Estimated round-trip cost: one tick

All leading validation hypotheses are negative after costs and remain negative on untouched test data. The least-negative validation family is failed-breakout continuation with a 60-minute hold; its test mean remains approximately -0.13R. The initial grid therefore identifies no tradeable candidate. That is a useful negative baseline, not a reason to tune against the test set.

## Entry timing V1.1

Run `PTR-5D6476A68406EECC0A84928F1E5647FC` adds a one-minute confirmation-close entry beside the immediate next-open entry. It evaluates 72 hypotheses and 440,388 samples over the same 19,948 observations. Confirmation modestly improves the least-negative range-breakout validation rows, but their untouched test results remain approximately -0.08R to -0.09R. Failed-breakout continuation remains negative, and reversal interpretations do not lead validation. No entry timing is promoted.

## Structural stops V1.2

Run `PTR-064358D12C00849EABD425013FCD6DE3` compares explicit structural invalidation policies over all 19,948 observations with a locked 1R target, 30-minute maximum hold, both entry clocks, a one-tick buffer, and one tick of estimated round-trip cost. The 16 hypotheses produce 97,864 immutable samples.

- Liquidity sweeps compare the sweep extreme with the reclaimed reference boundary.
- Range breakouts compare the broken boundary with the opposite side of the source range.
- Failed breakouts compare continuation and reversal direction interpretations with the structurally applicable boundary, extreme, or opposite-range stop.

The least-negative validation hypothesis is failed-breakout continuation entered at the next one-minute open with the opposite-range stop: -0.094R validation across 678 samples and -0.112R on 680 untouched test samples. Range-breakout continuation with the opposite-range stop is approximately -0.116R validation and -0.086R test. Every structural-stop hypothesis remains negative after estimated costs, so none is promoted.

An earlier pilot run, `PTR-EA3C285AE7902ABDD1A985430841964B`, selected only 640 observations because its explicit micro-symbol filter did not match the complete stored instrument universe. It remains retained for auditability but is not the representative result.

## Next research work

1. Add entry variants: reclaim retest, boundary limit, and delayed confirmation.
2. Add volatility-normalized invalidation and range-midpoint stops.
3. Add exits: partial targets, break-even movement, trailing structure, session close, and adverse time stop.
4. Segment results by instrument, direction, session, weekday, volatility, trend, and sequence stage.
5. Freeze promising definitions using validation only, then evaluate once on untouched test and prospective sandbox populations.
6. Convert validated state changes into informational notifications before considering any actionable alert language.
