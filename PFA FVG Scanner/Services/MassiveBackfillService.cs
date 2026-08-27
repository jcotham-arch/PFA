using System.Globalization;
using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class MassiveBackfillService
    {
        private readonly MassiveOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly RawMarketEventRepository _rawRepository;
        private readonly CandleRepository _candleRepository;

        public MassiveBackfillService(
            MassiveOptions options,
            IHttpClientFactory httpClientFactory,
            RawMarketEventRepository rawRepository,
            CandleRepository candleRepository)
        {
            _options = options;
            _httpClientFactory = httpClientFactory;
            _rawRepository = rawRepository;
            _candleRepository = candleRepository;
        }

        public async Task<BackfillResult> BackfillOneMinuteBarsAsync(
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

            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Massive API key is missing.");
            }

            startUtc = EnsureUtc(startUtc);
            endUtc = EnsureUtc(endUtc);

            long startNs =
                ToUnixNanoseconds(startUtc);

            long endNs =
                ToUnixNanoseconds(endUtc);

            string url =
                $"{_options.ApiBaseUrl}/futures/v1/aggs/" +
                $"{Uri.EscapeDataString(symbol.ToUpperInvariant())}" +
                $"?resolution=1min" +
                $"&window_start.gte={startNs}" +
                $"&window_start.lte={endNs}" +
                $"&limit=50000" +
                $"&sort=window_start.asc" +
                $"&apiKey={Uri.EscapeDataString(_options.ApiKey)}";

            HttpClient client =
                _httpClientFactory.CreateClient();

            using HttpResponseMessage response =
                await client.GetAsync(
                    url,
                    cancellationToken);

            string json =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Massive backfill failed with HTTP " +
                    $"{(int)response.StatusCode}: {json}");
            }

            using JsonDocument document =
                JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty(
                    "results",
                    out JsonElement results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                return new BackfillResult
                {
                    Symbol = symbol,
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    BarsReturned = 0,
                    BarsSaved = 0
                };
            }

            int returned = 0;
            int saved = 0;

            foreach (JsonElement item
                     in results.EnumerateArray())
            {
                returned++;

                if (!TryReadDecimal(
                        item,
                        "open",
                        out decimal open) ||
                    !TryReadDecimal(
                        item,
                        "high",
                        out decimal high) ||
                    !TryReadDecimal(
                        item,
                        "low",
                        out decimal low) ||
                    !TryReadDecimal(
                        item,
                        "close",
                        out decimal close) ||
                    !TryReadDecimal(
                        item,
                        "volume",
                        out decimal volume))
                {
                    continue;
                }

                if (!TryReadInt64(
                        item,
                        "window_start",
                        out long windowStartNs))
                {
                    continue;
                }

                DateTime openTimeUtc =
                    FromUnixNanoseconds(
                        windowStartNs);

                var candle =
                    new Candle
                    {
                        Symbol =
                            symbol.ToUpperInvariant(),

                        Timeframe = "1m",

                        OpenTimeUtc =
                            openTimeUtc,

                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume,

                        IsClosed = true
                    };

                await _candleRepository.SaveAsync(
                    candle,
                    "Massive Historical Backfill",
                    cancellationToken);

                string rawPayload =
                    item.GetRawText();

                await _rawRepository.SaveAsync(
                    provider:
                        "Massive Historical Backfill",

                    symbol:
                        symbol.ToUpperInvariant(),

                    eventType:
                        "AM_BACKFILL",

                    marketTimestampUtc:
                        openTimeUtc.AddMinutes(1),

                    receivedTimestampUtc:
                        DateTime.UtcNow,

                    rawPayload:
                        rawPayload,

                    cancellationToken:
                        cancellationToken);

                saved++;
            }

            return new BackfillResult
            {
                Symbol =
                    symbol.ToUpperInvariant(),

                StartUtc =
                    startUtc,

                EndUtc =
                    endUtc,

                BarsReturned =
                    returned,

                BarsSaved =
                    saved
            };
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

        private static long ToUnixNanoseconds(
            DateTime utc)
        {
            DateTimeOffset dto =
                new(utc);

            long milliseconds =
                dto.ToUnixTimeMilliseconds();

            return checked(
                milliseconds * 1_000_000L);
        }

        private static DateTime FromUnixNanoseconds(
            long nanoseconds)
        {
            long milliseconds =
                nanoseconds / 1_000_000L;

            return DateTimeOffset
                .FromUnixTimeMilliseconds(milliseconds)
                .UtcDateTime;
        }

        private static bool TryReadDecimal(
            JsonElement item,
            string propertyName,
            out decimal value)
        {
            value = 0;

            if (!item.TryGetProperty(
                    propertyName,
                    out JsonElement element))
            {
                return false;
            }

            if (element.ValueKind ==
                JsonValueKind.Number)
            {
                return element.TryGetDecimal(
                    out value);
            }

            if (element.ValueKind ==
                JsonValueKind.String)
            {
                return decimal.TryParse(
                    element.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out value);
            }

            return false;
        }

        private static bool TryReadInt64(
            JsonElement item,
            string propertyName,
            out long value)
        {
            value = 0;

            if (!item.TryGetProperty(
                    propertyName,
                    out JsonElement element))
            {
                return false;
            }

            if (element.ValueKind ==
                JsonValueKind.Number)
            {
                return element.TryGetInt64(
                    out value);
            }

            if (element.ValueKind ==
                JsonValueKind.String)
            {
                return long.TryParse(
                    element.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value);
            }

            return false;
        }
    }

    public sealed class BackfillResult
    {
        public string Symbol { get; set; } =
            string.Empty;

        public DateTime StartUtc { get; set; }

        public DateTime EndUtc { get; set; }

        public int BarsReturned { get; set; }

        public int BarsSaved { get; set; }
    }
}