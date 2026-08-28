using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Modules;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/product/modules")]
public sealed class ProductModulesController(ProductModuleCatalog catalog,ModuleEntitlementEvaluator evaluator,
    AgentTrainingReadinessService training):ControllerBase
{
    [HttpGet]
    public IActionResult Get()=>Ok(catalog.GetAll().Select(module=>new{module,preview=evaluator.Evaluate(new("ANONYMOUS",module.ModuleId,module.Version,false,false,false,[],DateTime.UtcNow))}));
    [HttpGet("capabilities")]
    public IActionResult Capabilities()=>Ok(new{subscriptionGated=true,externalApiModules=true,partnerModules=true,bringYourOwnAgent=true,paymentProviderConfigured=false,userAuthenticationConfigured=false,publicEntitlementMutation=false,subscriptionNeverBypassesSafety=true,liveRouting=false});
    [HttpGet("agent-training-readiness")]
    public async Task<IActionResult> Training(CancellationToken token)=>Ok(await training.GetAsync(token));
}
