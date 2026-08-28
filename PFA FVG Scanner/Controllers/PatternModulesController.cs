using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/patterns")]
public sealed class PatternModulesController : ControllerBase
{
    private readonly IMarketPatternModuleRegistry _registry;
    private readonly ObservationRepository _observations;
    private readonly UniversalMarketRecordRepository _universalRecords;
    private readonly PatternObservationResearchService _research;
    public PatternModulesController(IMarketPatternModuleRegistry registry, ObservationRepository observations,
        UniversalMarketRecordRepository universalRecords,PatternObservationResearchService research)
        => (_registry, _observations, _universalRecords,_research) = (registry, observations, universalRecords,research);

    [HttpGet("modules")]
    public IActionResult GetModules() => Ok(_registry.GetAll());

    [HttpGet("observations/fvg")]
    public async Task<IActionResult> GetFvgs([FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await _observations.GetRecentFvgsAsync(Math.Clamp(limit, 1, 200), cancellationToken));

    [HttpGet("observations")]
    public async Task<IActionResult> GetUniversalObservations([FromQuery] string? moduleId = null,
        [FromQuery] int limit = 100, CancellationToken cancellationToken = default) =>
        Ok(await _universalRecords.GetObservationsAsync(moduleId, limit, cancellationToken));

    [HttpGet("observations/{observationId}/research")]
    public async Task<IActionResult> GetObservationResearch(string observationId,CancellationToken cancellationToken=default)
    {var value=await _research.GetAsync(observationId,cancellationToken);return value is null?NotFound():Ok(value);}

    [HttpGet("outcomes")]
    public async Task<IActionResult> GetUniversalOutcomes([FromQuery] string? observationId = null,
        [FromQuery] int limit = 100, CancellationToken cancellationToken = default) =>
        Ok(await _universalRecords.GetOutcomesAsync(observationId, limit, cancellationToken));
}
