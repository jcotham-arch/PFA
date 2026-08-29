# Advanced Strategies — Current PFA Integration Packet

**Snapshot date:** August 29, 2026  
**Intended recipient:** Sam / the independent Advanced Strategies development task  
**PFA contract:** `partner-contract-1.0.0`

## Executive instruction

Build Advanced Strategies independently as a versioned HTTPS service. Do not copy the private strategy algorithm into PFA, connect directly to the PFA SQLite database, or implement broker routing. Sam can develop and test the module on his own. When it is ready, PFA will validate its manifest and API contract, attach it as a research-only connector, ingest its candidate ideas into the existing evidence pipeline, and determine where those ideas belong based on measured results rather than their source.

The complete original handoff remains in `Docs/PFA_ADVANCED_STRATEGIES_HANDOFF_FOR_SAM.md`. This packet adds the current live system state and the exact landing path created since that document was written.

## What PFA already has

### Market and research foundation

- .NET 10 ASP.NET Core application with append-only SQLite research records.
- Canonical bar and point-in-time event contracts with revisions, quality flags, known times, and source lineage.
- MES is the primary instrument. Current registered MES data includes 136,248 bars in the application dashboard.
- Active detection for FVGs, liquidity sweeps, market structure, range breakouts, failed breakouts, and pullback/continuation observations.
- Universal observations and outcomes, ordered market sequences, cross-day evidence, actionability research, trade-journal alignment, and agent-training datasets.
- Supported pattern source timeframes are generally 1m, 5m, 15m, and 1h; the current qualifying pullback lane has produced evidence only from 5m and therefore remains coverage-blocked.

### MES Adaptive Scenario Lab

- Generation 2 is active.
- Current development champion: pullback-continuation, directional-confirmation entry, opposite-range invalidation, fixed target/time exit, `0.50R` target, 60-minute maximum hold.
- Evidence: 54 resolved trades across 40 distinct resolved days.
- Development mean: `+0.020241R`; profit factor: `1.072945`.
- Current status: `AwaitingDevelopmentEvidence` because qualifying evidence covers only the 5m source timeframe.
- Blind-test outcomes cannot rewrite the version being tested.
- Controlled mutations become new immutable challenger versions.

The first development-only challenger replay produced:

| Mutation | Train | Validation | Decision |
|---|---:|---:|---|
| Target reduced to `0.25R` | `+0.098R`, PF `2.06`, n=43 | `-0.024R`, PF `0.87`, n=11 | Rejected |
| Hold reduced to 45 minutes | `+0.009R`, PF `1.03`, n=43 | `-0.030R`, PF `0.90`, n=11 | Rejected |
| Break-even after `0.5R` | `+0.014R`, PF `1.05`, n=43 | `+0.044R`, PF `1.15`, n=11 | Development survivor |

No Test-partition samples were generated or read by these tuning runs. Separately, four previously frozen Tier 1 variants have eight blind executions, all currently negative. That blind result remains audit evidence and does not leak back into the development selection process.

### Sandbox and safety

- Tier 1 Incubator admits development-qualified MES ideas earlier.
- Tier 2 Proving Ground retains strict validation, execution realism, and prop-firm rules.
- One through five MES contract economics are modeled separately.
- Strategy activation and real broker routing are disabled.
- No connector, subscription, payment, or partner result can bypass validation or safety gates.

### Agent status

PFA has immutable point-in-time dataset builders, feature families, chronological Train/Validation/Test splits, baseline and hurdle training services, ablations, and promotion gates. This is a research-learning foundation, not an autonomous live trading agent. Advanced Strategies should produce traceable research evidence that can become features or candidate labels later; it must not claim to train or activate PFA's agent directly.

## Where Advanced Strategies plugs in

```text
Sam's independent Advanced Strategies service
  │
  ├─ /.well-known/pfa-module
  ├─ /health
  ├─ /v1/analysis
  └─ /v1/research/candidates
          │ versioned HTTPS + scoped credentials
          ▼
PFA partner compatibility boundary
  │ validate identity, version, scopes, chronology, idempotency and authority
  ▼
Partner observation/candidate adapter
  │ convert accepted findings into immutable PFA research records
  ▼
Pattern/sequence evidence → Adaptive Scenario Lab → Tier 1 → blind replay
  │
  └─ only a separately frozen survivor may approach Tier 2 certification
```

PFA determines placement by output type:

| Advanced Strategies output | PFA destination |
|---|---|
| Structural or contextual fact | Universal market observation / context feature |
| Multi-event progression | Market sequence member or sequence context |
| Entry/stop/target idea | Development-only pattern-trade hypothesis |
| Fully specified strategy candidate | Adaptive Scenario Lab challenger |
| Explanation only | Research evidence attached to the source decision clock |
| Unsupported, future-known, or non-reproducible result | Rejected with a recorded reason |

Advanced Strategies is not required to use PFA terminology internally. Its external response must map each result into explicit, versioned fields with exact times and evidence references.

