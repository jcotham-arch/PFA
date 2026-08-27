using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseSixUniversalMarketRecordTests
{
    [Fact]
    public async Task LegacyFvgDualWritePreservesIdentityPayloadAndVersionDiscrepancy()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var fvg = TestData.BullishFvg();
        new ObservationRepository(factory.Database).SaveFvg(fvg);
        var records = await new UniversalMarketRecordRepository(factory.Database)
            .GetObservationsAsync("fvg", cancellationToken: TestContext.Current.CancellationToken);

        var record = Assert.Single(records);
        Assert.StartsWith("FVG-", record.ObservationId);
        Assert.Equal("legacy-1.0.0", record.ModuleVersion);
        Assert.Equal("pfa.fvg.observation/1.0", record.PayloadSchema);
        Assert.Equal(fvg.Symbol, record.InstrumentId);
        Assert.Contains("LowerBoundary", record.PayloadJson);
    }

    [Fact]
    public async Task UniversalObservationIsRevisionedImmutableAndIdempotent()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new UniversalMarketRecordRepository(factory.Database);
        var record = Observation("OBS-1", 1, "payload-a");
        await repository.SaveObservationAsync(record, TestContext.Current.CancellationToken);
        await repository.SaveObservationAsync(record, TestContext.Current.CancellationToken);
        await repository.SaveObservationAsync(Observation("OBS-1", 2, "payload-b"),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, await CountAsync(factory.Database, "UniversalMarketObservations"));
        Assert.Equal(2, await CountAsync(factory.Database, "UniversalObservationLifecycleEvents"));
    }

    [Fact]
    public async Task FvgOutcomeDualWriteStoresMetricsAndFirstEventChronology()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var fvg = TestData.BullishFvg();
        new ObservationRepository(factory.Database).SaveFvg(fvg);
        await InsertLegacySetupAsync(factory.Database, fvg);
        var outcome = Outcome(fvg);
        var repository = new FvgOutcomeRepository(factory.Database);
        await repository.SaveAsync(outcome, TestContext.Current.CancellationToken);
        await repository.SaveAsync(outcome, TestContext.Current.CancellationToken);

        Assert.Equal(1, await CountAsync(factory.Database, "Outcomes"));
        Assert.Equal(1, await CountAsync(factory.Database, "UniversalMarketOutcomes"));
        Assert.Equal(6, await CountAsync(factory.Database, "UniversalOutcomeMetrics"));
        Assert.Equal(3, await CountAsync(factory.Database, "UniversalOutcomeEvents"));
        Assert.Equal(new[] { "first-touch", "fill-25", "full-fill" },
            await ValuesAsync(factory.Database,
                "SELECT EventType FROM UniversalOutcomeEvents ORDER BY Ordinal"));
        Assert.Equal("1.1.0", await ScalarStringAsync(factory.Database,
            "SELECT OutcomeVersion FROM UniversalMarketOutcomes"));
    }

    [Fact]
    public async Task ConfigurableOutcomeHorizonCanBeStoredWithoutFvgSchemaChange()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var observation = Observation("GENERIC-1", 1, "{}");
        var repository = new UniversalMarketRecordRepository(factory.Database);
        await repository.SaveObservationAsync(observation, TestContext.Current.CancellationToken);
        await repository.SaveOutcomeAsync(new("OUT-GENERIC", observation.ObservationId, "1.0.0",
            TestData.BaseTime.AddMinutes(120), 120, "pfa.generic/1.0", "{}",
            [new("return", 120, 3.25m, "points")], [], MarketDataQualityFlags.None),
            TestContext.Current.CancellationToken);

        Assert.Equal("120", await ScalarStringAsync(factory.Database,
            "SELECT HorizonMinutes FROM UniversalOutcomeMetrics"));
    }

    [Fact]
    public async Task InitializationBackfillsLegacyObservationsWithoutChangingLegacyCounts()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var legacy = new ObservationRepository(factory.Database);
        legacy.SaveFvg(TestData.BullishFvg());
        var before = await CountAsync(factory.Database, "Observations");
        await factory.Database.InitializeAsync();
        Assert.Equal(before, await CountAsync(factory.Database, "Observations"));
        Assert.Equal(before, await CountAsync(factory.Database, "UniversalMarketObservations"));
    }

    private static UniversalMarketObservation Observation(string id, int revision, string payload) =>
        new(id, revision, "test", "1.0.0", "TestPattern", "MES", null, "5m",
            PatternDirection.Bullish, TestData.BaseTime, TestData.BaseTime.AddMinutes(5),
            PatternLifecycleState.Detected, "pfa.test/1.0", JsonSerializer.Serialize(payload), [],
            MarketDataQualityFlags.None, $"HASH-{revision}");

    private static FvgOutcome Outcome(FairValueGap fvg) => new()
    {
        OutcomeId = Guid.NewGuid(), FvgId = fvg.Id, Symbol = fvg.Symbol, Timeframe = fvg.Timeframe,
        Direction = fvg.Direction, FormationTimeUtc = fvg.FormationTimeUtc,
        ConfirmationTimeUtc = fvg.FormationTimeUtc.AddMinutes(5), LowerBoundary = fvg.LowerBoundary,
        UpperBoundary = fvg.UpperBoundary, GapSize = fvg.GapSize, Midpoint = fvg.Midpoint,
        FirstTouchTimeUtc = fvg.FormationTimeUtc.AddMinutes(10),
        TwentyFivePercentFillTimeUtc = fvg.FormationTimeUtc.AddMinutes(12),
        FullFillTimeUtc = fvg.FormationTimeUtc.AddMinutes(20), Return5Minutes = 1,
        Return15Minutes = 2, Return30Minutes = 3, Return60Minutes = 4,
        MaximumFavorableExcursion = 5, MaximumAdverseExcursion = -1,
        EvaluatedThroughUtc = fvg.FormationTimeUtc.AddMinutes(60), MinuteCandlesEvaluated = 60,
        EngineVersion = "1.1.0"
    };

    private static async Task<int> CountAsync(PfaDatabase database, string table) =>
        Convert.ToInt32(await ScalarAsync(database, $"SELECT COUNT(*) FROM {table}"));
    private static async Task InsertLegacySetupAsync(PfaDatabase database, FairValueGap fvg)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Setups (SetupId, Symbol, Timeframe, SetupType, Direction,
                FormationTimeUtc, EngineVersion, SnapshotJson, CreatedAtUtc)
            VALUES ($id, $symbol, $timeframe, 'FVG', $direction, $formation,
                '1.0.0', '{}', $createdAt);
            """;
        command.Parameters.AddWithValue("$id", fvg.Id.ToString());
        command.Parameters.AddWithValue("$symbol", fvg.Symbol);
        command.Parameters.AddWithValue("$timeframe", fvg.Timeframe);
        command.Parameters.AddWithValue("$direction", fvg.Direction.ToString());
        command.Parameters.AddWithValue("$formation", fvg.FormationTimeUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<string[]> ValuesAsync(PfaDatabase database, string sql)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        var values = new List<string>(); await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0)); return values.ToArray();
    }
    private static async Task<string> ScalarStringAsync(PfaDatabase database, string sql) =>
        Convert.ToString(await ScalarAsync(database, sql))!;
    private static async Task<object?> ScalarAsync(PfaDatabase database, string sql)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
