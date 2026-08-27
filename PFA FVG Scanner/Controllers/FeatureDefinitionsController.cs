using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Features;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/features/definitions")]
public sealed class FeatureDefinitionsController : ControllerBase
{
    private readonly IFeatureDefinitionRegistry _registry;
    public FeatureDefinitionsController(IFeatureDefinitionRegistry registry) => _registry = registry;

    [HttpGet]
    public IActionResult GetAll() => Ok(_registry.GetAll());

    [HttpGet("{id}")]
    public IActionResult Get(string id, [FromQuery] string? version = null)
    {
        var definition = _registry.Find(id, version);
        return definition is null ? NotFound() : Ok(definition);
    }
}
