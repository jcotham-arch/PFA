using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/sandbox/agent-promotion-readiness")]
public sealed class AgentSandboxPromotionController(AgentSandboxPromotionReadinessService readiness):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)=>Ok(await readiness.GetAsync(token));
}
