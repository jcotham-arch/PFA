using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class FvgQualificationController : ControllerBase
    {
        private readonly PfaDatabase _database;
        private readonly FvgQualificationService _qualificationService;

        public FvgQualificationController(
            PfaDatabase database,
            FvgQualificationService qualificationService)
        {
            _database = database;
            _qualificationService = qualificationService;
        }

        [HttpGet("historical")]
        public async Task<ActionResult> EvaluateHistorical(
            [FromQuery] string symbol,
            [FromQuery] DateTime formationTimeUtc,
            [FromQuery] string direction,
            [FromQuery] decimal lowerBoundary,
            [FromQuery] decimal upperBoundary,
            [FromQuery] DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new
                {
                    message =
                        "symbol is required."
                });
            }

            formationTimeUtc =
                EnsureUtc(
                    formationTimeUtc);

            endUtc =
                EnsureUtc(
                    endUtc);

            if (endUtc <= formationTimeUtc)
            {
                return BadRequest(new
                {
                    message =
                        "endUtc must be after formationTimeUtc."
                });
            }

            if (upperBoundary <= lowerBoundary)
            {
                return BadRequest(new
                {
                    message =
                        "upperBoundary must be greater than lowerBoundary."
                });
            }

            bool bearish =
                direction.Equals(
                    "Bearish",
                    StringComparison.OrdinalIgnoreCase);

            bool bullish =
                direction.Equals(
                    "Bullish",
                    StringComparison.OrdinalIgnoreCase);

            if (!bearish &&
                !bullish)
            {
                return BadRequest(new
                {
                    message =
                        "direction must be Bullish or Bearish."
                });
            }

            FairValueGap syntheticFvg =
                new()
                {
                    Symbol =
                        symbol
                            .Trim()
                            .ToUpperInvariant(),

                    Timeframe =
                        "5m",

                    Direction =
                        bearish
                            ? FvgDirection.Bearish
                            : FvgDirection.Bullish,

                    FormationTimeUtc =
                        formationTimeUtc,

                    LowerBoundary =
                        lowerBoundary,

                    UpperBoundary =
                        upperBoundary,

                    GapSize =
                        upperBoundary -
                        lowerBoundary,

                    CurrentPrice =
                        0,

                    FillPercentage =
                        0,

                    Status =
                        FvgStatus.Active,

                    DetectedAtUtc =
                        DateTime.UtcNow
                };

            DateTime historicalRecoveryTimeUtc =
                DateTime.UtcNow;

            FvgTradeQualification qualification =
                _qualificationService
                    .CreateQualification(
                        syntheticFvg,
                        source:
                            "HistoricalReplay",
                        historicalRecoveryTimeUtc:
                            historicalRecoveryTimeUtc);

            // --------------------------------------------------------
            // CRITICAL CHANGE:
            //
            // Evaluate the setup using chronological 1-minute candles.
            //
            // Candle 3 opens at formationTimeUtc and completes five
            // minutes later, so qualification begins at confirmation.
            // --------------------------------------------------------

            DateTime confirmationTimeUtc =
                formationTimeUtc
                    .AddMinutes(5);

            IReadOnlyList<Candle> futureMinuteCandles =
                await LoadOneMinuteCandlesAsync(
                    syntheticFvg.Symbol,
                    confirmationTimeUtc,
                    endUtc,
                    cancellationToken);

            foreach (Candle candle in
                     futureMinuteCandles)
            {
                _qualificationService
                    .EvaluateMinuteCandle(
                        qualification,
                        candle);
            }

            return Ok(new
            {
                symbol =
                    syntheticFvg.Symbol,

                timeframe =
                    syntheticFvg.Timeframe,

                direction =
                    syntheticFvg.Direction
                        .ToString(),

                formationTimeUtc =
                    syntheticFvg
                        .FormationTimeUtc,

                confirmationTimeUtc =
                    confirmationTimeUtc,

                lowerBoundary =
                    syntheticFvg
                        .LowerBoundary,

                upperBoundary =
                    syntheticFvg
                        .UpperBoundary,

                midpoint =
                    syntheticFvg
                        .Midpoint,

                gapSize =
                    syntheticFvg
                        .GapSize,

                source =
                    "HistoricalReplay",

                historicalRecoveryTimeUtc =
                    historicalRecoveryTimeUtc,

                executionResolution =
                    "1m",

                futureMinuteCandlesEvaluated =
                    futureMinuteCandles.Count,

                qualification
            });
        }

        private async Task<IReadOnlyList<Candle>>
            LoadOneMinuteCandlesAsync(
                string symbol,
                DateTime startUtc,
                DateTime endUtc,
                CancellationToken cancellationToken)
        {
            var candles =
                new List<Candle>();

            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

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
                    AND Timeframe = '1m'
                    AND OpenTimeUtc >= $startUtc
                    AND OpenTimeUtc <= $endUtc
                ORDER BY OpenTimeUtc ASC;
                """;

            command.Parameters.AddWithValue(
                "$symbol",
                symbol);

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

        private static DateTime EnsureUtc(
            DateTime value)
        {
            if (value.Kind ==
                DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind ==
                DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(
                value,
                DateTimeKind.Utc);
        }
    }
}