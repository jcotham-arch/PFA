using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/actionability-segments")]
public sealed class ActionabilitySegmentResearchController(ActionabilitySegmentResearchService research):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery]int minimumSamples=100,CancellationToken token=default)
    {try{return Ok(await research.AnalyzeAsync(minimumSamples,token));}catch(ArgumentOutOfRangeException exception){return BadRequest(new{message=exception.Message});}catch(InvalidOperationException exception){return Conflict(new{message=exception.Message});}}
}
