# Phase 18 — Sandbox Infrastructure

## Outcome

Phase 18 adds a durable, prospective virtual-trading sandbox for frozen strategy versions. It contains no real-broker adapter, no live-order route, and no automatic strategy activation. Existing historical scenario engines and `SimulatedMarketDataProvider` remain unchanged.

The sandbox accepts only strategy versions whose registry status is `ValidationComplete`. This is a conservative bridge while Phase 10 deliberately prevents transitions into active sandbox/live statuses. Phase 19 governance will add a separate authorization/veto layer; Phase 18 performance cannot promote itself.

## Virtual trading model

- Multiple USD virtual accounts with independent starting balances.
- Multiple frozen strategy versions and instances per account.
- Explicit trade-proposal-to-signal adapter; `NoTrade` decisions cannot become signals.
- Market, limit, and stop orders with working, partial, filled, cancelled, rejected, and expired lifecycle states.
- Versioned fill models with frozen content hashes covering latency, tick slippage, per-contract commission, and partial-fill policy.
- Missed fills when price or simulated available quantity is insufficient.
- Instrument-definition tick size and point value for fill and realized-P&L calculations.
- Positions, closed trades, commissions, cash balance, peak balance, and drawdown projections.
- Complete canonical-bar adapter retaining bar revision, data revision/content hash, and revision-known time.

## Point-in-time safeguards

- Signals whose `KnownAtUtc` is later than the sandbox clock are rejected.
- Market slices whose close or known time is later than the clock are rejected.
- Incomplete canonical bars are rejected.
- Orders cannot switch fill-model parameters between submission and processing under the same version label.
- Fill records retain source ID, canonical data revision, fill-model version, and fill-model hash.

## Durable recovery and isolation

`SandboxLedgerEvents` is an append-only event ledger. SQLite triggers forbid updates and deletes. Every event has:

- account and optional instance identity;
- monotonically increasing account sequence;
- command idempotency key;
- event type and occurrence time;
- typed JSON payload and content hash.

Account, instance, signal, order, fill, position, trade, and performance state is rebuilt by replay. Retrying a command does not duplicate effects. Multi-event commands append atomically, and one atomic append cannot cross account boundaries.

## Authenticated additive API

- `GET /api/sandbox/capabilities`
- `POST /api/sandbox/accounts`
- `GET /api/sandbox/accounts`
- `GET /api/sandbox/accounts/{accountId}`
- `POST /api/sandbox/accounts/{accountId}/instances`
- `POST /api/sandbox/accounts/{accountId}/instances/{instanceId}/start`
- `POST /api/sandbox/accounts/{accountId}/signals`
- `POST /api/sandbox/accounts/{accountId}/market`
- `POST /api/sandbox/accounts/{accountId}/orders/{orderId}/cancel`
- `POST /api/sandbox/accounts/{accountId}/instances/{instanceId}/stop`

All control and account-read endpoints require `X-PFA-Sandbox-Control`. The expected token is supplied only at runtime as `Sandbox:ControlToken` (for example through environment-based configuration). If no token is supplied, controls deny by default with service unavailable. No token or secret is stored in repository configuration.

## Verification coverage

The Phase 18 tests cover no-future signals and bars, canonical revision lineage, strategy decision adaptation, validation-complete entry gates, missed fills, partial fills, fill completion, market/limit behavior, frozen fill models, slippage, commissions, instrument economics, position closing, realized P&L, restart recovery, idempotent retries, multiple accounts, multiple strategy versions, account isolation, immutable ledger enforcement, and runtime-token deny-by-default behavior.

## Explicit limitations and next decisions

- Current fills operate on canonical bar ranges; intrabar ordering remains subject to the Phase 14 ambiguity model and must not be treated as tick-accurate.
- Available quantity is an explicit simulation input, not a claim of historical queue position or market depth.
- Partial fills, latency, commission, and slippage policies need empirical calibration and versioning per instrument/provider.
- Open-position mark-to-market equity is not yet included in cash performance; realized performance and commissions are authoritative in this phase.
- Portfolio overlap, correlation limits, account health, stale-feed vetoes, emergency stops, and operator approvals belong to Phase 19 governance.
- Continuous forward operation and historical-expectation comparison belong to Phase 20.

The sandbox is an auditable research environment, not permission or infrastructure for autonomous real-money execution.
