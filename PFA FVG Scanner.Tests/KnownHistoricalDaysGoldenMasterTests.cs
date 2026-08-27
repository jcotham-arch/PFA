using System.Text.Json;

namespace PFA_FVG_Scanner.Tests;

public sealed class KnownHistoricalDaysGoldenMasterTests
{
    [Fact]
    public void CuratedHistoricalDaysReplayToGoldenLifecycleOutputs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "known-replay-days.json");
        var days = JsonSerializer.Deserialize<List<ReplayDay>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(days.Count >= 3);
        var service = new HistoricalFvgReplayService();

        foreach (var day in days)
        {
            var fvg = new FairValueGap
            {
                Id = Guid.Parse(day.FvgId), Symbol = "MES", Timeframe = "5m",
                Direction = Enum.Parse<FvgDirection>(day.Direction),
                FormationTimeUtc = DateTime.Parse(day.FormationTimeUtc).ToUniversalTime(),
                LowerBoundary = day.LowerBoundary, UpperBoundary = day.UpperBoundary,
                GapSize = day.UpperBoundary - day.LowerBoundary
            };
            var candles = day.Candles.Select(x => new Candle
            {
                Symbol = "MES", Timeframe = "1m", IsClosed = true,
                OpenTimeUtc = DateTime.Parse(x.Time).ToUniversalTime(),
                Open = x.Open, High = x.High, Low = x.Low, Close = x.Close
            }).ToArray();
            var result = service.Evaluate(fvg, candles);
            Assert.Equal(day.ExpectedLifecycle, result.LifecycleStatus.ToString());
            Assert.Equal(day.ExpectedMinuteCandles, result.MinuteCandlesEvaluated);
            Assert.Equal(day.ExpectedFullFill, result.WasFullyFilled);
        }
    }

    private sealed record ReplayDay(string FvgId, string Direction, string FormationTimeUtc,
        decimal LowerBoundary, decimal UpperBoundary, List<ReplayCandle> Candles,
        string ExpectedLifecycle, int ExpectedMinuteCandles, bool ExpectedFullFill);
    private sealed record ReplayCandle(string Time, decimal Open, decimal High, decimal Low, decimal Close);
}
