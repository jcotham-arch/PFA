using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class HistoricalCandleRebuildController : ControllerBase
    {
        private readonly HistoricalCandleRebuildService _rebuildService;

        public HistoricalCandleRebuildController(
            HistoricalCandleRebuildService rebuildService)
        {
            _rebuildService = rebuildService;
        }

        [HttpPost("{symbol}")]
        public async Task<ActionResult> Rebuild(
            string symbol,
            [FromQuery] DateTime startUtc,
            [FromQuery] DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(new
                {
                    message = "Symbol is required."
                });
            }

            if (endUtc <= startUtc)
            {
                return BadRequest(new
                {
                    message = "endUtc must be after startUtc."
                });
            }

            try
            {
                HistoricalRebuildResult result =
                    await _rebuildService
                        .RebuildFiveMinuteCandlesAsync(
                            symbol,
                            startUtc,
                            endUtc,
                            cancellationToken);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message =
                            "Historical 5-minute candle rebuild failed.",

                        error =
                            ex.Message
                    });
            }
        }
    }
}