using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Tests;

public sealed class GenericPatternOutcomeTests
{
    [Fact]
    public void SamePointMoveUsesEachInstrumentsOwnEconomics()
    {
        var registry = new InstrumentDefinitionRegistry();
        var observation = Observation(PatternDirection.Bullish);
        var candles = new[] { TestData.Candle(0, 100m, 101.25m, 99.75m, 101m, symbol: "MESU6") };

        var mes = GenericPatternOutcomeReplayService.Calculate(observation, candles,
            registry.GetAll().Single(x => x.InstrumentId == "MES"), [5])!;
        var mnq = GenericPatternOutcomeReplayService.Calculate(observation with { InstrumentId = "MNQ", ContractId = "MNQU6" },
            candles, registry.GetAll().Single(x => x.InstrumentId == "MNQ"), [5])!;

        Assert.Equal(5m, Metric(mes, "directional-close-change", "usd-per-contract"));
        Assert.Equal(2m, Metric(mnq, "directional-close-change", "usd-per-contract"));
        Assert.Equal(4m, Metric(mes, "directional-close-change", "ticks"));
        Assert.False(mes.Events.Count == 0);
    }

    [Fact]
    public void BearishOutcomeKeepsFavorableAndAdverseChronologyDirectional()
    {
        var definition = new InstrumentDefinitionRegistry().GetAll().Single(x => x.InstrumentId == "MES");
        var candles = new[]
        {
            TestData.Candle(0, 100m, 100.5m, 98.5m, 99m, symbol: "MESU6"),
            TestData.Candle(1, 99m, 101m, 98m, 100m, symbol: "MESU6")
        };

        var outcome = GenericPatternOutcomeReplayService.Calculate(Observation(PatternDirection.Bearish),
            candles, definition, [5])!;

        Assert.Equal(0m, Metric(outcome, "directional-close-change", "points"));
        Assert.Equal(2m, Metric(outcome, "maximum-favorable-excursion", "points"));
        Assert.Equal(1m, Metric(outcome, "maximum-adverse-excursion", "points"));
        Assert.Equal("generic-forward-1.0.0", outcome.OutcomeVersion);
    }

    [Fact]
    public void NoFutureBarProducesNoFabricatedOutcome()
    {
        var definition = new InstrumentDefinitionRegistry().GetAll().Single(x => x.InstrumentId == "MES");
        Assert.Null(GenericPatternOutcomeReplayService.Calculate(Observation(PatternDirection.Bullish),
            [], definition, [5]));
    }

    [Fact]
    public async Task BatchSaveIsIdempotentForMultipleOutcomes()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new UniversalMarketRecordRepository(factory.Database);
        var first = Observation(PatternDirection.Bullish);
        var second = first with { ObservationId = "OBS-GENERIC-2", Direction = PatternDirection.Bearish,
            ContentHash = "hash-2" };
        await repository.SaveObservationAsync(first, TestContext.Current.CancellationToken);
        await repository.SaveObservationAsync(second, TestContext.Current.CancellationToken);
        var definition = new InstrumentDefinitionRegistry().GetAll().Single(x => x.InstrumentId == "MES");
        var candles = new[] { TestData.Candle(0, 100m, 101.25m, 99.75m, 101m, symbol: "MESU6") };
        var outcomes = new[]
        {
            GenericPatternOutcomeReplayService.Calculate(first, candles, definition, [5])!,
            GenericPatternOutcomeReplayService.Calculate(second, candles, definition, [5])!
        };

        await repository.SaveOutcomesAsync(outcomes, TestContext.Current.CancellationToken);
        await repository.SaveOutcomesAsync(outcomes, TestContext.Current.CancellationToken);

        Assert.Equal(2, (await repository.GetOutcomesAsync(limit: 10,
            cancellationToken: TestContext.Current.CancellationToken)).Count);
    }

    private static UniversalMarketObservation Observation(PatternDirection direction) => new(
        "OBS-GENERIC", 1, "liquidity-sweep", "capture-1.0.0", "LiquiditySweep", "MES", "MESU6", "1m",
        direction, TestData.BaseTime.AddMinutes(-1), TestData.BaseTime, PatternLifecycleState.Detected,
        "test", "{}", [], MarketDataQualityFlags.None, "hash");

    private static decimal Metric(UniversalMarketOutcome outcome, string name, string unit) =>
        outcome.Metrics.Single(x => x.MetricName == name && x.Unit == unit).Value;
}
