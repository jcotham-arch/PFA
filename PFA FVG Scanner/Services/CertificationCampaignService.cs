using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Certification;

namespace PFA_FVG_Scanner.Services;

public sealed class CertificationCampaignService(PfaDatabase database,CertificationCampaignEngine engine,
    CertificationCampaignRepository repository)
{
    public async Task<CertificationCampaignResult> RunAsync(CertificationCampaignRequest request,
        CancellationToken token=default)
    {
        if(!await IsEligibleAsync(request,token))
            throw new UnauthorizedAccessException("The exact strategy version is not ValidationComplete and linked to the supplied stable walk-forward evidence revision.");
        var result=engine.Evaluate(request);await repository.SaveAsync(request,result,token);return result;
    }

    private async Task<bool> IsEligibleAsync(CertificationCampaignRequest request,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT COUNT(*) FROM StrategyDefinitions d
            JOIN StrategyEvidenceLinks l ON l.StrategyId=d.StrategyId AND l.StrategyVersion=d.StrategyVersion
            JOIN WalkForwardReports w ON w.ReportId=l.EvidenceId
            WHERE d.StrategyId=$strategy AND d.StrategyVersion=$version AND w.Status='Stable'
              AND (w.ReportId=$evidence OR w.ContentHash=$evidence)
              AND (SELECT ToStatus FROM StrategyLifecycleEvents e
                   WHERE e.StrategyId=d.StrategyId AND e.StrategyVersion=d.StrategyVersion
                   ORDER BY e.OccurredAtUtc DESC LIMIT 1)='ValidationComplete';
            """;
        command.Parameters.AddWithValue("$strategy",request.StrategyId);command.Parameters.AddWithValue("$version",request.StrategyVersion);
        command.Parameters.AddWithValue("$evidence",request.EvidenceRevision);return Convert.ToInt64(await command.ExecuteScalarAsync(token))>0;
    }
}
