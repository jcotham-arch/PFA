using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Forward;
using PFA_FVG_Scanner.Domain.LivePilot;

namespace PFA_FVG_Scanner.Services;

public sealed record LivePilotReadinessProjection(
    LivePilotReadinessResult Gate,LivePilotEvidenceSnapshot? EvidenceCandidate,
    int RequiredDecisionCount,int ApprovedDecisionCount,string Guidance,
    IReadOnlyList<LivePilotDecisionRequirement> RequiredDecisions);

public sealed record LivePilotDecisionRequirement(string Topic,string DisplayName,string RequiredChoice,string RequiredEvidence);

public sealed class LivePilotReadinessProjectionService(PfaDatabase database,LivePilotReadinessAuditor auditor)
{
    public static readonly IReadOnlyList<LivePilotDecisionRequirement> DecisionRequirements=
    [
        new("execution-provider-and-certification","Execution provider and certification","Choose the exact futures broker/platform and paper or certification environment.","Official API terms, supported products, account eligibility, and completed certification proof."),
        new("credential-custody-and-rotation","Credential custody and rotation","Choose the secret store, least-privilege scopes, environments, and rotation/revocation owner.","Credential runbook and evidence that research/UI processes cannot read operational secrets."),
        new("separate-operational-authentication","Separate operational authentication","Name operational roles and approvals distinct from research, dashboard, sandbox, and governance access.","Role matrix plus successful allow/deny authorization tests."),
        new("pilot-account-and-capital-boundary","Pilot account and capital boundary","Set the exact account, maximum exposure, daily loss, drawdown, quantity, and pilot duration.","Signed bounded-risk record and broker/account limit evidence."),
        new("instrument-and-session-allowlist","Instrument and session allowlist","Choose exact contracts, rollover policy, exchange sessions, maintenance, and holiday behavior.","Versioned allowlist with session and rollover test evidence."),
        new("order-types-and-time-in-force","Order types and time in force","Choose permitted order types, TIF, bracket/OCO ownership, modification, and cancellation rules.","Paper-certification results for every permitted order lifecycle."),
        new("duplicate-order-idempotency","Duplicate-order idempotency","Approve durable client-order identity, retry, timeout, and ambiguous-ack rules.","Reconnect/retry drill proving no duplicate exposure."),
        new("reconnect-and-reconciliation","Reconnect and reconciliation","Choose broker-authoritative startup, outage-fill, unknown-order, mismatch, and fail-closed policies.","Restart and outage drills with complete reconciliation evidence."),
        new("partial-rejected-fill-policy","Partial and rejected fill policy","Choose residual quantity, protection, rejection escalation, rounding, and terminal-state behavior.","Paper tests covering partial, rejected, cancelled, and protective-order failures."),
        new("independent-kill-switch-ownership","Independent kill-switch ownership","Name emergency-stop owners and approve cancel/flatten, heartbeat, and test cadence.","Independent kill-switch drills and immutable audit records."),
        new("incident-response-and-rollback","Incident response and rollback","Name severity, notification, evidence, revocation, flattening, and sandbox-return owners.","Incident runbook and completed tabletop/rollback rehearsal.")
    ];

    public async Task<LivePilotReadinessProjection> GetAsync(CancellationToken token=default)
    {
        var evidence=await FindEvidenceAsync(token);var now=DateTime.UtcNow;
        var gate=auditor.Evaluate(new("CURRENT-LIVE-PILOT-READINESS",LivePilotReadinessAuditor.Version,[],evidence,now));
        return new(gate,evidence,LivePilotReadinessAuditor.RequiredTopics.Count,0,
            evidence is null
                ? "No exact strategy version currently has both stable walk-forward and stable nonzero forward evidence."
                : "Evidence prerequisites have a candidate, but all accountable design decisions remain unapproved. Live routing stays impossible.",
            DecisionRequirements);
    }

    private async Task<LivePilotEvidenceSnapshot?> FindEvidenceAsync(CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT c.CampaignJson,x.ComparisonJson FROM ForwardCampaigns c
            JOIN ForwardComparisons x ON x.CampaignId=c.CampaignId
            WHERE x.Status='Stable' ORDER BY x.ComparedAtUtc DESC;
            """;
        var candidates=new List<(string CampaignJson,string ComparisonJson)>();
        await using(var reader=await command.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token))candidates.Add((reader.GetString(0),reader.GetString(1)));
        foreach(var candidate in candidates)
        {
            var campaign=JsonSerializer.Deserialize<ForwardCampaign>(candidate.CampaignJson);
            var comparison=JsonSerializer.Deserialize<ForwardComparison>(candidate.ComparisonJson);
            if(campaign is null||comparison is null||comparison.ForwardTrades<1)continue;
            await using var evidence=connection.CreateCommand();evidence.CommandText="""
                SELECT w.ReportId,w.ContentHash FROM WalkForwardReports w
                JOIN StrategyEvidenceLinks l ON l.EvidenceId=w.ReportId
                WHERE w.ReportId=$report AND w.ContentHash=$hash AND w.Status='Stable'
                  AND l.StrategyId=$strategy AND l.StrategyVersion=$version
                  AND (SELECT ToStatus FROM StrategyLifecycleEvents e WHERE e.StrategyId=l.StrategyId
                       AND e.StrategyVersion=l.StrategyVersion ORDER BY e.OccurredAtUtc DESC LIMIT 1)='ValidationComplete'
                LIMIT 1;
                """;
            evidence.Parameters.AddWithValue("$report",campaign.Expectation.SourceReportId);
            evidence.Parameters.AddWithValue("$hash",campaign.Expectation.SourceContentHash);
            evidence.Parameters.AddWithValue("$strategy",campaign.StrategyId);evidence.Parameters.AddWithValue("$version",campaign.StrategyVersion);
            await using var match=await evidence.ExecuteReaderAsync(token);if(!await match.ReadAsync(token))continue;
            return new(campaign.StrategyId,campaign.StrategyVersion,match.GetString(0),match.GetString(1),true,
                campaign.CampaignId,comparison.ComparisonId,comparison.ContentHash,true,comparison.ForwardTrades,
                comparison.ComparedAtUtc);
        }
        return null;
    }
}
