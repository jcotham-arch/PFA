using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/sandbox/exploratory-campaigns")]
public sealed class ExploratoryPaperCampaignController(ExploratoryPaperCampaignService campaigns):ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ExploratoryPaperDashboard>> Get([FromQuery]string instrumentId="MES",
        CancellationToken token=default)
    {try{return Ok(await campaigns.DashboardAsync(instrumentId,token));}catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}}

    [HttpPost("blind-replay")]
    public async Task<ActionResult<ExploratoryPaperDashboard>> Run([FromQuery]string instrumentId="MES",
        CancellationToken token=default)
    {try{return Ok(await campaigns.RunBlindReplayAsync(instrumentId,token));}catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}}

    [HttpGet("{campaignId}")]
    public async Task<ActionResult<ExploratoryPaperCampaignDetail>> Detail(string campaignId,CancellationToken token=default)
    {var result=await campaigns.DetailAsync(campaignId,token);return result is null?NotFound():Ok(result);}
}
