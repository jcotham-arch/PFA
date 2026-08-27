# Phase 7 — Sequence Intelligence Foundation

Phase 7 adds a pattern-neutral sequence engine over immutable universal observations. FVG has no privileged role in the engine or built-in definition.

The engine retains ordered members, roles, transition timestamps/durations, overlapping starts, partial paths, successful paths, timeouts, explicit termination, session termination, deterministic identities, and point-in-time confidence. Observations with `KnownAtUtc` after the replay cutoff are excluded.

The initial `intraday-pattern-progression/capture-1.0.0` definition accepts every observation type through an explicit wildcard. It is a descriptive capture definition, not a strategy or profitability claim. As Phase 8 and later modules emit observations, they enter the same pipeline without ranking or bias.

Persistence is additive through `MarketSequenceDefinitions`, `MarketSequenceInstances`, `MarketSequenceMembers`, and `MarketSequenceTransitions`. Replays are idempotent and never mutate source observations.

Additive endpoints:

- `GET /api/sequences/definitions`
- `GET /api/sequences/preview/{definitionId}?asOfUtc={utc}&observationLimit=500`

Preview results are explicitly marked `IsPersisted=false`. Persistent research runs require a later run manifest and are not triggered by a read request.

The current session boundary remains the explicit legacy UTC compatibility calendar from Phase 1. No authoritative CME session semantics are claimed.
