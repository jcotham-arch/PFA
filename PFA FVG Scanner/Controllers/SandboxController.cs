using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

public sealed record CreateSandboxAccountRequest(string CommandId,string AccountId,string DisplayName,decimal InitialBalance);
public sealed record CreateSandboxInstanceRequest(string CommandId,string InstanceId,string StrategyId,string StrategyVersion,string InstrumentId,string? ContractId);
public sealed record SandboxCommandRequest(string CommandId);
public sealed record SandboxSignalRequest(string CommandId,SandboxSignal Signal,SandboxFillModel FillModel);
public sealed record SandboxMarketRequest(string CommandId,SandboxMarketSlice Market,SandboxFillModel FillModel);

[ApiController]
[Route("api/sandbox")]
public sealed class SandboxController:ControllerBase
{
    private readonly SandboxService _service;private readonly GovernedSandboxService _governed;private readonly SandboxControlAuthorizer _authorization;
    public SandboxController(SandboxService service,GovernedSandboxService governed,SandboxControlAuthorizer authorization){_service=service;_governed=governed;_authorization=authorization;}
    [HttpGet("capabilities")]
    public ActionResult Capabilities()=>Ok(new{virtualOnly=true,realBrokerRoutes=false,controlTokenConfigured=_authorization.IsConfigured,controlAndReadRequireToken=true,appendOnlyLedger=true,noFutureClock=true,automaticStrategyActivation=false,fillModel="explicit-versioned"});
    [HttpPost("accounts")]
    public async Task<ActionResult<SandboxAccountState>> CreateAccount(CreateSandboxAccountRequest request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(()=>_service.CreateAccountAsync(request.CommandId,request.AccountId,request.DisplayName,request.InitialBalance,token));}
    [HttpPost("accounts/{accountId}/instances")]
    public async Task<ActionResult<SandboxAccountState>> CreateInstance(string accountId,CreateSandboxInstanceRequest request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(()=>_service.CreateInstanceAsync(request.CommandId,accountId,request.InstanceId,request.StrategyId,request.StrategyVersion,request.InstrumentId,request.ContractId,token));}
    [HttpPost("accounts/{accountId}/instances/{instanceId}/start")]
    public async Task<ActionResult<SandboxAccountState>> Start(string accountId,string instanceId,SandboxCommandRequest request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(()=>_service.StartInstanceAsync(request.CommandId,accountId,instanceId,token));}
    [HttpPost("accounts/{accountId}/signals")]
    public async Task<ActionResult> Signal(string accountId,SandboxSignalRequest request,CancellationToken token){if(!Authorized(out var denied))return denied;try{var result=await _governed.SubmitSignalAsync(request.CommandId,accountId,request.Signal,request.FillModel,token);return result.State is null?StatusCode(403,result):Ok(result);}catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}}
    [HttpPost("accounts/{accountId}/market")]
    public async Task<ActionResult<SandboxAccountState>> Market(string accountId,SandboxMarketRequest request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(()=>_service.ProcessMarketAsync(request.CommandId,accountId,request.Market,request.FillModel,token));}
    [HttpPost("accounts/{accountId}/orders/{orderId}/cancel")]
    public async Task<ActionResult<SandboxAccountState>> Cancel(string accountId,string orderId,SandboxCommandRequest request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(()=>_service.CancelOrderAsync(request.CommandId,accountId,orderId,token));}
    [HttpPost("accounts/{accountId}/instances/{instanceId}/stop")]
    public async Task<ActionResult<SandboxAccountState>> Stop(string accountId,string instanceId,SandboxCommandRequest request,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(()=>_service.StopInstanceAsync(request.CommandId,accountId,instanceId,token));}
    [HttpGet("accounts/{accountId}")]
    public async Task<ActionResult<SandboxAccountState>> Account(string accountId,CancellationToken token){if(!Authorized(out var denied))return denied;return await Execute(()=>_service.GetAccountAsync(accountId,token));}
    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<string>>> Accounts(CancellationToken token){if(!Authorized(out var denied))return denied;return Ok(await _service.GetAccountIdsAsync(token));}
    private bool Authorized(out ActionResult denied){if(!_authorization.IsConfigured){denied=StatusCode(503,new{message="Sandbox controls are disabled until Sandbox:ControlToken is supplied at runtime."});return false;}if(!_authorization.Authorize(Request.Headers["X-PFA-Sandbox-Control"].FirstOrDefault())){denied=Unauthorized(new{message="A valid sandbox control token is required."});return false;}denied=null!;return true;}
    private async Task<ActionResult<SandboxAccountState>> Execute(Func<Task<SandboxAccountState>> action){try{return Ok(await action());}catch(KeyNotFoundException ex){return NotFound(new{message=ex.Message});}catch(UnauthorizedAccessException ex){return StatusCode(403,new{message=ex.Message});}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}}
}
