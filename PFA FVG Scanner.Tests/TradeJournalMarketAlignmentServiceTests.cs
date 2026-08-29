using System.Text;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class TradeJournalMarketAlignmentServiceTests
{
    [Fact]
    public async Task AlignmentUsesOnlyPatternsAndBarsKnownByEntryClock()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var journals=new TradeJournalImportService(factory.Database);
        var manifest=await journals.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(Csv)),"journal.csv",TestContext.Current.CancellationToken);
        await Seed(factory);
        var service=new TradeJournalMarketAlignmentService(factory.Database,journals,new DailyMarketDiscoveryService(factory.Database));
        var report=await service.BuildAsync(manifest.ImportId,TestContext.Current.CancellationToken);
        var repeated=await service.BuildAsync(manifest.ImportId,TestContext.Current.CancellationToken);
        Assert.Equal(report.ReportId,repeated.ReportId);Assert.Equal(1,report.Episodes);
        Assert.Equal(1,report.CanonicalBarAlignedEpisodes);Assert.Equal(1,report.PatternMatchedEpisodes);
        var metric=Assert.Single(report.PatternMetrics);Assert.Equal("range-breakout",metric.ModuleId);
        var segment=Assert.Single(report.DirectionalSegments);Assert.Equal("Aligned",segment.DirectionRelationship);
        Assert.Equal("range-breakout",segment.SignalType);Assert.Equal(1,segment.MatchedEpisodes);
        var alignment=Assert.Single(await service.GetAlignmentsAsync(report.ReportId,TestContext.Current.CancellationToken));
        Assert.Equal("BAR-BEFORE",alignment.CanonicalBarId);
        var match=Assert.Single(alignment.PatternMatches);Assert.Equal("OBS-BEFORE",match.ObservationId);
        Assert.Equal(5m,match.MinutesBeforeEntry);Assert.DoesNotContain(alignment.PatternMatches,x=>x.ObservationId=="OBS-FUTURE");
        Assert.Single(await service.GetReportsAsync(TestContext.Current.CancellationToken));
        Assert.False(report.IsStrategyValidation);Assert.False(report.CanActivateStrategy);
    }

    private static async Task Seed(TestDatabaseFactory factory)
    {
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using(var bar=connection.CreateCommand()){bar.CommandText="""
            INSERT INTO CanonicalResolvedResearchBars
            (CanonicalBarId,InstrumentId,Timeframe,OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume)
            VALUES('BAR-BEFORE','MES','1m','2026-08-20T14:58:00.0000000Z','2026-08-20T14:59:00.0000000Z','99','101','98','100','10');
            """;await bar.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);}
        foreach(var item in new[]{("OBS-BEFORE","2026-08-20T14:55:00.0000000Z"),("OBS-FUTURE","2026-08-20T15:01:00.0000000Z")})
        {await using var observation=connection.CreateCommand();observation.CommandText="""
            INSERT INTO UniversalMarketObservations
            (ObservationId,Revision,ModuleId,ModuleVersion,PatternType,InstrumentId,ContractId,Timeframe,Direction,
             FormationTimeUtc,KnownAtUtc,LifecycleState,PayloadSchema,PayloadJson,SourceReferencesJson,QualityFlags,ContentHash,CreatedAtUtc)
            VALUES($id,1,'range-breakout','1.0.0','RangeBreakout','MES','MESU6','1m','Bullish',$known,$known,
                   'Detected','test','{}','[]',0,$hash,$known);
            """;observation.Parameters.AddWithValue("$id",item.Item1);observation.Parameters.AddWithValue("$known",item.Item2);
            observation.Parameters.AddWithValue("$hash",$"HASH-{item.Item1}");await observation.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);}
    }

    private const string Csv="""
        name,order_id,symbol,mov_time,mov_type,exec_qty,price_done,points,profit,created_on
        TEST,OPEN,CM.MESU6,Thu Aug 20 2026 10:00:00 GMT-0500 (Central Daylight Time),1,1,100,,,2026-08-20T15:00:00.000Z
        TEST,CLOSE,CM.MESU6,Thu Aug 20 2026 10:05:00 GMT-0500 (Central Daylight Time),2,-1,102,2,8.96,2026-08-20T15:00:00.000Z
        """;
}
