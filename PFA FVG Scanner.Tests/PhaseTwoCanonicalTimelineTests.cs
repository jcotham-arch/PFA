using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Contracts;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseTwoCanonicalTimelineTests
{
    [Fact]
    public async Task EquivalentLiveAndHistoricalInputsConvergeOnOneCanonicalRevision()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new CanonicalTimelineRepository(factory.Database);
        var canonicalizer = Canonicalizer();
        var candle = TestData.Candle(0, 100, 101, 99, 100);
        var live = canonicalizer.Canonicalize(Request(candle, "Massive", "LIVE", "live-run"));
        var historical = canonicalizer.Canonicalize(Request(candle, "Massive Historical Backfill", "BACKFILL", "history-run"));
        var first = await repository.WriteAsync(live, TestContext.Current.CancellationToken);
        var second = await repository.WriteAsync(historical, TestContext.Current.CancellationToken);

        Assert.Equal(first.Bar.CanonicalBarId, second.Bar.CanonicalBarId);
        Assert.True(first.CreatedRevision);
        Assert.True(second.WasEquivalentDuplicate);
        Assert.False(second.CreatedRevision);
        Assert.Single(await repository.GetHistoryAsync(first.Bar.CanonicalBarId,
            TestContext.Current.CancellationToken));
        Assert.Equal(2L, await ScalarAsync(factory.Database, "SELECT COUNT(*) FROM CanonicalBarSources"));
    }

    [Fact]
    public async Task SameProviderCorrectionCreatesPreservedRevision()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new CanonicalTimelineRepository(factory.Database);
        var canonicalizer = Canonicalizer();
        var original = TestData.Candle(0, 100, 101, 99, 100);
        var corrected = TestData.Candle(0, 100, 102, 99, 101);
        var first = await repository.WriteAsync(canonicalizer.Canonicalize(Request(original, "Massive", "LIVE", "run-1")),
            TestContext.Current.CancellationToken);
        var second = await repository.WriteAsync(canonicalizer.Canonicalize(Request(corrected, "Massive", "LIVE", "run-2")),
            TestContext.Current.CancellationToken);

        Assert.Equal(first.Bar.CanonicalBarId, second.Bar.CanonicalBarId);
        Assert.Equal(2, second.Bar.Revision);
        Assert.Equal(CorrectionState.CorrectedRevision, second.Bar.CorrectionState);
        Assert.True(second.Bar.QualityFlags.HasFlag(MarketDataQualityFlags.Corrected));
        Assert.Equal(2, (await repository.GetHistoryAsync(first.Bar.CanonicalBarId,
            TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task ConflictingProvidersRemainExplicitAndNeverOverwrite()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new CanonicalTimelineRepository(factory.Database);
        var canonicalizer = Canonicalizer();
        var massive = TestData.Candle(0, 100, 101, 99, 100);
        var tradovate = TestData.Candle(0, 100, 103, 99, 102);
        var first = await repository.WriteAsync(canonicalizer.Canonicalize(Request(massive, "Massive", "LIVE", "run-1")),
            TestContext.Current.CancellationToken);
        var second = await repository.WriteAsync(canonicalizer.Canonicalize(Request(tradovate, "Tradovate", "LIVE", "run-2")),
            TestContext.Current.CancellationToken);

        Assert.True(second.WasProviderConflict);
        Assert.Equal(CorrectionState.ProviderConflict, second.Bar.CorrectionState);
        Assert.True(second.Bar.QualityFlags.HasFlag(MarketDataQualityFlags.ProviderConflict));
        var history = await repository.GetHistoryAsync(first.Bar.CanonicalBarId,
            TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 101m, 103m }, history.Select(x => x.High).ToArray());
    }

    [Fact]
    public void QualityFlagsMissingIdentityInvalidOhlcAndLegacySession()
    {
        var candle = TestData.Candle(0, 100, 99, 101, 100, closed: false, symbol: "UNKNOWN");
        var result = Canonicalizer(false).Canonicalize(Request(candle, "Unknown", "LIVE", "run"));
        Assert.True(result.Bar.QualityFlags.HasFlag(MarketDataQualityFlags.Incomplete));
        Assert.True(result.Bar.QualityFlags.HasFlag(MarketDataQualityFlags.InvalidOhlc));
        Assert.True(result.Bar.QualityFlags.HasFlag(MarketDataQualityFlags.UnresolvedInstrument));
        Assert.True(result.Bar.QualityFlags.HasFlag(MarketDataQualityFlags.UnresolvedContract));
        Assert.True(result.Bar.QualityFlags.HasFlag(MarketDataQualityFlags.LegacySession));
    }

    [Fact]
    public async Task SchemaMigrationIsIdempotentAndDoesNotAlterLegacyCounts()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        await factory.Database.InitializeAsync();
        Assert.Equal(1L, await ScalarAsync(factory.Database,
            "SELECT COUNT(*) FROM CanonicalMigrationJournal WHERE MigrationId='PHASE2_CANONICAL_TIMELINE_1'"));
        Assert.Equal(0L, await ScalarAsync(factory.Database, "SELECT COUNT(*) FROM Candles"));
        Assert.Equal(0L, await ScalarAsync(factory.Database, "SELECT COUNT(*) FROM RawMarketEvents"));
    }

    [Fact]
    public async Task ConcurrentEquivalentWritesAreIdempotent()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new CanonicalTimelineRepository(factory.Database);
        var candidate = Canonicalizer().Canonicalize(Request(
            TestData.Candle(0, 100, 101, 99, 100), "Massive", "LIVE", "same-run"));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            repository.WriteAsync(candidate, TestContext.Current.CancellationToken)));
        Assert.Equal(1L, await ScalarAsync(factory.Database, "SELECT COUNT(*) FROM CanonicalBars"));
        Assert.Equal(1L, await ScalarAsync(factory.Database, "SELECT COUNT(*) FROM CanonicalBarSources"));
    }

    private static CanonicalBarCanonicalizer Canonicalizer(bool mapContract = true)
    {
        IEnumerable<ProviderContractMapping> mappings = mapContract
            ? new[] { "Massive", "Massive Historical Backfill", "Tradovate" }.Select(provider =>
                new ProviderContractMapping(provider, "MES",
                    new FuturesContract("MES-TEST", "MES", "MES", 2026, 9, "test")))
            : Array.Empty<ProviderContractMapping>();
        return new(new InstrumentDefinitionRegistry(), new ContractResolver(mappings),
            new LegacyUtcTradingSessionService());
    }

    private static CanonicalizationRequest Request(Candle candle, string provider,
        string type, string run) => new(candle, provider, candle.Symbol, type,
        candle.OpenTimeUtc, candle.OpenTimeUtc.AddSeconds(1), "test", run);

    private static async Task<long> ScalarAsync(PfaDatabase database, string sql)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
