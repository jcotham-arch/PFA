using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/pattern-trades")]
public sealed class PatternTradeResearchController(PatternTradeResearchService service):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)=>Ok(await service.GetAllAsync(token));

    [HttpPost]
    public async Task<IActionResult> Run([FromBody] PatternTradeResearchRequest request,CancellationToken token)
    {
        if(!HttpContext.Request.Host.Host.Equals("127.0.0.1",StringComparison.OrdinalIgnoreCase)&&
           !HttpContext.Request.Host.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase))return NotFound();
        return Ok(await service.RunAsync(request,token));
    }
}
