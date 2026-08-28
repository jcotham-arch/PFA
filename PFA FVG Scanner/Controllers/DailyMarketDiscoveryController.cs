using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/daily-discovery")]
public sealed class DailyMarketDiscoveryController(DailyMarketDiscoveryService service):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? date,CancellationToken token)
        =>Ok(await service.StudyAsync(date??DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),token));
}
