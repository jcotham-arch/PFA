using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Data
{
    public sealed class ObservationRepository
    {
        private const string FvgEngineVersion =
            "1.0.0";

        private readonly PfaDatabase _database;

        public ObservationRepository(
            PfaDatabase database)
        {
            _database =
                database;
        }

        public void SaveFvg(
            FairValueGap fvg)
        {
            if (fvg is null)
            {
                throw new ArgumentNullException(
                    nameof(fvg));
            }

            using SqliteConnection connection =
                _database.CreateConnection();

            connection.Open();

            using SqliteCommand command =
                connection.CreateCommand();

            string observationId =
                CreateDeterministicFvgObservationId(
                    fvg);

            command.CommandText = """
                INSERT OR IGNORE INTO Observations
                (
                    ObservationId,
                    Symbol,
                    Timeframe,
                    ObservationType,
                    MarketTimeUtc,
                    Direction,
                    Value1,
                    Value2,
                    Value3,
                    EngineVersion,
                    MetadataJson,
                    CreatedAtUtc
                )
                VALUES
                (
                    $observationId,
                    $symbol,
                    $timeframe,
                    $observationType,
                    $marketTimeUtc,
                    $direction,
                    $value1,
                    $value2,
                    $value3,
                    $engineVersion,
                    $metadataJson,
                    $createdAtUtc
                );
                """;

            var metadata =
                new
                {
                    fvg.Id,
                    fvg.LowerBoundary,
                    fvg.UpperBoundary,
                    fvg.GapSize,
                    fvg.Midpoint,
                    fvg.CurrentPrice,
                    fvg.FillPercentage,

                    Status =
                        fvg.Status.ToString(),

                    fvg.DetectedAtUtc
                };

            command.Parameters.AddWithValue(
                "$observationId",
                observationId);

            command.Parameters.AddWithValue(
                "$symbol",
                fvg.Symbol.Trim().ToUpperInvariant());

            command.Parameters.AddWithValue(
                "$timeframe",
                fvg.Timeframe.Trim().ToLowerInvariant());

            command.Parameters.AddWithValue(
                "$observationType",
                "FVG");

            command.Parameters.AddWithValue(
                "$marketTimeUtc",
                EnsureUtc(
                        fvg.FormationTimeUtc)
                    .ToString("O"));

            command.Parameters.AddWithValue(
                "$direction",
                fvg.Direction.ToString());

            command.Parameters.AddWithValue(
                "$value1",
                fvg.LowerBoundary.ToString(
                    CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$value2",
                fvg.UpperBoundary.ToString(
                    CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$value3",
                fvg.GapSize.ToString(
                    CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$engineVersion",
                FvgEngineVersion);

            command.Parameters.AddWithValue(
                "$metadataJson",
                JsonSerializer.Serialize(
                    metadata));

            command.Parameters.AddWithValue(
                "$createdAtUtc",
                DateTime.UtcNow.ToString("O"));

            command.ExecuteNonQuery();

            using SqliteCommand universalCommand = connection.CreateCommand();
            universalCommand.CommandText = """
                INSERT OR IGNORE INTO UniversalPatternObservationReferences
                    (PatternObservationId, ModuleId, ModuleVersion, PatternType,
                     LegacyObservationId, CreatedAtUtc)
                VALUES ($id, 'fvg', 'legacy-1.0.0', 'FairValueGap', $id, $createdAtUtc);
                """;
            universalCommand.Parameters.AddWithValue("$id", observationId);
            universalCommand.Parameters.AddWithValue("$createdAtUtc", DateTime.UtcNow.ToString("O"));
            universalCommand.ExecuteNonQuery();
        }

        public async Task<int> DeleteFvgsInMarketWindowAsync(
            string symbol,
            string timeframe,
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

            if (string.IsNullOrWhiteSpace(timeframe))
            {
                throw new ArgumentException(
                    "Timeframe is required.",
                    nameof(timeframe));
            }

            if (endUtc < startUtc)
            {
                throw new ArgumentException(
                    "endUtc must be greater than or equal to startUtc.");
            }

            symbol =
                symbol.Trim().ToUpperInvariant();

            timeframe =
                timeframe.Trim().ToLowerInvariant();

            startUtc =
                EnsureUtc(startUtc);

            endUtc =
                EnsureUtc(endUtc);

            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using SqliteTransaction transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using SqliteCommand referenceCommand = connection.CreateCommand();
            referenceCommand.Transaction = transaction;
            referenceCommand.CommandText = """
                DELETE FROM UniversalPatternObservationReferences
                WHERE LegacyObservationId IN
                (
                    SELECT ObservationId FROM Observations
                    WHERE ObservationType = 'FVG' AND Symbol = $symbol AND Timeframe = $timeframe
                        AND MarketTimeUtc >= $startUtc AND MarketTimeUtc <= $endUtc
                );
                """;
            referenceCommand.Parameters.AddWithValue("$symbol", symbol);
            referenceCommand.Parameters.AddWithValue("$timeframe", timeframe);
            referenceCommand.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
            referenceCommand.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
            await referenceCommand.ExecuteNonQueryAsync(cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText = """
                DELETE FROM Observations
                WHERE
                    ObservationType = 'FVG'
                    AND Symbol = $symbol
                    AND Timeframe = $timeframe
                    AND MarketTimeUtc >= $startUtc
                    AND MarketTimeUtc <= $endUtc;
                """;

            command.Parameters.AddWithValue(
                "$symbol",
                symbol);

            command.Parameters.AddWithValue(
                "$timeframe",
                timeframe);

            command.Parameters.AddWithValue(
                "$startUtc",
                startUtc.ToString("O"));

            command.Parameters.AddWithValue(
                "$endUtc",
                endUtc.ToString("O"));

            int deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return deleted;
        }

        public async Task<int> GetFvgCountAsync(
            CancellationToken cancellationToken = default)
        {
            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText = """
                SELECT COUNT(*)
                FROM Observations
                WHERE ObservationType = 'FVG';
                """;

            object? result =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            return Convert.ToInt32(
                result);
        }

        public async Task<IReadOnlyList<object>>
            GetRecentFvgsAsync(
                int limit = 100,
                CancellationToken cancellationToken = default)
        {
            if (limit <= 0)
            {
                limit =
                    100;
            }

            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText = """
                SELECT
                    ObservationId,
                    Symbol,
                    Timeframe,
                    MarketTimeUtc,
                    Direction,
                    Value1,
                    Value2,
                    Value3,
                    EngineVersion,
                    MetadataJson,
                    CreatedAtUtc
                FROM Observations
                WHERE ObservationType = 'FVG'
                ORDER BY MarketTimeUtc DESC
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue(
                "$limit",
                limit);

            var observations =
                new List<object>();

            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                observations.Add(
                    new
                    {
                        observationId =
                            reader.GetString(0),

                        symbol =
                            reader.GetString(1),

                        timeframe =
                            reader.GetString(2),

                        marketTimeUtc =
                            reader.GetString(3),

                        direction =
                            reader.GetString(4),

                        lowerBoundary =
                            reader.GetString(5),

                        upperBoundary =
                            reader.GetString(6),

                        gapSize =
                            reader.GetString(7),

                        engineVersion =
                            reader.GetString(8),

                        metadataJson =
                            reader.IsDBNull(9)
                                ? null
                                : reader.GetString(9),

                        createdAtUtc =
                            reader.GetString(10)
                    });
            }

            return observations;
        }

        private static string
            CreateDeterministicFvgObservationId(
                FairValueGap fvg)
        {
            string naturalKey =
                string.Join(
                    "|",
                    fvg.Symbol
                        .Trim()
                        .ToUpperInvariant(),

                    fvg.Timeframe
                        .Trim()
                        .ToLowerInvariant(),

                    EnsureUtc(
                            fvg.FormationTimeUtc)
                        .ToString("O"),

                    fvg.Direction.ToString(),

                    fvg.LowerBoundary.ToString(
                        "G29",
                        CultureInfo.InvariantCulture),

                    fvg.UpperBoundary.ToString(
                        "G29",
                        CultureInfo.InvariantCulture),

                    fvg.GapSize.ToString(
                        "G29",
                        CultureInfo.InvariantCulture),

                    FvgEngineVersion);

            byte[] bytes =
                Encoding.UTF8.GetBytes(
                    naturalKey);

            byte[] hash =
                SHA256.HashData(
                    bytes);

            return
                "FVG-" +
                Convert.ToHexString(hash);
        }

        private static DateTime EnsureUtc(
            DateTime value)
        {
            if (value.Kind ==
                DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind ==
                DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(
                    value,
                    DateTimeKind.Utc);
            }

            return value.ToUniversalTime();
        }
    }
}
