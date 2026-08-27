# Phase 22 — Mandatory Live-Pilot Design Review

## Status

Phase 22 implementation is **not authorized**. The repository contains no broker execution provider, no live credentials, no live order route, and no transition into `LivePilotEligible` or `LivePilotActive`. Existing governance continues to reject every destination other than `Sandbox`.

This record defines the decisions and evidence required before inert live-pilot infrastructure may be built. Passing the review authorizes an infrastructure design/build only; it never authorizes trading, strategy activation, credential installation, or use of real capital.

## Required decision records

Each topic requires an explicit version, accountable decision owner, UTC decision time, rationale, and durable evidence reference:

1. **Execution provider and certification** — exact broker/platform, supported futures products, paper/certification environment, API terms, and certification evidence.
2. **Credential custody and rotation** — secret store, least-privilege scopes, environment separation, rotation/revocation procedure, and audit access.
3. **Separate operational authentication** — identity roles and authorization distinct from research, dashboard, sandbox, and governance tokens.
4. **Pilot account and capital boundary** — exact account, maximum funded exposure, daily loss, drawdown, open risk, order quantity, and duration.
5. **Instrument and session allowlist** — exact contracts, rollover policy, exchange sessions, maintenance handling, and holidays.
6. **Order types and time in force** — permitted order semantics, bracket/OCO ownership, modification/cancellation rules, and unsupported behavior.
7. **Duplicate-order idempotency** — durable client order identity, retry rules, ambiguity handling, and proof that reconnect cannot duplicate exposure.
8. **Reconnect and reconciliation** — broker-authoritative state, startup sequence, fills during outage, unknown orders, position mismatch, and fail-closed criteria.
9. **Partial and rejected fill policy** — residual quantity, protective orders, rejection escalation, price/quantity rounding, and terminal-state rules.
10. **Independent kill-switch ownership** — operator roles, broker-side cancellation/liquidation semantics, heartbeat behavior, test cadence, and immutable audit.
11. **Incident response and rollback** — severity levels, notification owners, evidence preservation, credential revocation, account flattening authority, and return to `SandboxActive` or `Suspended`.

## Evidence prerequisites

The review must reference exact immutable hashes for:

- a stable Phase 16 walk-forward report;
- a stable Phase 20 forward-sandbox comparison;
- the strategy ID and version shared by both;
- a nonzero forward trade population known before the review time.

Operational coverage failure, degradation, insufficient trades, missing hashes, or future-known evidence fails closed.

## Executable gate

`LivePilotReadinessAuditor` evaluates the eleven decision topics and evidence snapshot. It reports one of:

- `DesignReviewRequired`
- `EvidenceRequired`
- `ReadyForInfrastructureBuild`

All results permanently return `CanRouteToRealBroker = false` and `CanActivateStrategy = false`. No service registration, API, database migration, provider package, secret, or execution implementation is introduced by this design-gate increment.

## Decisions still requiring the product owner

- Which broker or futures execution platform should be evaluated first.
- Whether the first certified target is paper-only or a separately funded micro pilot after certification.
- The exact account, capital-at-risk ceiling, instruments, sessions, quantity, loss/drawdown limits, and pilot duration.
- Who holds approval, incident-response, and independent emergency-stop authority.

Until those decisions and the prerequisite evidence are recorded, Phase 22 remains inert and implementation does not proceed.
