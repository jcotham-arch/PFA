using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Patterns.Fvg;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseFiveFvgPatternModuleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UniversalModulePreservesLegacyDetectionGeometryAndIdentity(bool bullish)
    {
        var detector = new FvgDetectionService();
        var bars = bullish
            ? new[] { Bar("A", 0, 100, 99, 99.5m), Bar("B", 5, 102, 99, 101), Bar("C", 10, 103, 101, 102) }
            : new[] { Bar("A", 0, 103, 102, 102.5m), Bar("B", 5, 103, 99, 100), Bar("C", 10, 101, 99, 100) };
        var candles = bars.Select(FvgPatternModule.MapCandle).ToArray();
        var legacy = detector.Detect(candles[0], candles[1], candles[2]);
        var context = new MarketPatternContext("MES", "MES-2026-09", "5m", bars[^1].CloseTimeUtc,
            bars, MarketDataQualityFlags.None);
        var universal = new FvgPatternModule(detector).Detect(context);

        Assert.NotNull(legacy);
        var observation = Assert.Single(universal.Observations);
        var geometry = Assert.IsType<PriceZoneGeometry>(observation.Geometry);
        Assert.Equal(legacy.LowerBoundary, geometry.LowerBoundary);
        Assert.Equal(legacy.UpperBoundary, geometry.UpperBoundary);
        Assert.Equal(FvgPatternModule.CreateLegacyObservationId(legacy), observation.ObservationId);
        Assert.Equal(bars.Select(x => x.CanonicalBarId), observation.SourceCanonicalBarIds);
    }

    [Fact]
    public void UniversalModulePreservesNoDetectionResult()
    {
        var bars = new[] { Bar("A", 0, 101, 99, 100), Bar("B", 5, 101, 99, 100), Bar("C", 10, 101, 99, 100) };
        var result = new FvgPatternModule(new FvgDetectionService()).Detect(
            new("MES", "MES-2026-09", "5m", bars[^1].CloseTimeUtc, bars, MarketDataQualityFlags.None));
        Assert.True(result.Accepted);
        Assert.Empty(result.Observations);
    }

    [Fact]
    public async Task LegacyFvgPersistenceDualWritesIdempotentUniversalReference()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new ObservationRepository(factory.Database);
        var fvg = new FairValueGap
        {
            Symbol = "MESU6", Timeframe = "5m", Direction = FvgDirection.Bullish,
            FormationTimeUtc = TestData.BaseTime, LowerBoundary = 100, UpperBoundary = 101, GapSize = 1
        };
        repository.SaveFvg(fvg);
        repository.SaveFvg(fvg);
        Assert.Equal(1, await CountAsync(factory.Database, "Observations"));
        Assert.Equal(1, await CountAsync(factory.Database, "UniversalPatternObservationReferences"));
    }

    private static CanonicalBar Bar(string id, int minute, decimal high, decimal low, decimal close) =>
        new(id, 1, "MES", "MES-2026-09", "MESU6", "5m", TestData.BaseTime.AddMinutes(minute),
            TestData.BaseTime.AddMinutes(minute + 5), close, high, low, close, 10, true,
            "MES|2025-01-06|LEGACY-UTC", new DateOnly(2025, 1, 6), "1.0.0", "test",
            CorrectionState.Original, MarketDataQualityFlags.None, TestData.BaseTime, $"HASH-{id}");

    private static async Task<int> CountAsync(PfaDatabase database, string table)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
