using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Sequences;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/sequences")]
public sealed class MarketSequencesController : ControllerBase
{
    private readonly IMarketSequenceDefinitionRegistry _definitions;
    private readonly IMarketSequenceEngine _engine;
    private readonly UniversalMarketRecordRepository _observations;
    public MarketSequencesController(IMarketSequenceDefinitionRegistry definitions,
        IMarketSequenceEngine engine, UniversalMarketRecordRepository observations) =>
        (_definitions, _engine, _observations) = (definitions, engine, observations);

    [HttpGet("definitions")]
    public IActionResult GetDefinitions() => Ok(_definitions.GetAll());

    [HttpGet("preview/{definitionId}")]
    public async Task<IActionResult> Preview(string definitionId, [FromQuery] DateTime? asOfUtc = null,
        [FromQuery] int observationLimit = 500, CancellationToken cancellationToken = default)
    {
        var definition = _definitions.Find(definitionId);
        if (definition is null) return NotFound(new { message = $"Unknown sequence definition '{definitionId}'." });
        var cutoff = asOfUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var observations = await _observations.GetObservationsAsync(limit: observationLimit,
            cancellationToken: cancellationToken);
        return Ok(new
        {
            definition.SequenceDefinitionId,
            definition.Version,
            AsOfUtc = cutoff,
            IsPersisted = false,
            Sequences = _engine.Replay(definition, observations, cutoff)
        });
    }
}
