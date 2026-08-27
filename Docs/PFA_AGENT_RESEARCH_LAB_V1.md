# PFA Agent Research Lab V1

## Objective

Build an auditable research agent that learns only from immutable, point-in-time datasets and demonstrates behavior in a $50,000 virtual account before any execution integration is considered. The lab cannot guarantee accuracy or profitability and cannot connect to a live broker.

## Required inputs

- Canonical multi-instrument bars, provenance, quality, corrections, sessions, and rollovers
- Versioned market facts, features, pattern observations, lifecycle events, and sequences
- Market-regime and cross-market context aligned by `KnownAtUtc`
- Immutable strategy definitions and dataset manifests
- Realistic contract economics, fees, slippage, latency, and ambiguity
- Prop-account rule packs when evaluating challenge compatibility

## Virtual account

The initial account definition is USD 50,000 with an append-only ledger. Every decision records the information available, model/agent version, strategy version, confidence/calibration, proposed risk, governor decision, order simulation, fills, costs, positions, and resulting equity. Default action is `NO TRADE` whenever data, authorization, or risk evidence is missing.

## Training and evaluation

Historical training, unseen validation, walk-forward testing, and prospective sandbox operation are separate datasets and stages. The agent cannot learn from a validation or forward outcome and then continue claiming that period as unseen. Evaluation includes net expectancy, calibration, maximum drawdown, tail loss, risk-of-ruin estimates, stability across regimes, rule violations, turnover/cost sensitivity, and performance degradation—not raw directional accuracy alone.

## Safety boundary

The Agent Research Lab is introduced only after universal observations/sequences and immutable strategy definitions exist. Phase 18 supplies sandbox infrastructure; Phase 19 supplies independent deny-by-default governance; Phase 22 may build inert live-pilot infrastructure after a separate high-stakes design review. Broker support, including any Robinhood capability, requires an official supported API, account eligibility, legal/terms review, credential isolation, reconciliation, duplicate-order protection, and explicit human/governance authorization.

The agent must never self-promote, alter risk policy, enable a broker, or move from historical learning to live action on its own.
