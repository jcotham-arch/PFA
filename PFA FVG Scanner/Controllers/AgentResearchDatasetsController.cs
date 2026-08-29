using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/agent/research-datasets")]
public sealed class AgentResearchDatasetsController(GenericOutcomeDatasetService datasets,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token) => Ok(await datasets.GetAllAsync(token));

    [HttpPost]
    public async Task<IActionResult> Build(GenericOutcomeDatasetRequest request, CancellationToken token)
    {
        if (!environment.IsDevelopment() || HttpContext.Connection.RemoteIpAddress is not { } address ||
            !System.Net.IPAddress.IsLoopback(address)) return NotFound();
        try { return Ok(await datasets.BuildAsync(request, token)); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpGet("{datasetId}/feature-coverage")]
    public async Task<IActionResult> FeatureCoverage(string datasetId,[FromQuery]string? prefix,CancellationToken token)
    {
        try{return Ok(await datasets.GetFeatureCoverageAsync(datasetId,prefix,token));}
        catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}
        catch(KeyNotFoundException exception){return NotFound(new{message=exception.Message});}
    }
}
