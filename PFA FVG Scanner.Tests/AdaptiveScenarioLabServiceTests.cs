using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class AdaptiveScenarioLabServiceTests
{
    private static readonly DateTime Now=new(2026,8,29,14,0,0,DateTimeKind.Utc);

    [Fact]
    public async Task GenerationUsesDevelopmentRowsOnlyAndQueuesImmutableControlledMutations()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();await Seed(factory.Database);
        var service=new AdaptiveScenarioLabService(factory.Database,new ExploratorySandboxCandidateService(factory.Database));
        var dashboard=await service.GenerateAsync("MES",TestContext.Current.CancellationToken);
        Assert.NotNull(dashboard.Latest);var generation=dashboard.Latest!;Assert.NotNull(generation.Champion);var champion=generation.Champion!;
        Assert.Equal(4,champion.DevelopmentTrades);Assert.Equal(2,champion.DistinctDevelopmentDays);
        Assert.Equal(["15m","1m","5m"],champion.Timeframes);
        Assert.Equal(.5m,champion.DevelopmentMeanNetR);
        Assert.Equal(3,generation.Challengers.Count);Assert.All(generation.Challengers,x=>Assert.False(x.HasSeenBlindResults));
        Assert.False(generation.UsedTestPartitionForSelection);Assert.False(generation.MutatesFrozenVersion);
        Assert.False(generation.CanActivateStrategy);Assert.Equal(AdaptiveScenarioGenerationStatus.AwaitingDevelopmentEvidence,generation.Status);
        Assert.Equal(new DateOnly(2026,8,30),generation.EarliestNextBlindTradingDate);
        Assert.DoesNotContain(champion.Segments,x=>x.Timeframe=="1h");
    }

    private static async Task Seed(PfaDatabase database)
    {
        var summaries=new[]{Summary("Train",40,.4m,2m),Summary("Validation",15,.2m,1.5m),Summary("Test",20,99m,999m)};
        var run=new PatternTradeResearchRun("PTR-ADAPT",PatternTradeHypothesisEngine.Version,Now,["MES"],
            ["pullback-continuation"],75,1,75,summaries,"RUN-HASH",Now);
        await using var connection=database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT INTO PatternTradeResearchRuns
            (RunId,EngineVersion,AsOfUtc,ObservationCount,HypothesisCount,SampleCount,ContentHash,RunJson,CreatedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
            VALUES('PTR-ADAPT',$version,$now,75,1,75,'RUN-HASH',$json,$now,0,0);
            """;command.Parameters.AddWithValue("$version",run.EngineVersion);command.Parameters.AddWithValue("$now",Now.ToString("O"));
            command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(run));await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);}
        var rows=new[]{
            ("A","Train","1m",Now.AddDays(-3),1m),("B","Train","5m",Now.AddDays(-3),-1m),
            ("C","Validation","15m",Now.AddDays(-2),1m),("D","Validation","1m",Now.AddDays(-2),1m),
            ("LEAK","Test","1h",Now,99m)};
        foreach(var row in rows)
        {
            var sample=new PatternTradeHypothesisSample($"S-{row.Item1}","H-ADAPT",$"O-{row.Item1}","MES",null,
                "pullback-continuation","Pullback","Bullish",row.Item4,row.Item4,5000m,4999m,5001m,row.Item4.AddMinutes(1),5001m,
                HypothesisExitOutcome.Target,row.Item5,row.Item5,1m,0m,"test",$"HASH-{row.Item1}",row.Item2);
            await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
                INSERT INTO UniversalMarketObservations
                (ObservationId,Revision,ModuleId,ModuleVersion,PatternType,InstrumentId,ContractId,Timeframe,Direction,
                 FormationTimeUtc,KnownAtUtc,LifecycleState,PayloadSchema,PayloadJson,SourceReferencesJson,QualityFlags,ContentHash,CreatedAtUtc)
                VALUES($observation,1,'pullback-continuation','1','Pullback','MES',NULL,$timeframe,'Bullish',$time,$time,'Detected','test','{}','[]',0,$ohash,$time);
                INSERT INTO PatternTradeResearchSamples
                (RunId,SampleId,HypothesisId,ObservationId,InstrumentId,ModuleId,Split,Outcome,NetR,ContentHash,SampleJson)
                VALUES('PTR-ADAPT',$sample,'H-ADAPT',$observation,'MES','pullback-continuation',$split,'Target',$net,$hash,$json);
                """;command.Parameters.AddWithValue("$observation",sample.ObservationId);command.Parameters.AddWithValue("$timeframe",row.Item3);
            command.Parameters.AddWithValue("$time",row.Item4.ToString("O"));command.Parameters.AddWithValue("$ohash",$"OHASH-{row.Item1}");
            command.Parameters.AddWithValue("$sample",sample.SampleId);command.Parameters.AddWithValue("$split",row.Item2);
            command.Parameters.AddWithValue("$net",row.Item5);command.Parameters.AddWithValue("$hash",sample.ContentHash);
            command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(sample));await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private static PatternTradeHypothesisSummary Summary(string split,int samples,decimal mean,decimal pf)=>
        new("H-ADAPT","pullback-continuation","directional-confirmation-close","opposite-range-invalidation",
            HypothesisDirectionPolicy.PatternDirection,1m,60,split,samples,20,10,5,0,0,mean,.6m,pf,2m,false,"fixed-target-or-time",0);
}
