namespace PFA_FVG_Scanner.Domain.Agent;

public sealed record AgentHurdleTrainingRequest(string DatasetId);
public sealed record AgentHurdleHeadArtifact(string Head,string Target,IReadOnlyList<string> FeatureNames,
    IReadOnlyList<decimal> Means,IReadOnlyList<decimal> Scales,IReadOnlyList<decimal> Coefficients,string ContentHash);
public sealed record AgentHurdleEconomicMetric(string Split,int Samples,int SelectedSamples,decimal ScoreThreshold,
    decimal MeanNetR,decimal WinRate,decimal ProfitFactor,decimal MaximumDrawdownR,decimal ProfitabilityBrierScore);
public sealed record AgentHurdleRun(string RunId,string ModelVersion,string DatasetId,string DatasetContentHash,
    int TrainingSamples,decimal ValidationSelectedThreshold,AgentHurdleEconomicMetric Validation,
    AgentHurdleEconomicMetric Test,IReadOnlyList<AgentHurdleHeadArtifact> Artifacts,string Status,
    IReadOnlyList<string> Reasons,DateTime TrainedAtUtc,string ContentHash,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);
