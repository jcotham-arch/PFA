using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Patterns.Breakouts;
using PFA_FVG_Scanner.Domain.Patterns.Liquidity;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PatternTradeHypothesisEngineTests
{
    private static readonly DateTime Now=new(2026,8,1,14,0,0,DateTimeKind.Utc);

    [Fact]
    public void SweepHypothesisUsesNextBarStructuralRiskAndFirstTargetHit()
    {
        var result=PatternTradeHypothesisEngine.Evaluate(Definition("liquidity-sweep"),Sweep(),
            [Bar(0,100,101.5m,99.5m,101)],.25m);
        Assert.Equal(HypothesisExitOutcome.Target,result.Outcome);Assert.Equal(1m,result.GrossR);
        Assert.Equal(100m,result.EntryPrice);Assert.Equal(98.75m,result.StopPrice);Assert.Equal(101.25m,result.TargetPrice);
        Assert.False(result.CanActivateStrategy);Assert.False(result.CanRouteToRealBroker);
    }

    [Fact]
    public void SameMinuteStopAndTargetRemainsAmbiguous()
    {
        var result=PatternTradeHypothesisEngine.Evaluate(Definition("liquidity-sweep"),Sweep(),
            [Bar(0,100,101.5m,98.5m,100)],.25m);
        Assert.Equal(HypothesisExitOutcome.Ambiguous,result.Outcome);Assert.Null(result.NetR);
        Assert.Contains("intrabar order is unknown",result.Reason);
    }

    [Fact]
    public void ConfirmationEntryWaitsForCompletedBarAndStartsPathAfterItsClose()
    {
        var definition=Definition("liquidity-sweep") with{EntryPolicy="one-minute-confirmation-close"};
        var result=PatternTradeHypothesisEngine.Evaluate(definition,Sweep(),
            [Bar(0,100,105,99.5m,100.25m),Bar(1,100.25m,101.75m,100,101.5m)],.25m);
        Assert.Equal(Now.AddMinutes(1),result.EntryTimeUtc);Assert.Equal(100.25m,result.EntryPrice);
        Assert.Equal(HypothesisExitOutcome.Target,result.Outcome);
    }

    [Fact]
    public void FailedBreakoutCanTestOpposingReversalWithoutChangingPatternFact()
    {
        var observation=Sweep() with{ModuleId="failed-breakout",PatternType="FailedBreakout",
            Direction=PatternDirection.Bullish,Geometry=new RangeBreakoutGeometry(RangeBoundarySide.Upper,98,101,102,100,1,false,[])};
        var definition=Definition("failed-breakout") with{DirectionPolicy=HypothesisDirectionPolicy.OpposePatternDirection};
        var result=PatternTradeHypothesisEngine.Evaluate(definition,observation,[Bar(0,100,100.5m,97,98)],.25m);
        Assert.Equal("Bearish",result.Direction);Assert.Equal(HypothesisExitOutcome.Target,result.Outcome);
        Assert.Equal(PatternDirection.Bullish,observation.Direction);
    }

    [Fact]
    public void SweepBoundaryStopUsesReclaimedLevelInsteadOfSweepExtreme()
    {
        var definition=Definition("liquidity-sweep") with{StopPolicy="boundary-invalidation"};
        var result=PatternTradeHypothesisEngine.Evaluate(definition,Sweep(),
            [Bar(0,100,100.5m,99,100)],.25m);
        Assert.Equal(99m,result.StopPrice);
        Assert.Equal(101m,result.TargetPrice);
    }

    [Fact]
    public void RangeOppositeBoundaryStopUsesFullRangeRisk()
    {
        var observation=Sweep() with{ModuleId="range-breakout",PatternType="RangeBreakout",
            Direction=PatternDirection.Bullish,Geometry=new RangeBreakoutGeometry(RangeBoundarySide.Upper,98,101,102,101.5m,1,true,[])};
        var definition=Definition("range-breakout") with{StopPolicy="opposite-range-invalidation"};
        var result=PatternTradeHypothesisEngine.Evaluate(definition,observation,[Bar(0,102,106.5m,101,106)],.25m);
        Assert.Equal(97.75m,result.StopPrice);
        Assert.Equal(106.25m,result.TargetPrice);
    }

    [Fact]
    public void UnknownStopPolicyIsRejected()
    {
        var definition=Definition("liquidity-sweep") with{StopPolicy="invented-stop"};
        var error=Assert.Throws<NotSupportedException>(()=>
            PatternTradeHypothesisEngine.Evaluate(definition,Sweep(),[Bar(0,100,101,99,100)],.25m));
        Assert.Contains("invented-stop",error.Message);
    }

    [Fact]
    public void BreakEvenPolicyMovesStopOnlyAfterActivationBarCompletes()
    {
        var definition=Definition("liquidity-sweep") with{ExitPolicy="break-even-after-0.5r"};
        var result=PatternTradeHypothesisEngine.Evaluate(definition,Sweep(),
            [Bar(0,100,100.75m,99.5m,100.5m),Bar(1,100.5m,100.75m,99.75m,100)],.25m);
        Assert.Equal(HypothesisExitOutcome.BreakEven,result.Outcome);
        Assert.Equal(100m,result.ExitPrice);Assert.Equal(0m,result.GrossR);
    }

    [Fact]
    public void SameBarBreakEvenActivationAndStructuralStopIsAmbiguous()
    {
        var definition=Definition("liquidity-sweep") with{ExitPolicy="break-even-after-0.5r"};
        var result=PatternTradeHypothesisEngine.Evaluate(definition,Sweep(),
            [Bar(0,100,100.75m,98.5m,100)],.25m);
        Assert.Equal(HypothesisExitOutcome.Ambiguous,result.Outcome);
        Assert.Contains("activation and structural stop",result.Reason);
    }

    [Fact]
    public void UnknownExitPolicyIsRejected()
    {
        var definition=Definition("liquidity-sweep") with{ExitPolicy="clairvoyant-exit"};
        var error=Assert.Throws<NotSupportedException>(()=>
            PatternTradeHypothesisEngine.Evaluate(definition,Sweep(),[Bar(0,100,101,99,100)],.25m));
        Assert.Contains("clairvoyant-exit",error.Message);
    }

    [Fact]
    public void PatternNotificationLifecycleDoesNotRevealEntryOrOutcomeEarly()
    {
        var definition=Definition("liquidity-sweep") with{EntryPolicy="one-minute-confirmation-close"};
        var sample=PatternTradeHypothesisEngine.Evaluate(definition,Sweep(),
            [Bar(0,100,100.5m,99.5m,100.25m),Bar(1,100.25m,101.75m,100,101.5m)],.25m);
        var detected=PatternTradeNotificationInterpreter.Interpret(sample,Now,Now.AddHours(1));
        var eligible=PatternTradeNotificationInterpreter.Interpret(sample,Now.AddMinutes(1),Now.AddHours(1));
        var terminal=PatternTradeNotificationInterpreter.Interpret(sample,Now.AddMinutes(2),Now.AddHours(1));
        Assert.Equal(PatternTradeNotificationState.Detected,detected.State);
        Assert.Equal(PatternTradeNotificationState.ResearchEntryEligible,eligible.State);
        Assert.Equal(PatternTradeNotificationState.TargetReached,terminal.State);
        Assert.False(eligible.IsActionable);Assert.False(eligible.CanActivateStrategy);Assert.False(eligible.CanRouteToRealBroker);
    }

    [Fact]
    public async Task ResearchRunUsesChronologicalSplitsAndPersistsImmutableSamples()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new PFA_FVG_Scanner.Data.UniversalMarketRecordRepository(factory.Database);
        for(var index=0;index<10;index++)
        {
            var known=Now.AddMinutes(index*20);await repository.SaveObservationAsync(new($"OBS-{index}",1,"liquidity-sweep","1",
                "LiquiditySweep","MES","MESU6","5m",PatternDirection.Bullish,known.AddMinutes(-5),known,
                PatternLifecycleState.Detected,"test","{\"geometry\":{\"SweepExtreme\":99}}",[],MarketDataQualityFlags.None,$"H-{index}"),TestContext.Current.CancellationToken);
            await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command=connection.CreateCommand();command.CommandText="""
                INSERT INTO CanonicalResolvedResearchBars
                (CanonicalBarId,InstrumentId,Timeframe,OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume)
                VALUES($id,'MES','1m',$openTime,$closeTime,'100','101.5','99.5','101','100');
                """;command.Parameters.AddWithValue("$id",$"BAR-{index}");command.Parameters.AddWithValue("$openTime",known.ToString("O"));
            command.Parameters.AddWithValue("$closeTime",known.AddMinutes(1).ToString("O"));await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var service=new PatternTradeResearchService(factory.Database,new InstrumentDefinitionRegistry());
        var run=await service.RunAsync(new(Now.AddHours(4),["MES"],["liquidity-sweep"],[1],[15],1,1,
            ["extreme-invalidation"],["fixed-target-or-time"]),TestContext.Current.CancellationToken);
        Assert.Equal(10,run.ObservationCount);Assert.Equal(20,run.SampleCount);Assert.Equal(2,run.HypothesisCount);
        Assert.Equal(14,run.Summaries.Where(x=>x.Split=="Train").Sum(x=>x.Samples));
        Assert.Equal(2,run.Summaries.Where(x=>x.Split=="Validation").Sum(x=>x.Samples));
        Assert.Equal(4,run.Summaries.Where(x=>x.Split=="Test").Sum(x=>x.Samples));
        Assert.Single(await service.GetAllAsync(TestContext.Current.CancellationToken));
        var dataset=await new ActionabilityOutcomeDatasetService(factory.Database).BuildAsync(
            new(Now.AddHours(4),["MES"],["liquidity-sweep"]),TestContext.Current.CancellationToken);
        Assert.Equal(ActionabilityOutcomeDatasetService.Version,dataset.DatasetVersion);
        Assert.Contains("netR",dataset.LabelNames);Assert.Contains("maximumFavorableExcursionR",dataset.LabelNames);
        Assert.True(dataset.ExampleCount>=3);Assert.Equal(0,dataset.TargetHorizonMinutes);
        await using(var chronology=factory.Database.CreateConnection()){await chronology.OpenAsync(TestContext.Current.CancellationToken);
            await using var command=chronology.CreateCommand();command.CommandText="SELECT COUNT(*) FROM AgentResearchExamples WHERE DatasetId=$id AND (FeatureKnownAtUtc<>DecisionTimeUtc OR OutcomeKnownAtUtc<=DecisionTimeUtc)";command.Parameters.AddWithValue("$id",dataset.DatasetId);
            Assert.Equal(0L,Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)));}
    }

    private static PatternTradeHypothesisDefinition Definition(string module)=>new($"test-{module}","1",module,
        HypothesisDirectionPolicy.PatternDirection,"next-one-minute-open","extreme-invalidation",1m,15,1m);
    private static MarketPatternObservation Sweep()=>new("OBS","liquidity-sweep","1","LiquiditySweep","MES","MESU6","5m",
        PatternDirection.Bullish,Now.AddMinutes(-5),Now,PatternLifecycleState.Detected,
        new LiquiditySweepGeometry(LiquiditySide.SellSide,99.25m,99m,.25m,true,1,[]),[],MarketDataQualityFlags.None);
    private static CanonicalBar Bar(int minute,decimal open,decimal high,decimal low,decimal close)=>new($"BAR-{minute}",1,
        "MES","MESU6","MESU6","1m",Now.AddMinutes(minute),Now.AddMinutes(minute+1),open,high,low,close,100,true,
        "S",DateOnly.FromDateTime(Now),"1","1",CorrectionState.Original,MarketDataQualityFlags.None,Now,$"H-{minute}");
}
