using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;

namespace PFA_FVG_Scanner.Services
{
    public sealed class MarketDataGapService
    {
        private readonly PfaDatabase _database;

        public MarketDataGapService(
            PfaDatabase database)
        {
            _database = database;
        }

        public async Task<IReadOnlyList<DateTime>> FindMissingOneMinuteBarsAsync(
            string symbol,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException(
                    "Symbol is required.",
                    nameof(symbol));
            }

            if (endUtc <= startUtc)
            {
                throw new ArgumentException(
                    "End time must be after start time.");
            }

            startUtc = NormalizeToMinute(startUtc);
            endUtc = NormalizeToMinute(endUtc);

            HashSet<DateTime> receivedTimes =
                await LoadAvailableOneMinuteTimesAsync(
                    symbol,
                    startUtc,
                    endUtc,
                    cancellationToken);

            var missing =
                new List<DateTime>();

            for (DateTime cursor = startUtc;
                 cursor <= endUtc;
                 cursor = cursor.AddMinutes(1))
            {
                if (!receivedTimes.Contains(cursor))
                {
                    missing.Add(cursor);
                }
            }

            return missing;
        }

        public async Task<GapSummary> GetGapSummaryAsync(
            string symbol,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            startUtc = NormalizeToMinute(startUtc);
            endUtc = NormalizeToMinute(endUtc);

            IReadOnlyList<DateTime> missing =
                await FindMissingOneMinuteBarsAsync(
                    symbol,
                    startUtc,
                    endUtc,
                    cancellationToken);

            int expectedCount =
                (int)Math.Floor(
                    (endUtc - startUtc).TotalMinutes) + 1;

            int missingCount =
                missing.Count;

            int receivedCount =
                Math.Max(
                    0,
                    expectedCount - missingCount);

            return new GapSummary
            {
                Symbol =
                    symbol.ToUpperInvariant(),

                StartUtc =
                    startUtc,

                EndUtc =
                    endUtc,

                ExpectedMinuteCount =
                    expectedCount,

                ReceivedMinuteCount =
                    receivedCount,

                MissingMinuteCount =
                    missingCount,

                MissingMinutes =
                    missing
            };
        }

        private async Task<HashSet<DateTime>>
            LoadAvailableOneMinuteTimesAsync(
                string symbol,
                DateTime startUtc,
                DateTime endUtc,
                CancellationToken cancellationToken)
        {
            var result =
                new HashSet<DateTime>();

            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            // --------------------------------------------------------
            // SOURCE 1:
            // RAW MASSIVE EVENTS
            //
            // AM          = live/delayed WebSocket
            // AM_BACKFILL = historical REST recovery
            // --------------------------------------------------------

            await using (SqliteCommand command =
                         connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT
                        EventType,
                        MarketTimestampUtc
                    FROM RawMarketEvents
                    WHERE
                        Symbol = $symbol
                        AND EventType IN ('AM', 'AM_BACKFILL')
                        AND MarketTimestampUtc IS NOT NULL
                        AND MarketTimestampUtc >= $startUtc
                        AND MarketTimestampUtc <= $endUtc
                    ORDER BY MarketTimestampUtc;
                    """;

                command.Parameters.AddWithValue(
                    "$symbol",
                    symbol.ToUpperInvariant());

                command.Parameters.AddWithValue(
                    "$startUtc",
                    startUtc.ToString("O"));

                // Backfilled RawMarketEvents are stored using the
                // END of the minute bar as MarketTimestampUtc.
                //
                // We include one extra minute on the upper boundary
                // and normalize AM_BACKFILL records back to the bar's
                // opening minute below.
                command.Parameters.AddWithValue(
                    "$endUtc",
                    endUtc.AddMinutes(1).ToString("O"));

                await using SqliteDataReader reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                while (await reader.ReadAsync(
                           cancellationToken))
                {
                    string eventType =
                        reader.GetString(0);

                    string timestampValue =
                        reader.GetString(1);

                    if (!DateTime.TryParse(
                            timestampValue,
                            null,
                            System.Globalization.DateTimeStyles.RoundtripKind,
                            out DateTime timestamp))
                    {
                        continue;
                    }

                    timestamp =
                        NormalizeToMinute(timestamp);

                    // Historical backfill records currently store
                    // marketTimestampUtc as the END of the minute.
                    // Convert back to the candle's opening minute.
                    if (eventType.Equals(
                            "AM_BACKFILL",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        timestamp =
                            timestamp.AddMinutes(-1);
                    }

                    if (timestamp >= startUtc &&
                        timestamp <= endUtc)
                    {
                        result.Add(timestamp);
                    }
                }
            }

            // --------------------------------------------------------
            // SOURCE 2:
            // NORMALIZED 1-MINUTE CANDLES
            //
            // Historical backfill also saves actual 1m Candle records.
            // Checking this table makes the gap detector resilient even
            // if raw-event naming changes later.
            // --------------------------------------------------------

            await using (SqliteCommand command =
                         connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT OpenTimeUtc
                    FROM Candles
                    WHERE
                        Symbol = $symbol
                        AND Timeframe = '1m'
                        AND OpenTimeUtc >= $startUtc
                        AND OpenTimeUtc <= $endUtc
                    ORDER BY OpenTimeUtc;
                    """;

                command.Parameters.AddWithValue(
                    "$symbol",
                    symbol.ToUpperInvariant());

                command.Parameters.AddWithValue(
                    "$startUtc",
                    startUtc.ToString("O"));

                command.Parameters.AddWithValue(
                    "$endUtc",
                    endUtc.ToString("O"));

                await using SqliteDataReader reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                while (await reader.ReadAsync(
                           cancellationToken))
                {
                    string timestampValue =
                        reader.GetString(0);

                    if (!DateTime.TryParse(
                            timestampValue,
                            null,
                            System.Globalization.DateTimeStyles.RoundtripKind,
                            out DateTime timestamp))
                    {
                        continue;
                    }

                    result.Add(
                        NormalizeToMinute(timestamp));
                }
            }

            return result;
        }

        private static DateTime NormalizeToMinute(
            DateTime value)
        {
            if (value.Kind != DateTimeKind.Utc)
            {
                value = value.ToUniversalTime();
            }

            return new DateTime(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                0,
                DateTimeKind.Utc);
        }
    }

    public sealed class GapSummary
    {
        public string Symbol { get; set; } =
            string.Empty;

        public DateTime StartUtc { get; set; }

        public DateTime EndUtc { get; set; }

        public int ExpectedMinuteCount { get; set; }

        public int ReceivedMinuteCount { get; set; }

        public int MissingMinuteCount { get; set; }

        public IReadOnlyList<DateTime> MissingMinutes
        {
            get;
            set;
        } = Array.Empty<DateTime>();
    }
}