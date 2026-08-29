using System.Text;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class TradeJournalImportServiceTests
{
    [Fact]
    public async Task ImportReconstructsPartialExitsCostsAndDirectionsWithoutPersistingAccountName()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var service=new TradeJournalImportService(factory.Database);
        var first=await service.ImportAsync(Stream(Csv),"journal.csv",TestContext.Current.CancellationToken);
        var repeated=await service.ImportAsync(Stream(Csv),"renamed.csv",TestContext.Current.CancellationToken);
        Assert.Equal(first.ImportId,repeated.ImportId);Assert.Equal(5,first.ExecutionCount);Assert.Equal(2,first.EpisodeCount);
        Assert.Equal(1,first.Wins);Assert.Equal(1,first.Losses);Assert.Equal(.5m,first.WinRate);
        var episodes=await service.GetEpisodesAsync(first.ImportId,TestContext.Current.CancellationToken);
        var longTrade=Assert.Single(episodes,x=>x.Direction==PFA_FVG_Scanner.Domain.Research.TradeJournalDirection.Long);
        Assert.Equal(3,longTrade.ExecutionCount);Assert.Equal(2,longTrade.RealizedLegCount);Assert.Equal(2,longTrade.MaximumContracts);
        Assert.Equal(5m,longTrade.GrossProfit);Assert.Equal(2.08m,longTrade.EstimatedCosts);Assert.Equal(2.92m,longTrade.NetProfit);
        Assert.Equal(new DateTime(2026,8,20,15,0,0,DateTimeKind.Utc),longTrade.OpenedAtUtc);
        var shortTrade=Assert.Single(episodes,x=>x.Direction==PFA_FVG_Scanner.Domain.Research.TradeJournalDirection.Short);
        Assert.Equal("Loss",shortTrade.Outcome);Assert.Equal(-6.04m,shortTrade.NetProfit);
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM TradeJournalExecutions WHERE ExecutionJson LIKE '%TEST-ACCOUNT%'";
        Assert.Equal(0L,(long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
        Assert.Single(await service.GetImportsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportRejectsMovementQuantityContradiction()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var service=new TradeJournalImportService(factory.Database);
        var invalid=Csv.Replace("1,2,100", "1,-2,100",StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ImportAsync(Stream(invalid),"bad.csv",TestContext.Current.CancellationToken));
    }

    private static MemoryStream Stream(string value)=>new(Encoding.UTF8.GetBytes(value));
    private const string Csv="""
        name,order_id,symbol,mov_time,mov_type,exec_qty,price_done,points,profit,created_on
        TEST-ACCOUNT,OPEN-L,CM.MESU6,Thu Aug 20 2026 10:00:00 GMT-0500 (Central Daylight Time),1,2,100,,,2026-08-20T15:00:00.000Z
        TEST-ACCOUNT,CLOSE-L1,CM.MESU6,Thu Aug 20 2026 10:02:00 GMT-0500 (Central Daylight Time),2,-1,102,2,8.96,2026-08-20T15:00:00.000Z
        TEST-ACCOUNT,CLOSE-L2,CM.MESU6,Thu Aug 20 2026 10:04:00 GMT-0500 (Central Daylight Time),2,-1,99,-1,-6.04,2026-08-20T15:00:00.000Z
        TEST-ACCOUNT,OPEN-S,CM.MESU6,Thu Aug 20 2026 11:00:00 GMT-0500 (Central Daylight Time),3,-1,105,,,2026-08-20T16:00:00.000Z
        TEST-ACCOUNT,CLOSE-S,CM.MESU6,Thu Aug 20 2026 11:03:00 GMT-0500 (Central Daylight Time),4,1,106,-1,-6.04,2026-08-20T16:00:00.000Z
        """;
}