## Contract Sam should implement

### Manifest

```json
{
  "moduleId": "advanced-strategies",
  "displayName": "Advanced Strategies",
  "moduleVersion": "1.0.0",
  "contractVersion": "partner-contract-1.0.0",
  "integration": "ExternalApi",
  "capabilities": [
    "market-context-analysis",
    "strategy-candidate-generation",
    "research-explanation"
  ],
  "supportedInstruments": ["MES"],
  "supportedTimeframes": ["1m", "5m", "15m", "1h"],
  "requiredDataScopes": [
    "canonical-bars:read",
    "market-observations:read",
    "market-sequences:read"
  ],
  "canActivateStrategy": false,
  "canRouteToRealBroker": false,
  "contentHash": "DETERMINISTIC_HASH"
}
```

PFA now has a compiled compatibility validator for this manifest. During local development it is available at:

```text
GET  /api/product/modules/advanced-strategies/integration-packet
POST /api/product/modules/advanced-strategies/compatibility
```

The compatibility endpoint returns HTTP 200 for a compatible manifest and HTTP 422 with machine-readable issues when identity, contract version, required capabilities/timeframes, scopes, or safety authority do not match.

### Analysis request requirements

Every request must include:

- stable request ID and idempotency key;
- exact contract version;
- `MES` plus an explicit dated contract ID;
- one of `1m`, `5m`, `15m`, or `1h`;
- exact `asOfUtc` decision time;
- canonical data revision and immutable bar references;
- observation and sequence references supplied to the module;
- only point-in-time features known by `asOfUtc`;
- a reference to a short-lived entitlement assertion, not billing data;
- trace ID;
- false strategy-activation and broker-routing declarations.

### Analysis response requirements

Every response must include:

- stable analysis and request IDs;
- module and contract versions;
- echoed instrument, contract, timeframe, decision clock, and data revision;
- explicit `TRADE_CANDIDATE`, `CONTEXT_ONLY`, or `NO_TRADE` decision;
- zero or more candidates with pattern family, direction, optional entry/stop/target/hold, explanation, and evidence references;
- assumptions, exclusions, and rejection reasons;
- deterministic content hash;
- `canActivateStrategy: false` and `canRouteToRealBroker: false`.

## Chronology and evaluation rules

1. Identical inputs and versions must produce identical results.
2. A bar closing after `asOfUtc` must be rejected.
3. A feature, observation, sequence, outcome, or label known after `asOfUtc` must be rejected.
4. Training, validation, and blind testing must be chronological.
5. A parameter may change only in a new version; never modify a frozen version during evaluation.
6. Initial comparisons should change one primary variable at a time so causality remains interpretable.
7. Success must repeat across distinct days, sessions, and relevant timeframes.
8. Training improvement with validation deterioration is rejection evidence, not progress.
9. PFA owns final Sandbox placement, promotion, termination, and safety decisions.

## What Sam can work on without PFA access

Sam can immediately build:

- independent service and UI;
- manifest and health endpoints;
- versioned request/response models;
- his private strategy logic;
- deterministic analysis and candidate generation;
- local fixtures representing MES 1m/5m/15m/1h inputs;
- future-data rejection, idempotency, compatibility, timeout, and `NO_TRADE` tests;
- deployment and rollback documentation.

He does not need the PFA database or repository to do this. For eventual integration he should deliver the repository/package, its OpenAPI document, sample requests/responses, automated test results, module base URL, and a list of unresolved assumptions.

## Acceptance sequence when Sam hands it back

1. Run the manifest through PFA's compatibility validator.
2. Review threat model, scopes, secrets, and independent deployment boundary.
3. Execute deterministic contract fixtures.
4. Connect a read-only local adapter with MES data only.
5. Import output as research records with complete lineage.
6. Run chronological development comparisons across days and timeframes.
7. Freeze a survivor before opening a later untouched blind window.
8. Admit to Tier 1 only if the evidence supports it.
9. Keep all strategy activation and broker routing disabled.

## Copy/paste prompt for Sam's independent task

> Build Advanced Strategies as an independently deployable HTTPS service compatible with PFA `partner-contract-1.0.0`. Use module ID `advanced-strategies`, customer name `Advanced Strategies`, support MES on 1m/5m/15m/1h, and expose a deterministic capability manifest, health endpoint, point-in-time analysis endpoint, and research-candidate endpoint. Preserve your private algorithms behind the API. Reject future-known inputs, conflicting idempotency keys, unsupported versions/scopes, and invalid contract identities. Return explicit `NO_TRADE` outcomes and complete evidence references. Do not connect directly to the PFA database, activate strategies, route broker orders, store billing data, or assume that subscription entitlement grants safety authority. Deliver OpenAPI, DTOs, fixtures, automated contract/safety tests, deployment instructions, rollback instructions, and unresolved assumptions. The receiving PFA system will independently validate, adapt, research, sandbox, and decide placement of your outputs.
