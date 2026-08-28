# Phase 22 Owner Direction

## Recorded direction

On 2026-08-27, the product owner explicitly approved the following Phase 22 direction:

1. Initial certification and execution work is paper/simulation only.
2. No real capital may be used until paper certification passes the independent evidence and governance gates.
3. The initial bounded pilot instrument is MES with a maximum quantity of one micro contract.

These constraints are authoritative upper bounds. Later decisions may narrow them but may not silently expand them.

## What this approval does not authorize

This direction does not approve a broker, install execution credentials, open or fund an account, route an order, activate a strategy, settle CME session or rollover semantics, or complete the eleven-topic live-pilot design review. Subscription purchase never grants trading authority.

## Remaining details before a design topic can be approved

- Exact paper/certification execution provider and its official API/certification terms
- Pilot account identity, maximum exposure, daily loss, drawdown, and duration
- MES contract rollover, exchange session, maintenance, and holiday rules
- Permitted order types, time in force, bracket/OCO ownership, and cancel/replace behavior
- Credential custody, operational authentication, idempotency, reconciliation, partial/rejected fill, kill-switch, and incident ownership
- Stable immutable walk-forward and nonzero forward-sandbox evidence for the exact same strategy version
