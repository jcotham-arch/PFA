using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Validation;

public enum WalkForwardFoldStatus { InsufficientEvidence, Passed, Failed }
public enum WalkForwardAggregateStatus { InsufficientEvidence, Stable, Unstable, Degraded }

public sealed record WalkForwardPlanRequest(
    string Name,string FrozenSignature,string FrozenParameterHash,string DatasetId,string DataRevision,
    DateTime StartUtc,DateTime EndUtc,int TrainingDays,int ValidationDays,int StepDays,int EmbargoDays=1,int MinimumSamplesPerFold=20);

public sealed record WalkForwardFoldDefinition(
    string FoldId,int Ordinal,DateTime TrainingStartUtc,DateTime TrainingEndUtc,
    DateTime ValidationStartUtc,DateTime ValidationEndUtc,string DatasetId,string DataRevision);

public sealed record WalkForwardPlan(
    string PlanId,string PlanVersion,string Name,string FrozenSignature,string FrozenParameterHash,
    string DatasetId,string DataRevision,int MinimumSamplesPerFold,IReadOnlyList<WalkForwardFoldDefinition> Folds,DateTime CreatedAtUtc);

public sealed record WalkForwardObservation(
    string ObservationId,string Signature,string ParameterHash,string DatasetId,string DataRevision,
    DateTime OccurredAtUtc,decimal RealizedR,bool IsIndependentEvent=true);

public sealed record WalkForwardFoldResult(
    string FoldId,WalkForwardFoldStatus Status,int Samples,int IndependentEvents,decimal ExpectancyR,
    decimal WinRate,decimal ProfitFactor,decimal MaximumDrawdownR,string ObservationContentHash,
    bool ParameterDriftDetected,bool CanActivateStrategy=false);

public sealed record WalkForwardValidationReport(
    string ReportId,string ValidationEngineVersion,string PlanId,string FrozenSignature,string FrozenParameterHash,
    WalkForwardAggregateStatus Status,IReadOnlyList<WalkForwardFoldResult> Folds,int PassedFolds,int FailedFolds,
    decimal WeightedExpectancyR,decimal WorstFoldExpectancyR,decimal ExpectancyDegradationPercentage,
    bool ParameterDriftDetected,string DatasetId,string DataRevision,string ContentHash,DateTime CreatedAtUtc,
    bool CanActivateStrategy=false);

