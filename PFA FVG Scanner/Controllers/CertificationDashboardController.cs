using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Certification;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Sequences;
using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/certification")]
public sealed class CertificationDashboardController(
    PfaDatabase database,IInstrumentDefinitionRegistry instruments,IMarketPatternModuleRegistry patterns,
    IMarketSequenceDefinitionRegistry sequences,MarketChartService charts,IMarketDataProvider provider,
    LivePilotReadinessProjectionService livePilot):ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken token)
    {
        var coverage=await charts.GetAllCoverageAsync(token);
        var definitions=instruments.GetAll().Where(x=>x.InstrumentId=="MES").ToArray();
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var validated=await Rows(connection,"""
            SELECT d.StrategyId,d.StrategyVersion,d.DisplayName,
             COALESCE((SELECT ToStatus FROM StrategyLifecycleEvents e WHERE e.StrategyId=d.StrategyId AND e.StrategyVersion=d.StrategyVersion ORDER BY e.OccurredAtUtc DESC LIMIT 1),'Draft') Status
            FROM StrategyDefinitions d ORDER BY d.DisplayName,d.StrategyVersion
            """,token);
        var stableReports=await Rows(connection,"SELECT ReportId,PlanId,Status,ContentHash,CreatedAtUtc FROM WalkForwardReports WHERE Status='Stable' ORDER BY CreatedAtUtc DESC",token);
        var eligible=await Rows(connection,"""
            SELECT DISTINCT d.StrategyId,d.StrategyVersion,d.DisplayName,w.ReportId,w.ContentHash
            FROM StrategyDefinitions d JOIN StrategyEvidenceLinks l ON l.StrategyId=d.StrategyId AND l.StrategyVersion=d.StrategyVersion
            JOIN WalkForwardReports w ON w.ReportId=l.EvidenceId AND w.Status='Stable'
            WHERE (SELECT ToStatus FROM StrategyLifecycleEvents e WHERE e.StrategyId=d.StrategyId AND e.StrategyVersion=d.StrategyVersion ORDER BY e.OccurredAtUtc DESC LIMIT 1)='ValidationComplete'
            """,token);
        var observationCounts=await Rows(connection,"""
            SELECT ModuleId,COUNT(*) ObservationCount,MIN(FormationTimeUtc) EarliestUtc,MAX(FormationTimeUtc) LatestUtc
            FROM UniversalMarketObservations GROUP BY ModuleId ORDER BY ModuleId
            """,token);
        var certificationResults=await Rows(connection,"""
            SELECT c.CampaignId,c.StrategyId,c.StrategyVersion,r.Status,r.EvaluatedAtUtc,r.ResultId
            FROM CertificationCampaigns c JOIN CertificationResults r ON r.CampaignId=c.CampaignId
            ORDER BY r.EvaluatedAtUtc DESC LIMIT 25
            """,token);
        var livePilotReadiness=await livePilot.GetAsync(token);return Ok(new
        {
            generatedAtUtc=DateTime.UtcNow,mode="CertificationSandbox",startingBalance=50000m,realBrokerRoutes=false,liveCredentials=false,
            provider=new{provider.ProviderName,status=provider.ConnectionState.Status.ToString(),provider.ConnectionState.Message,connectedAtUtc=provider.ConnectionState.ConnectedAtUtc,lastCandleReceivedUtc=provider.ConnectionState.LastCandleReceivedUtc},
            instruments=definitions.Select(i=>new{i.InstrumentId,i.RootSymbol,i.DisplayName,assetClass=i.AssetClass.ToString(),i.Exchange,i.TickSize,i.TickValue,i.DefinitionVersion,coverage=coverage.Where(c=>c.Symbol.StartsWith(i.RootSymbol,StringComparison.OrdinalIgnoreCase)).ToArray()}),
            patternModules=patterns.GetAll().Select(x=>new{x.ModuleId,x.DisplayName,x.Version,x.Maturity,active=x.Version!="definition-pending",x.SupportedTimeframes}),
            patternTracking=observationCounts,
            sequenceDefinitions=sequences.GetAll(),
            persistedSequences=await Count(connection,"MarketSequenceInstances",null,token),
            evidence=new{generalHypotheses=await Count(connection,"GeneralResearchHypotheses",null,token),positiveHypotheses=await Count(connection,"GeneralResearchHypotheses","Status='Positive'",token),crossDaySignatures=await Count(connection,"GeneralCrossDaySignatureEvidence",null,token),stableWalkForwardReports=stableReports.Count,stableForwardComparisons=await Count(connection,"ForwardComparisons","Status='Stable'",token)},
            strategies=new{registered=validated,eligibleForCertification=eligible,gateMessage=eligible.Count==0?"No strategy is currently linked to both a stable walk-forward report and ValidationComplete registry status. Backtest candidates remain research-only.":$"{eligible.Count} strategy version(s) meet the certification entry gate."},
            certification=new{campaigns=await Count(connection,"CertificationCampaigns",null,token),results=certificationResults,
                payoutEligible=await Count(connection,"CertificationResults","Status='PayoutEligible'",token),
                liveRouting=false,automaticPromotion=false},
            livePilotReadiness,
            sandbox=new{accounts=await Count(connection,"SandboxLedgerEvents","EventType='AccountCreated'",token),orders=await Count(connection,"SandboxLedgerEvents","EventType='OrderSubmitted'",token),fills=await Count(connection,"SandboxLedgerEvents","EventType='FillRecorded'",token),closedTrades=await Count(connection,"SandboxLedgerEvents","EventType='TradeClosed'",token),forwardCampaigns=await Count(connection,"ForwardCampaigns",null,token)},
            realism=new{executionEngine=CertificationExecutionEngine.Version,ruleEngine=PropFirmCertificationEngine.Version,reconciliationEngine=CertificationReconciliationEngine.Version,profile=PropFirmRulePackCatalog.PfaConservative50K(DateTime.UnixEpoch),features=new[]{"seeded latency and jitter","bid/ask execution","queue-ahead uncertainty","participation-limited partial fills","volatility and size impact","commissions","stale-feed rejection","venue outages","intraday trailing drawdown","daily loss and contract limits","news/session restrictions","automation permissions","payout gates","restart reconciliation"}}
        });
    }

    [HttpGet("capabilities")]
    public IActionResult Capabilities()=>Ok(new{selfContained=true,startingBalance=50000,realBrokerRoutes=false,liveCredentials=false,externalProviderRequired=false,marketDataProviderRequiredForRealBars=true,canActivateStrategy=false,publicMutationEnabled=false});

    private static async Task<long> Count(SqliteConnection connection,string table,string? where,CancellationToken token){await using var command=connection.CreateCommand();command.CommandText=$"SELECT COUNT(*) FROM {table}"+(where is null?"":$" WHERE {where}");return Convert.ToInt64(await command.ExecuteScalarAsync(token));}
    private static async Task<List<Dictionary<string,object?>>> Rows(SqliteConnection connection,string sql,CancellationToken token){await using var command=connection.CreateCommand();command.CommandText=sql;var values=new List<Dictionary<string,object?>>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var row=new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase);for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);values.Add(row);}return values;}
}
