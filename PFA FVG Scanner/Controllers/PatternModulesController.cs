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
    public PatternModulesController(IMarketPatternModuleRegistry registry, ObservationRepository observations)
        => (_registry, _observations) = (registry, observations);

    [HttpGet("modules")]
    public IActionResult GetModules() => Ok(_registry.GetAll());

    [HttpGet("observations/fvg")]
    public async Task<IActionResult> GetFvgs([FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await _observations.GetRecentFvgsAsync(Math.Clamp(limit, 1, 200), cancellationToken));
}
