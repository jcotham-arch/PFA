using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Certification;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/certification/campaigns")]
public sealed class CertificationCampaignController(CertificationCampaignService service,
    SandboxControlAuthorizer authorization):ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Run(CertificationCampaignRequest request,CancellationToken token)
    {
        if(!authorization.IsConfigured)return StatusCode(503,new{message="Certification controls are disabled until Sandbox:ControlToken is supplied at runtime."});
        if(!authorization.Authorize(Request.Headers["X-PFA-Sandbox-Control"].FirstOrDefault()))return Unauthorized(new{message="A valid sandbox control token is required."});
        try{return Ok(await service.RunAsync(request,token));}
        catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
        catch(UnauthorizedAccessException ex){return StatusCode(403,new{message=ex.Message});}
        catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}
    }
}
