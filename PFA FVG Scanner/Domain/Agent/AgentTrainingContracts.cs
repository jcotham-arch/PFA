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
