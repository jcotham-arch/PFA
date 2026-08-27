using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Patterns;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/patterns")]
public sealed class PatternModulesController : ControllerBase
{
    private readonly IMarketPatternModuleRegistry _registry;
    private readonly ObservationRepository _observations;
    private readonly UniversalMarketRecordRepository _universalRecords;
    public PatternModulesController(IMarketPatternModuleRegistry registry, ObservationRepository observations,
        UniversalMarketRecordRepository universalRecords)
        => (_registry, _observations, _universalRecords) = (registry, observations, universalRecords);

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

    [HttpGet("outcomes")]
    public async Task<IActionResult> GetUniversalOutcomes([FromQuery] string? observationId = null,
        [FromQuery] int limit = 100, CancellationToken cancellationToken = default) =>
        Ok(await _universalRecords.GetOutcomesAsync(observationId, limit, cancellationToken));
}
