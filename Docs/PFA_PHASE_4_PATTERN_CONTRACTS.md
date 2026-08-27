# Phase 4 Generalized MarketPattern Contracts

Phase 4 defines detector, context, result, observation, lifecycle, geometry, module-definition, and registry boundaries without changing the legacy FVG algorithms or persistence path.

## Contract guarantees

- Detection receives an explicit point-in-time canonical-bar context.
- Future bars, unsupported resolutions, incomplete/invalid data, unresolved instruments, and provider conflicts are rejected before detection.
- Universal observation identities are deterministic across case-normalized equivalent inputs.
- Pattern geometry remains strongly typed; detectors are not forced into anonymous value bags.
- Module inventory is additive and diagnostic only. It cannot activate a strategy.

The registry lists FVG as the existing legacy operational module and explicitly defers its universal adapter to Phase 5. A test-only second detector proves that the contracts do not depend on any FVG model.

## Rollback

Stop registering the module registry and remove the additive inventory endpoint. Existing FVG detection, replay, research, evidence, APIs, and database records continue unchanged.
