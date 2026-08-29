using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

public sealed record AgentResearchScoringRequest(DateTime FeaturesKnownAtUtc,IReadOnlyDictionary<string,decimal> Features,
    string Variant="ridge-linear");

[ApiController]
[Route("api/agent/research-scores")]
public sealed class AgentResearchScoringController(AgentBaselineTrainingService training):ControllerBase
{
    [HttpPost("{runId}")]
    public async Task<IActionResult> Score(string runId,AgentResearchScoringRequest request,CancellationToken token)
    {try{return Ok(await training.ScoreAsync(runId,request.FeaturesKnownAtUtc,request.Features,request.Variant,token));}
     catch(KeyNotFoundException exception){return NotFound(new{message=exception.Message});}
     catch(InvalidOperationException exception){return Conflict(new{message=exception.Message});}}
}
