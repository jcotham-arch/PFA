using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/product")]
public sealed class ProductOverviewController : ControllerBase
{
    private readonly PfaDatabase _database;
    public ProductOverviewController(PfaDatabase database) => _database = database;

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            mode = "Research",
            canActivateStrategy = false,
            legacy = new
            {
                candles = await CountAsync(connection, "Candles", cancellationToken),
                fvgObservations = await CountAsync(connection,
                    "Observations", cancellationToken, "ObservationType = 'FVG'")
            },
            canonical = new
            {
                bars = await CountAsync(connection, "CanonicalBars", cancellationToken),
                sources = await CountAsync(connection, "CanonicalBarSources", cancellationToken),
                providerConflicts = await CountAsync(connection, "CanonicalBars", cancellationToken,
                    "(QualityFlags & 32) != 0"),
                coverage = await GetCanonicalCoverageAsync(connection, cancellationToken)
            },
            features = new
            {
                definitions = await CountAsync(connection, "FeatureDefinitions", cancellationToken),
                values = await CountAsync(connection, "FeatureValues", cancellationToken),
                stateSnapshots = await CountAsync(connection, "MarketStateSnapshots", cancellationToken)
            }
        });
    }

    private static async Task<IReadOnlyList<object>> GetCanonicalCoverageAsync(SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT COALESCE(r.InstrumentId,b.InstrumentId),b.Timeframe,COUNT(*),MIN(b.OpenTimeUtc),MAX(b.OpenTimeUtc)
            FROM CanonicalBars b LEFT JOIN CanonicalBarInstrumentResolutions r
              ON r.CanonicalBarId=b.CanonicalBarId AND r.ResolutionVersion='root-symbol-resolution-1.0.0'
            WHERE b.Revision=1 GROUP BY COALESCE(r.InstrumentId,b.InstrumentId),b.Timeframe
            ORDER BY COALESCE(r.InstrumentId,b.InstrumentId),b.Timeframe;
            """;
        var values=new List<object>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))values.Add(new
        {instrumentId=reader.GetString(0),timeframe=reader.GetString(1),bars=reader.GetInt64(2),
            earliestUtc=reader.GetString(3),latestUtc=reader.GetString(4)});
        return values;
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table,
        CancellationToken cancellationToken, string? where = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}" +
            (string.IsNullOrWhiteSpace(where) ? string.Empty : $" WHERE {where}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
