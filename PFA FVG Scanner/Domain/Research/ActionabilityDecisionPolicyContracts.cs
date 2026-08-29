namespace PFA_FVG_Scanner.Domain.Research;

public sealed record ActionabilityDecisionPolicyMetric(string Split,int Samples,decimal ScoreThreshold,
    decimal MeanNetR,decimal WinRate,decimal ProfitFactor,decimal MaximumDrawdownR);

public sealed record ActionabilityDecisionPolicyCandidate(string PolicyId,string RunId,string ArtifactId,
    ActionabilityDecisionPolicyMetric Validation,ActionabilityDecisionPolicyMetric Test,string Status,
    IReadOnlyList<string> Reasons);

public sealed record ActionabilityDecisionPolicyReport(string Version,string DatasetId,string DatasetContentHash,
    string RunId,string ArtifactId,int ValidationExamples,int TestExamples,int ThresholdsTested,
    IReadOnlyList<ActionabilityDecisionPolicyCandidate> Candidates,DateTime GeneratedAtUtc,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);
