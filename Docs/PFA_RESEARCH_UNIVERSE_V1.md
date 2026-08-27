# PFA Research Universe V1

The research universe is futures-native and versioned. Instrument registration does not imply that historical data has been collected or that a prop firm permits the product.

## Core execution research

- MES — Micro E-mini S&P 500
- MNQ — Micro E-mini Nasdaq-100
- MYM — Micro E-mini Dow
- M2K — Micro E-mini Russell 2000

## Commodities and metals

- GC — Gold
- SI — Silver
- HG — Copper
- CL — WTI Crude Oil
- NG — Henry Hub Natural Gas
- ZC — Corn
- ZW — Chicago SRW Wheat
- ZS — Soybeans

## Rates and currencies

- ZN — 10-Year U.S. Treasury Note
- 6E — Euro FX
- 6B — British Pound
- 6J — Japanese Yen
- 6A — Australian Dollar

Silver, additional currency futures, Micro Dow, Micro Russell, Copper, Natural Gas, Corn, Wheat, and Soybeans were added in instrument-definition version 1.1.0 using CME specification references. Spot forex is not silently combined with CME currency futures because its venue, price formation, sessions, and provider semantics differ.

Before long-range collection, every root requires reviewed provider symbols, dated-contract mappings, rollover policy, session calendar, availability/cost estimate, and prop-firm eligibility. The first automated campaign will prove the process on rollover-aware MES before expanding in controlled waves.
