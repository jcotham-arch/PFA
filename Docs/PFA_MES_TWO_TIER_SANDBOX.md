# PFA MES Two-Tier Sandbox

## Operating boundary

MES is the first and only instrument admitted to the initial exploratory lane. MYM and M2K follow as separately versioned lanes only after the MES protocol is understood. Exploratory paper admission is deliberately easier than certification, but it never authorizes strategy activation or broker routing.

## Tier 1 — Incubator

Version `mes-exploratory-sandbox-admission-1.0.0` reads MES-only pattern-trade research runs and selects hypotheses using training and validation evidence only. Admission requires at least 30 resolved training samples, 10 resolved validation samples, positive mean net R in both partitions, and profit factor above 1.0 in both. Test evidence is explicitly withheld from selection.

This is an early-observation gate, not a profitability claim. A candidate entering Tier 1 remains statistically unvalidated and cannot activate a strategy or route to a broker. The next Tier 1 implementation increments are:

1. persist an immutable `Incubator` tier on a frozen exploratory strategy version;
2. create an append-only exploratory campaign lifecycle;
3. consume only market events known after the freeze clock;
4. use a clearly labeled lightweight fill model until tick replay is available;
5. retain every signal, abstention, fill, cost, position, and terminal outcome;
6. terminate subscriptions and cancel working orders when a frozen culling rule is reached.

## Tier 2 — Proving Ground

The existing certification lane remains strict. Standard sandbox instances require a validation-complete frozen strategy. The execution certification engine already models seeded latency and jitter, bid/ask execution, queue-ahead uncertainty, participation-limited partial fills, volatility and quantity slippage, commissions, stale data, and venue outages. Prop-firm certification already supports static, end-of-day trailing, and intraday high-water drawdown; daily loss, maximum contracts, session flatten, consistency, automation, payout, and operational-data rules.

Tier 1 status can never be treated as Tier 2 eligibility. A separate immutable transition must prove blind-test and walk-forward evidence before certification.

## Telemetry status

The append-only sandbox ledger currently retains signals, requested prices, orders, granted fill prices, commissions, slippage, timestamps, market source identity, data revision, positions, trades, and account performance. Research samples retain bar-derived MFE and MAE. The following remain data- and implementation-gated:

- a dedicated point-in-time `trade_telemetry` snapshot containing multi-level depth, 1-minute and 5-minute CVD trajectories, session VWAP distance, and availability/quality flags;
- tick-by-tick MFE, MAE, `time_to_mfe`, and `time_to_mae` in milliseconds;
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

The existing forward campaign monitor can automatically suspend degraded or operationally invalid campaigns and create governance incidents. Tier 1 still needs a separate frozen rolling culling policy, such as profit factor below 0.50 after 100 closed trades. Culling should terminate the campaign subscription and cancel working sandbox orders; it should not kill arbitrary operating-system processes. Any open virtual position requires an explicit close-or-quarantine policy and immutable audit.
