using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Context;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/actionability")]
public sealed class ActionabilityEvidenceController(ActionabilityEvidenceService service,
    CanonicalTimelineRepository timeline,BarDerivedResearchContextEngine contextEngine):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? date,[FromQuery] ActionabilitySubjectKind? subjectKind,
        [FromQuery] string? instrumentId,CancellationToken token)
    {
        var value=await service.GetDayAsync(date??DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),token);
        var records=value.Records.Where(x=>(!subjectKind.HasValue||x.SubjectKind==subjectKind)&&
            (string.IsNullOrWhiteSpace(instrumentId)||x.InstrumentId.Equals(instrumentId.Trim(),StringComparison.OrdinalIgnoreCase))).ToArray();
        if(records.Length==value.Records.Count)return Ok(value);
        var coverage=new ActionabilityCoverageSummary(records.Length,records.Count(x=>x.CoverageStatus==ActionabilityCoverageStatus.Evaluated),
            records.Count(x=>x.CoverageStatus==ActionabilityCoverageStatus.PartiallyEvaluated),
            records.Count(x=>x.CoverageStatus==ActionabilityCoverageStatus.AwaitingScenarioEvaluation),records.Sum(x=>x.Scenarios.Count),
            records.Sum(x=>x.Scenarios.Count(s=>s.EligibleForAgentTraining)));
        return Ok(value with{Coverage=coverage,Records=records});
    }

    [HttpGet("records/{recordId}/context")]
    public async Task<IActionResult> Context(string recordId,[FromQuery] DateOnly? date,CancellationToken token)
    {
        var report=await service.GetDayAsync(date??DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),token);
        var record=report.Records.FirstOrDefault(x=>x.RecordId.Equals(recordId,StringComparison.Ordinal));
        if(record is null)return NotFound(new{message="Actionability record was not found for the selected UTC date."});
        var bars=await timeline.GetCurrentBarsAsync(record.InstrumentId,"1m",token);
        var snapshot=contextEngine.Build(record.InstrumentId,record.ContractId,"1m",record.RecognizedAtUtc,bars);
        return Ok(new{record.RecordId,record.SubjectKind,record.SourceId,record.EventType,record.RecognizedAtUtc,
            sourceTimeframe=record.Timeframe,contextTimeframe="1m",snapshot,
            interpretation="Every feature uses completed source bars known no later than the recognition clock."});
    }
}
