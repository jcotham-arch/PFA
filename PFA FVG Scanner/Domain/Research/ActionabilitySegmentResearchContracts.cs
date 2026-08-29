namespace PFA_FVG_Scanner.Domain.Research;

public sealed record ActionabilitySegmentMetric(string Split,int Samples,decimal MeanNetR,decimal WinRate,
    decimal ProfitFactor,decimal MaximumDrawdownR);
public sealed record ActionabilitySegmentCandidate(string SegmentId,string Granularity,string ModuleId,string InstrumentId,
    string Session,string ContextBucket,string EntryPolicy,string StopPolicy,string ExitPolicy,string DirectionPolicy,decimal TargetR,
    decimal MaximumHoldingMinutes,ActionabilitySegmentMetric Train,ActionabilitySegmentMetric Validation,
    ActionabilitySegmentMetric? Test,string Status,IReadOnlyList<string> Reasons);
public sealed record ActionabilitySegmentResearchReport(string ReportId,string Version,string DatasetId,string DatasetContentHash,
    int Examples,int EvaluatedCandidates,int DevelopmentRejectedCandidates,int ValidationSelectedCandidates,int UntouchedTestConfirmedCandidates,
    IReadOnlyList<ActionabilitySegmentCandidate> Candidates,DateTime GeneratedAtUtc,
    string ContentHash="",bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);
