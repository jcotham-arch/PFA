using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;

namespace PFA_FVG_Scanner.Tests;

public sealed class DatabaseGoldenMasterTests
{
    [Fact]
    public async Task CandleWritesAreIdempotentPerProviderButPreserveProviderConflicts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new CandleRepository(factory.Database);
        var candle = TestData.Candle(0, 100, 101, 99, 100);

        await repository.SaveAsync(candle, "Massive", cancellationToken);
        await repository.SaveAsync(candle, "Massive", cancellationToken);
        await repository.SaveAsync(candle, "Tradovate", cancellationToken);

        Assert.Equal(2L, await ScalarAsync(factory.Database, "SELECT COUNT(*) FROM Candles"));
        Assert.Equal(2L, await ScalarAsync(factory.Database,
            "SELECT COUNT(DISTINCT Provider) FROM Candles"));
    }

    [Fact]
    public async Task ConcurrentDuplicateCandleWritesProduceOneRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new CandleRepository(factory.Database);
        var candle = TestData.Candle(0, 100, 101, 99, 100);
        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => repository.SaveAsync(candle, "Massive", cancellationToken)));
        Assert.Equal(1L, await ScalarAsync(factory.Database, "SELECT COUNT(*) FROM Candles"));
    }

    [Fact]
    public async Task FvgObservationUsesDeterministicIdentityAndVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new ObservationRepository(factory.Database);
        var fvg = TestData.BullishFvg();
        repository.SaveFvg(fvg);
        fvg.Id = Guid.NewGuid();
        repository.SaveFvg(fvg);

        Assert.Equal(1, await repository.GetFvgCountAsync(cancellationToken));
        await using var connection = factory.Database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ObservationId, EngineVersion FROM Observations";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        Assert.StartsWith("FVG-", reader.GetString(0));
        Assert.Equal("1.0.0", reader.GetString(1));
    }

    private static async Task<long> ScalarAsync(PfaDatabase database, string sql)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}

internal sealed class TestDatabaseFactory : IDisposable
{
    private readonly string _root;
    internal PfaDatabase Database { get; }

    private TestDatabaseFactory(string root)
    {
        _root = root;
        Database = new PfaDatabase(new TestWebHostEnvironment { ContentRootPath = root });
    }

    internal static async Task<TestDatabaseFactory> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pfa-golden-master-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var factory = new TestDatabaseFactory(root);
        await factory.Database.InitializeAsync();
        return factory;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "PFA FVG Scanner.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
