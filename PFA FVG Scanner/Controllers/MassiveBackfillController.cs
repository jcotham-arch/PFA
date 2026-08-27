using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class MassiveBackfillController : ControllerBase
    {
        private readonly MassiveBackfillService _backfillService;

        public MassiveBackfillController(
            MassiveBackfillService backfillService)
        {
            _backfillService = backfillService;
        }

        [HttpPost("{symbol}")]
        public async Task<ActionResult> Backfill(
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
                BackfillResult result =
                    await _backfillService
                        .BackfillOneMinuteBarsAsync(
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
                            "Massive historical backfill failed.",

                        error =
                            ex.Message
                    });
            }
        }
    }
}