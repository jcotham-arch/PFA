# Phase 20 — Forward Sandbox Operation

## Outcome

Phase 20 adds continuous, durable forward campaigns that compare prospective sandbox results with frozen walk-forward expectations. Historical research and validation records remain unchanged. Forward evidence can suspend a campaign and strategy scope; it can never promote or activate a strategy.

The monitor treats operational-data failure separately from strategy degradation. Missing health samples, stale/unhealthy feed periods, reconnect gaps, or an unclosed session produce `OperationallyInvalid` evidence and a safe suspension—not a false claim that the strategy's economics degraded.

## Frozen expectations

A campaign must reference an exact Phase 16 walk-forward report ID and content hash whose status is `Stable` and whose activation flag is false. Frozen values must exactly match the report's weighted expectancy, sample-weighted win rate, and worst fold drawdown.

The expectation also versions:

- fixed risk dollars used to normalize forward P&L into R;
- minimum forward trade count;
- health-sample interval and minimum operational coverage;
- minimum expectancy retention;
- maximum win-rate decline;
- maximum drawdown multiple.

Campaign creation verifies the sandbox account, instance, strategy ID, and version. Starting requires a running sandbox instance, an effective governance policy, active account and strategy approvals, and no emergency stop.

## Continuous evidence

The hosted monitor resumes all durable `Running` campaigns after restart. Each minute it:

1. records versioned health telemetry in the campaign's configured time bucket;
2. checks whether the prior UTC compatibility session has closed;
3. builds one immutable closed-day snapshot if one does not already exist;
4. compares all forward snapshots with the frozen expectation;
5. safely suspends degraded or operationally invalid campaigns.

Health telemetry retains healthy/stale state, last market event, last health check, reconnect attempt, and health message. Coverage is calculated against expected sampling intervals, so application downtime cannot masquerade as healthy coverage.

Daily snapshots retain trades, wins/losses, gross P&L, commissions, net P&L, normalized expectancy R, win rate, drawdown R, health/reconnect counts, operational coverage, session closure, known time, and sandbox ledger sequence lineage. Future-known snapshots and sessions that have not closed are rejected.

## Comparison outcomes

- `Accumulating` — operations are sufficient but the frozen minimum trade count has not been met.
- `Stable` — forward expectancy, win rate, and drawdown remain inside frozen thresholds.
- `Degraded` — sufficient evidence breaches an economic threshold.
- `OperationallyInvalid` — session/health coverage is insufficient; economic performance is not classified.

Both `Degraded` and `OperationallyInvalid` append an automatic campaign suspension, a strategy-version governance suspension, a forward incident, and a governance incident. Existing evidence is retained.

## Durable storage

- `ForwardCampaigns`
- `ForwardCampaignEvents`
- `ForwardHealthSamples`
- `ForwardDailySnapshots`
- `ForwardComparisons`
- `ForwardIncidents`

Campaign definitions, daily snapshots, and comparisons are immutable. Campaign lifecycle changes are append-only events. Restart recovery projects the latest lifecycle state and original start time.

## Authenticated additive API

- `GET /api/forward-campaigns/capabilities`
- `POST /api/forward-campaigns`
- `GET /api/forward-campaigns`
- `GET /api/forward-campaigns/{campaignId}`
- `POST /api/forward-campaigns/{campaignId}/start`
- `POST /api/forward-campaigns/{campaignId}/stop`
- `POST /api/forward-campaigns/{campaignId}/health`
- `POST /api/forward-campaigns/{campaignId}/days/{tradingDate}`

Control and dashboard endpoints require the runtime-only sandbox/governance token and fail closed when it is absent.

## Verification coverage

Phase 20 tests cover stable, accumulating, expectancy, win-rate, drawdown, and operational outcomes; no-future snapshots; open-session rejection; reconnect telemetry; expected health coverage; closed-day aggregation; restart recovery; idempotent immutable records; automatic campaign suspension; governance strategy suspension; forward/governance incidents; and permanent strategy non-promotion.

## Explicit limitations

- Session closure is still the documented UTC compatibility day pending an authoritative exchange calendar.
- Current health history begins when a campaign runs; it cannot reconstruct uptime before telemetry existed.
- Realistic queue position, intrabar fill ordering, and mark-to-market equity remain governed by their existing sandbox/ambiguity limitations.
- There is still no real-broker execution route. Phase 22 may build broker-neutral pilot infrastructure, but cannot enable it automatically.
