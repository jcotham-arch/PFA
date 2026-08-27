using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Instruments;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/instruments")]
public sealed class InstrumentDefinitionsController : ControllerBase
{
    private readonly IInstrumentDefinitionRegistry _registry;

    public InstrumentDefinitionsController(IInstrumentDefinitionRegistry registry) => _registry = registry;

    [HttpGet]
    public IActionResult GetAll() => Ok(_registry.GetAll());

    [HttpGet("{instrumentId}")]
    public IActionResult Get(string instrumentId, [FromQuery] DateOnly? asOfDate = null)
    {
        var definition = _registry.Find(instrumentId, asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        return definition is null ? NotFound() : Ok(definition);
    }
}
