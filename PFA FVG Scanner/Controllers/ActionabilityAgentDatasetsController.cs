using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/agent/actionability-datasets")]
public sealed class ActionabilityAgentDatasetsController(ActionabilityOutcomeDatasetService datasets,IWebHostEnvironment environment):ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Build(ActionabilityOutcomeDatasetRequest request,CancellationToken token)
    {
        if(!environment.IsDevelopment()||HttpContext.Connection.RemoteIpAddress is not{} address||!System.Net.IPAddress.IsLoopback(address))return NotFound();
        try{return Ok(await datasets.BuildAsync(request,token));}
        catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}
        catch(InvalidOperationException exception){return Conflict(new{message=exception.Message});}
    }
}
