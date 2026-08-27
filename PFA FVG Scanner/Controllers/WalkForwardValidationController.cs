using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Validation;

namespace PFA_FVG_Scanner.Controllers;

public sealed record WalkForwardEvaluationRequest(WalkForwardPlanRequest Plan,IReadOnlyList<WalkForwardObservation> Observations);

[ApiController]
[Route("api/walk-forward")]
public sealed class WalkForwardValidationController:ControllerBase
{
    private readonly WalkForwardPlanner _planner;private readonly WalkForwardValidationEngine _engine;private readonly IWalkForwardValidationRepository _repository;
    public WalkForwardValidationController(WalkForwardPlanner planner,WalkForwardValidationEngine engine,IWalkForwardValidationRepository repository){_planner=planner;_engine=engine;_repository=repository;}
    [HttpPost("evaluate")]
    public async Task<ActionResult<WalkForwardValidationReport>> Evaluate(WalkForwardEvaluationRequest request,CancellationToken token)
    {try{var plan=_planner.Create(request.Plan,DateTime.UtcNow);var report=_engine.Evaluate(plan,request.Observations,DateTime.UtcNow);await _repository.SaveAsync(plan,report,token);return Ok(report);}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}}
    [HttpGet("reports/{reportId}")]
    public async Task<ActionResult<WalkForwardValidationReport>> Report(string reportId,CancellationToken token){var report=await _repository.FindReportAsync(reportId,token);return report is null?NotFound():Ok(report);}
    [HttpGet("plans/{planId}")]
    public async Task<ActionResult<WalkForwardPlan>> Plan(string planId,CancellationToken token){var plan=await _repository.FindPlanAsync(planId,token);return plan is null?NotFound():Ok(plan);}
    [HttpGet("capabilities")]
    public ActionResult Capabilities()=>Ok(new{plannerVersion=WalkForwardPlanner.Version,engineVersion=WalkForwardValidationEngine.Version,
        rollingFolds=true,nonOverlappingValidation=true,embargoSupported=true,frozenParameterHashRequired=true,dataRevisionIsolation=true,
        parameterDriftDetection=true,canActivateStrategy=false,legacySingleFoldValidatorPreserved=true});
}
