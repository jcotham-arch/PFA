using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/patterns/outcomes/replay")]
public sealed class PatternOutcomeReplayController(GenericPatternOutcomeReplayService replay,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Run([FromQuery] string instrumentId, [FromQuery] string contractId,
        [FromQuery] string timeframe, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() || HttpContext.Connection.RemoteIpAddress is not { } address ||
            !System.Net.IPAddress.IsLoopback(address)) return NotFound();
        try { return Ok(await replay.ReplayAsync(instrumentId, contractId, timeframe, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }
}
