using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Modules;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/product/modules/advanced-strategies")]
public sealed class AdvancedStrategiesIntegrationController(AdvancedStrategiesCompatibilityService compatibility):ControllerBase
{
    [HttpGet("integration-packet")]
    public ActionResult<AdvancedStrategiesIntegrationPacket> Packet()=>Ok(compatibility.Packet());

    [HttpPost("compatibility")]
    public ActionResult<AdvancedStrategiesCompatibilityResult> Validate(AdvancedStrategiesManifest manifest)
    {var result=compatibility.Validate(manifest);return result.Compatible?Ok(result):UnprocessableEntity(result);}
}
