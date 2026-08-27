using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.MarketData;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class MassiveContractsController : ControllerBase
    {
        private readonly MassiveOptions _options;
        private readonly IHttpClientFactory _httpClientFactory;

        public MassiveContractsController(
            MassiveOptions options,
            IHttpClientFactory httpClientFactory)
        {
            _options = options;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("mes")]
        public async Task<IActionResult> GetCurrentMesContracts()
        {
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
                    $"?product_code=MES" +
                    $"&date={date}" +
                    $"&active=true" +
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
                    "No active MES contracts were found " +
                    "for today or the previous 7 calendar days.",

                requestedDate =
                    todayUtc.ToString("yyyy-MM-dd")
            });
        }
    }
}