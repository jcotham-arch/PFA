using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseFourMarketPatternContractTests
{
    [Fact]
    public void ContractCreatesDeterministicPatternIds()
    {
        var first = MarketPatternContract.CreateObservationId("test", "1.0.0", "MES", "5m",
            TestData.BaseTime, "bullish-zone");
        var second = MarketPatternContract.CreateObservationId("TEST", "1.0.0", "mes", "5M",
            TestData.BaseTime, "BULLISH-ZONE");
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void ContractRejectsUnsupportedResolution()
    {
        var detector = new TestDetector();
        var context = Context("15m", MarketDataQualityFlags.None);
        Assert.Contains("not supported", MarketPatternContract.Validate(detector, context));
    }

    [Theory]
    [InlineData(MarketDataQualityFlags.Incomplete)]
    [InlineData(MarketDataQualityFlags.InvalidOhlc)]
    [InlineData(MarketDataQualityFlags.UnresolvedInstrument)]
    [InlineData(MarketDataQualityFlags.ProviderConflict)]
    public void ContractRejectsIneligibleQuality(MarketDataQualityFlags quality)
    {
        Assert.Contains("not eligible", MarketPatternContract.Validate(new TestDetector(), Context("5m", quality)));
    }

    [Fact]
    public void ContractRejectsFutureBars()
    {
        var context = Context("5m", MarketDataQualityFlags.None) with { AsOfUtc = TestData.BaseTime };
        Assert.Contains("Future bars", MarketPatternContract.Validate(new TestDetector(), context));
    }

    [Fact]
    public void SecondDetectorCanUseContractsWithoutFvgTypes()
    {
        var result = new TestDetector().Detect(Context("5m", MarketDataQualityFlags.None));
        Assert.True(result.Accepted);
        Assert.Single(result.Observations);
        Assert.IsType<PriceZoneGeometry>(result.Observations[0].Geometry);
    }

    [Fact]
    public void ModuleInventoryDocumentsLegacyBoundaryWithoutWrappingItEarly()
    {
        var modules = new MarketPatternModuleRegistry().GetAll();
        Assert.Equal(new[] { "fvg", "liquidity-sweep", "range-breakout", "failed-breakout" },
            modules.Where(x => x.Version != "definition-pending").Select(x => x.ModuleId));
        Assert.Equal(7, modules.Count(x => x.Version == "definition-pending"));
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(5, 12)]
    [InlineData(15, 4)]
    [InlineData(60, 1)]
    public void ChartAggregationSupportsRequiredTimeframes(int minutes, int expected)
    {
        var source = Enumerable.Range(0, 60).Select(i => new Candle
        {
            Symbol = "MESU6", Timeframe = "1m", OpenTimeUtc = TestData.BaseTime.AddMinutes(i),
            Open = 100 + i, High = 101 + i, Low = 99 + i, Close = 100.5m + i,
            Volume = 10, IsClosed = true
        }).ToArray();
        var bars = MarketChartService.Aggregate(source, minutes);
        Assert.Equal(expected, bars.Count);
        Assert.Equal(100m, bars[0].Open);
        Assert.Equal(600m / expected, bars[0].Volume);
        Assert.All(bars, x => Assert.True(x.IsComplete));
    }

    private static MarketPatternContext Context(string timeframe, MarketDataQualityFlags quality)
    {
        var bar = new CanonicalBar("A", 1, "MES", "MES-TEST", "MESU6", "5m", TestData.BaseTime,
            TestData.BaseTime.AddMinutes(5), 100, 102, 99, 101, 10, true,
            "MES|2025-01-06|LEGACY-UTC", new DateOnly(2025, 1, 6), "1.0.0", "test",
            CorrectionState.Original, quality, TestData.BaseTime, "HASH");
        return new("MES", "MES-TEST", timeframe, TestData.BaseTime.AddMinutes(5), [bar], quality);
    }

    private sealed class TestDetector : IMarketPatternDetector
    {
        public string ModuleId => "test-zone";
        public string ModuleVersion => "1.0.0";
        public IReadOnlySet<string> SupportedTimeframes { get; } = new HashSet<string> { "5m" };

        public PatternDetectionResult Detect(MarketPatternContext context)
        {
            var rejection = MarketPatternContract.Validate(this, context);
            if (rejection is not null) return PatternDetectionResult.Rejected(rejection);
            var time = context.Bars[^1].CloseTimeUtc;
            var observation = new MarketPatternObservation(
                MarketPatternContract.CreateObservationId(ModuleId, ModuleVersion, context.InstrumentId,
                    context.Timeframe, time, "zone"), ModuleId, ModuleVersion, "PriceZone",
                context.InstrumentId, context.ContractId, context.Timeframe, PatternDirection.Neutral,
                time, time, PatternLifecycleState.Detected, new PriceZoneGeometry(100, 101),
                [context.Bars[^1].CanonicalBarId], context.QualityFlags);
            return PatternDetectionResult.Success([observation]);
        }
    }
}
