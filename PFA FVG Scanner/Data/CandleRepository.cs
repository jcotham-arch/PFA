using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Data
{
    public sealed class CandleRepository
    {
        private const string SourceVersion = "1.0.0";

        private readonly PfaDatabase _database;

        public CandleRepository(PfaDatabase database)
        {
            _database = database;
        }

        public async Task SaveAsync(
            Candle candle,
            string provider,
            CancellationToken cancellationToken = default)
        {
            if (candle is null)
            {
                throw new ArgumentNullException(nameof(candle));
            }

            if (string.IsNullOrWhiteSpace(provider))
            {
                throw new ArgumentException(
                    "Provider is required.",
                    nameof(provider));
            }

            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText = """
                INSERT OR IGNORE INTO Candles
                (
                    Symbol,
                    Timeframe,
                    OpenTimeUtc,
                    CloseTimeUtc,
                    Open,
                    High,
                    Low,
                    Close,
                    Volume,
                    Provider,
                    IsComplete,
                    SourceVersion,
                    CreatedAtUtc
                )
                VALUES
                (
                    $symbol,
                    $timeframe,
                    $openTimeUtc,
                    $closeTimeUtc,
                    $open,
                    $high,
                    $low,
                    $close,
                    $volume,
                    $provider,
                    $isComplete,
                    $sourceVersion,
                    $createdAtUtc
                );
                """;

            DateTime? closeTimeUtc =
                CalculateCloseTimeUtc(candle);

            command.Parameters.AddWithValue(
                "$symbol",
                candle.Symbol);

            command.Parameters.AddWithValue(
                "$timeframe",
                candle.Timeframe);

            command.Parameters.AddWithValue(
                "$openTimeUtc",
                candle.OpenTimeUtc.ToString("O"));

            command.Parameters.AddWithValue(
                "$closeTimeUtc",
                closeTimeUtc.HasValue
                    ? closeTimeUtc.Value.ToString("O")
                    : DBNull.Value);

            command.Parameters.AddWithValue(
                "$open",
                candle.Open.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$high",
                candle.High.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$low",
                candle.Low.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$close",
                candle.Close.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$volume",
                candle.Volume.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$provider",
                provider);

            command.Parameters.AddWithValue(
                "$isComplete",
                candle.IsClosed ? 1 : 0);

            command.Parameters.AddWithValue(
                "$sourceVersion",
                SourceVersion);

            command.Parameters.AddWithValue(
                "$createdAtUtc",
                DateTime.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }

        public async Task<IReadOnlyList<Candle>> GetRecentAsync(
            string symbol,
            string timeframe,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            if (limit <= 0)
            {
                limit = 100;
            }

            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText = """
                SELECT
                    Symbol,
                    Timeframe,
                    OpenTimeUtc,
                    Open,
                    High,
                    Low,
                    Close,
                    Volume,
                    IsComplete
                FROM Candles
                WHERE
                    Symbol = $symbol
                    AND Timeframe = $timeframe
                ORDER BY OpenTimeUtc DESC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue(
                "$symbol",
                symbol);

            command.Parameters.AddWithValue(
                "$timeframe",
                timeframe);

            command.Parameters.AddWithValue(
                "$limit",
                limit);

            var candles =
                new List<Candle>();

            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                candles.Add(
                    new Candle
                    {
                        Symbol =
                            reader.GetString(0),

                        Timeframe =
                            reader.GetString(1),

                        OpenTimeUtc =
                            DateTime.Parse(
                                reader.GetString(2),
                                null,
                                System.Globalization
                                    .DateTimeStyles
                                    .RoundtripKind),

                        Open =
                            decimal.Parse(
                                reader.GetString(3),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        High =
                            decimal.Parse(
                                reader.GetString(4),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        Low =
                            decimal.Parse(
                                reader.GetString(5),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        Close =
                            decimal.Parse(
                                reader.GetString(6),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        Volume =
                            decimal.Parse(
                                reader.GetString(7),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        IsClosed =
                            reader.GetInt32(8) == 1
                    });
            }

            return candles;
        }

        private static DateTime? CalculateCloseTimeUtc(
            Candle candle)
        {
            if (!candle.IsClosed)
            {
                return null;
            }

            return candle.Timeframe
                .Trim()
                .ToLowerInvariant() switch
            {
                "1m" =>
                    candle.OpenTimeUtc.AddMinutes(1),

                "5m" =>
                    candle.OpenTimeUtc.AddMinutes(5),

                "15m" =>
                    candle.OpenTimeUtc.AddMinutes(15),

                "1h" =>
                    candle.OpenTimeUtc.AddHours(1),

                "4h" =>
                    candle.OpenTimeUtc.AddHours(4),

                "1d" or "daily" =>
                    candle.OpenTimeUtc.AddDays(1),

                _ => null
            };
        }
    }
}