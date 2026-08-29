using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Domain.Strategies;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class ExploratoryPaperCampaignServiceTests
{
    private static readonly DateTime Now=new(2026,8,29,14,0,0,DateTimeKind.Utc);

    [Fact]
    public async Task BlindReplayFreezesCandidateUsesOnlyTestSamplesAndModelsOneToFiveContracts()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();await Seed(factory.Database);
        var registry=new StrategyRegistryRepository(factory.Database);var service=new ExploratoryPaperCampaignService(
            factory.Database,new ExploratorySandboxCandidateService(factory.Database),registry,new InstrumentDefinitionRegistry());
        var dashboard=await service.RunBlindReplayAsync("MES",TestContext.Current.CancellationToken);
        var campaign=Assert.Single(dashboard.Results);Assert.True(dashboard.HasStartedActualSandboxTesting);
        Assert.Equal(2,campaign.ResolvedExecutions);Assert.Equal(3,campaign.SourceTestSamples);
        Assert.False(campaign.AdmissionUsedTestPartition);Assert.False(campaign.CanActivateStrategy);
        Assert.False(campaign.CanRouteToRealBroker);Assert.Equal("BlindHistoricalTestPartitionReplay",campaign.Mode);
        Assert.Equal(ExploratoryPaperCampaignStatus.AccumulatingProspectiveEvidence,campaign.Status);
        Assert.Equal(5,campaign.ContractMetrics.Count);Assert.Equal(5,campaign.ContractMetrics[^1].Contracts);
        Assert.Equal(campaign.ContractMetrics[0].NetProfitLoss*5,campaign.ContractMetrics[^1].NetProfitLoss);
        var frozen=await registry.FindAsync(campaign.StrategyId,campaign.StrategyVersion,TestContext.Current.CancellationToken);
        Assert.NotNull(frozen);Assert.Equal(StrategyRegistryStatus.FrozenResearch,frozen.Status);
        var replay=await service.RunBlindReplayAsync("MES",TestContext.Current.CancellationToken);
        Assert.Single(replay.Results);Assert.Equal(2,replay.Executions);
        Assert.Equal(1,await Count(factory.Database,"ExploratoryPaperCampaigns"));
        Assert.Equal(2,await Count(factory.Database,"ExploratoryPaperExecutions"));
    }

    private static async Task Seed(PfaDatabase database)
    {
        var summaries=new[]{Summary("Train",40,.05m,1.2m),Summary("Validation",15,.02m,1.1m),Summary("Test",3,-.25m,.6m)};
        var run=new PatternTradeResearchRun("PTR-BLIND",PatternTradeHypothesisEngine.Version,Now,["MES"],
            ["pullback-continuation"],58,1,58,summaries,"RUN-HASH",Now);
        var samples=new[]{Sample("WIN",HypothesisExitOutcome.Target,1m,.75m,5001m,1.2m,.25m),
            Sample("LOSS",HypothesisExitOutcome.Stop,-1m,-1.25m,4999m,.2m,1m),
            Sample("NOENTRY",HypothesisExitOutcome.NoEntry,null,null,null,null,null)};
        await using var connection=database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT INTO PatternTradeResearchRuns
            (RunId,EngineVersion,AsOfUtc,ObservationCount,HypothesisCount,SampleCount,ContentHash,RunJson,CreatedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
            VALUES($id,$version,$asOf,58,1,58,$hash,$json,$created,0,0);
            """;command.Parameters.AddWithValue("$id",run.RunId);command.Parameters.AddWithValue("$version",run.EngineVersion);
            command.Parameters.AddWithValue("$asOf",run.AsOfUtc.ToString("O"));command.Parameters.AddWithValue("$hash",run.ContentHash);
            command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(run));command.Parameters.AddWithValue("$created",run.CreatedAtUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);}
        foreach(var sample in samples){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT INTO PatternTradeResearchSamples
            (RunId,SampleId,HypothesisId,ObservationId,InstrumentId,ModuleId,Split,Outcome,NetR,ContentHash,SampleJson)
            VALUES('PTR-BLIND',$id,'H-BLIND',$observation,'MES','pullback-continuation','Test',$outcome,$net,$hash,$json);
            """;command.Parameters.AddWithValue("$id",sample.SampleId);command.Parameters.AddWithValue("$observation",sample.ObservationId);
            command.Parameters.AddWithValue("$outcome",sample.Outcome.ToString());command.Parameters.AddWithValue("$net",(object?)sample.NetR??DBNull.Value);
            command.Parameters.AddWithValue("$hash",sample.ContentHash);command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(sample));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);}
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private static PatternTradeHypothesisSample Sample(string id,HypothesisExitOutcome outcome,decimal? gross,
        decimal? net,decimal? exit,decimal? mfe,decimal? mae)=>new(id,"H-BLIND",$"OBS-{id}","MES","MESU6",
        "pullback-continuation","Pullback","Bullish",Now.AddHours(-2),exit.HasValue?Now.AddHours(-1):null,
        exit.HasValue?5000m:null,exit.HasValue?4999m:null,exit.HasValue?5001m:null,exit.HasValue?Now:null,exit,
        outcome,gross,net,mfe,mae,outcome.ToString(),$"HASH-{id}","Test");

    private static PatternTradeHypothesisSummary Summary(string split,int samples,decimal mean,decimal profitFactor)=>
        new("H-BLIND","pullback-continuation","directional-confirmation-close","opposite-range-invalidation",
            HypothesisDirectionPolicy.PatternDirection,.5m,60,split,samples,5,2,Math.Max(0,samples-7),0,0,mean,.55m,
            profitFactor,2m,false,"fixed-target-or-time",0);

    private static async Task<int> Count(PfaDatabase database,string table)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);await using var command=connection.CreateCommand();command.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));}
}
