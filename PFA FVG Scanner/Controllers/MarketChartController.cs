using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/market")]
public sealed class MarketChartController : ControllerBase
{
    private readonly MarketChartService _service;
    public MarketChartController(MarketChartService service) => _service = service;

    [HttpGet("chart/{symbol}")]
    public async Task<IActionResult> GetChart(string symbol, [FromQuery] string timeframe = "5m",
        [FromQuery] int limit = 160,[FromQuery] DateTime? focusUtc=null,CancellationToken cancellationToken = default)
    {
        try { return Ok(focusUtc.HasValue
            ? await _service.GetFocusedAsync(symbol,timeframe,limit,focusUtc.Value,cancellationToken)
            : await _service.GetAsync(symbol, timeframe, limit, cancellationToken)); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("coverage")]
    public async Task<IActionResult> GetCoverage(CancellationToken cancellationToken = default) =>
        Ok(await _service.GetAllCoverageAsync(cancellationToken));
}
