using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Forward;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

public sealed record CreateForwardCampaignRequest(string AccountId,string InstanceId,ForwardExpectation Expectation,string Actor);
public sealed record ForwardCampaignCommand(string Actor,string Reason);

[ApiController]
[Route("api/forward-campaigns")]
public sealed class ForwardCampaignController:ControllerBase
{
    private readonly ForwardCampaignService _service;private readonly ForwardCampaignRepository _repository;private readonly SandboxControlAuthorizer _authorization;
    public ForwardCampaignController(ForwardCampaignService service,ForwardCampaignRepository repository,SandboxControlAuthorizer authorization){_service=service;_repository=repository;_authorization=authorization;}
    [HttpGet("capabilities")]
    public ActionResult Capabilities()=>Ok(new{comparatorVersion=ForwardExpectationComparator.Version,continuousHealthSampling=true,closedSessionSnapshots=true,restartRecovery=true,operationalVsStrategyDegradationSeparated=true,automaticSafeSuspension=true,canPromoteStrategy=false,realBrokerRoutes=false,controlTokenConfigured=_authorization.IsConfigured});
    [HttpPost]
    public async Task<ActionResult> Create(CreateForwardCampaignRequest request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(async()=>await _service.CreateAsync(request.AccountId,request.InstanceId,request.Expectation,request.Actor,token));}
    [HttpPost("{campaignId}/start")]
    public async Task<ActionResult> Start(string campaignId,ForwardCampaignCommand request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(async()=>await _service.StartAsync(campaignId,request.Actor,request.Reason,token));}
    [HttpPost("{campaignId}/stop")]
    public async Task<ActionResult> Stop(string campaignId,ForwardCampaignCommand request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(async()=>await _service.StopAsync(campaignId,request.Actor,request.Reason,token));}
    [HttpPost("{campaignId}/health")]
    public async Task<ActionResult> Health(string campaignId,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(async()=>await _service.CaptureHealthAsync(campaignId,token));}
    [HttpPost("{campaignId}/days/{tradingDate}")]
    public async Task<ActionResult> CloseDay(string campaignId,DateOnly tradingDate,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(async()=>await _service.CaptureClosedDayAsync(campaignId,tradingDate,token));}
    [HttpGet("{campaignId}")]
    public async Task<ActionResult> Dashboard(string campaignId,CancellationToken token){if(!Authorized(out var denied))return denied;var value=await _service.DashboardAsync(campaignId,token);return value is null?NotFound():Ok(value);}
    [HttpGet]
    public async Task<ActionResult> Running(CancellationToken token){if(!Authorized(out var denied))return denied;return Ok(await _repository.GetByStatusAsync(ForwardCampaignStatus.Running,token));}
    private async Task<ActionResult> Execute(Func<Task<object>> action){try{return Ok(await action());}catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});}catch(UnauthorizedAccessException ex){return StatusCode(403,new{message=ex.Message});}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}}
    private bool Authorized(out ActionResult denied){if(!_authorization.IsConfigured){denied=StatusCode(503,new{message="Forward campaign controls are disabled until Sandbox:ControlToken is supplied at runtime."});return false;}if(!_authorization.Authorize(Request.Headers["X-PFA-Sandbox-Control"].FirstOrDefault())){denied=Unauthorized(new{message="A valid forward-operation control token is required."});return false;}denied=null!;return true;}
}
