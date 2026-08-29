using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Context;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/context-families")]
public sealed class ResearchContextController(IResearchContextFamilyRegistry registry,
    BarDerivedResearchContextEngine engine,CanonicalTimelineRepository timeline):ControllerBase
{
    [HttpGet]
    public IActionResult Get()=>Ok(registry.GetCatalog());

    [HttpGet("{familyId}")]
    public IActionResult Get(string familyId)=>registry.Find(familyId) is { } family?Ok(family):NotFound();

    [HttpGet("snapshot")]
    public async Task<IActionResult> Snapshot([FromQuery] string instrumentId,[FromQuery] string timeframe="1m",
        [FromQuery] string? contractId=null,[FromQuery] DateTime? decisionTimeUtc=null,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(instrumentId))return BadRequest(new{message="InstrumentId is required."});
        var clock=decisionTimeUtc??DateTime.UtcNow;var bars=await timeline.GetCurrentBarsAsync(instrumentId,timeframe,token);
        return Ok(engine.Build(instrumentId.Trim().ToUpperInvariant(),contractId,timeframe,clock,bars));
    }
}
