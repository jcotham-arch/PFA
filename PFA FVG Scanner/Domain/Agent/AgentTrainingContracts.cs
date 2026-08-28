using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Agent;

public sealed record AgentTrainingExample(
    string ExampleId,string DatasetId,string InstrumentId,string? ContractId,string Timeframe,
    DateTime EventTimeUtc,DateTime FeatureKnownAtUtc,DateTime DecisionTimeUtc,
    DateTime OutcomeKnownAtUtc,IReadOnlyDictionary<string,decimal> NumericFeatures,
    IReadOnlyList<string> PatternModuleIds,IReadOnlyList<string> SequenceRoles,decimal OutcomeR,
    string SourceRevision,string ContentHash);

public sealed record AgentTrainingDataset(
    string DatasetId,string DatasetVersion,string DataRevision,DateTime AsOfUtc,
    IReadOnlyList<AgentTrainingExample> Examples,string ContentHash,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed class AgentTrainingDatasetBuilder
{
    public const string Version="point-in-time-agent-dataset-1.0.0";
    public AgentTrainingDataset Build(string datasetId,string dataRevision,DateTime asOfUtc,
        IReadOnlyList<AgentTrainingExample> examples)
    {
        if(string.IsNullOrWhiteSpace(datasetId)||string.IsNullOrWhiteSpace(dataRevision))throw new ArgumentException("Dataset identity and data revision are required.");
        var ordered=examples.OrderBy(x=>x.EventTimeUtc).ThenBy(x=>x.ExampleId,StringComparer.Ordinal).ToArray();
        if(ordered.Select(x=>x.ExampleId).Distinct(StringComparer.Ordinal).Count()!=ordered.Length)throw new InvalidOperationException("Training example identities must be unique.");
        if(ordered.Any(x=>x.FeatureKnownAtUtc>x.DecisionTimeUtc||x.DecisionTimeUtc<x.EventTimeUtc||x.OutcomeKnownAtUtc<=x.DecisionTimeUtc||x.OutcomeKnownAtUtc>asOfUtc))throw new InvalidOperationException("Agent training data violates point-in-time chronology or uses future-known outcomes.");
        var hash=Hash(JsonSerializer.Serialize(new{datasetId,Version,dataRevision,asOfUtc,Examples=ordered.Select(x=>x.ContentHash)}));
        return new(datasetId,Version,dataRevision,asOfUtc,ordered,hash,false,false);
    }
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record GenericOutcomeDatasetRequest(DateTime AsOfUtc, int TargetHorizonMinutes = 15,
    IReadOnlyList<string>? InstrumentIds = null);

public sealed record GenericOutcomeResearchExample(
    string ExampleId,string ObservationId,string OutcomeId,string InstrumentId,string? ContractId,
    string Timeframe,string ModuleId,string PatternType,string Direction,DateTime EventTimeUtc,
    DateTime FeatureKnownAtUtc,DateTime DecisionTimeUtc,DateTime OutcomeKnownAtUtc,string Split,
    IReadOnlyDictionary<string,decimal> NumericFeatures,IReadOnlyDictionary<string,decimal> Labels,
    string SourceRevision,string ContentHash);

public sealed record GenericOutcomeDatasetManifest(
    string DatasetId,string DatasetVersion,string DataRevision,DateTime AsOfUtc,int TargetHorizonMinutes,
    int ExampleCount,int TrainCount,int ValidationCount,int TestCount,DateTime EarliestEventUtc,
    DateTime LatestEventUtc,IReadOnlyList<string> InstrumentIds,IReadOnlyList<string> ModuleIds,
    IReadOnlyList<string> FeatureNames,IReadOnlyList<string> LabelNames,string ContentHash,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record AgentBaselineTrainingRequest(string DatasetId,
    string TargetName = "directionalCloseTicks");

public sealed record AgentBaselineMetric(string Split,int SampleCount,decimal MeanAbsoluteError,
    decimal RootMeanSquaredError,decimal DirectionalAccuracy,decimal MeanActual,decimal MeanPrediction);

public sealed record AgentBaselineSegmentMetric(string InstrumentId,string Split,int SampleCount,
    decimal MeanAbsoluteError,decimal RootMeanSquaredError,decimal DirectionalAccuracy,
    decimal MeanActual,decimal MeanPrediction);

public sealed record AgentBaselineRun(string RunId,string ModelVersion,string DatasetId,string DatasetContentHash,
    string TargetName,int TrainingSamples,int GroupCount,IReadOnlyList<AgentBaselineMetric> Metrics,
    DateTime TrainedAtUtc,string ContentHash,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false,
    IReadOnlyList<AgentBaselineSegmentMetric>? SegmentMetrics=null);
