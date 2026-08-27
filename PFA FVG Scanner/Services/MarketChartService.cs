using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services;

public sealed record MarketChartBar(DateTime OpenTimeUtc, DateTime CloseTimeUtc,
    decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, bool IsComplete);

public sealed record MarketDataCoverage(string Symbol, string Timeframe, long BarCount,
    DateTime? EarliestUtc, DateTime? LatestUtc, decimal? CalendarDaysCovered);

public sealed record MarketChartSnapshot(string Symbol, string Timeframe, int RequestedBars,
    IReadOnlyList<MarketChartBar> Bars, IReadOnlyList<object> FvgOverlays, MarketDataCoverage Coverage);

public sealed class MarketChartService
{
    private static readonly IReadOnlyDictionary<string, int> Supported =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["1m"] = 1, ["5m"] = 5, ["15m"] = 15, ["1h"] = 60 };

    private readonly CandleRepository _candles;
    private readonly ObservationRepository _observations;
    private readonly PfaDatabase _database;

    public MarketChartService(CandleRepository candles, ObservationRepository observations, PfaDatabase database)
    {
        _candles = candles;
        _observations = observations;
        _database = database;
    }

    public IReadOnlyCollection<string> SupportedTimeframes => Supported.Keys.ToArray();

    public async Task<MarketChartSnapshot> GetAsync(string symbol, string timeframe, int limit,
        CancellationToken cancellationToken)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        if (!Supported.TryGetValue(timeframe, out var minutes))
            throw new ArgumentException("Supported timeframes are 1m, 5m, 15m and 1h.", nameof(timeframe));
        limit = Math.Clamp(limit, 20, 300);
        var sourceLimit = Math.Min(limit * minutes + minutes, 20_000);
        var source = (await _candles.GetRecentAsync(normalizedSymbol, "1m", sourceLimit, cancellationToken))
            .OrderBy(x => x.OpenTimeUtc).ToArray();
        var bars = Aggregate(source, minutes).TakeLast(limit).ToArray();
        var fvgs = (await _observations.GetRecentFvgsAsync(100, cancellationToken)).ToArray();
        var coverage = await GetCoverageAsync(normalizedSymbol, "1m", cancellationToken);
        return new(normalizedSymbol, timeframe.ToLowerInvariant(), limit, bars, fvgs, coverage);
    }

    public async Task<IReadOnlyList<MarketDataCoverage>> GetAllCoverageAsync(CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Symbol, Timeframe, COUNT(DISTINCT OpenTimeUtc), MIN(OpenTimeUtc), MAX(OpenTimeUtc)
            FROM Candles GROUP BY Symbol, Timeframe ORDER BY Symbol, Timeframe;
            """;
        var results = new List<MarketDataCoverage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadCoverage(reader));
        return results;
    }

    private async Task<MarketDataCoverage> GetCoverageAsync(string symbol, string timeframe,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Symbol, Timeframe, COUNT(DISTINCT OpenTimeUtc), MIN(OpenTimeUtc), MAX(OpenTimeUtc)
            FROM Candles WHERE Symbol = $symbol AND Timeframe = $timeframe;
            """;
        command.Parameters.AddWithValue("$symbol", symbol);
        command.Parameters.AddWithValue("$timeframe", timeframe);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCoverage(reader)
            : new(symbol, timeframe, 0, null, null, null);
    }

    public static IReadOnlyList<MarketChartBar> Aggregate(IReadOnlyList<Candle> source, int minutes)
    {
        if (minutes is not (1 or 5 or 15 or 60)) throw new ArgumentOutOfRangeException(nameof(minutes));
        return source.GroupBy(x => Align(x.OpenTimeUtc, minutes)).OrderBy(x => x.Key).Select(group =>
        {
            var ordered = group.OrderBy(x => x.OpenTimeUtc).ToArray();
            return new MarketChartBar(group.Key, group.Key.AddMinutes(minutes), ordered[0].Open,
                ordered.Max(x => x.High), ordered.Min(x => x.Low), ordered[^1].Close,
                ordered.Sum(x => x.Volume), ordered.Length == minutes && ordered.All(x => x.IsClosed));
        }).ToArray();
    }

    private static DateTime Align(DateTime value, int minutes)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        var ticks = TimeSpan.FromMinutes(minutes).Ticks;
        return new DateTime(utc.Ticks - utc.Ticks % ticks, DateTimeKind.Utc);
    }

    private static MarketDataCoverage ReadCoverage(SqliteDataReader reader)
    {
        var earliest = reader.IsDBNull(3) ? (DateTime?)null : DateTime.Parse(reader.GetString(3), null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        var latest = reader.IsDBNull(4) ? (DateTime?)null : DateTime.Parse(reader.GetString(4), null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        decimal? days = earliest.HasValue && latest.HasValue
            ? Math.Round((decimal)(latest.Value - earliest.Value).TotalDays, 2) : null;
        return new(reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1), reader.GetInt64(2), earliest, latest, days);
    }
}
