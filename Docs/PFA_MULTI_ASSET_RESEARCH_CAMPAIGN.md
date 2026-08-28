# PFA Multi-Asset Research Campaign

## Purpose

This campaign expands Prop Firm Assassins research beyond MES and beyond fair value gaps. Completion requires real historical data, comparable setup detections, chronological sequence instances, and explicitly separated forward outcomes for every supported asset.

Historical pattern frequency is evidence, not proof of profitability. No campaign result can activate a strategy or authorize live routing.

## Active dated-contract campaign

- Job: `HJOB-FE013197257B686C65C46253DDF81238`
- Provider: Massive
- Requested interval: 2025-08-28 00:00 UTC through 2026-08-28 00:00 UTC
- Source resolution: one minute
- Rebuild resolution: five minutes
- Window size: seven days
- Maximum concurrency: two
- Work windows: 848

The job is durable and resumable. Every provider symbol is an explicit dated futures contract. The campaign does not claim to be a back-adjusted or rollover-aware continuous series.

| Root | Provider contract |
|---|---|
| MNQ | MNQU6 |
| GC | GCU6 |
| CL | CLV6 |
| ZN | ZNU6 |
| 6E | 6EU6 |
| SI | SIU6 |
| 6B | 6BU6 |
| 6J | 6JU6 |
| 6A | 6AU6 |
| MYM | MYMU6 |
| M2K | M2KU6 |
| HG | HGU6 |
| NG | NGF27 |
| ZC | ZCU6 |
| ZW | ZWU6 |
| ZS | ZSU6 |

MESU6 was collected separately by job `HJOB-14593CF81891158D20F859AE1A23EE20`: 53 of 53 windows completed, 118,444 bars reported saved, zero failed windows, and zero reported quality issues.

## Learning matrix

For every instrument, contract, supported timeframe, pattern module, and direction, PFA must report:

1. Observation count and historical coverage.
2. Earliest and latest formation times.
3. Chronological sequence count, state, transition duration, and point-in-time confidence.
4. Forward outcome sample count and metric distributions when evaluation exists.
5. Entry, stop, target, R, chronology, ambiguous-candle, and execution assumptions where applicable.
6. Session, weekday, trend, volatility, and cross-market context when those versioned features are available.
7. In-sample, out-of-sample, walk-forward, and prospective sandbox evidence as separate populations.

Empty or inadequate samples must be reported as insufficient evidence. They must never be represented as zero-value outcomes or successful strategies.

## Current active detector boundary

- Fair value gaps: five-minute compatibility adapter.
- Liquidity sweeps: 1m, 5m, 15m, and 1h.
- Range breakouts: 1m, 5m, 15m, and 1h.
- Failed breakouts: 1m, 5m, 15m, and 1h.

Market structure, displacement, session references, and volume/volatility remain registered but definition-pending. They must not emit evidence until their detectors and golden-master tests exist.

## Versioned sequence studies

- Neutral intraday pattern progression
- Liquidity sweep to imbalance
- Liquidity sweep to range breakout
- Breakout continuation
- Breakout followed by failure
- Failed breakout to opposing breakout

These definitions capture chronology; their names are research hypotheses, not claims of causality or profitability.

## Generic forward outcome measurements

Non-FVG setup modules use a point-in-time generic evaluator with 5, 15, and 60 minute horizons. The entry reference is explicitly the first completed one-minute bar open at or after the observation's `KnownAtUtc`. Measurements include directional close change, maximum favorable excursion, and maximum adverse excursion in:

- price points;
- instrument-native ticks; and
- USD per contract using the versioned instrument definition.

These fixed-horizon measurements are not fills and do not supply a strategy-specific stop, target, realized R, commission, spread, or slippage model. Those belong to a separately frozen execution hypothesis and the certification sandbox.

## Known limitations and required follow-up

- One dated contract is not a continuous futures history. Rollover selection and adjustment semantics remain unresolved.
- Some current contracts did not trade or were not liquid for the entire requested year. Sparse history must remain visible in coverage.
- The legacy MES scenario engine uses MES-specific tick and dollar economics. Its results must not be generalized to other instruments. Cross-asset execution research requires the versioned instrument registry economics.
- The active multi-asset job began under inclusive provider-window boundaries. Database uniqueness prevents duplicate candle rows, but its manifest `BarsSaved` can overcount duplicate boundary responses. The corrected pipeline uses half-open windows and counts only successful inserts; database coverage is authoritative for this active job.
- The first sequence definition is a broad intraday observation progression. Named market narratives require frozen, versioned sequence definitions and independent validation.
- A detected setup is not yet a labeled outcome. Outcome generation and pattern replay are separate auditable stages.
