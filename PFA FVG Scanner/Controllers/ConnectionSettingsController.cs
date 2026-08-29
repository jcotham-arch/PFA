using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Modules;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/settings/connections")]
public sealed class ConnectionSettingsController(ConnectionSettingsService settings):ControllerBase
{
    [HttpGet]
    public ActionResult<ConnectionSettingsDashboard> Get()=>Ok(settings.Get());
}
