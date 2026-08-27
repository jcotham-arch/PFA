using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/runs")]
public sealed class GeneralResearchRunsController : ControllerBase
{
    private readonly IGeneralResearchRepository _repository;
    public GeneralResearchRunsController(IGeneralResearchRepository repository) => _repository = repository;

    [HttpGet]
    public async Task<IActionResult> GetRecent([FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await _repository.GetRecentAsync(limit, cancellationToken));

    [HttpGet("{researchRunId}")]
    public async Task<IActionResult> Get(string researchRunId, CancellationToken cancellationToken = default)
    {
        var run = await _repository.FindAsync(researchRunId, cancellationToken);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("capabilities")]
    public IActionResult GetCapabilities() => Ok(new
    {
        ReproducibleRunManifests = true,
        CompleteSearchSpaceRetention = true,
        NegativeResultsRetained = true,
        CanActivateStrategy = false,
        PublicMutationEnabled = false
    });
}
