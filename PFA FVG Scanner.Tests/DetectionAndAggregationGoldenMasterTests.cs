namespace PFA_FVG_Scanner.Tests;

public sealed class DetectionAndAggregationGoldenMasterTests
{
    [Fact]
    public void DetectsBullishGapAtMinimumThreshold()
    {
        var result = new FvgDetectionService().Detect(
            TestData.Candle(0, 99, 100, 98, 99, "5m"),
            TestData.Candle(5, 99, 104, 99, 103, "5m"),
            TestData.Candle(10, 101, 103, 100.5m, 102, "5m"));

        Assert.NotNull(result);
        Assert.Equal(FvgDirection.Bullish, result.Direction);
        Assert.Equal((100m, 100.5m, .5m),
            (result.LowerBoundary, result.UpperBoundary, result.GapSize));
        Assert.Equal(TestData.BaseTime.AddMinutes(10), result.FormationTimeUtc);
    }

    [Fact]
    public void DetectsBearishGapAndRejectsSubThresholdOrInvalidInputs()
    {
        var detector = new FvgDetectionService();
        var bearish = detector.Detect(
            TestData.Candle(0, 103, 104, 102, 103, "5m"),
            TestData.Candle(5, 103, 103, 98, 99, "5m"),
            TestData.Candle(10, 100, 101.5m, 99, 100, "5m"));

        Assert.NotNull(bearish);
        Assert.Equal(FvgDirection.Bearish, bearish.Direction);
        Assert.Equal((101.5m, 102m, .5m),
            (bearish.LowerBoundary, bearish.UpperBoundary, bearish.GapSize));
        Assert.Null(detector.Detect(
            TestData.Candle(0, 99, 100, 98, 99, "5m"),
            TestData.Candle(5, 99, 101, 99, 100, "5m"),
            TestData.Candle(10, 100, 101, 100.25m, 100, "5m")));
        Assert.Null(detector.Detect(
            TestData.Candle(0, 99, 100, 98, 99, "5m", closed: false),
            TestData.Candle(5, 99, 104, 99, 103, "5m"),
            TestData.Candle(10, 101, 103, 100.5m, 102, "5m")));
    }

    [Fact]
    public void AggregatesOutOfOrderMinutesWithDeterministicOhlcv()
    {
        var aggregator = new FiveMinuteCandleAggregator();
        Candle? result = null;
        foreach (var minute in new[] { 3, 0, 4, 1, 2 })
            result = aggregator.AddMinuteCandle(
                TestData.Candle(minute, 100 + minute, 101 + minute,
                    99 + minute, 100.5m + minute, volume: minute + 1));

        Assert.NotNull(result);
        Assert.Equal("5m", result.Timeframe);
        Assert.Equal(TestData.BaseTime, result.OpenTimeUtc);
        Assert.Equal((100m, 105m, 99m, 104.5m, 15m),
            (result.Open, result.High, result.Low, result.Close, result.Volume));
    }

    [Fact]
    public void AggregatorIgnoresDuplicatesWrongTimeframesAndIncompleteCandles()
    {
        var aggregator = new FiveMinuteCandleAggregator();
        var first = TestData.Candle(0, 100, 101, 99, 100);
        Assert.Null(aggregator.AddMinuteCandle(first));
        Assert.Null(aggregator.AddMinuteCandle(first));
        Assert.Null(aggregator.AddMinuteCandle(TestData.Candle(1, 100, 101, 99, 100, "5m")));
        Assert.Null(aggregator.AddMinuteCandle(TestData.Candle(1, 100, 101, 99, 100, closed: false)));
        foreach (var minute in new[] { 1, 2, 3 })
            Assert.Null(aggregator.AddMinuteCandle(TestData.Candle(minute, 100, 101, 99, 100)));
        Assert.NotNull(aggregator.AddMinuteCandle(TestData.Candle(4, 100, 101, 99, 100)));
    }

    [Fact]
    public async Task ConcurrentAggregationEmitsExactlyOneCompleteBucket()
    {
        var aggregator = new FiveMinuteCandleAggregator();
        var inputs = Enumerable.Range(0, 5)
            .SelectMany(i => Enumerable.Repeat(TestData.Candle(i, 100, 101, 99, 100), 4));
        var results = await Task.WhenAll(inputs.Select(c => Task.Run(() => aggregator.AddMinuteCandle(c))));
        Assert.Single(results, x => x is not null);
    }

    [Fact]
    public void AggregatorCurrentlyCountsDistinctTimestampsRatherThanExpectedMinuteOffsets()
    {
        var aggregator = new FiveMinuteCandleAggregator();
        Candle? result = null;
        foreach (var seconds in new[] { 0, 10, 20, 30, 40 })
        {
            var candle = TestData.Candle(0, 100, 101, 99, 100);
            candle.OpenTimeUtc = candle.OpenTimeUtc.AddSeconds(seconds);
            result = aggregator.AddMinuteCandle(candle);
        }
        Assert.NotNull(result);
        Assert.Equal(TestData.BaseTime, result.OpenTimeUtc);
    }
}