public sealed class WalkForwardPlanner
{
    public const string Version="1.0.0";
    public WalkForwardPlan Create(WalkForwardPlanRequest request,DateTime createdAtUtc)
    {
        var start=Utc(request.StartUtc);var end=Utc(request.EndUtc);
        Required(request.Name,nameof(request.Name));Required(request.FrozenSignature,nameof(request.FrozenSignature));Required(request.FrozenParameterHash,nameof(request.FrozenParameterHash));Required(request.DatasetId,nameof(request.DatasetId));Required(request.DataRevision,nameof(request.DataRevision));
        if(end<=start)throw new ArgumentException("EndUtc must be after StartUtc.");
        if(request.TrainingDays<1||request.ValidationDays<1||request.EmbargoDays<0||request.MinimumSamplesPerFold<1)throw new ArgumentOutOfRangeException(nameof(request));
        if(request.StepDays<request.ValidationDays)throw new ArgumentException("StepDays must be at least ValidationDays so validation folds cannot overlap.");
        var folds=new List<WalkForwardFoldDefinition>();var cursor=start;var ordinal=0;
        while(true){var trainEnd=cursor.AddDays(request.TrainingDays);var validationStart=trainEnd.AddDays(request.EmbargoDays);var validationEnd=validationStart.AddDays(request.ValidationDays);if(validationEnd>end)break;
            var seed=$"{request.DatasetId}|{request.DataRevision}|{cursor:O}|{trainEnd:O}|{validationStart:O}|{validationEnd:O}";
            folds.Add(new(Hex(seed)[..24],++ordinal,cursor,trainEnd,validationStart,validationEnd,request.DatasetId,request.DataRevision));cursor=cursor.AddDays(request.StepDays);}
        if(folds.Count==0)throw new ArgumentException("The requested range is too short to produce a complete fold.");
        var identity=JsonSerializer.Serialize(new{Version,request.Name,request.FrozenSignature,request.FrozenParameterHash,request.DatasetId,request.DataRevision,start,end,request.TrainingDays,request.ValidationDays,request.StepDays,request.EmbargoDays,request.MinimumSamplesPerFold,Folds=folds});
        return new(Hex(identity)[..32],Version,request.Name.Trim(),request.FrozenSignature.Trim(),request.FrozenParameterHash.Trim(),request.DatasetId.Trim(),request.DataRevision.Trim(),request.MinimumSamplesPerFold,folds,Utc(createdAtUtc));
    }
    internal static string Hex(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(value,DateTimeKind.Utc):value.ToUniversalTime();
    private static void Required(string value,string name){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException($"{name} is required.");}
}

public sealed class WalkForwardValidationEngine
{
    public const string Version="1.0.0";
    public WalkForwardValidationReport Evaluate(WalkForwardPlan plan,IReadOnlyList<WalkForwardObservation> observations,DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);observations??=[];
        var eligible=observations.Where(x=>x.Signature==plan.FrozenSignature).ToArray();
        if(eligible.Any(x=>x.DatasetId!=plan.DatasetId||x.DataRevision!=plan.DataRevision))throw new InvalidOperationException("Every observation must belong to the plan's frozen dataset and data revision.");
        var drift=eligible.Any(x=>x.ParameterHash!=plan.FrozenParameterHash);var results=new List<WalkForwardFoldResult>();
        foreach(var fold in plan.Folds)
        {
            if(fold.TrainingEndUtc>fold.ValidationStartUtc)throw new InvalidOperationException("Training and validation windows overlap.");
            var rows=eligible.Where(x=>x.OccurredAtUtc>=fold.ValidationStartUtc&&x.OccurredAtUtc<fold.ValidationEndUtc).OrderBy(x=>x.OccurredAtUtc).ThenBy(x=>x.ObservationId,StringComparer.Ordinal).ToArray();
            var independent=rows.Count(x=>x.IsIndependentEvent);var expectancy=rows.Length==0?0:rows.Average(x=>x.RealizedR);var wins=rows.Count(x=>x.RealizedR>0);var gains=rows.Where(x=>x.RealizedR>0).Sum(x=>x.RealizedR);var losses=Math.Abs(rows.Where(x=>x.RealizedR<0).Sum(x=>x.RealizedR));var pf=losses==0?(gains>0?decimal.MaxValue:0):gains/losses;
            decimal equity=0,peak=0,maxDd=0;foreach(var row in rows){equity+=row.RealizedR;peak=Math.Max(peak,equity);maxDd=Math.Max(maxDd,peak-equity);}
            var foldStatus=independent<plan.MinimumSamplesPerFold?WalkForwardFoldStatus.InsufficientEvidence:expectancy>0&&pf>=1?WalkForwardFoldStatus.Passed:WalkForwardFoldStatus.Failed;
            var hash=WalkForwardPlanner.Hex(JsonSerializer.Serialize(rows.Select(x=>new{x.ObservationId,x.DataRevision,x.RealizedR}).ToArray()));
            results.Add(new(fold.FoldId,foldStatus,rows.Length,independent,expectancy,rows.Length==0?0:100m*wins/rows.Length,pf,maxDd,hash,drift,false));
        }
        var sufficient=results.Where(x=>x.Status!=WalkForwardFoldStatus.InsufficientEvidence).ToArray();var passed=results.Count(x=>x.Status==WalkForwardFoldStatus.Passed);var failed=results.Count(x=>x.Status==WalkForwardFoldStatus.Failed);
        var weighted=results.Sum(x=>x.ExpectancyR*x.Samples)/Math.Max(1,results.Sum(x=>x.Samples));var worst=results.Min(x=>x.ExpectancyR);var first=results[0].ExpectancyR;var last=results[^1].ExpectancyR;var degradation=first==0?0:100m*(first-last)/Math.Abs(first);
        var aggregateStatus=drift?WalkForwardAggregateStatus.Unstable:sufficient.Length==0?WalkForwardAggregateStatus.InsufficientEvidence:degradation>50||failed>passed?WalkForwardAggregateStatus.Degraded:failed==0&&passed==results.Count?WalkForwardAggregateStatus.Stable:WalkForwardAggregateStatus.Unstable;
        var identity=JsonSerializer.Serialize(new{plan.PlanId,plan.FrozenSignature,plan.FrozenParameterHash,Status=aggregateStatus,Folds=results,weighted,worst,degradation,plan.DatasetId,plan.DataRevision,CanActivateStrategy=false});var content=WalkForwardPlanner.Hex(identity);
        return new($"WFR-{content[..32]}",Version,plan.PlanId,plan.FrozenSignature,plan.FrozenParameterHash,aggregateStatus,results,passed,failed,weighted,worst,degradation,drift,plan.DatasetId,plan.DataRevision,content,WalkForwardPlanner.Utc(createdAtUtc),false);
    }
}
