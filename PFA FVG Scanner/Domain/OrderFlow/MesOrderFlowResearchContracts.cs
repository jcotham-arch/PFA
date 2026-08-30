namespace PFA_FVG_Scanner.Domain.OrderFlow;

public sealed record MesSweepProxySample(
    string SampleId,DateTime SignalTimeUtc,string Direction,string Split,decimal EntryPrice,decimal StopPrice,
    decimal TargetPrice,decimal RiskPoints,decimal VolumeRatio,string Outcome,decimal NetR,int HoldingMinutes,string ContentHash);

public sealed record MesSweepProxyMetrics(
    string Variant,string Split,int Signals,int Resolved,int Wins,int Losses,decimal WinRate,
    decimal MeanNetR,decimal ProfitFactor,decimal NetR,int DistinctDays);

public sealed record MesOrderFlowResearchReport(
    string ReportId,string EngineVersion,DateTime AsOfUtc,int BarsEvaluated,int StructuralSweepCandidates,
    int VolumeQualifiedCandidates,IReadOnlyList<MesSweepProxyMetrics> Metrics,
    OrderFlowCoverageReport TrueOrderFlowCoverage,string DataTier,string Interpretation,
    bool TrueOrderFlowTestingActive,bool EligibleForAgentTraining,bool CanRouteToRealBroker,string ContentHash);
