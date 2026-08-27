namespace PFA_FVG_Scanner.Tests;

public sealed class ReplayAndScenarioGoldenMasterTests
{
    private readonly MesScenarioEngine _engine = new(new MesExecutionNormalizationService());

    [Fact]
    public void HistoricalReplayStartsAtConfirmationAndCapturesFillChronology()
    {
        var fvg = TestData.BullishFvg();
        var candles = new[]
        {
            TestData.Candle(1, 102, 103, 99, 100), // pre-confirmation: excluded
            TestData.Candle(5, 103, 104, 101.75m, 102),
            TestData.Candle(6, 102, 103, 101m, 101.5m),
            TestData.Candle(7, 101, 102, 99.75m, 100)
        };
        var result = new HistoricalFvgReplayService().Evaluate(fvg, candles);

        Assert.Equal("1.1.0", result.EngineVersion);
        Assert.Equal(3, result.MinuteCandlesEvaluated);
        Assert.Equal(TestData.BaseTime.AddMinutes(5), result.FirstTouchTimeUtc);
        Assert.Equal(TestData.BaseTime.AddMinutes(6), result.FiftyPercentFillTimeUtc);
        Assert.Equal(TestData.BaseTime.AddMinutes(7), result.FullFillTimeUtc);
        Assert.True(result.WasFullyFilled);
        Assert.Equal(FvgLifecycleStatus.FullyFilled, result.LifecycleStatus);
    }

    [Theory]
    [InlineData(MesEntryModel.BoundaryTouch, 102.00)]
    [InlineData(MesEntryModel.TwentyFivePercent, 101.50)]
    [InlineData(MesEntryModel.FiftyPercent, 101.00)]
    [InlineData(MesEntryModel.SeventyFivePercent, 100.50)]
    public void EntryModelsPreserveCurrentDepths(MesEntryModel model, decimal expected)
    {
        var fvg = TestData.BullishFvg();
        var scenario = _engine.EvaluateSingleScenario(fvg, TestData.Outcome(fvg),
            new[] { TestData.Candle(5, 102, 103, 99, 101) }, model, 1, 1m);
        Assert.Equal(expected, scenario.TheoreticalEntryPrice);
        Assert.Equal(expected, scenario.EntryPrice);
    }

    [Fact]
    public void EntryStopTargetRiskAndMesDollarsAreGoldenMastered()
    {
        var fvg = TestData.BullishFvg();
        var candles = new[]
        {
            TestData.Candle(5, 102, 102.25m, 101.75m, 102),
            TestData.Candle(6, 102, 104.25m, 101.5m, 104)
        };
        var scenario = _engine.EvaluateSingleScenario(
            fvg, TestData.Outcome(fvg), candles, MesEntryModel.BoundaryTouch, 2, 1.5m);

        Assert.Equal((102m, 99.75m, 2.25m, 9m, 22.50m),
            (scenario.EntryPrice, scenario.StopPrice, scenario.RiskPoints,
             scenario.RiskTicks, scenario.GrossDollarRisk));
        Assert.Equal((105.50m, 3.50m, 1.5555555555555555555555555556m, 35m),
            (scenario.TargetPrice, scenario.TargetPoints, scenario.EffectiveTargetR,
             scenario.GrossTargetProfit));
        Assert.Equal("1.1.0", scenario.EngineVersion);
    }

    [Fact]
    public void StopBeforeTargetIsResolvedAcrossCandles()
    {
        var fvg = TestData.BullishFvg();
        var scenario = _engine.EvaluateSingleScenario(fvg, TestData.Outcome(fvg), new[]
        {
            TestData.Candle(5, 102, 102.25m, 101.75m, 102),
            TestData.Candle(6, 101, 102, 99.5m, 100),
            TestData.Candle(7, 100, 105, 100, 104)
        }, MesEntryModel.BoundaryTouch, 1, 1m);

        Assert.True(scenario.StopBeforeTarget);
        Assert.False(scenario.TargetBeforeStop);
        Assert.Equal(MesScenarioStatus.StopHit, scenario.Status);
        Assert.Equal(-1m, scenario.RealizedR);
        Assert.Equal(-11.25m, scenario.GrossProfitLoss);
    }

    [Fact]
    public void SameLaterCandleStopAndTargetRemainAmbiguousWithoutPnl()
    {
        var fvg = TestData.BullishFvg();
        var scenario = _engine.EvaluateSingleScenario(fvg, TestData.Outcome(fvg), new[]
        {
            TestData.Candle(5, 102, 102.25m, 101.75m, 102),
            TestData.Candle(6, 102, 105, 99, 102)
        }, MesEntryModel.BoundaryTouch, 1, 1m);

        Assert.True(scenario.TargetHit);
        Assert.True(scenario.StopHit);
        Assert.True(scenario.IntrabarSequenceUnknown);
        Assert.Null(scenario.RealizedR);
        Assert.Null(scenario.GrossProfitLoss);
        Assert.Null(scenario.NetProfitLoss);
    }

    [Fact]
    public void EntryCandleTouchingExitRemainsAmbiguous()
    {
        var fvg = TestData.BullishFvg();
        var scenario = _engine.EvaluateSingleScenario(fvg, TestData.Outcome(fvg),
            new[] { TestData.Candle(5, 102, 105, 99, 102) },
            MesEntryModel.BoundaryTouch, 1, 1m);
        Assert.True(scenario.IntrabarSequenceUnknown);
        Assert.Null(scenario.RealizedR);
    }

    [Fact]
    public void EvaluateAllPreservesCurrentScenarioMatrix()
    {
        var fvg = TestData.BullishFvg();
        var scenarios = _engine.EvaluateAll(fvg, TestData.Outcome(fvg), Array.Empty<Candle>());
        Assert.Equal(32, scenarios.Count);
        Assert.Equal(4, scenarios.Select(x => x.EntryModel).Distinct().Count());
        Assert.Equal(new[] { 1, 2 }, scenarios.Select(x => x.Contracts).Distinct().Order().ToArray());
        Assert.Equal(new[] { 1m, 1.5m, 2m, 3m }, scenarios.Select(x => x.TargetR).Distinct().Order().ToArray());
    }
}
