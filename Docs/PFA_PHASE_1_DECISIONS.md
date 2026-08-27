# Phase 1 Foundation Decisions

Phase 1 adds versioned instrument, contract-resolution, continuous-series, and session contracts without migrating legacy FVG consumers or changing existing APIs.

## Reviewed instrument definitions

The initial definition registry is effective 2026-08-27 and versioned `1.0.0`. Economics were checked against official CME Group contract materials linked by each definition. The registry contains MES, MNQ, GC Gold, CL WTI Crude Oil, ZN 10-Year U.S. Treasury Note, and 6E Euro FX. A definition change requires a new version and effective date.

## Compatibility behavior

`LegacyUtcTradingSessionService` names the existing UTC-calendar-day and UTC-hour bucket behavior. Its quality is always `LegacyCompatibility`; it must not be presented as an authoritative CME trading session. Existing feature analysis and cross-day services remain untouched.

Provider symbols resolve only through reviewed mappings supplied to `ContractResolver`. Unknown symbols remain explicitly unresolved. Phase 1 does not infer expiry codes or silently map root symbols to dated contracts.

## Open decisions intentionally not settled

- CME trading-date assignment and exchange-local session boundaries
- DST, holidays, early closes, and maintenance-break representation
- Contract listing and expiry calendar source
- Rollover trigger, adjusted versus unadjusted continuous prices, and back-adjustment method
- Provider-symbol reconciliation and confidence rules
- Whether Gold means GC or a smaller Gold product in later strategy research

Until these are reviewed, continuous-series definitions must use a named unresolved policy and preserve raw dated-contract prices.
