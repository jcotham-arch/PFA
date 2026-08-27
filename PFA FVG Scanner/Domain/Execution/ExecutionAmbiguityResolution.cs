using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Execution;

public enum ExecutionResolution { OneMinute, OneSecond, Tick }
public enum ExecutionChronology { TargetBeforeStop, StopBeforeTarget, StillAmbiguous, NoEvidence }

public sealed record ExecutionEvidenceRequest(
    string RequestId,
    string SubjectId,
    string InstrumentId,
    PatternDirection Direction,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    decimal StopPrice,
    decimal TargetPrice,
    ExecutionResolution OriginalResolution,
    string ExecutionModelVersion,
    string DataRevision);

public sealed record ExecutionPriceInterval(
    DateTime TimestampUtc,
    decimal High,
    decimal Low,
    string SourceReference,
    MarketDataQualityFlags QualityFlags);

public sealed record ExecutionEvidenceSlice(
    ExecutionResolution Resolution,
    string Provider,
    string SourceVersion,
    IReadOnlyList<ExecutionPriceInterval> Events);

public sealed record ExecutionResolutionAttempt(
    ExecutionResolution Resolution,
    ExecutionChronology Result,
    string Reason,
    IReadOnlyList<string> SourceReferences);

public sealed record ExecutionAmbiguityResult(
    string ResultId,
    string RequestId,
    ExecutionChronology Chronology,
    ExecutionResolution? ResolvedAtResolution,
    DateTime? FirstEventTimeUtc,
    IReadOnlyList<ExecutionResolutionAttempt> Attempts,
    string ResolutionEngineVersion,
    DateTime CreatedAtUtc,
    bool UsedOptimisticFallback = false);

public interface IExecutionAmbiguityResolver
{
    ExecutionAmbiguityResult Resolve(ExecutionEvidenceRequest request,
        IReadOnlyList<ExecutionEvidenceSlice> evidence, DateTime createdAtUtc);
}

public interface IExecutionAmbiguityRepository
{
    Task SaveAsync(ExecutionEvidenceRequest request,ExecutionAmbiguityResult result,
        CancellationToken cancellationToken=default);
    Task<ExecutionAmbiguityResult?> FindResultAsync(string resultId,CancellationToken cancellationToken=default);
}

public sealed class ExecutionAmbiguityResolver : IExecutionAmbiguityResolver
{
    public const string EngineVersion="1.0.0";
    private static readonly ExecutionResolution[] Hierarchy=[ExecutionResolution.OneSecond,ExecutionResolution.Tick];
    public ExecutionAmbiguityResult Resolve(ExecutionEvidenceRequest request,
        IReadOnlyList<ExecutionEvidenceSlice> evidence,DateTime createdAtUtc)
    {
        var attempts=new List<ExecutionResolutionAttempt>();
        foreach(var resolution in Hierarchy)
        {
            var slices=evidence.Where(x=>x.Resolution==resolution).ToArray();
            if(slices.Length==0){attempts.Add(new(resolution,ExecutionChronology.NoEvidence,"resolution-unavailable",[]));continue;}
            var rejected=MarketDataQualityFlags.Incomplete|MarketDataQualityFlags.InvalidOhlc|
                MarketDataQualityFlags.ProviderConflict|MarketDataQualityFlags.UnresolvedInstrument;
            var events=slices.SelectMany(x=>x.Events).Where(x=>(x.QualityFlags&rejected)==0)
                .Where(x=>x.TimestampUtc>=request.WindowStartUtc&&x.TimestampUtc<=request.WindowEndUtc)
                .OrderBy(x=>x.TimestampUtc).ThenBy(x=>x.SourceReference,StringComparer.Ordinal).ToArray();
            if(events.Length==0){attempts.Add(new(resolution,ExecutionChronology.NoEvidence,"no-events-in-window",[]));continue;}
            var outcome=Evaluate(request,events);
            attempts.Add(new(resolution,outcome.Chronology,outcome.Reason,outcome.References));
            if(outcome.Chronology is ExecutionChronology.TargetBeforeStop or ExecutionChronology.StopBeforeTarget)
                return new($"{request.RequestId}|{EngineVersion}",request.RequestId,outcome.Chronology,resolution,
                    outcome.Time,attempts,EngineVersion,createdAtUtc,false);
        }
        return new($"{request.RequestId}|{EngineVersion}",request.RequestId,
            attempts.All(x=>x.Result==ExecutionChronology.NoEvidence)?ExecutionChronology.NoEvidence:ExecutionChronology.StillAmbiguous,
            null,null,attempts,EngineVersion,createdAtUtc,false);
    }

    private static (ExecutionChronology Chronology,string Reason,DateTime? Time,IReadOnlyList<string> References)
        Evaluate(ExecutionEvidenceRequest request,IReadOnlyList<ExecutionPriceInterval> events)
    {
        foreach(var item in events)
        {
            var target=item.Low<=request.TargetPrice&&item.High>=request.TargetPrice;
            var stop=item.Low<=request.StopPrice&&item.High>=request.StopPrice;
            if(target&&stop)return(ExecutionChronology.StillAmbiguous,"both-levels-in-same-event",null,[item.SourceReference]);
            if(target)return(ExecutionChronology.TargetBeforeStop,"target-first",item.TimestampUtc,[item.SourceReference]);
            if(stop)return(ExecutionChronology.StopBeforeTarget,"stop-first",item.TimestampUtc,[item.SourceReference]);
        }
        return(ExecutionChronology.NoEvidence,"levels-not-touched",null,events.Select(x=>x.SourceReference).ToArray());
    }
}
