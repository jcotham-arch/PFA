using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/mes-order-flow")]
public sealed class MesOrderFlowResearchController(MesOrderFlowResearchService service):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Latest(CancellationToken token=default)=>Ok(await service.LatestAsync(token));
    [HttpPost("run")]
    public async Task<IActionResult> Run([FromQuery]int lookbackDays=120,CancellationToken token=default)
    {try{return Ok(await service.RunAsync(lookbackDays,token));}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}}
}
