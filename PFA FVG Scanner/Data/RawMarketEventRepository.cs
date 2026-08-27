using Microsoft.Data.Sqlite;

namespace PFA_FVG_Scanner.Data
{
    public sealed class RawMarketEventRepository
    {
        private readonly PfaDatabase _database;

        public RawMarketEventRepository(
            PfaDatabase database)
        {
            _database = database;
        }

        public async Task SaveAsync(
            string provider,
            string? symbol,
            string? eventType,
            DateTime? marketTimestampUtc,
            DateTime receivedTimestampUtc,
            string rawPayload,
            CancellationToken cancellationToken = default)
        {
            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText = """
                INSERT INTO RawMarketEvents
                (
                    Provider,
                    Symbol,
                    EventType,
                    MarketTimestampUtc,
                    ReceivedTimestampUtc,
                    LatencyMilliseconds,
                    RawPayload,
                    CreatedAtUtc
                )
                VALUES
                (
                    $provider,
                    $symbol,
                    $eventType,
                    $marketTimestampUtc,
                    $receivedTimestampUtc,
                    $latencyMilliseconds,
                    $rawPayload,
                    $createdAtUtc
                );
                """;

            long? latencyMilliseconds = null;

            if (marketTimestampUtc.HasValue)
            {
                latencyMilliseconds =
                    (long)(
                        receivedTimestampUtc -
                        marketTimestampUtc.Value)
                    .TotalMilliseconds;
            }

            command.Parameters.AddWithValue(
                "$provider",
                provider);

            command.Parameters.AddWithValue(
                "$symbol",
                string.IsNullOrWhiteSpace(symbol)
                    ? DBNull.Value
                    : symbol);

            command.Parameters.AddWithValue(
                "$eventType",
                string.IsNullOrWhiteSpace(eventType)
                    ? DBNull.Value
                    : eventType);

            command.Parameters.AddWithValue(
                "$marketTimestampUtc",
                marketTimestampUtc.HasValue
                    ? marketTimestampUtc.Value.ToString("O")
                    : DBNull.Value);

            command.Parameters.AddWithValue(
                "$receivedTimestampUtc",
                receivedTimestampUtc.ToString("O"));

            command.Parameters.AddWithValue(
                "$latencyMilliseconds",
                latencyMilliseconds.HasValue
                    ? latencyMilliseconds.Value
                    : DBNull.Value);

            command.Parameters.AddWithValue(
                "$rawPayload",
                rawPayload);

            command.Parameters.AddWithValue(
                "$createdAtUtc",
                DateTime.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }

        public async Task<int> GetCountAsync(
            CancellationToken cancellationToken = default)
        {
            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                "SELECT COUNT(*) FROM RawMarketEvents;";

            object? result =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            return Convert.ToInt32(result);
        }
    }
}