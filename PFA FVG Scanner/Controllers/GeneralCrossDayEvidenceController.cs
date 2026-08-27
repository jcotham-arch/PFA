using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Evidence;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/evidence/cross-day")]
public sealed class GeneralCrossDayEvidenceController : ControllerBase
{
    private readonly ICrossDayEvidenceRepository _repository;
    public GeneralCrossDayEvidenceController(ICrossDayEvidenceRepository repository) => _repository = repository;
    [HttpGet("{reportId}")]
    public async Task<IActionResult> Get(string reportId,CancellationToken cancellationToken=default)
    {var report=await _repository.FindAsync(reportId,cancellationToken);return report is null?NotFound():Ok(report);}
    [HttpGet("capabilities")]
    public IActionResult Capabilities()=>Ok(new{ImmutableTradingDates=true,MissingDaysRetained=true,
        PersistentNegativesRetained=true,RegimeMetadata=true,CanActivateStrategy=false,PublicMutationEnabled=false});
}
