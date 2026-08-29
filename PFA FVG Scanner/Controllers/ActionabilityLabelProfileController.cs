using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/actionability-label-profile")]
public sealed class ActionabilityLabelProfileController(ActionabilityLabelProfileService profile):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {try{return Ok(await profile.GetAsync(token));}catch(InvalidOperationException exception){return Conflict(new{message=exception.Message});}}
}
