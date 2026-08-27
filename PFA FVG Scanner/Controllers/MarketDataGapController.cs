using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class MarketDataGapController : ControllerBase
    {
        private readonly MarketDataGapService _gapService;

        public MarketDataGapController(
            MarketDataGapService gapService)
        {
            _gapService = gapService;
        }

        [HttpGet("{symbol}")]
        public async Task<ActionResult> GetGapSummary(
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

            GapSummary summary =
                await _gapService.GetGapSummaryAsync(
                    symbol,
                    startUtc,
                    endUtc,
                    cancellationToken);

            return Ok(summary);
        }
    }
}