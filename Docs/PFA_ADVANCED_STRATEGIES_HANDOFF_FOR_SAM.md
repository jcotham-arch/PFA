# Advanced Strategies — PFA Integration Handoff for Sam

## How to use this document

This is a standalone build brief for the developer responsible for **Advanced Strategies**. It may be pasted into a new ChatGPT or Codex task as project context. The customer-facing product name is **Advanced Strategies**. Developer ownership must not be embedded in the public module ID or display name.

For the latest live MES research results, compiled compatibility endpoint, and exact current landing path, also provide `Docs/PFA_ADVANCED_STRATEGIES_CURRENT_INTEGRATION_PACKET.md`.

## Product being built

Prop Firm Assassins (PFA) is a modular futures market-intelligence, research, prop-firm certification, and eventually tightly governed trading-assistance platform. Its core objective is to turn point-in-time market data into reproducible pattern, sequence, strategy, and risk evidence that helps traders pass futures prop-firm challenges and qualify for payouts without overstating backtest performance.

PFA currently includes:

- a .NET 10 ASP.NET Core application with SQLite persistence;
- an interactive browser UI and independent certification-sandbox UI;
- a versioned instrument universe covering equity-index, metal, energy, rates, FX, and agriculture futures;
- canonical/legacy market-data ingestion with Massive futures data;
- FVG, liquidity-sweep, range-breakout, and failed-breakout detection;
- universal observations, outcomes, sequence instances, research hypotheses, cross-day and cross-market evidence;
- walk-forward validation, order-flow foundations, virtual sandbox ledgers, governance, forward campaigns, and machine-discovery research;
- a $50,000 conservative prop-firm certification model with realistic latency, spread, slippage, partial fills, commission, drawdown, consistency, and payout gates;
- a subscription-oriented module catalog and Agent & Module Center;
- no live broker route, live execution credential, real-capital authority, or automatic strategy activation.

The current registered research universe is MES, MNQ, GC, CL, ZN, 6E, SI, 6B, 6J, 6A, MYM, M2K, HG, NG, ZC, ZW, and ZS. PFA is collecting explicit dated-contract data for this universe. A dated contract must never be presented as a continuous future unless a versioned rollover policy is supplied.

## Current PFA interoperability contracts

Advanced Strategies should model its adapters around these current PFA concepts, without coupling directly to controller URLs or SQLite tables:

- `InstrumentDefinition`: instrument/root identity, exchange, asset class, tick size, point value, supported resolutions, definition version, and effective date.
- `CanonicalBar`: instrument and contract identity, timeframe, OHLCV, event/known times, revision, correction state, data-quality flags, and source lineage.
- `UniversalMarketObservation`: module/version, pattern type, instrument/contract/timeframe, direction, formation time, `KnownAtUtc`, lifecycle, geometry payload, source references, quality flags, and content hash.
- `UniversalMarketOutcome`: observation identity, evaluator version, evaluated-through time, sample count, metrics, chronological events, and quality flags.
- `MarketSequenceInstance`: definition/version, instrument/contract/timeframe/session, state, ordered members, transitions, duration, and point-in-time confidence.
- `AgentTrainingExample`: observation time, known time, label-available time, feature JSON, label JSON, split, source revision, and content hash.

Current PFA read projections include the instrument registry, pattern-module inventory, universal observations/outcomes, market coverage, sequence definitions/instances, product-module catalog, agent-training readiness, certification dashboard, and the multi-asset learning summary. Mutation/replay endpoints remain local-development-only and are not partner contracts.

The generic research outcome layer reports fixed-horizon directional close change, maximum favorable excursion, and maximum adverse excursion in points, instrument-native ticks, and USD per contract. These are measurements, not entry/stop/target recommendations and not realized R. Advanced Strategies must preserve that distinction.

## Where Advanced Strategies fits

Advanced Strategies is an independently developed, subscription-gated partner module. It must be usable in either of two modes:

1. As an independent product with its own UI and service lifecycle.
2. As a module connected to PFA through a versioned external API contract.

PFA owns customer entitlement decisions, governed data access, module activation state, and system-level safety. Advanced Strategies owns its strategy-specific algorithms, internal intellectual property, module UI, and its contract-compliant output.

The PFA module identity is:

```text
moduleId: advanced-strategies
displayName: Advanced Strategies
integration: ExternalApi
subscriptionSku: PFA-ADVANCED-STRATEGIES
initialContractVersion: partner-contract-1.0.0
```

Subscription entitlement permits access only. It never permits live order routing, strategy promotion, access to unrelated customer information, or bypass of evidence and governance gates.

## Recommended integration architecture

Build Advanced Strategies as a separate service with a narrow HTTPS API. Do not copy its private algorithm into PFA and do not give it direct access to the PFA database. PFA should communicate through explicit versioned DTOs and scoped service credentials.

Required separation:

```text
PFA application
  ├─ entitlement and activation decision
  ├─ governed market/evidence data projection
  ├─ audit and safety gate
  └─ Advanced Strategies API client
          │ HTTPS + versioned contract
          ▼
Advanced Strategies service
  ├─ capability manifest
  ├─ health/compatibility endpoint
  ├─ strategy analysis engine
  ├─ independently deployable UI/API
  └─ no direct broker or PFA database access
```

## Required module manifest

