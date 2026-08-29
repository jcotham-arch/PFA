using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/trade-journals")]
public sealed class TradeJournalController(TradeJournalImportService journals,TradeJournalMarketAlignmentService alignment,
    IWebHostEnvironment environment):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)=>Ok(await journals.GetImportsAsync(token));

    [HttpGet("{importId}/episodes")]
    public async Task<IActionResult> Episodes(string importId,CancellationToken token)
    {var values=await journals.GetEpisodesAsync(importId,token);return values.Count==0?NotFound():Ok(values);}

    [HttpGet("alignment-reports")]
    public async Task<IActionResult> AlignmentReports(CancellationToken token)=>Ok(await alignment.GetReportsAsync(token));

    [HttpGet("alignment-reports/{reportId}/episodes")]
    public async Task<IActionResult> AlignedEpisodes(string reportId,CancellationToken token)
    {var values=await alignment.GetAlignmentsAsync(reportId,token);return values.Count==0?NotFound():Ok(values);}

    [HttpPost("{importId}/align")]
    public async Task<IActionResult> Align(string importId,CancellationToken token)
    {
        if(!environment.IsDevelopment()||HttpContext.Connection.RemoteIpAddress is not { } address||
           !System.Net.IPAddress.IsLoopback(address))return NotFound();
        try{return Ok(await alignment.BuildAsync(importId,token));}
        catch(KeyNotFoundException exception){return NotFound(new{message=exception.Message});}
    }

    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Import(IFormFile file,CancellationToken token)
    {
        if(!environment.IsDevelopment()||HttpContext.Connection.RemoteIpAddress is not { } address||
           !System.Net.IPAddress.IsLoopback(address))return NotFound();
        if(file.Length==0)return BadRequest(new{message="A non-empty CSV trade journal is required."});
        try{await using var stream=file.OpenReadStream();return Ok(await journals.ImportAsync(stream,file.FileName,token));}
        catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}
        catch(InvalidOperationException exception){return UnprocessableEntity(new{message=exception.Message});}
    }
}
