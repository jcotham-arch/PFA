using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/position-sizing")]
public sealed class PositionSizingResearchController(PositionSizingResearchEngine engine):ControllerBase
{
    [HttpPost("evaluate")]
    public IActionResult Evaluate(PositionSizingResearchRequest request)
    {try{return Ok(engine.Evaluate(request));}catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}}
}
