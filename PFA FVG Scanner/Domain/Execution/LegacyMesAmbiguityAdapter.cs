using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Domain.Execution;

public static class LegacyMesAmbiguityAdapter
{
    public static ExecutionEvidenceRequest? CreateRequest(MesTradeScenario scenario,string instrumentId,string dataRevision)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if(!scenario.IntrabarSequenceUnknown||!scenario.StopPrice.HasValue||!scenario.TargetPrice.HasValue)return null;
        var eventTime=scenario.StopHitTimeUtc??scenario.TargetHitTimeUtc;
        if(!eventTime.HasValue)return null;
        var start=DateTime.SpecifyKind(eventTime.Value,DateTimeKind.Utc);
        return new($"MES-AMBIGUITY-{scenario.ScenarioId:N}",scenario.ScenarioId.ToString(),instrumentId,
            scenario.Direction==FvgDirection.Bullish?PatternDirection.Bullish:PatternDirection.Bearish,
            start,start.AddMinutes(1),scenario.StopPrice.Value,scenario.TargetPrice.Value,
            ExecutionResolution.OneMinute,scenario.EngineVersion,dataRevision);
    }
}
