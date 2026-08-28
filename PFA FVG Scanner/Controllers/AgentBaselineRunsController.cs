using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/agent/baseline-runs")]
public sealed class AgentBaselineRunsController(AgentBaselineTrainingService training,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)=>Ok(await training.GetAllAsync(token));

    [HttpPost]
    public async Task<IActionResult> Train(AgentBaselineTrainingRequest request,CancellationToken token)
    {
        if(!environment.IsDevelopment()||HttpContext.Connection.RemoteIpAddress is not { } address||
           !System.Net.IPAddress.IsLoopback(address))return NotFound();
        try{return Ok(await training.TrainAsync(request,token));}
        catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}
        catch(KeyNotFoundException exception){return NotFound(new{message=exception.Message});}
        catch(InvalidOperationException exception){return Conflict(new{message=exception.Message});}
    }
}
