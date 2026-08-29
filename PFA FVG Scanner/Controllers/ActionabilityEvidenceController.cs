using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/actionability")]
public sealed class ActionabilityEvidenceController(ActionabilityEvidenceService service):ControllerBase
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
}
