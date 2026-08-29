using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/agent/hurdle-runs")]
public sealed class AgentHurdleRunsController(AgentHurdleTrainingService training,IWebHostEnvironment environment):ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get(CancellationToken token)=>Ok(await training.GetAllAsync(token));
    [HttpPost] public async Task<IActionResult> Train(AgentHurdleTrainingRequest request,CancellationToken token)
    {if(!environment.IsDevelopment()||HttpContext.Connection.RemoteIpAddress is not{} address||!System.Net.IPAddress.IsLoopback(address))return NotFound();try{return Ok(await training.TrainAsync(request,token));}catch(ArgumentException e){return BadRequest(new{message=e.Message});}catch(KeyNotFoundException e){return NotFound(new{message=e.Message});}catch(InvalidOperationException e){return Conflict(new{message=e.Message});}}
}
