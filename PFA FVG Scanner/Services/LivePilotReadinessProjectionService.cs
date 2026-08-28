using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Forward;
using PFA_FVG_Scanner.Domain.LivePilot;

namespace PFA_FVG_Scanner.Services;

public sealed record LivePilotReadinessProjection(
    LivePilotReadinessResult Gate,LivePilotEvidenceSnapshot? EvidenceCandidate,
    int RequiredDecisionCount,int ApprovedDecisionCount,string Guidance);

public sealed class LivePilotReadinessProjectionService(PfaDatabase database,LivePilotReadinessAuditor auditor)
{
    public async Task<LivePilotReadinessProjection> GetAsync(CancellationToken token=default)
    {
        var evidence=await FindEvidenceAsync(token);var now=DateTime.UtcNow;
        var gate=auditor.Evaluate(new("CURRENT-LIVE-PILOT-READINESS",LivePilotReadinessAuditor.Version,[],evidence,now));
        return new(gate,evidence,LivePilotReadinessAuditor.RequiredTopics.Count,0,
            evidence is null
                ? "No exact strategy version currently has both stable walk-forward and stable nonzero forward evidence."
                : "Evidence prerequisites have a candidate, but all accountable design decisions remain unapproved. Live routing stays impossible.");
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
