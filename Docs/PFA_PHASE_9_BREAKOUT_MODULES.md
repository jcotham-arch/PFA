# Phase 9 — Breakout and Failed-Breakout Modules

Phase 9 adds two distinct Capture/Research modules over a demonstrated shared prior-range calculation. Neither module is a strategy or profitability claim.

- `range-breakout/capture-1.0.0` requires penetration and a completed close beyond the same-session prior range.
- `failed-breakout/capture-1.0.0` requires penetration and a completed close back inside the prior range.

Both retain range boundaries, break extreme, close, excursion depth, source bars, direction of the boundary excursion, and deterministic identity. They support 1m, 5m, 15m, and 1h canonical bars and inherit common quality/future-data rejection.

A liquidity sweep and a failed breakout may describe the same market bar under their separate definitions. Both observations remain available with distinct identities. Phase 9 does not force a preferred label or collapse evidence. Outside bars can record failed breaks at both boundaries.

Ranges never silently cross the currently assigned trading-session boundary. The legacy UTC compatibility-calendar limitation remains explicit.
