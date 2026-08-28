using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/campaigns/patterns")]
public sealed class PatternResearchCampaignController(PatternResearchCampaignService campaigns,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Run(PatternResearchCampaignRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() || HttpContext.Connection.RemoteIpAddress is not { } address ||
            !System.Net.IPAddress.IsLoopback(address)) return NotFound();
        try { return Ok(await campaigns.RunAsync(request, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
    }
}
