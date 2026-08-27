using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Governance;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/governance")]
public sealed class GovernanceController:ControllerBase
{
    private readonly GovernanceRepository _repository;private readonly SandboxControlAuthorizer _authorization;
    public GovernanceController(GovernanceRepository repository,SandboxControlAuthorizer authorization){_repository=repository;_authorization=authorization;}
    [HttpGet("capabilities")]
    public ActionResult Capabilities()=>Ok(new{engineVersion=GovernanceEngine.Version,defaultDeny=true,realBrokerAuthorization=false,immutableDecisionAudit=true,policyRequired=true,healthRequired=true,riskRequired=true,approvalsSupported=true,suspensionsSupported=true,emergencyStopSupported=true,controlTokenConfigured=_authorization.IsConfigured});
    [HttpPost("policies")]
    public async Task<ActionResult> Policy(GovernancePolicy policy,CancellationToken token){if(!Authorized(out var denied))return denied;try{await _repository.SavePolicyAsync(policy,token);return Ok(policy);}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}}
    [HttpGet("policies/effective")]
    public async Task<ActionResult<GovernancePolicy>> EffectivePolicy(CancellationToken token){if(!Authorized(out var denied))return denied;var value=await _repository.GetEffectivePolicyAsync(DateTime.UtcNow,token);return value is null?NotFound():Ok(value);}
    [HttpPost("approvals")]
    public async Task<ActionResult> Approval(GovernanceApproval value,CancellationToken token){if(!Authorized(out var denied))return denied;await _repository.GrantApprovalAsync(value,token);return Ok(value);}
    [HttpPost("approvals/revoke")]
    public async Task<ActionResult> Revoke(GovernanceApprovalRevocation value,CancellationToken token){if(!Authorized(out var denied))return denied;await _repository.RevokeApprovalAsync(value,token);return Ok(value);}
    [HttpGet("approvals")]
    public async Task<ActionResult<IReadOnlyList<GovernanceApproval>>> Approvals(CancellationToken token){if(!Authorized(out var denied))return denied;return Ok(await _repository.GetApprovalsAsync(token));}
    [HttpPost("suspensions")]
    public async Task<ActionResult> Suspend(GovernanceSuspension value,CancellationToken token){if(!Authorized(out var denied))return denied;await _repository.SuspendAsync(value,token);return Ok(value);}
    [HttpPost("suspensions/resume")]
    public async Task<ActionResult> Resume(GovernanceSuspensionResume value,CancellationToken token){if(!Authorized(out var denied))return denied;await _repository.ResumeAsync(value,token);return Ok(value);}
    [HttpGet("suspensions")]
    public async Task<ActionResult<IReadOnlyList<GovernanceSuspension>>> Suspensions(CancellationToken token){if(!Authorized(out var denied))return denied;return Ok(await _repository.GetSuspensionsAsync(token));}
    [HttpPost("emergency-stop")]
    public async Task<ActionResult> Emergency(GovernanceEmergencyStop value,CancellationToken token){if(!Authorized(out var denied))return denied;await _repository.SaveEmergencyStopAsync(value,token);if(value.IsActive){var evidence=System.Text.Json.JsonSerializer.Serialize(value);var hash=GovernanceHash.Of(evidence);await _repository.SaveIncidentAsync(new($"GVI-{hash[..32]}","Critical","EmergencyStop",null,null,value.OccurredAtUtc,value.Reason,evidence,hash),token);}return Ok(value);}
    [HttpGet("emergency-stop")]
    public async Task<ActionResult<GovernanceEmergencyStop>> Emergency(CancellationToken token){if(!Authorized(out var denied))return denied;var value=await _repository.GetEmergencyStopAsync(token);return value is null?NotFound():Ok(value);}
    [HttpGet("decisions/{accountId}")]
    public async Task<ActionResult<IReadOnlyList<GovernanceDecision>>> Decisions(string accountId,[FromQuery]int limit=100,CancellationToken token=default){if(!Authorized(out var denied))return denied;return Ok(await _repository.GetDecisionsAsync(accountId,limit,token));}
    [HttpGet("incidents")]
    public async Task<ActionResult<IReadOnlyList<GovernanceIncident>>> Incidents([FromQuery]int limit=100,CancellationToken token=default){if(!Authorized(out var denied))return denied;return Ok(await _repository.GetIncidentsAsync(limit,token));}
    private bool Authorized(out ActionResult denied){if(!_authorization.IsConfigured){denied=StatusCode(503,new{message="Governance controls are disabled until Sandbox:ControlToken is supplied at runtime."});return false;}if(!_authorization.Authorize(Request.Headers["X-PFA-Sandbox-Control"].FirstOrDefault())){denied=Unauthorized(new{message="A valid governance control token is required."});return false;}denied=null!;return true;}
}
