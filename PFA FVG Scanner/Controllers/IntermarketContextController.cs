using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Intermarket;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/intermarket-context")]
public sealed class IntermarketContextController(IntermarketContextService service):ControllerBase
{
    [HttpGet("radar")]
    public async Task<IActionResult> Radar([FromQuery]DateTime? asOfUtc=null,CancellationToken token=default)
    {try{return Ok(await service.GetRadarAsync(asOfUtc,token));}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message,researchOnly=true});}}

    [HttpPost("radar/capture")]
    public async Task<IActionResult> Capture([FromQuery]DateTime? asOfUtc=null,CancellationToken token=default)
    {try{return Ok(await service.CaptureAsync(asOfUtc,token));}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message,researchOnly=true});}}

    [HttpPost("radar/evaluate")]
    public async Task<IActionResult> Evaluate(CancellationToken token=default)=>Ok(await service.EvaluateAsync(token));

    [HttpGet("radar/calibration")]
    public async Task<IActionResult> Calibration(CancellationToken token=default)=>Ok(await service.GetCalibrationAsync(token));

    [HttpPost("observations")]
    public async Task<IActionResult> Observe([FromBody]IntermarketObservationBatch batch,CancellationToken token=default)
    {await service.SaveAsync(batch,token);return Accepted(new{stored=true,researchOnly=true,canRouteToRealBroker=false});}
}
