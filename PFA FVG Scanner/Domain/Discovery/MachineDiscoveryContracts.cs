using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Domain.Discovery;

public sealed record DiscoveryTemporalSplit(DateTime TrainingStartUtc,DateTime TrainingEndUtc,DateTime EvaluationStartUtc,DateTime EvaluationEndUtc,TimeSpan Embargo)
{
    public void Validate(){if(TrainingStartUtc>=TrainingEndUtc||EvaluationStartUtc>=EvaluationEndUtc||TrainingEndUtc+Embargo>EvaluationStartUtc)throw new ArgumentException("Discovery partitions must be ordered, non-overlapping, and respect the embargo.");}
}
public sealed record MachineDiscoveryManifest(string RunId,string EngineVersion,string ModelVersion,ResearchDatasetManifest Dataset,DiscoveryTemporalSplit Split,IReadOnlyList<string> FeatureIds,int ClusterCount,int RandomSeed,string SearchVersion,string MultipleComparisonMethod,decimal FamilyWiseAlpha,DateTime AsOfUtc)
{
    public string ContentHash()=>Hash(JsonSerializer.Serialize(this));
    internal static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
public sealed record MachineDiscoveryObservation(string ObservationId,string InstrumentId,DateTime EventTimeUtc,DateTime FeatureKnownAtUtc,DateTime OutcomeKnownAtUtc,string DatasetId,string DataRevision,IReadOnlyDictionary<string,decimal> Features,decimal OutcomeR);
public sealed record FeatureExplanation(string FeatureId,decimal ClusterMean,decimal PopulationMean,decimal AbsoluteDifference,int Rank);
public sealed record MachineFeatureCluster(string ClusterId,int Ordinal,IReadOnlyDictionary<string,decimal> Centroid,int TrainingSamples,int EvaluationSamples,decimal TrainingMeanR,decimal EvaluationMeanR,decimal RawPValue,decimal AdjustedPValue,IReadOnlyList<FeatureExplanation> Explanations,string ContentHash);
public sealed record MachineDiscoveryResult(MachineDiscoveryManifest Manifest,string ManifestHash,string ModelId,string InputContentHash,IReadOnlyList<MachineFeatureCluster> Clusters,GeneralResearchRun ResearchRun,bool CanActivateStrategy=false)
{
    public string ContentHash()=>MachineDiscoveryManifest.Hash(JsonSerializer.Serialize(new{ManifestHash,ModelId,InputContentHash,Clusters,ResearchRun=ResearchRun.ContentHash(),CanActivateStrategy}));
}
public interface IMachineDiscoveryRepository
{
    Task SaveAsync(MachineDiscoveryResult result,CancellationToken cancellationToken=default);
    Task<MachineDiscoveryResult?> FindAsync(string runId,CancellationToken cancellationToken=default);
}
