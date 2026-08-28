# PFA Modular Product Architecture

## Objective

PFA is a composable product. Core intelligence, Agent Research Lab, Live Agent, Advanced Strategies, Prop Firm Coaching, and Bring Your Own Agent are independently versioned modules. A customer may subscribe to one or more modules. Entitlement permits product access but never grants strategy promotion, governance approval, or broker-routing authority.

## Module contract

Each module has an immutable ID, customer-facing name, semantic version, integration type, subscription SKU, paid-entitlement requirement, safety-gate requirement, optional partner ownership metadata, capability/data-scope manifest, and permanent live-routing declaration.

Native modules run inside PFA. External partner modules connect through HTTPS APIs and versioned manifests. External code does not receive direct database access and is not loaded into the PFA process as an arbitrary plugin.

## Initial catalog

| Module ID | Name | Integration | Access |
|---|---|---|---|
| `pfa-core` | PFA Market Intelligence | Native | Included |
| `agent-research-lab` | PFA Agent Research Lab | Native | Paid |
| `live-agent` | PFA Live Agent | Native, design-gated | Paid plus safety authorization |
| `advanced-strategies` | Advanced Strategies | External API | Paid |
| `prop-firm-coaching` | Prop Firm Coaching | Native | Paid |
| `custom-agent-access` | Bring Your Own Agent | External API | Paid plus safety authorization |

## Activation states

- `Locked`: no valid matching entitlement.
- `Available`: entitled but disabled by the customer.
- `Active`: entitled, enabled, compatible, and healthy within current safety boundaries.
- `Suspended`: connector unavailable, incompatible, cancelled, or operationally unhealthy.
- `SafetyBlocked`: an independent evidence/governance requirement is not satisfied.

Entitlements are matched by customer, exact subscription SKU, status, and effective interval. Client-side flags cannot create access. Payment-provider webhook ingestion and authenticated user identity remain future integrations and are reported as unconfigured until implemented.

## Agent learning boundary

The agent-training contract consumes immutable point-in-time examples containing explicit instrument/contract/timeframe, event time, feature-known time, decision time, outcome-known time, numeric features, pattern modules, sequence roles, outcome in R, and source revision. Dataset construction rejects future-known labels, predictor leakage, duplicate identities, or results evaluated after the dataset clock.

The Agent tab reports corpus and eligible R-labeled example counts. Supervised training remains gated until at least 100 chronologically valid R-labeled examples span 90 days. This threshold permits research only; it does not activate a strategy or live route.

## Commerce and safety separation

Billing decides entitlement. PFA module activation additionally checks user intent, connector health, compatibility, and any independent safety gate. Live trading—if ever authorized—is controlled by separate operational authentication, evidence, governance, reconciliation, and kill-switch systems. A paid subscription cannot override those controls.
