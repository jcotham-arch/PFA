using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Strategies;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/strategies")]
public sealed class StrategyRegistryController : ControllerBase
{
    private readonly IStrategyRegistry _registry;
    public StrategyRegistryController(IStrategyRegistry registry) => _registry = registry;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default) =>
        Ok(await _registry.GetAllAsync(cancellationToken));

    [HttpGet("{strategyId}/versions/{strategyVersion}")]
    public async Task<IActionResult> Get(string strategyId, string strategyVersion,
        CancellationToken cancellationToken = default)
    {
        var entry = await _registry.FindAsync(strategyId, strategyVersion, cancellationToken);
        return entry is null ? NotFound() : Ok(entry);
    }

    [HttpGet("capabilities")]
    public IActionResult GetCapabilities() => Ok(new
    {
        CanRegisterThroughPublicApi = false,
        CanActivateStrategy = false,
        CanPlaceTrades = false,
        SupportsNoTradeDecision = true,
        HighestPhaseTenStatus = StrategyRegistryStatus.ValidationComplete.ToString()
    });
}
