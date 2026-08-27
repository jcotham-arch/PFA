# Phase 19 — Risk and Governance

## Outcome

Phase 19 separates sandbox trade proposals from authorization. A signal cannot reach `SandboxService` without a short-lived cryptographic permit tied to the exact governance decision, account, instance, and signal. Permits are issued only for an authorized `Sandbox` destination; governance can never authorize a real broker.

Missing, stale, unhealthy, expired, suspended, over-limit, or future-dated evidence denies by default. Sandbox and governance controls remain protected by the runtime-only `Sandbox:ControlToken`; no secret was added to repository configuration.

## Authorization inputs

Every proposal is evaluated against:

- one immutable effective policy version;
- active account and frozen-strategy-version approvals;
- global/account/strategy/instrument suspensions;
- the latest emergency-stop state;
- feed health, staleness, last-event time, and health-check age;
- account cash health;
- proposal decision latency;
- UTC-day realized P&L and commissions;
- current drawdown;
- current and resulting open risk;
- current and resulting per-instrument contracts;
- current and resulting correlated exposure;
- allowed instruments and a versioned correlation-group map.

Risk-reducing orders are evaluated on resulting exposure rather than blindly adding their quantity. Market orders without a price/stop pair use the policy's conservative fallback risk per contract. Limit/stop evidence uses instrument point value when both prices are supplied.

## Default-deny vetoes

The engine records all applicable reasons, including:

- missing or invalid policy, health, or risk evidence;
- unhealthy/stale feed or stale health check;
- excessive decision latency or future-dated evidence;
- unhealthy account;
- missing, revoked, or expired approvals;
- emergency stop or active scoped suspension;
- daily loss, drawdown, open-risk, position, and correlated-exposure limits;
- unsupported instrument;
- any non-sandbox destination.

An authorization is evidence for one short-lived sandbox submission only. It is not strategy promotion and cannot route to live execution.

## Durable audit model

Additive tables retain:

- `GovernancePolicies`
- `GovernanceApprovalEvents`
- `GovernanceSuspensionEvents`
- `GovernanceEmergencyStopEvents`
- `GovernanceDecisions`
- `GovernanceIncidents`

Policies and decisions are immutable; SQLite triggers forbid update/delete. Approvals and suspensions use append-only grant/revoke and suspend/resume events. Emergency stops produce critical incidents. Decision evidence contains policy hash, approvals, active suspensions, emergency state, health, risk, engine version, and the complete action request.

## Additive authenticated API

- `GET /api/governance/capabilities`
- `POST /api/governance/policies`
- `GET /api/governance/policies/effective`
- `POST /api/governance/approvals`
- `POST /api/governance/approvals/revoke`
- `GET /api/governance/approvals`
- `POST /api/governance/suspensions`
- `POST /api/governance/suspensions/resume`
- `GET /api/governance/suspensions`
- `POST /api/governance/emergency-stop`
- `GET /api/governance/emergency-stop`
- `GET /api/governance/decisions/{accountId}`
- `GET /api/governance/incidents`

All state/control endpoints require `X-PFA-Sandbox-Control`. With no runtime token, they fail closed. Sandbox signal submission now calls `GovernedSandboxService`; a veto returns the audited decision and creates no order.

## Verification coverage

Phase 19 tests cover healthy authorization, non-sandbox denial, missing/invalid inputs, every feed/health/latency veto, future evidence, account and strategy approvals, expiry, all suspension scopes, emergency stop, daily loss, drawdown, open risk, per-instrument size, correlated exposure, unsupported instruments, risk-reducing exposure, permit binding/expiry/forgery, durable approval and suspension reconstruction, policy/decision immutability, incidents, and an end-to-end governed sandbox submission followed by an emergency-stop veto.

## Explicit limitations

- Daily P&L currently uses UTC day boundaries because the authoritative exchange calendar remains unresolved.
- Correlation groups and fallback risk are policy inputs that require empirical review and versioning.
- Phase 19 governs the virtual sandbox only. There is still no real-broker adapter or authorization path.
- Continuous stale-feed suspension, incident response, forward-vs-historical degradation, and restart operations belong to Phase 20.
- Prop-firm-specific trailing drawdown and challenge rules remain a later dedicated rules/account layer; they must not be approximated silently in the generic governor.
