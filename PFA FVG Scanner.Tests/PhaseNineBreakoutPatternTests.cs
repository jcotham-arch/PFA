using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Patterns.Breakouts;
using PFA_FVG_Scanner.Domain.Patterns.Liquidity;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseNineBreakoutPatternTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BreakoutRequiresCompletedCloseBeyondPriorRange(bool upper)
    {
        var bars = upper
            ? new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100), Bar("C", 10, 103, 100, 102.5m) }
            : new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100), Bar("C", 10, 100, 97, 97.5m) };
        var observation = Assert.Single(new RangeBreakoutPatternModule().Detect(Context(bars)).Observations);
        var geometry = Assert.IsType<RangeBreakoutGeometry>(observation.Geometry);
        Assert.True(geometry.ClosedBeyondBoundary);
        Assert.Equal(upper ? RangeBoundarySide.Upper : RangeBoundarySide.Lower, geometry.BoundarySide);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedBreakoutRequiresPenetrationAndCloseBackInside(bool upper)
    {
        var bars = upper
            ? new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100), Bar("C", 10, 103, 100, 101) }
            : new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100), Bar("C", 10, 100, 97, 99) };
        var observation = Assert.Single(new FailedBreakoutPatternModule().Detect(Context(bars)).Observations);
        Assert.False(Assert.IsType<RangeBreakoutGeometry>(observation.Geometry).ClosedBeyondBoundary);
    }

    [Fact]
    public void SweepAndFailedBreakoutCoexistWithoutForcedPreference()
    {
        var bars = new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100),
            Bar("C", 10, 103, 100, 101) };
        var context = Context(bars);
        var sweep = Assert.Single(new LiquiditySweepPatternModule().Detect(context).Observations);
        var failed = Assert.Single(new FailedBreakoutPatternModule().Detect(context).Observations);
        Assert.Equal("LiquiditySweep", sweep.PatternType);
        Assert.Equal("FailedBreakout", failed.PatternType);
        Assert.NotEqual(sweep.ObservationId, failed.ObservationId);
    }

    [Fact]
    public void OutsideBarClosingInsideCanFailAtBothBoundaries()
    {
        var bars = new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100),
            Bar("C", 10, 103, 97, 100) };
        Assert.Equal(2, new FailedBreakoutPatternModule().Detect(Context(bars)).Observations.Count);
    }

    [Fact]
    public void NoPenetrationProducesNoBreakoutClassification()
    {
        var bars = new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100),
            Bar("C", 10, 101, 99, 100) };
        Assert.Empty(new RangeBreakoutPatternModule().Detect(Context(bars)).Observations);
        Assert.Empty(new FailedBreakoutPatternModule().Detect(Context(bars)).Observations);
    }

    [Fact]
    public void ReplayAndIncrementalInvocationHaveIdenticalIds()
    {
        var bars = new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100),
            Bar("C", 10, 103, 100, 102.5m) };
        var module = new RangeBreakoutPatternModule();
        var first = Assert.Single(module.Detect(Context(bars)).Observations);
        var second = Assert.Single(module.Detect(Context(bars.ToArray())).Observations);
        Assert.Equal(first.ObservationId, second.ObservationId);
    }

    [Fact]
    public void RangeCannotCrossSessionOrUseConflictedData()
    {
        var bars = new[] { Bar("A", 0, 102, 99, 100, "D1"), Bar("B", 5, 101, 98, 100, "D1"),
            Bar("C", 10, 103, 100, 102.5m, "D2") };
        Assert.False(new RangeBreakoutPatternModule().Detect(Context(bars)).Accepted);
        var normal = new[] { Bar("A", 0, 102, 99, 100), Bar("B", 5, 101, 98, 100), Bar("C", 10, 103, 100, 102.5m) };
        Assert.False(new RangeBreakoutPatternModule().Detect(
            Context(normal, MarketDataQualityFlags.ProviderConflict)).Accepted);
    }

    private static MarketPatternContext Context(CanonicalBar[] bars,
        MarketDataQualityFlags quality = MarketDataQualityFlags.None) =>
        new("MES", "MES-2026-09", "5m", bars[^1].CloseTimeUtc, bars, quality);
    private static CanonicalBar Bar(string id, int minute, decimal high, decimal low, decimal close,
        string session = "DAY-1") => new(id, 1, "MES", "MES-2026-09", "MESU6", "5m",
            TestData.BaseTime.AddMinutes(minute), TestData.BaseTime.AddMinutes(minute + 5), close, high, low,
            close, 10, true, session, new DateOnly(2025, 1, 6), "1", "test",
            CorrectionState.Original, MarketDataQualityFlags.None, TestData.BaseTime, $"HASH-{id}");
}