Expose a read-only endpoint such as `GET /.well-known/pfa-module` returning a signed or content-hashed manifest:

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
  "canRouteToRealBroker": false,
  "canActivateStrategy": false,
  "schemaReferences": {},
  "contentHash": "..."
}
```

Manifest rules:

- Module ID is immutable.
- Module and contract versions use explicit semantic versions.
- Capabilities and data scopes are allowlists.
- Unknown versions or expanded scopes fail closed.
- `canRouteToRealBroker` and `canActivateStrategy` must be false.
- Manifest content is deterministic and hashable.

## Recommended API surface

Start with these endpoints:

```text
GET  /.well-known/pfa-module
GET  /health
POST /v1/analysis
GET  /v1/analysis/{analysisId}
POST /v1/research/candidates
GET  /v1/research/candidates/{candidateId}
```

`POST /v1/analysis` should accept an immutable point-in-time request containing:

- request ID and idempotency key;
- module/contract version;
- instrument and explicit contract ID;
- timeframe;
- `asOfUtc` decision clock;
- canonical bar references and data revision;
- pattern observation IDs/revisions;
- sequence instance IDs/versions;
- market-state/feature values with `KnownAtUtc`;
- PFA entitlement assertion reference, never raw billing information;
- trace/correlation ID.

The response should contain:

- stable analysis ID and content hash;
- exact module/engine version;
- received data revision and `asOfUtc`;
- structured findings and explanations;
- candidate entry/stop/target only when the module contract supports them;
- confidence/calibration metadata;
- assumptions, exclusions, and rejection reasons;
- `NO TRADE` as an explicit valid outcome;
- `canRouteToRealBroker: false` and `canActivateStrategy: false`.

## Data and chronology rules

Every analysis must be reproducible from immutable inputs. The module must reject:

- bars closing after `asOfUtc`;
- features or observations known after the decision clock;
- outcomes or labels leaked into predictors;
- incomplete/invalid OHLC data;
- unresolved instruments or contracts;
- unresolved provider conflicts;
- duplicate idempotency keys with different content;
- unsupported contract, timeframe, schema, or module versions.

Training and evaluation data must use temporal splits, embargoes where appropriate, frozen data revisions, deterministic seeds, and separate evaluation windows. A profitable training result is research, not activation authority.

## Entitlement and activation behavior

PFA evaluates the customer's subscription entitlement. Advanced Strategies should receive only a short-lived, signed entitlement assertion containing the minimum claims needed to serve that request:

```text
subject/customer reference
moduleId = advanced-strategies
subscription SKU
entitlement status
effective/expiry time
allowed data scopes
nonce/request audience
```

Do not accept a client-side boolean such as `isPaid=true`. Do not store card data or depend on the PFA browser to enforce access. Expired, cancelled, revoked, wrong-user, wrong-SKU, or unverifiable assertions must fail closed.

Module activation states are:

- `Locked` — no active paid entitlement;
- `Available` — entitled but user has not enabled it;
- `Active` — entitled, enabled, compatible, and healthy;
- `Suspended` — connector unhealthy/incompatible;
- `SafetyBlocked` — a required independent safety gate is not satisfied.

## Security boundary

- Use HTTPS and service-to-service authentication.
- Keep partner credentials outside source control and UI configuration.
- Use least-privilege scopes and rotate/revoke credentials.
- Never log access tokens, secrets, full entitlement assertions, or unnecessary customer data.
- Apply request size, timeout, retry, and rate limits.
- Use durable idempotency for mutation-like analysis requests.
- Maintain immutable audit references without exposing private algorithm internals.
- Treat PFA responses, browser input, and external market data as untrusted until validated.

## Resilience behavior

- Health must distinguish live, ready, degraded, and incompatible.
- Timeouts and connector failures suspend the module; PFA continues operating without it.
- Retries must not duplicate analysis records.
- Same ID/same content is idempotent; same ID/different content is a conflict.
- PFA must be able to disable the connector without redeploying Advanced Strategies.
- Version incompatibility fails closed with a useful machine-readable reason.

## Required tests before integration

1. Manifest identity, hashing, schema, and version compatibility.
2. Entitlement allow/deny cases, including expiry, revocation, wrong user, and wrong SKU.
3. Point-in-time leakage and future-bar rejection.
4. Instrument, contract, timeframe, and data-quality validation.
5. Idempotency and conflicting duplicate requests.
6. Timeout, retry, connector outage, and recovery.
7. Deterministic replay from identical inputs.
8. `NO TRADE` output and explanation completeness.
9. No strategy activation or broker-routing capability.
10. API backward compatibility for supported contract versions.
11. Rate limiting and audit-reference completeness.
12. Independent deployment and PFA-without-module degradation behavior.

## Deliverables requested from Sam

- Architecture and threat-model document
- Module manifest schema and example
- OpenAPI specification for the initial API
- Versioned request/response DTOs
- Health and compatibility contract
- Entitlement assertion validation design
- Idempotency and audit model
- Automated contract and safety tests
- Local development instructions
- Independent UI or embed/link approach
- Deployment and rollback instructions
- Explicit list of assumptions and questions requiring PFA owner decisions

## Initial development boundary

The first integration target is research and paper/simulation only, initially constrained to MES and a maximum conceptual quantity of one micro contract. No broker selection, live account, funded capital, live credential, or execution route is authorized. Advanced Strategies should focus first on producing traceable, reproducible research analyses that complement PFA's patterns, sequences, evidence, and sandbox.

## Suggested Codex task prompt

> Build the Advanced Strategies module described in this handoff as an independently deployable, versioned external API service. Begin with the capability manifest, health/compatibility endpoint, point-in-time analysis contracts, entitlement assertion boundary, idempotency, and automated safety/contract tests. Do not implement live broker routing, strategy activation, billing-card handling, or direct access to the PFA database. Preserve a clean independent-product mode and a PFA-connected mode. Stop and document any unresolved semantic or product-owner decision rather than inventing it.
