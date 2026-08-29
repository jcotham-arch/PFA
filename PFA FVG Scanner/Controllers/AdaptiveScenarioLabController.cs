using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/sandbox/adaptive-scenarios")]
public sealed class AdaptiveScenarioLabController(AdaptiveScenarioLabService lab):ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdaptiveScenarioDashboard>> Get([FromQuery]string instrumentId="MES",CancellationToken token=default)
    {try{return Ok(await lab.DashboardAsync(instrumentId,token));}catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}}

    [HttpPost("generate")]
    public async Task<ActionResult<AdaptiveScenarioDashboard>> Generate([FromQuery]string instrumentId="MES",CancellationToken token=default)
    {try{return Ok(await lab.GenerateAsync(instrumentId,token));}catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}}

    [HttpPost("evaluate")]
    public async Task<ActionResult<AdaptiveScenarioDashboard>> Evaluate([FromQuery]string instrumentId="MES",CancellationToken token=default)
    {try{return Ok(await lab.EvaluateLatestAsync(instrumentId,token));}catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}}
}
