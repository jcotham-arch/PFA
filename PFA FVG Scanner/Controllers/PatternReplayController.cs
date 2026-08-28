using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/patterns/replay")]
public sealed class PatternReplayController(PatternSequenceReplayService replay,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Run([FromQuery] string instrumentId = "MES",
        [FromQuery] string? contractId = null, [FromQuery] string timeframe = "5m",
        [FromQuery] DateTime? startUtc = null, [FromQuery] DateTime? endUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() || HttpContext.Connection.RemoteIpAddress is not { } address ||
            !System.Net.IPAddress.IsLoopback(address))
            return NotFound();
        return Ok(await replay.ReplayAsync(instrumentId, contractId, timeframe, startUtc, endUtc, cancellationToken));
    }
}
