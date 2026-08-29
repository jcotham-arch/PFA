using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Context;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/context-families")]
public sealed class ResearchContextController(IResearchContextFamilyRegistry registry):ControllerBase
{
    [HttpGet]
    public IActionResult Get()=>Ok(registry.GetCatalog());

    [HttpGet("{familyId}")]
    public IActionResult Get(string familyId)=>registry.Find(familyId) is { } family?Ok(family):NotFound();
}
