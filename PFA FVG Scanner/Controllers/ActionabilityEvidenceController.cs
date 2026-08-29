using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Context;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/actionability")]
public sealed class ActionabilityEvidenceController(ActionabilityEvidenceService service,
    CanonicalTimelineRepository timeline,BarDerivedResearchContextEngine contextEngine,
    PositionSizingResearchEngine positionSizing):ControllerBase
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

    [HttpPost("records/{recordId}/scenarios/{scenarioId}/position-sizing")]
    public async Task<IActionResult> PositionSizing(string recordId,string scenarioId,PositionSizingAccountRequest request,
        [FromQuery] DateOnly? date,CancellationToken token)
    {
        var report=await service.GetDayAsync(date??DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),token);
        var record=report.Records.FirstOrDefault(x=>x.RecordId.Equals(recordId,StringComparison.Ordinal));
        if(record is null)return NotFound(new{message="Actionability record was not found for the selected UTC date."});
        var scenario=record.Scenarios.FirstOrDefault(x=>x.ScenarioId.Equals(scenarioId,StringComparison.Ordinal));
        if(scenario is null)return NotFound(new{message="Scenario was not found on the selected actionability record."});
        if(!scenario.NetR.HasValue)return Conflict(new{message="The selected scenario has no finalized R outcome to size."});
        try
        {
            var evaluation=positionSizing.Evaluate(new(scenario.NetR.Value,request.RiskDollarsPerContract,
                request.RoundTurnCommissionPerContract,request.AccountBalance,request.MaximumDailyLossDollars,
                request.MaximumDrawdownDollars,request.MinimumContracts,request.MaximumContracts));
            return Ok(new{record.RecordId,record.SourceId,scenario.ScenarioId,scenario.HypothesisId,scenario.Outcome,
                scenario.NetR,evaluation});
        }
        catch(ArgumentException exception){return BadRequest(new{message=exception.Message});}
    }
}
