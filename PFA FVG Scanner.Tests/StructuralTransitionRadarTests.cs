using PFA_FVG_Scanner.Domain.Intermarket;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class StructuralTransitionRadarTests
{
    [Fact]
    public async Task ContextUsesOnlyEvidenceKnownAtDecisionClockAndTranslatesSpxWallWithBasis()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var service=Service(factory);
        var clock=TestData.BaseTime.AddHours(1);
        var usable=new OptionsGammaObservation("G1",clock.AddMinutes(-5),clock.AddMinutes(-1),"SPX","TEST",-10,5000,5010,4980,4990,-1,"v1","H1");
        var future=new OptionsGammaObservation("G2",clock.AddMinutes(-4),clock.AddMinutes(1),"SPX","TEST",10,5000,5100,4900,5000,1,"v1","H2");
        var breadth=new IntermarketBreadthObservation("B1",clock.AddMinutes(-1),clock.AddMinutes(-1),"TEST",0,5012,18000,5000,.8m,12,"v1","H3");
        var token=TestContext.Current.CancellationToken;
        await service.SaveAsync(new(usable,null,breadth),token);await service.SaveAsync(new(future,null,null),token);

        var snapshot=await service.GetContextAsync(clock,5018,token);

        Assert.Equal("G1",snapshot.Gamma!.ObservationId);Assert.True(snapshot.IsNegativeGammaRegime);
        Assert.Equal(16,snapshot.DistanceToCallWallTicks);Assert.Contains("volatility-term-structure",snapshot.MissingContext);
    }

    [Fact]
    public async Task RadarProducesResearchOnlyUncalibratedPointInTimeProbability()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var candles=new CandleRepository(factory.Database);
        for(var i=0;i<80;i++)
        {
            var center=5000m+i*.05m;var width=i>=74?.25m:1m;
            await candles.SaveAsync(TestData.Candle(i,center,center+width,center-width,center+.05m,
                volume:i>=74?180:100),"TEST",TestContext.Current.CancellationToken);
        }
        var radar=await Service(factory).GetRadarAsync(TestData.BaseTime.AddMinutes(81),TestContext.Current.CancellationToken);

        Assert.Equal("MES",radar.InstrumentId);Assert.Equal("UncalibratedShadow",radar.CalibrationStatus);
        Assert.Contains("cannot authorize",radar.ResearchAuthority,StringComparison.OrdinalIgnoreCase);
        Assert.InRange(radar.TransitionProbability,10,88);Assert.NotEmpty(radar.Evidence);
        Assert.Contains("options-gamma",radar.MissingContext);
    }

    [Fact]
    public async Task FrozenHistoricalPredictionIsScoredOnlyAfterItsHorizonExists()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var candles=new CandleRepository(factory.Database);var token=TestContext.Current.CancellationToken;
        for(var i=0;i<70;i++){var price=5000m+(i<45?i*.02m:(i-45)*.75m);await candles.SaveAsync(
            TestData.Candle(i,price,price+.5m,price-.5m,price+.25m,volume:100),"TEST",token);}
        var service=Service(factory);var prediction=await service.CaptureAsync(TestData.BaseTime.AddMinutes(45),token);
        var calibration=await service.EvaluateAsync(token);

        Assert.Equal(1,calibration.Predictions);Assert.Equal(1,calibration.Evaluated);
        Assert.Equal(prediction.PredictionId,Assert.Single(calibration.LatestOutcomes).PredictionId);
    }

    [Fact]
    public async Task MultiClockBackfillProducesMeasuredCalibrationBands()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var candles=new CandleRepository(factory.Database);var token=TestContext.Current.CancellationToken;
        for(var i=0;i<180;i++){var wave=(decimal)Math.Sin(i/8d)*3m;var price=5000m+wave;await candles.SaveAsync(
            TestData.Candle(i,price,price+1m,price-1m,price+.25m,volume:100+i%20),"TEST",token);}
        var result=await Service(factory).BackfillAsync(1,15,25,token);

        Assert.True(result.PredictionsStored>0);Assert.True(result.Calibration.Evaluated>0);
        Assert.NotEmpty(result.Calibration.Bands);
    }

    private static IntermarketContextService Service(TestDatabaseFactory factory)=>new(factory.Database);
}
