using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Discovery;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/machine-discovery")]
public sealed class MachineDiscoveryController(MachineBehaviorDiscoveryEngine engine,IMachineDiscoveryRepository repository):ControllerBase
{
    [HttpGet("capabilities")]
    public IActionResult Capabilities()=>Ok(new{EngineVersion="deterministic-feature-cluster-1.0.0",FrameworkNeutral=true,TemporalSplitRequired=true,PointInTimeLeakageChecks=true,SeededReproducibility=true,CompleteClusterRetention=true,MultipleComparisonCorrection="Bonferroni",ProducesOrdinaryResearchHypotheses=true,CanActivateStrategy=false,CanBypassEvidenceStages=false,PublicMutationEnabled=false});
    [HttpGet("runs/{runId}")]
    public async Task<IActionResult> Get(string runId,CancellationToken cancellationToken){var value=await repository.FindAsync(runId,cancellationToken);return value is null?NotFound():Ok(value);}
    internal MachineDiscoveryResult Evaluate(MachineDiscoveryManifest manifest,IReadOnlyList<MachineDiscoveryObservation> observations)=>engine.Discover(manifest,observations);
}
