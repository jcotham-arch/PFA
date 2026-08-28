using System.Text.Json;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class SequenceTradeResearchServiceTests
{
    private static readonly DateTime Now=new(2026,8,1,14,0,0,DateTimeKind.Utc);

    [Fact]
    public async Task StudyUsesOnlySequenceContextKnownByTradeDecisionAndPersistsLineage()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        var summary=new PatternTradeHypothesisSummary("H","range-breakout","next-one-minute-open",
            "opposite-range-invalidation",HypothesisDirectionPolicy.PatternDirection,1,30,"Train",1,1,0,0,0,0,
            .5m,1,999,0,false,"fixed-target-or-time",0);
        var source=new PatternTradeResearchRun("PTR-SOURCE","pattern-trade-hypothesis-engine-1.3.0",Now,
            ["MES"],["range-breakout"],1,1,1,[summary],"SOURCE-HASH",Now);
        await using(var connection=factory.Database.CreateConnection())
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command=connection.CreateCommand();command.CommandText="""
                INSERT INTO PatternTradeResearchRuns VALUES('PTR-SOURCE','pattern-trade-hypothesis-engine-1.3.0',$now,1,1,1,'SOURCE-HASH',$run,$now,0,0);
                INSERT INTO UniversalMarketObservations VALUES('OBS',1,'range-breakout','1','RangeBreakout','MES',NULL,'5m','Bullish',$now,$now,'Detected','test','{}','[]',0,'OBS-HASH',$now);
                INSERT INTO PatternTradeResearchSamples VALUES('PTR-SOURCE','SAMPLE','H','OBS','MES','range-breakout','Train','Target','0.5','SAMPLE-HASH','{}');
                INSERT INTO MarketSequenceDefinitions VALUES('breakout-continuation','1','Breakout continuation',2700,1,'{}',$now);
                INSERT INTO MarketSequenceInstances VALUES('KNOWN','breakout-continuation','1','MES',NULL,'5m','SESSION','2026-08-01','Successful',1,$now,$now,'1',NULL,$now);
                INSERT INTO MarketSequenceMembers VALUES('KNOWN','OBS',1,'continuation-breakout',2,$now);
                INSERT INTO MarketSequenceInstances VALUES('FUTURE','breakout-continuation','1','MES',NULL,'5m','SESSION','2026-08-01','Successful',1,$now,$future,'1',NULL,$future);
                INSERT INTO MarketSequenceMembers VALUES('FUTURE','OBS',1,'continuation-breakout',2,$future);
                """;
            command.Parameters.AddWithValue("$now",Now.ToString("O"));command.Parameters.AddWithValue("$future",Now.AddMinutes(1).ToString("O"));
            command.Parameters.AddWithValue("$run",JsonSerializer.Serialize(source));await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var service=new SequenceTradeResearchService(factory.Database);
        var run=await service.RunAsync(new(Now.AddMinutes(2),source.RunId),TestContext.Current.CancellationToken);
        Assert.Equal(1,run.SequenceCompletionCount);Assert.Equal(1,run.ContextSampleCount);
        var row=Assert.Single(run.Summaries);Assert.Equal("breakout-continuation",row.SequenceDefinitionId);
        Assert.Equal(.5m,row.MeanNetR);Assert.False(row.IsTradableEvidence);
        Assert.Single(await service.GetAllAsync(TestContext.Current.CancellationToken));
    }
}
