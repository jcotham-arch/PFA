using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Historical;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/historical-pipeline")]
public sealed class HistoricalPipelineController : ControllerBase
{
    private readonly HistoricalUniversePlanner _planner;
    private readonly HistoricalPipelineService _pipeline;
    private readonly HistoricalPipelineRepository _repository;
    public HistoricalPipelineController(HistoricalUniversePlanner planner,HistoricalPipelineService pipeline,HistoricalPipelineRepository repository)
    { _planner=planner;_pipeline=pipeline;_repository=repository; }

    [HttpPost("jobs")]
    public async Task<ActionResult<HistoricalJobSnapshot>> Submit(HistoricalDatasetRequest request,CancellationToken token)
    {
        try
        {
            var plan=_planner.Create(request,DateTime.UtcNow);
            var job=await _pipeline.SubmitAsync(plan,DateTime.UtcNow,token);
            return CreatedAtAction(nameof(Status),new{jobId=job.JobId},job);
        }
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
    }

    [HttpGet("jobs/{jobId}")]
    public async Task<ActionResult<HistoricalJobSnapshot>> Status(string jobId,CancellationToken token)
    {var job=await _repository.FindAsync(jobId,token);return job is null?NotFound():Ok(job);}

    [HttpPost("jobs/{jobId}/run")]
    public async Task<ActionResult<HistoricalJobSnapshot>> Run(string jobId,CancellationToken token)
    {
        var existing=await _repository.FindAsync(jobId,token);if(existing is null)return NotFound();
        if(!string.Equals(existing.Plan.Provider,"Massive",StringComparison.OrdinalIgnoreCase))
            return Conflict(new{message="The installed execution adapter supports only the explicitly configured Massive provider."});
        try{return Ok(await _pipeline.RunAsync(jobId,token));}
        catch(InvalidOperationException ex){return StatusCode(503,new{message=ex.Message});}
    }

    [HttpGet("capabilities")]
    public ActionResult Capabilities()=>Ok(new
    {
        planVersion=HistoricalUniversePlanner.Version,
        providerAdapter="Massive",
        submissionStartsProviderDownload=false,
        explicitRunRequired=true,
        requiresExplicitDatedProviderSymbols=true,
        automaticRolloverResolution=false,
        sessionModel="legacy-utc-1.0.0",
        notes="Plans and checkpoints are durable. Contract rollover and authoritative CME session calendars remain unresolved inputs."
    });
}
