using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Patterns.Liquidity;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseEightLiquiditySweepPatternTests
{
    [Fact]
    public void DetectsBuySideSweepAndReclaimWithoutFutureBars()
    {
        var bars = new[] { Bar("A", 0, 101, 99, 100), Bar("B", 5, 102, 100, 101),
            Bar("C", 10, 103, 101, 101.5m) };
        var observation = Assert.Single(Detect(bars).Observations);
        var geometry = Assert.IsType<LiquiditySweepGeometry>(observation.Geometry);
        Assert.Equal(LiquiditySide.BuySide, geometry.LiquiditySide);
        Assert.Equal(102, geometry.ReferenceLevel);
        Assert.Equal(1, geometry.PenetrationDepth);
        Assert.True(geometry.ReclaimedOnDetectionBar);
        Assert.Equal(PatternDirection.Bearish, observation.Direction);
        Assert.Equal(bars[^1].CloseTimeUtc, observation.KnownAtUtc);
    }

    [Fact]
    public void DetectsSellSideSweepAndRetainsFailedReclaimBranch()
    {
        var bars = new[] { Bar("A", 0, 101, 99, 100), Bar("B", 5, 100, 98, 99),
            Bar("C", 10, 99, 97, 97.5m) };
        var geometry = Assert.IsType<LiquiditySweepGeometry>(Assert.Single(Detect(bars).Observations).Geometry);
        Assert.Equal(LiquiditySide.SellSide, geometry.LiquiditySide);
        Assert.False(geometry.ReclaimedOnDetectionBar);
        Assert.Equal(1, geometry.PenetrationDepth);
    }

    [Fact]
    public void EqualLevelsAreCapturedAsReferenceEvidence()
    {
        var bars = new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 102, 100, 101),
            Bar("C", 10, 103, 101, 101) };
        var geometry = Assert.IsType<LiquiditySweepGeometry>(Assert.Single(Detect(bars).Observations).Geometry);
        Assert.Equal(2, geometry.EqualLevelCount);
        Assert.Equal(new[] { "A", "B" }, geometry.ReferenceBarIds);
    }

    [Fact]
    public void OutsideBarCanFactuallySweepBothSides()
    {
        var bars = new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100),
            Bar("C", 10, 103, 97, 100) };
        var result = Detect(bars);
        Assert.Equal(2, result.Observations.Count);
        Assert.Equal(new[] { LiquiditySide.BuySide, LiquiditySide.SellSide },
            result.Observations.Select(x => Assert.IsType<LiquiditySweepGeometry>(x.Geometry).LiquiditySide));
    }

    [Fact]
    public void PriorSessionLevelsAreNotSilentlyMixedIntoCurrentSession()
    {
        var bars = new[] { Bar("A", 0, 110, 90, 100, "DAY-1"), Bar("B", 5, 105, 95, 100, "DAY-1"),
            Bar("C", 10, 102, 98, 100, "DAY-2") };
        var result = Detect(bars);
        Assert.False(result.Accepted);
        Assert.Contains("same trading session", result.RejectionReason);
    }

    [Fact]
    public void HistoricalAndIncrementalInputsYieldSameObservationIdentity()
    {
        var bars = new[] { Bar("A", 0, 101, 99, 100), Bar("B", 5, 102, 100, 101),
            Bar("C", 10, 103, 101, 101.5m) };
        var incremental = Detect(bars);
        var replay = new LiquiditySweepPatternModule().Detect(Context(bars));
        Assert.Equal(Assert.Single(incremental.Observations).ObservationId,
            Assert.Single(replay.Observations).ObservationId);
    }

    [Fact]
    public void QualityAndTimeframeContractsApplyToSweepDetector()
    {
        var bars = new[] { Bar("A", 0, 101, 99, 100), Bar("B", 5, 102, 100, 101),
            Bar("C", 10, 103, 101, 101) };
        var detector = new LiquiditySweepPatternModule();
        Assert.False(detector.Detect(Context(bars, "30m")).Accepted);
        Assert.False(detector.Detect(Context(bars, quality: MarketDataQualityFlags.ProviderConflict)).Accepted);
    }

    private static PatternDetectionResult Detect(CanonicalBar[] bars) =>
        new LiquiditySweepPatternModule().Detect(Context(bars));
    private static MarketPatternContext Context(CanonicalBar[] bars, string timeframe = "5m",
        MarketDataQualityFlags quality = MarketDataQualityFlags.None) =>
        new("MES", "MES-2026-09", timeframe, bars[^1].CloseTimeUtc, bars, quality);
    private static CanonicalBar Bar(string id, int minute, decimal high, decimal low, decimal close,
        string session = "DAY-1") => new(id, 1, "MES", "MES-2026-09", "MESU6", "5m",
            TestData.BaseTime.AddMinutes(minute), TestData.BaseTime.AddMinutes(minute + 5), close, high, low,
            close, 10, true, session, new DateOnly(2025, 1, 6), "1.0.0", "test",
            CorrectionState.Original, MarketDataQualityFlags.None, TestData.BaseTime, $"HASH-{id}");
}
