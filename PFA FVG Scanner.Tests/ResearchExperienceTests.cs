using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Patterns.Liquidity;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class ResearchExperienceTests
{
    private static readonly JsonSerializerOptions WebJson=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};

    [Fact]
    public async Task DailyDiscoveryFindsExpansionWithoutDependingOnNamedPatternTerminology()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var candles=new CandleRepository(factory.Database);
        for(var minute=0;minute<105;minute++)
        {
            var expansion=minute==104;var candle=TestData.Candle(minute,100,expansion?104:100.5m,expansion?99:99.5m,expansion?103:100,
                symbol:"MESU6",volume:expansion?1000:10);
            await candles.SaveAsync(candle,"test",TestContext.Current.CancellationToken);
        }
        var service=new DailyMarketDiscoveryService(factory.Database);
        var result=await service.StudyAsync(DateOnly.FromDateTime(TestData.BaseTime),TestContext.Current.CancellationToken);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(result,WebJson));var root=json.RootElement;
        Assert.Equal(105,root.GetProperty("oneMinuteBars").GetInt32());
        Assert.Contains(root.GetProperty("discoveredEvents").EnumerateArray(),x=>x.GetProperty("type").GetString()=="RangeExpansion");
        Assert.True(root.GetProperty("method").GetProperty("terminologyNeutral").GetBoolean());
    }

    [Fact]
    public async Task SetupResearchClassifiesZeroNetReturnAsNeutral()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new UniversalMarketRecordRepository(factory.Database);
        var known=TestData.BaseTime;var observation=new MarketPatternObservation("OBS-NEUTRAL","liquidity-sweep","capture-1.0.0",
            "LiquiditySweep","MES","MESU6","5m",PatternDirection.Bullish,known.AddMinutes(-5),known,
            PatternLifecycleState.Detected,new LiquiditySweepGeometry(LiquiditySide.SellSide,99.25m,99m,.25m,true,2,[]),[],MarketDataQualityFlags.None);
        await repository.SaveObservationAsync(UniversalMarketRecordRepository.FromPattern(observation),TestContext.Current.CancellationToken);
        var sample=new PatternTradeHypothesisSample("SAMPLE-NEUTRAL","H-NEUTRAL",observation.ObservationId,"MES","MESU6",
            "liquidity-sweep","LiquiditySweep","Bullish",known,known,100,99,101,known.AddMinutes(1),101,
            HypothesisExitOutcome.Target,1,0,1,.25m,"Target reached but estimated costs consume the return.","HASH");
        await using(var connection=factory.Database.CreateConnection())
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);await using var command=connection.CreateCommand();command.CommandText="""
                INSERT INTO PatternTradeResearchRuns VALUES('RUN-NEUTRAL','pattern-trade-hypothesis-engine-1.3.0',$now,1,1,1,'HASH','{}',$now,0,0);
                INSERT INTO PatternTradeResearchSamples VALUES('RUN-NEUTRAL','SAMPLE-NEUTRAL','H-NEUTRAL','OBS-NEUTRAL','MES','liquidity-sweep','Test','Target','0','HASH',$sample);
                """;command.Parameters.AddWithValue("$now",known.ToString("O"));command.Parameters.AddWithValue("$sample",JsonSerializer.Serialize(sample));await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var detail=await new PatternObservationResearchService(factory.Database).GetAsync(observation.ObservationId,TestContext.Current.CancellationToken);
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(detail,WebJson));var scenario=json.RootElement.GetProperty("scenarios")[0];
        Assert.Equal("Neutral",scenario.GetProperty("classification").GetString());Assert.Equal(0,scenario.GetProperty("netR").GetDecimal());
    }
}
