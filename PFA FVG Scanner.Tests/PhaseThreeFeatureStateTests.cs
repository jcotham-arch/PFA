using System.Globalization;
using PFA_FVG_Scanner.Domain.Features;
using PFA_FVG_Scanner.Domain.MarketState;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseThreeFeatureStateTests
{
    [Fact]
    public void RegistrySeparatesPredictorsFromOutcomesAndDiagnostics()
    {
        var registry = new FeatureDefinitionRegistry();
        Assert.Equal(FeatureRole.Predictor, registry.Find("fvg.gap_size_points")!.Role);
        Assert.Equal(FeatureRole.OutcomeLabel, registry.Find("outcome.realized_r")!.Role);
        Assert.Equal(FeatureRole.Diagnostic, registry.Find("diagnostic.maximum_favorable_r")!.Role);
        Assert.All(registry.GetAll(), x => Assert.Equal("1.0.0", x.Version));
    }

    [Fact]
    public void LegacyAdapterPreservesValuesAndAvailabilityTimesWithoutLeakage()
    {
        var record = TestData.Feature(0, 0, 1.5m);
        record.ConfirmationTimeUtc = TestData.BaseTime;
        record.EntryTimeUtc = TestData.BaseTime.AddMinutes(10);
        record.GapSizePoints = 1.5m;
        record.RiskTicks = 6m;
        record.TargetHitTimeUtc = TestData.BaseTime.AddMinutes(20);
        record.MaximumFavorableR = 2m;
        record.ScenarioEngineVersion = "1.1.0";
        var adapter = new LegacyFvgFeatureAdapter();
        var values = adapter.Adapt(record);

        Assert.Equal("1.5", values.Single(x => x.FeatureDefinitionId == "fvg.gap_size_points").Value);
        Assert.Equal(TestData.BaseTime.AddMinutes(10), values.Single(x => x.FeatureDefinitionId == "execution.risk_ticks").KnownAtUtc);
        Assert.Equal(TestData.BaseTime.AddMinutes(20), values.Single(x => x.FeatureDefinitionId == "outcome.realized_r").KnownAtUtc);
        var predictorsAtConfirmation = adapter.AvailablePredictors(record, TestData.BaseTime, new FeatureDefinitionRegistry());
        Assert.Equal(5, predictorsAtConfirmation.Count);
        Assert.DoesNotContain(predictorsAtConfirmation, x => x.FeatureDefinitionId.StartsWith("outcome."));
        Assert.DoesNotContain(predictorsAtConfirmation, x => x.FeatureDefinitionId.StartsWith("diagnostic."));
    }

    [Fact]
    public void LegacyAdapterIsDeterministicAndPropagatesQuality()
    {
        var record = TestData.Feature(0, 0, -1m);
        record.ConfirmationTimeUtc = TestData.BaseTime;
        record.EntryTimeUtc = TestData.BaseTime.AddMinutes(5);
        record.StopHitTimeUtc = TestData.BaseTime.AddMinutes(8);
        record.ExecutionPricesValid = false;
        var adapter = new LegacyFvgFeatureAdapter();
        var first = adapter.Adapt(record);
        var second = adapter.Adapt(record);
        Assert.Equal(first.Select(x => x.FeatureValueId), second.Select(x => x.FeatureValueId));
        Assert.True(first.Single(x => x.FeatureDefinitionId == "outcome.realized_r")
            .QualityFlags.HasFlag(MarketDataQualityFlags.InvalidOhlc));
    }

    [Fact]
    public void MarketStateExcludesFutureBarsAndFutureEffectiveRevisions()
    {
        var asOf = TestData.BaseTime.AddMinutes(10);
        var bars = new[]
        {
            Bar("A", 1, 0, 100m, TestData.BaseTime),
            Bar("B", 1, 5, 101m, TestData.BaseTime),
            Bar("C", 1, 10, 999m, TestData.BaseTime), // closes after as-of
            Bar("A", 2, 0, 500m, asOf.AddMinutes(1)) // revision not yet effective
        };
        var snapshot = new MarketStateEngine().Build("MES", "MES-TEST", asOf, "rev-1", bars);
        Assert.Equal("101", snapshot.Facts.Single(x => x.FeatureDefinitionId == "market.close").Value);
        Assert.Equal("2", snapshot.Facts.Single(x => x.FeatureDefinitionId == "market.bar_count").Value);
        Assert.Equal(2, snapshot.SourceCanonicalBarIds.Count);
    }

    [Fact]
    public void MarketStateRecomputationIsDeterministicAndCarriesQuality()
    {
        var bar = Bar("A", 1, 0, 100m, TestData.BaseTime) with
        { QualityFlags = MarketDataQualityFlags.ProviderConflict };
        var engine = new MarketStateEngine();
        var asOf = TestData.BaseTime.AddMinutes(5);
        var first = engine.Build("MES", "MES-TEST", asOf, "rev-1", new[] { bar });
        var second = engine.Build("MES", "MES-TEST", asOf, "rev-1", new[] { bar });
        Assert.Equal(first.MarketStateSnapshotId, second.MarketStateSnapshotId);
        Assert.Equal(first.Facts.Select(x => x.FeatureValueId), second.Facts.Select(x => x.FeatureValueId));
        Assert.True(first.QualityFlags.HasFlag(MarketDataQualityFlags.ProviderConflict));
    }

    [Fact]
    public async Task FeatureSchemaAndPersistenceAreIdempotent()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new FeatureStateRepository(factory.Database);
        var registry = new FeatureDefinitionRegistry();
        await repository.SaveDefinitionsAsync(registry.GetAll(), TestContext.Current.CancellationToken);
        await repository.SaveDefinitionsAsync(registry.GetAll(), TestContext.Current.CancellationToken);
        var snapshot = new MarketStateEngine().Build("MES", "MES-TEST", TestData.BaseTime.AddMinutes(5),
            "rev-1", new[] { Bar("A", 1, 0, 100m, TestData.BaseTime) });
        await repository.SaveSnapshotAsync(snapshot, TestContext.Current.CancellationToken);
        await repository.SaveSnapshotAsync(snapshot, TestContext.Current.CancellationToken);
        Assert.Equal(registry.GetAll().Count, await CountAsync(factory.Database, "FeatureDefinitions"));
        Assert.Equal(1, await CountAsync(factory.Database, "MarketStateSnapshots"));
        Assert.Equal(snapshot.Facts.Count, await CountAsync(factory.Database, "FeatureValues"));
    }

    private static CanonicalBar Bar(string id, int revision, int minute, decimal close, DateTime effective) => new(
        id, revision, "MES", "MES-TEST", "MES", "5m", TestData.BaseTime.AddMinutes(minute),
        TestData.BaseTime.AddMinutes(minute + 5), close, close + 1, close - 1, close, 10m, true,
        "MES|2025-01-06|LEGACY-UTC", new DateOnly(2025, 1, 6), "1.0.0", "test",
        CorrectionState.Original, MarketDataQualityFlags.None, effective, $"HASH-{id}-{revision}");

    private static async Task<int> CountAsync(PfaDatabase database, string table)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
    }
}
