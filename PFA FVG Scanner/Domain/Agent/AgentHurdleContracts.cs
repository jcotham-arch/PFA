namespace PFA_FVG_Scanner.Domain.Agent;

public sealed record AgentHurdleTrainingRequest(string DatasetId);
public sealed record AgentHurdleHeadArtifact(string Head,string Target,IReadOnlyList<string> FeatureNames,
    IReadOnlyList<decimal> Means,IReadOnlyList<decimal> Scales,IReadOnlyList<decimal> Coefficients,string ContentHash);
public sealed record AgentProbabilityCalibrationBin(int Bin,decimal LowerBound,decimal UpperBound,
    int TrainingSamples,decimal RawMeanProbability,decimal CalibratedProbability);
public sealed record AgentHurdleEconomicMetric(string Split,int Samples,int SelectedSamples,decimal ScoreThreshold,
    decimal MeanNetR,decimal WinRate,decimal ProfitFactor,decimal MaximumDrawdownR,decimal ProfitabilityBrierScore,
    decimal RawProfitabilityBrierScore=0);
public sealed record AgentHurdleSegmentMetric(string SegmentType,string SegmentId,string Split,int Samples,
    decimal ProfitableRate,decimal MeanRawProbability,decimal MeanCalibratedProbability,
    decimal RawBrierScore,decimal CalibratedBrierScore);
public sealed record AgentHurdleRun(string RunId,string ModelVersion,string DatasetId,string DatasetContentHash,
    int TrainingSamples,decimal ValidationSelectedThreshold,AgentHurdleEconomicMetric Validation,
    AgentHurdleEconomicMetric Test,IReadOnlyList<AgentHurdleHeadArtifact> Artifacts,string Status,
    IReadOnlyList<string> Reasons,DateTime TrainedAtUtc,string ContentHash,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false,
    IReadOnlyList<AgentProbabilityCalibrationBin>? CalibrationBins=null,
    IReadOnlyList<AgentHurdleSegmentMetric>? SegmentMetrics=null);
