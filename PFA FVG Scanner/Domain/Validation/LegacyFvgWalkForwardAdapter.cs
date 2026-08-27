using System.Text.Json;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Domain.Validation;

public static class LegacyFvgWalkForwardAdapter
{
    public static (WalkForwardPlan Plan,WalkForwardValidationReport Report) MapSingleFold(
        FvgOutOfSampleValidationReport legacy,string frozenSignature,string frozenParameterHash,
        string datasetId,string dataRevision,DateTime discoveryStartUtc,DateTime discoveryEndUtc,DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        if(legacy.CanActivateStrategy)throw new UnauthorizedAccessException("Legacy validation evidence cannot activate a strategy.");
        var validationStart=WalkForwardPlanner.Utc(legacy.ValidationStartUtc);var validationEnd=WalkForwardPlanner.Utc(legacy.ValidationEndUtc);
        if(WalkForwardPlanner.Utc(discoveryEndUtc)>validationStart)throw new ArgumentException("Legacy discovery and validation windows overlap.");
        var foldId=WalkForwardPlanner.Hex($"{datasetId}|{dataRevision}|{discoveryStartUtc:O}|{discoveryEndUtc:O}|{validationStart:O}|{validationEnd:O}")[..24];
        var fold=new WalkForwardFoldDefinition(foldId,1,WalkForwardPlanner.Utc(discoveryStartUtc),WalkForwardPlanner.Utc(discoveryEndUtc),validationStart,validationEnd,datasetId,dataRevision);
        var planHash=WalkForwardPlanner.Hex(JsonSerializer.Serialize(new{AdapterVersion="legacy-single-fold-1.0.0",frozenSignature,frozenParameterHash,datasetId,dataRevision,fold}));
        var plan=new WalkForwardPlan(planHash[..32],"legacy-single-fold-1.0.0","Legacy FVG single-fold compatibility",frozenSignature,frozenParameterHash,datasetId,dataRevision,legacy.RequiredDistinctFvgs,[fold],WalkForwardPlanner.Utc(createdAtUtc));
        var status=legacy.Decision switch{ValidationDecision.PassedValidation=>WalkForwardFoldStatus.Passed,ValidationDecision.FailedValidation=>WalkForwardFoldStatus.Failed,_=>WalkForwardFoldStatus.InsufficientEvidence};
        var sourceHash=WalkForwardPlanner.Hex(JsonSerializer.Serialize(new{legacy.EngineVersion,legacy.ValidationStartUtc,legacy.ValidationEndUtc,legacy.MatchingTrades,legacy.ExpectancyR,legacy.ProfitFactorR,legacy.MaximumDrawdownR}));
        var foldResult=new WalkForwardFoldResult(foldId,status,legacy.MatchingTrades,legacy.DistinctFvgs,legacy.ExpectancyR,legacy.WinRate,legacy.ProfitFactorR,legacy.MaximumDrawdownR,sourceHash,false,false);
        var aggregate=status==WalkForwardFoldStatus.Passed?WalkForwardAggregateStatus.Stable:status==WalkForwardFoldStatus.Failed?WalkForwardAggregateStatus.Degraded:WalkForwardAggregateStatus.InsufficientEvidence;
        var content=WalkForwardPlanner.Hex(JsonSerializer.Serialize(new{plan.PlanId,Fold=foldResult,Status=aggregate,datasetId,dataRevision,CanActivateStrategy=false}));
        var report=new WalkForwardValidationReport($"WFR-{content[..32]}","legacy-adapter-1.0.0",plan.PlanId,frozenSignature,frozenParameterHash,aggregate,[foldResult],status==WalkForwardFoldStatus.Passed?1:0,status==WalkForwardFoldStatus.Failed?1:0,legacy.ExpectancyR,legacy.ExpectancyR,0,false,datasetId,dataRevision,content,WalkForwardPlanner.Utc(createdAtUtc),false);
        return(plan,report);
    }
}
