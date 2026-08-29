using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/sandbox/exploratory-candidates")]
public sealed class ExploratorySandboxController(ExploratorySandboxCandidateService candidates):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery]string instrumentId="MES",CancellationToken token=default)
    {try{return Ok(await candidates.GetAsync(instrumentId,token));}catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}}
}
