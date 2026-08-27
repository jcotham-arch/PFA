# Phase 8 — Liquidity Sweep Pattern Module

Liquidity sweeps are Pattern Module #2 and use the same canonical input and universal pattern contracts as every other detector. The module has Capture/Research maturity only; it emits factual observations and no entry, profitability, ranking, or activation decision.

Version `capture-1.0.0` compares the completed detection bar with up to 20 prior completed bars from the same explicit trading session. A penetration beyond the prior high records a buy-side liquidity sweep; penetration below the prior low records a sell-side liquidity sweep. The payload retains the reference level, extreme, depth, equal-level count/source bars, and whether the detection bar reclaimed the level. Failed reclaims are retained rather than discarded. An outside bar may factually emit both sides.

The detector supports canonical 1m, 5m, 15m, and 1h bars, rejects future/incomplete/conflicted inputs through the shared contract, and never uses later bars to confirm a prior swing. Historical replay and incremental invocation produce the same deterministic identity.

The minimum penetration in this initial capture definition is any strict break beyond the reference. Tick-normalized research thresholds remain a future versioned definition; they must not silently alter `capture-1.0.0`.

The active session model is still the Phase 1 legacy UTC compatibility calendar. Cross-session prior-day liquidity must be introduced later as an explicit session-reference observation, not silently mixed into this detector.
