# PFA MES Two-Tier Sandbox

## Operating boundary

MES is the first and only instrument admitted to the initial exploratory lane. MYM and M2K follow as separately versioned lanes only after the MES protocol is understood. Exploratory paper admission is deliberately easier than certification, but it never authorizes strategy activation or broker routing.

## Tier 1 — Incubator

Version `mes-exploratory-sandbox-admission-1.0.0` reads MES-only pattern-trade research runs and selects hypotheses using training and validation evidence only. Admission requires at least 30 resolved training samples, 10 resolved validation samples, positive mean net R in both partitions, and profit factor above 1.0 in both. Test evidence is explicitly withheld from selection.

This is an early-observation gate, not a profitability claim. A candidate entering Tier 1 remains statistically unvalidated and cannot activate a strategy or route to a broker. Version `mes-tier1-blind-paper-replay-1.0.0` now:

1. freezes each admitted candidate as an immutable `FrozenResearch` strategy version;
2. opens the withheld Test partition only after the candidate identity is frozen;
3. persists immutable campaign, execution, and telemetry-supplement records;
4. records requested entry, simulated fill, stop, target, exit, net R, MAE, MFE, and minute-resolution time to MAE/MFE;
5. reports separate dollar economics and drawdown for one through five MES contracts;
6. marks candidates for Tier 2 review only after at least 30 blind executions with positive mean net R and profit factor above 1.10;
7. automatically terminates a Tier 1 candidate only when profit factor remains below 0.50 across at least 100 blind executions.

The first real blind replay began on August 29, 2026. Four frozen pullback-continuation variants produced eight resolved executions in total. All eight lost, with mean net result `-0.596R` and profit factor `0.00`. Each candidate currently has only two resolved blind executions, so the evidence is negative but too small for the frozen 100-trade culling rule. The versions remain unchanged and are accumulating prospective evidence.

## Adaptive Scenario Lab

The MES Sandbox includes an immutable champion/challenger learning loop. It reads only Train and Validation partitions when selecting a development champion, joins every sample to its source observation, and reports coverage by resolved trading day and source timeframe. The withheld Test partition cannot influence selection.

Each generation proposes controlled neighboring variants for target, maximum hold/time-abort, and exit management. A variant is a new version with explicit parent lineage; the frozen version under blind evaluation never mutates. Repeated generation requests are idempotent until the source run, evidence cutoff, policy, or champion changes.

Current live MES generation 2 has 54 resolved development trades across 40 distinct resolved trading days. Its mean result is `+0.020241R` and profit factor is `1.072945`, but its qualifying evidence comes only from the 5-minute pattern stream. It therefore remains `AwaitingDevelopmentEvidence` until the 1-minute, 15-minute, and 1-hour lanes are evaluated. Three one-variable challengers are queued, and the Test partition was not used for champion selection.

The next layer replays queued challengers over chronological development windows, compares pattern families in parallel lanes, ranks champion versus challenger by stability rather than the best single result, freezes one survivor, and reserves a strictly later untouched date range for blind replay.

## Tier 2 — Proving Ground

The existing certification lane remains strict. Standard sandbox instances require a validation-complete frozen strategy. The execution certification engine already models seeded latency and jitter, bid/ask execution, queue-ahead uncertainty, participation-limited partial fills, volatility and quantity slippage, commissions, stale data, and venue outages. Prop-firm certification already supports static, end-of-day trailing, and intraday high-water drawdown; daily loss, maximum contracts, session flatten, consistency, automation, payout, and operational-data rules.

Tier 1 status can never be treated as Tier 2 eligibility. A separate immutable transition must prove blind-test and walk-forward evidence before certification.

## Telemetry status

The append-only sandbox ledger currently retains signals, requested prices, orders, granted fill prices, commissions, slippage, timestamps, market source identity, data revision, positions, trades, and account performance. Tier 1 campaign storage now adds immutable execution-level MFE, MAE, minute-resolution `time_to_mfe` and `time_to_mae`, inferred round-trip friction, and contract variants. The following remain data-gated:

- a dedicated point-in-time `trade_telemetry` snapshot containing multi-level depth, 1-minute and 5-minute CVD trajectories, session VWAP distance, and availability/quality flags;
- tick-resolution MFE, MAE, `time_to_mfe`, and `time_to_mae` rather than the current one-minute estimates;
- prevailing spread and executed volume at the price level at the exact fill clock;
- immutable links from every telemetry record to its candidate version, market-event sequence, and fill model.

Missing L2 or order-flow measurements must remain absent behind explicit availability gates. They must never be imputed as neutral values.

## Shadow-learning safeguards

Rolling Tier 1 family performance may become a point-in-time meta-feature only from trades closed before the next decision clock. It requires a minimum-history gate and cannot include the active trade or later outcomes. Stop and time-abort research must be fitted on training data, frozen, and validated chronologically. A winning-trade 95th percentile may be reported, but it cannot be hard-coded directly into Tier 2 without accounting for losing trades, costs, parameter stability, and untouched validation.

## Tick replay and event distribution

Canonical trade and top-of-book quote event contracts already exist. Full L2 book updates and a production provider do not. Bar fills therefore remain an explicitly approximate Incubator fallback, never exchange-quality evidence. The intended progression is:

1. connect timestamped Level 1 trades/quotes;
2. connect ordered Level 2 book updates and snapshot/recovery semantics;
3. implement deterministic tick replay and multi-level queue simulation;
4. introduce an in-process bounded event channel first;
5. deploy Redis Streams, NATS, or RabbitMQ only when measured concurrency or durability requirements justify an external broker.

Do not add infrastructure solely because dozens of models might exist later. Profile the bounded in-process fan-out first and preserve deterministic replay identity across any transport.

## Culling

Tier 1 now has a frozen status rule: profit factor below 0.50 after 100 blind executions produces `Terminated`; at least 30 executions with positive mean net R and profit factor above 1.10 produces `Tier2ReviewEligible`. These are lifecycle classifications, never broker authority. Prospective event subscription, working-order cancellation, and explicit open-position close-or-quarantine behavior remain the next culling increment.
