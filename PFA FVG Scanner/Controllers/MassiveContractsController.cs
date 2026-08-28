using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Domain.Instruments;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class MassiveContractsController : ControllerBase
    {
        private readonly MassiveOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IInstrumentDefinitionRegistry _instruments;

        public MassiveContractsController(
            MassiveOptions options,
            IHttpClientFactory httpClientFactory,IInstrumentDefinitionRegistry instruments)
        {
            _options = options;
            _httpClientFactory = httpClientFactory;
            _instruments = instruments;
        }

        [HttpGet("mes")]
        public async Task<IActionResult> GetCurrentMesContracts()
            => await GetCurrentContracts("MES");

        [HttpGet("{productCode}")]
        public async Task<IActionResult> GetCurrentProductContracts(string productCode)
            => await GetCurrentContracts(productCode);

        private async Task<IActionResult> GetCurrentContracts(string productCode)
        {
            productCode=productCode.Trim().ToUpperInvariant();
            if(_instruments.GetAll().All(x=>!x.RootSymbol.Equals(productCode,StringComparison.OrdinalIgnoreCase)))
                return NotFound(new{message=$"Product '{productCode}' is not in the registered research universe."});
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                return BadRequest(new
                {
                    message = "Massive API key is missing."
                });
            }

            var client = _httpClientFactory.CreateClient();

            DateTime todayUtc = DateTime.UtcNow.Date;

            // Try today first, then walk backward several days.
            for (int daysBack = 0; daysBack <= 7; daysBack++)
            {
                DateTime lookupDate =
                    todayUtc.AddDays(-daysBack);

                string date =
                    lookupDate.ToString("yyyy-MM-dd");

                string url =
                    $"{_options.ApiBaseUrl}/futures/v1/contracts" +
                    $"?product_code={Uri.EscapeDataString(productCode)}" +
                    $"&date={date}" +
                    $"&active=true" +
                    $"&type=single" +
                    $"&limit=100" +
                    $"&sort=ticker.asc" +
                    $"&apiKey={Uri.EscapeDataString(_options.ApiKey)}";

                using HttpResponseMessage response =
                    await client.GetAsync(url);

                string body =
                    await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using JsonDocument document =
                    JsonDocument.Parse(body);

                if (!document.RootElement.TryGetProperty(
                        "results",
                        out JsonElement results))
                {
                    continue;
                }

                if (results.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                if (results.GetArrayLength() == 0)
                {
                    continue;
                }

                return Ok(new
                {
                    requestedDate =
                        todayUtc.ToString("yyyy-MM-dd"),

                    matchedDate = date,

                    daysBack,

                    count =
                        results.GetArrayLength(),

                    results =
                        JsonSerializer.Deserialize<object>(
                            results.GetRawText())
                });
            }

            return NotFound(new
            {
                message =
                    $"No active {productCode} contracts were found " +
                    "for today or the previous 7 calendar days.",

                requestedDate =
                    todayUtc.ToString("yyyy-MM-dd")
            });
        }
    }
}
