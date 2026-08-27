# Phase 10 — Immutable Strategy Registry

Phase 10 separates versioned strategy proposals from patterns, sequences, evidence, validation, sandbox operation, governance, and execution. A pattern observation is never itself permission to trade.

Each immutable definition includes family/version identity; environment; direction policy; entry, stop, target, management, risk and abstention (`NO TRADE`) definitions; supported instruments/sessions; typed pattern/sequence/context requirements; evidence links; discovery/validation dataset references; author; compatibility source; and the full engine-version manifest required by the architecture.

Material changes under an existing `(StrategyId, StrategyVersion)` are rejected and require a new version. Requirements, evidence links, and lifecycle events are additive and auditable. The Phase 10 lifecycle permits only:

- Draft -> FrozenResearch or Rejected
- FrozenResearch -> ValidationPending or Rejected
- ValidationPending -> ValidationComplete or Rejected
- ValidationComplete -> Rejected

Sandbox eligibility/activation and live-pilot eligibility/activation are explicitly unauthorized. The public API is read-only and reports that it cannot register, activate, or place trades.

`FrozenFvgStrategyAdapter` preserves the legacy frozen-candidate path as one compatibility input. It is not the core model and receives no preference over other pattern or sequence requirements.

Additive tables: `StrategyDefinitions`, `StrategyRequirements`, `StrategyEvidenceLinks`, and `StrategyLifecycleEvents`.

Additive endpoints:

- `GET /api/strategies`
- `GET /api/strategies/{strategyId}/versions/{strategyVersion}`
- `GET /api/strategies/capabilities`

Rollback is to stop using the registry tables. Existing candidate discovery, frozen validation, scenarios, APIs, and permanent `CanActivateStrategy=false` behavior remain unchanged.
