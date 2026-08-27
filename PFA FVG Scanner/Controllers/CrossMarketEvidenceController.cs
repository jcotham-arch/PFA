using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Evidence;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/evidence/cross-market")]
public sealed class CrossMarketEvidenceController:ControllerBase
{
    private readonly ICrossMarketEvidenceRepository _repository;
    public CrossMarketEvidenceController(ICrossMarketEvidenceRepository repository)=>_repository=repository;
    [HttpGet("{resultId}")]
    public async Task<IActionResult> Get(string resultId,CancellationToken cancellationToken=default){var result=await _repository.FindAsync(resultId,cancellationToken);return result is null?NotFound():Ok(result);}
    [HttpGet("capabilities")]
    public IActionResult Capabilities()=>Ok(new{TickPointDollarNormalization=true,ComparabilityNotes=true,
        MissingFeaturesAreNonComparable=true,SessionDifferencesAreExplicit=true,
        CanInvalidateSourceHypothesis=false,CanActivateStrategy=false,PublicMutationEnabled=false});
}
