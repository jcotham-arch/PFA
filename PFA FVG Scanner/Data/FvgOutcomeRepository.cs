using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Data
{
    public sealed class FvgOutcomeRepository
    {
        private readonly PfaDatabase _database;

        public FvgOutcomeRepository(
            PfaDatabase database)
        {
            _database = database;
        }

        public async Task SaveAsync(
            FvgOutcome outcome,
            CancellationToken cancellationToken = default)
        {
            if (outcome is null)
            {
                throw new ArgumentNullException(
                    nameof(outcome));
            }

            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText = """
                INSERT INTO Outcomes
                (
                    OutcomeId,
                    SetupId,

                    FirstTouchTimeUtc,
                    FirstTouchPrice,

                    TwentyFivePercentFillTimeUtc,
                    FiftyPercentFillTimeUtc,
                    SeventyFivePercentFillTimeUtc,
                    FullFillTimeUtc,

                    MaximumFavorableExcursion,
                    MaximumAdverseExcursion,

                    HighestPriceAfterSetup,
                    LowestPriceAfterSetup,

                    Return5Minutes,
                    Return15Minutes,
                    Return30Minutes,
                    Return60Minutes,

                    SetupLifetimeMinutes,

                    OutcomeJson,

                    CreatedAtUtc
                )
                VALUES
                (
                    $outcomeId,
                    $setupId,

                    $firstTouchTimeUtc,
                    $firstTouchPrice,

                    $twentyFivePercentFillTimeUtc,
                    $fiftyPercentFillTimeUtc,
                    $seventyFivePercentFillTimeUtc,
                    $fullFillTimeUtc,

                    $maximumFavorableExcursion,
                    $maximumAdverseExcursion,

                    $highestPriceAfterSetup,
                    $lowestPriceAfterSetup,

                    $return5Minutes,
                    $return15Minutes,
                    $return30Minutes,
                    $return60Minutes,

                    $setupLifetimeMinutes,

                    $outcomeJson,

                    $createdAtUtc
                )
                ON CONFLICT(OutcomeId)
                DO UPDATE SET
                    FirstTouchTimeUtc =
                        excluded.FirstTouchTimeUtc,

                    FirstTouchPrice =
                        excluded.FirstTouchPrice,

                    TwentyFivePercentFillTimeUtc =
                        excluded.TwentyFivePercentFillTimeUtc,

                    FiftyPercentFillTimeUtc =
                        excluded.FiftyPercentFillTimeUtc,

                    SeventyFivePercentFillTimeUtc =
                        excluded.SeventyFivePercentFillTimeUtc,

                    FullFillTimeUtc =
                        excluded.FullFillTimeUtc,

                    MaximumFavorableExcursion =
                        excluded.MaximumFavorableExcursion,

                    MaximumAdverseExcursion =
                        excluded.MaximumAdverseExcursion,

                    HighestPriceAfterSetup =
                        excluded.HighestPriceAfterSetup,

                    LowestPriceAfterSetup =
                        excluded.LowestPriceAfterSetup,

                    Return5Minutes =
                        excluded.Return5Minutes,

                    Return15Minutes =
                        excluded.Return15Minutes,

                    Return30Minutes =
                        excluded.Return30Minutes,

                    Return60Minutes =
                        excluded.Return60Minutes,

                    SetupLifetimeMinutes =
                        excluded.SetupLifetimeMinutes,

                    OutcomeJson =
                        excluded.OutcomeJson;
                """;

            string outcomeJson =
                JsonSerializer.Serialize(outcome);

            command.Parameters.AddWithValue(
                "$outcomeId",
                outcome.OutcomeId.ToString());

            command.Parameters.AddWithValue(
                "$setupId",
                outcome.FvgId.ToString());

            AddNullableDateTime(
                command,
                "$firstTouchTimeUtc",
                outcome.FirstTouchTimeUtc);

            AddNullableDecimal(
                command,
                "$firstTouchPrice",
                outcome.FirstTouchPrice);

            AddNullableDateTime(
                command,
                "$twentyFivePercentFillTimeUtc",
                outcome.TwentyFivePercentFillTimeUtc);

            AddNullableDateTime(
                command,
                "$fiftyPercentFillTimeUtc",
                outcome.FiftyPercentFillTimeUtc);

            AddNullableDateTime(
                command,
                "$seventyFivePercentFillTimeUtc",
                outcome.SeventyFivePercentFillTimeUtc);

            AddNullableDateTime(
                command,
                "$fullFillTimeUtc",
                outcome.FullFillTimeUtc);

            AddNullableDecimal(
                command,
                "$maximumFavorableExcursion",
                outcome.MaximumFavorableExcursion);

            AddNullableDecimal(
                command,
                "$maximumAdverseExcursion",
                outcome.MaximumAdverseExcursion);

            AddNullableDecimal(
                command,
                "$highestPriceAfterSetup",
                outcome.HighestPriceAfterSetup);

            AddNullableDecimal(
                command,
                "$lowestPriceAfterSetup",
                outcome.LowestPriceAfterSetup);

            AddNullableDecimal(
                command,
                "$return5Minutes",
                outcome.Return5Minutes);

            AddNullableDecimal(
                command,
                "$return15Minutes",
                outcome.Return15Minutes);

            AddNullableDecimal(
                command,
                "$return30Minutes",
                outcome.Return30Minutes);

            AddNullableDecimal(
                command,
                "$return60Minutes",
                outcome.Return60Minutes);

            command.Parameters.AddWithValue(
                "$setupLifetimeMinutes",
                outcome.SetupLifetimeMinutes.HasValue
                    ? outcome.SetupLifetimeMinutes.Value
                    : DBNull.Value);

            command.Parameters.AddWithValue(
                "$outcomeJson",
                outcomeJson);

            command.Parameters.AddWithValue(
                "$createdAtUtc",
                DateTime.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(
                cancellationToken);

            var universalRepository = new UniversalMarketRecordRepository(_database);
            await universalRepository.SaveOutcomeAsync(
                UniversalMarketRecordRepository.FromFvgOutcome(outcome), cancellationToken);
        }

        private static void AddNullableDateTime(
            SqliteCommand command,
            string parameterName,
            DateTime? value)
        {
            command.Parameters.AddWithValue(
                parameterName,
                value.HasValue
                    ? value.Value.ToUniversalTime().ToString("O")
                    : DBNull.Value);
        }

        private static void AddNullableDecimal(
            SqliteCommand command,
            string parameterName,
            decimal? value)
        {
            command.Parameters.AddWithValue(
                parameterName,
                value.HasValue
                    ? value.Value.ToString(
                        CultureInfo.InvariantCulture)
                    : DBNull.Value);
        }
    }
}
