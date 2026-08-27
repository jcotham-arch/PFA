using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.OrderFlow;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

public sealed record OrderFlowSnapshotRequest(string InstrumentId,string? ContractId,DateTime StartUtc,DateTime EndUtc,decimal PriceIncrement,DateTime AsOfUtc,string DataRevision);

[ApiController]
[Route("api/order-flow")]
public sealed class OrderFlowController:ControllerBase
{
    private readonly OrderFlowService _service;private readonly OrderFlowRepository _repository;
    public OrderFlowController(OrderFlowService service,OrderFlowRepository repository){_service=service;_repository=repository;}
    [HttpPost("events")]
    public async Task<ActionResult<OrderFlowCanonicalizationBatch>> Ingest(IReadOnlyList<ProviderOrderFlowEvent> events,CancellationToken token)
    {try{return Ok(await _service.IngestAsync(events,token));}catch(InvalidOperationException ex){return Conflict(new{message=ex.Message});}}
    [HttpPost("snapshots")]
    public async Task<ActionResult<OrderFlowFeatureSnapshot>> Snapshot(OrderFlowSnapshotRequest request,CancellationToken token)
    {try{return Ok(await _service.BuildSnapshotAsync(request.InstrumentId,request.ContractId,request.StartUtc,request.EndUtc,request.PriceIncrement,request.AsOfUtc,request.DataRevision,token));}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}}
    [HttpGet("snapshots/{snapshotId}")]
    public async Task<ActionResult<OrderFlowFeatureSnapshot>> Snapshot(string snapshotId,CancellationToken token){var value=await _repository.FindSnapshotAsync(snapshotId,token);return value is null?NotFound():Ok(value);}
    [HttpGet("capabilities")]
    public ActionResult Capabilities()=>Ok(new{canonicalizationVersion=OrderFlowCanonicalizer.Version,classifierVersion=TradeAggressorClassifier.Version,featureSetVersion=OrderFlowFeatureEngine.Version,
        exactProviderSourceSelected=false,installedProviderAdapters=Array.Empty<string>(),normalizedResearchIngestEnabled=true,
        candleDerivedOrderFlowForbidden=true,correctionAndCancelLineage=true,pointInTimeSafety=true,automaticDeletionEnabled=false,
        note="Provider payload semantics, entitlement, volume, and cost must be selected before a production adapter is enabled."});
}
