using System.Text.Json;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PointInTimeCrossMarketIndexTests
{
    private static readonly DateTime Clock=new(2026,8,28,15,0,0,DateTimeKind.Utc);

    [Fact]
    public async Task SnapshotUsesOnlyFreshPeerBarsKnownAtDecisionClock()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        await Seed(factory,"6E",Clock,includeFuture:true);
        await Seed(factory,"MES",Clock,includeFuture:false);
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);
        var index=await PointInTimeCrossMarketIndex.LoadAsync(connection,Clock.AddMinutes(1),TestContext.Current.CancellationToken);
        using var document=JsonDocument.Parse(index.SnapshotJson("MES",Clock));
        var peer=Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("6E",peer.GetProperty("instrumentId").GetString());
        Assert.Equal("6E-5",peer.GetProperty("latestSourceId").GetString());
        Assert.Equal(.05m,peer.GetProperty("return5Fraction").GetDecimal());
    }

    [Fact]
    public async Task SnapshotFailsClosedForStaleOrUnavailablePeers()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();await Seed(factory,"6E",Clock,includeFuture:false);
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);
        var index=await PointInTimeCrossMarketIndex.LoadAsync(connection,Clock,TestContext.Current.CancellationToken);
        using var stale=JsonDocument.Parse(index.SnapshotJson("MES",Clock.AddMinutes(3)));
        Assert.Empty(stale.RootElement.EnumerateArray());
        using var self=JsonDocument.Parse(index.SnapshotJson("6E",Clock));
        Assert.Empty(self.RootElement.EnumerateArray());
    }

    private static async Task Seed(TestDatabaseFactory factory,string instrument,DateTime clock,bool includeFuture)
    {
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);
        for(var index=0;index<6+(includeFuture?1:0);index++)
        {
            var closeTime=index==6?clock.AddMinutes(1):clock.AddMinutes(index-5);
            var close=index==6?999:100+index;
            await using var command=connection.CreateCommand();command.CommandText="""
                INSERT INTO CanonicalResolvedResearchBars
                (CanonicalBarId,InstrumentId,Timeframe,OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume)
                VALUES($id,$instrument,'1m',$openTime,$closeTime,$open,$high,$low,$close,100);
                """;
            command.Parameters.AddWithValue("$id",$"{instrument}-{index}");command.Parameters.AddWithValue("$instrument",instrument);
            command.Parameters.AddWithValue("$openTime",closeTime.AddMinutes(-1).ToString("O"));command.Parameters.AddWithValue("$closeTime",closeTime.ToString("O"));
            command.Parameters.AddWithValue("$open",(100+index).ToString());command.Parameters.AddWithValue("$high",(101+index).ToString());
            command.Parameters.AddWithValue("$low",(99+index).ToString());command.Parameters.AddWithValue("$close",close.ToString());
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
