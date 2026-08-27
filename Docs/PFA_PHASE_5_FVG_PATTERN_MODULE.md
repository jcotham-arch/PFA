# Phase 5 FVG Pattern Module #1

Phase 5 adapts canonical bars into the unchanged legacy `Candle` model, runs the existing `FvgDetectionService`, and maps the result into a universal, source-linked market-pattern observation.

## Compatibility guarantees

- The legacy FVG detector, tracking, replay, scenario, feature, discovery, evidence, and validation services are unchanged.
- Universal geometry, direction, formation time, and observation identity match the legacy result.
- The adapter supports the preserved 5m behavior only; new resolution semantics are not inferred.
- Existing FVG persistence also writes an idempotent universal reference. Legacy observations remain authoritative and are not deleted or rewritten.
- Universal observations record the three canonical source-bar IDs and inherit context quality.

## Rollback

Stop registering `FvgPatternModule` and stop reading `UniversalPatternObservationReferences`. The legacy FVG APIs, tables, and algorithms continue unchanged.
