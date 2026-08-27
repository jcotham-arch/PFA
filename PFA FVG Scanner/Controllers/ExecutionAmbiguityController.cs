using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Execution;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/execution/ambiguity")]
public sealed class ExecutionAmbiguityController:ControllerBase
{
    private readonly IExecutionAmbiguityRepository _repository;
    public ExecutionAmbiguityController(IExecutionAmbiguityRepository repository)=>_repository=repository;
    [HttpGet("results/{resultId}")]
    public async Task<IActionResult> Get(string resultId,CancellationToken cancellationToken=default){var value=await _repository.FindResultAsync(resultId,cancellationToken);return value is null?NotFound():Ok(value);}
    [HttpGet("capabilities")]
    public IActionResult Capabilities()=>Ok(new{ResolutionHierarchy=new[]{"1s","tick"},
        OptimisticFallback=false,AmbiguityRetained=true,ExactLineage=true,PublicReprocessingEnabled=false});
}
